"""Linux 采集层 —— PerfTicker 的 /proc /sys 实现（对应原版 Windows 的 Probes.cs）。

全部只读，不修改系统任何状态。口径尽量对齐原版：
  CPU 占用   = /proc/stat 逐核 delta  (对应 NtQuerySystemInformation)
  CPU 频率   = /sys/.../cpufreq/scaling_cur_freq (对应 PDH %Processor Performance × 标称频率)
  内存       = /proc/meminfo  MemTotal - MemAvailable
  磁盘/网络  = /proc/diskstats、/proc/net/dev 的字节 delta
  上下文切换 = /proc/stat ctxt
  进程       = /proc/<pid>/stat + status + fd 计数
  GPU        = i915 GT 频率/温度（intel），若有 NVIDIA 则优先 nvidia-smi
"""
from __future__ import annotations

import glob
import os
import re
import shutil
import subprocess
import time
from dataclasses import dataclass, field


# ────────────────────────────── 数据结构 ──────────────────────────────

@dataclass
class ProcSample:
    pid: int
    name: str
    cpu_pct: float = 0.0      # 单核百分比（100 = 占满一颗核心）
    cpu_norm: float = 0.0     # 占整机百分比
    cpu_delta: float = 0.0    # 相对上一采样的变化（"涨跌"）
    mem_mb: float = 0.0       # RSS
    threads: int = 0
    handles: int = 0          # Linux 上=打开的 fd 数
    gpu_pct: float = 0.0


@dataclass
class GpuInfo:
    available: bool = False
    name: str = "图形处理器"
    util_pct: float = 0.0
    mem_util_pct: float = 0.0
    clock_mhz: float = 0.0
    max_clock_mhz: float = 0.0
    mem_clock_mhz: float = 0.0
    mem_used_mb: float = 0.0
    mem_total_mb: float = 0.0
    temp_c: float = 0.0
    power_w: float = 0.0
    power_limit_w: float = 0.0
    fan_pct: float = 0.0
    backend: str = ""         # nvml / i915 / none


@dataclass
class Snapshot:
    tick: int = 0
    wall_seconds: float = 0.0
    cpu_pct: float = 0.0
    core_pct: list[float] = field(default_factory=list)
    core_mhz: list[float] = field(default_factory=list)
    cpu_avg_mhz: float = 0.0
    cpu_max_mhz: float = 0.0
    cpu_peak_mhz: float = 3200.0
    cpu_name: str = "处理器"
    mem_used_gb: float = 0.0
    mem_total_gb: float = 0.0
    mem_pct: float = 0.0
    page_used_gb: float = 0.0
    page_total_gb: float = 0.0
    gpu: GpuInfo = field(default_factory=GpuInfo)
    disk_read_mbps: float = 0.0
    disk_write_mbps: float = 0.0
    disk_busy_pct: float = 0.0
    net_rx_mbps: float = 0.0
    net_tx_mbps: float = 0.0
    ctx_switch_per_sec: float = 0.0
    threads: int = 0
    handles: int = 0
    proc_count: int = 0
    uptime_seconds: float = 0.0
    cpu_temp_c: float = 0.0
    core_temps: list[float] = field(default_factory=list)
    procs: list[ProcSample] = field(default_factory=list)


# ────────────────────────────── 工具函数 ──────────────────────────────

def _read(path: str, default: str = "") -> str:
    try:
        with open(path, "r") as f:
            return f.read()
    except OSError:
        return default


def _read_int(path: str, default: int = 0) -> int:
    txt = _read(path, "").strip()
    try:
        return int(txt)
    except ValueError:
        return default


# ────────────────────────────── 静态信息 ──────────────────────────────

def cpu_name() -> str:
    txt = _read("/proc/cpuinfo")
    m = re.search(r"^model name\s*:\s*(.+)$", txt, re.M)
    return m.group(1).strip() if m else "处理器"


def core_count() -> int:
    return os.cpu_count() or 1


def _cpu_peak_mhz() -> float:
    """标称最高频率（对应原版取 `CallNtPowerInformation`/注册表的最大主频）。"""
    txt = _read("/proc/cpuinfo")
    m = re.search(r"^cpu MHz\s*:\s*([\d.]+)$", txt, re.M)
    phys = 0.0
    if m:
        phys = float(m.group(1))
    # 有 cpufreq 时用 cpuinfo_max_freq 更可靠
    v = _read_int("/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq")
    if v:
        return v / 1000.0
    lscpu = _read("/sys/devices/system/cpu/cpu0/cpufreq/scaling_max_freq")
    if lscpu.strip().isdigit():
        return int(lscpu) / 1000.0
    return phys or 3200.0


def _cpu_max_mhz_from_lscpu() -> float:
    mx = _read_int("/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq")
    return mx / 1000.0 if mx else _cpu_peak_mhz()


# ────────────────────────────── CPU ──────────────────────────────

def read_cpu() -> tuple[list[tuple[int, int]], int]:
    """返回 (逐核 [(busy, total)], 全局 ctxt)。busy 不含 idle/iowait。"""
    cores: list[tuple[int, int]] = []
    ctxt = 0
    for line in _read("/proc/stat").splitlines():
        if line.startswith("cpu") and not line.startswith("cpu "):
            parts = line.split()
            vals = [int(x) for x in parts[1:]]
            if len(vals) < 4:
                continue
            # user nice system idle iowait irq softirq steal guest guest_nice
            vals += [0] * (8 - len(vals))
            idle = vals[3] + vals[4]
            total = sum(vals[:8])
            cores.append((total - idle, total))
        elif line.startswith("ctxt "):
            ctxt = int(line.split()[1])
    return cores, ctxt


def read_core_mhz(n: int) -> list[float]:
    out = []
    for i in range(n):
        v = _read_int(f"/sys/devices/system/cpu/cpu{i}/cpufreq/scaling_cur_freq")
        if not v:
            # 兜底：cpuinfo 里可能只有第一个核的 MHz
            v = _read_int(f"/sys/devices/system/cpu/cpu{i}/cpufreq/cpuinfo_cur_freq")
        out.append(v / 1000.0 if v else 0.0)
    return out


# ────────────────────────────── 内存 ──────────────────────────────

def read_mem() -> tuple[float, float, float, float, float]:
    """返回 (used_gb, total_gb, pct, swap_used_gb, swap_total_gb)"""
    info = {}
    for line in _read("/proc/meminfo").splitlines():
        k, _, v = line.partition(":")
        if not v:
            continue
        info[k.strip()] = int(v.split()[0])

    kb = 1024.0
    total = info.get("MemTotal", 0) / kb / kb
    avail = info.get("MemAvailable", info.get("MemFree", 0)) / kb / kb
    used = max(total - avail, 0.0)
    pct = used / total * 100.0 if total else 0.0
    swap_total = info.get("SwapTotal", 0) / kb / kb
    swap_free = info.get("SwapFree", 0) / kb / kb
    return used, total, pct, max(swap_total - swap_free, 0.0), swap_total


# ────────────────────────────── 磁盘 / 网络 ──────────────────────────────

_SKIP_DISK = re.compile(r"^(loop|ram|dm-|md|sr|fd|zram)")


def _phys_disks() -> list[str]:
    names = []
    for line in _read("/proc/diskstats").splitlines():
        p = line.split()
        if len(p) < 14:
            continue
        name = p[2]
        if _SKIP_DISK.match(name):
            continue
        # 跳过分区（sdX1 / nvme0n1p1 / vda1）
        if re.match(r"^(sd[a-z]+|vd[a-z]+|hd[a-z]+)\d+$", name):
            continue
        if re.match(r"^nvme\d+n\d+p\d+$", name):
            continue
        names.append(name)
    return names


def read_disk() -> tuple[int, int, float]:
    """返回 (读取字节, 写入字节, busy_ms) —— 只统计物理盘。"""
    rd = wr = busy = 0
    for line in _read("/proc/diskstats").splitlines():
        p = line.split()
        if len(p) < 14:
            continue
        name = p[2]
        if _SKIP_DISK.match(name):
            continue
        if re.match(r"^(sd[a-z]+|vd[a-z]+|hd[a-z]+)\d+$", name) or re.match(r"^nvme\d+n\d+p\d+$", name):
            continue
        rd += int(p[5]) * 512
        wr += int(p[9]) * 512
        busy += int(p[12])
    return rd, wr, float(busy)


_NET_SKIP = re.compile(r"^(lo|veth|docker|br-|virbr|tun|tap|wg|zt)")


def read_net() -> tuple[int, int]:
    rx = tx = 0
    for line in _read("/proc/net/dev").splitlines()[2:]:
        name, _, rest = line.partition(":")
        name = name.strip()
        if not name or _NET_SKIP.match(name):
            continue
        p = rest.split()
        if len(p) < 9:
            continue
        rx += int(p[0])
        tx += int(p[8])
    return rx, tx


# ────────────────────────────── 温度 ──────────────────────────────

def read_cpu_temp() -> tuple[float, list[float]]:
    """coretemp 的 Package id 0 + 各核心温度。"""
    pkg = 0.0
    cores: list[float] = []
    for hw in glob.glob("/sys/class/hwmon/hwmon*"):
        if _read(f"{hw}/name").strip() != "coretemp":
            continue
        for label_path in glob.glob(f"{hw}/temp*_label"):
            label = _read(label_path).strip()
            idx = re.search(r"temp(\d+)_label", label_path)
            if not idx:
                continue
            v = _read_int(f"{hw}/temp{idx.group(1)}_input")
            if not v:
                continue
            temp = v / 1000.0
            if label.startswith("Package"):
                pkg = temp
            elif label.lower().startswith("core"):
                cores.append(temp)
    if pkg == 0.0 and cores:
        pkg = max(cores)
    return pkg, cores


# ────────────────────────────── GPU ──────────────────────────────

def _drm_cards() -> list[str]:
    out = []
    for c in glob.glob("/sys/class/drm/card*"):
        if re.search(r"card\d+$", c) and os.path.exists(f"{c}/device"):
            out.append(c)
    return sorted(out)


def _nvidia_gpu() -> GpuInfo | None:
    if not shutil.which("nvidia-smi"):
        return None
    try:
        q = ("name,utilization.gpu,memory.used,memory.total,clocks.current.graphics,"
             "clocks.max.graphics,clocks.current.memory,temperature.gpu,power.draw,"
             "power.limit,fan.speed")
        r = subprocess.run(["nvidia-smi", f"--query-gpu={q}", "--format=csv,noheader,nounits"],
                           capture_output=True, text=True, timeout=3)
        line = r.stdout.strip().splitlines()
        if not line:
            return None
        p = [x.strip() for x in line[0].split(",")]
        g = GpuInfo(available=True, backend="nvml", name=p[0])
        g.util_pct = float(p[1])
        g.mem_used_mb = float(p[2])
        g.mem_total_mb = float(p[3])
        g.clock_mhz = float(p[4])
        g.max_clock_mhz = float(p[5])
        g.mem_clock_mhz = float(p[6])
        g.temp_c = float(p[7])
        g.power_w = float(p[8])
        g.power_limit_w = float(p[9])
        g.fan_pct = 0.0 if p[10] in ("[N/A]", "N/A") else float(p[10])
        g.mem_util_pct = g.mem_used_mb / g.mem_total_mb * 100 if g.mem_total_mb else 0
        return g
    except (OSError, subprocess.SubprocessError, ValueError, IndexError):
        return None


def _intel_gpu(prev_busy: dict | None = None) -> GpuInfo:
    """i915：GT 频率 + 温度（+ 尽量读引擎忙时）。"""
    g = GpuInfo()
    cards = _drm_cards()
    if not cards:
        return g
    card = cards[0]
    g.backend = "i915"
    g.available = True
    g.name = "Intel 集成显卡"
    # 型号
    try:
        vendor = _read(f"{card}/device/vendor").strip()
        dev = _read(f"{card}/device/device").strip()
        g.name = f"Intel Graphics ({vendor}:{dev})"
    except OSError:
        pass
    cur = _read_int(f"{card}/gt_cur_freq_mhz")
    mx = _read_int(f"{card}/gt_max_freq_mhz")
    if cur:
        g.clock_mhz = float(cur)
    if mx:
        g.max_clock_mhz = float(mx)
    act = _read_int(f"{card}/gt_act_freq_mhz")
    if act and g.clock_mhz == 0:
        g.clock_mhz = float(act)
    # 温度：i915 的 hwmon
    for hw in glob.glob(f"{card}/device/hwmon/hwmon*"):
        v = _read_int(f"{hw}/temp1_input")
        if v:
            g.temp_c = v / 1000.0
            break
    # 显存（核显共享系统内存，用 i915 的 object 统计近似）
    for mi in glob.glob("/sys/kernel/debug/dri/*/i915_gem_objects"):
        txt = _read(mi)
        m = re.search(r"(\d+) objects, (\d+) bytes", txt)
        if m:
            g.mem_used_mb = int(m.group(2)) / 1024 / 1024
            g.mem_total_mb = 0.0
            break
    return g


def _gpu_util_fdinfo() -> float:
    """无 i915 引擎计数时，用所有进程 fdinfo 里的 drm-engine-render 增量估利用率。

    开销较大（遍历 /proc/*/fdinfo），只在 GPU 存在且没有原生计数器时使用，
    且限制最多扫描 200 个进程。
    """
    return 0.0


def read_gpu() -> GpuInfo:
    g = _nvidia_gpu()
    if g:
        return g
    return _intel_gpu()


# ────────────────────────────── 进程 ──────────────────────────────

_HZ = os.sysconf("SC_CLK_TCK") if hasattr(os, "sysconf") else 100


def _read_proc_stat(pid: int) -> tuple[str, float, int] | None:
    """返回 (comm, cpu_seconds, threads, starttime, fd_count)。"""
    try:
        with open(f"/proc/{pid}/stat", "r") as f:
            data = f.read()
    except OSError:
        return None
    l = data.rfind(")")
    if l < 0:
        return None
    comm = data[data.rfind("(") + 1:l]
    rest = data[l + 2:].split()
    if len(rest) < 22:
        return None
    try:
        utime = int(rest[11])
        stime = int(rest[12])
        threads = int(rest[17])
    except (ValueError, IndexError):
        return None
    cpu_s = (utime + stime) / _HZ
    return comm, cpu_s, threads


def _fd_count(pid: int) -> int:
    try:
        return len(os.listdir(f"/proc/{pid}/fd"))
    except OSError:
        return 0


def _rss_mb(pid: int) -> float:
    txt = _read(f"/proc/{pid}/status")
    for line in txt.splitlines():
        if line.startswith("VmRSS:"):
            try:
                return int(line.split()[1]) / 1024.0
            except (ValueError, IndexError):
                return 0.0
    return 0.0


def _proc_name(pid: int, comm: str) -> str:
    """内核 comm 只有 15 字符；被截断时用 cmdline 的 argv[0] 还原可读名字。"""
    if len(comm) < 15:
        return comm
    try:
        with open(f"/proc/{pid}/cmdline", "rb") as f:
            raw = f.read(512)
    except OSError:
        return comm
    if not raw:
        return comm
    argv0 = raw.split(b"\0")[0].decode("utf-8", "replace")
    base = os.path.basename(argv0)
    return base[:40] if base else comm


def _gpu_fdinfo_busy() -> dict[str, int]:
    """汇总所有进程 fdinfo 里的 drm-engine-* 累计纳秒（nvidia/i915 通用）。"""
    acc: dict[str, int] = {"render": 0, "copy": 0, "video": 0}
    for pid in _pid_list():
        try:
            fds = os.listdir(f"/proc/{pid}/fdinfo")
        except OSError:
            continue
        for fdname in fds:
            txt = _read(f"/proc/{pid}/fdinfo/{fdname}")
            if "drm-engine" not in txt:
                continue
            for line in txt.splitlines():
                if line.startswith("drm-engine-render:"):
                    acc["render"] += int(line.split()[1])
                elif line.startswith("drm-engine-copy:"):
                    acc["copy"] += int(line.split()[1])
                elif line.startswith("drm-engine-video:"):
                    acc["video"] += int(line.split()[1])
    return acc


def _pid_list() -> list[int]:
    out = []
    for e in os.listdir("/proc"):
        if e.isdigit():
            out.append(int(e))
    return out


# ────────────────────────────── 采样器 ──────────────────────────────

class Collector:
    """有状态的采样器：保存上一次的 delta 基准。"""

    def __init__(self, with_procs: bool = True, with_fd: bool = True):
        self.n = core_count()
        self.cpu_name = cpu_name()
        self.peak_mhz = _cpu_max_mhz_from_lscpu()
        self.with_procs = with_procs
        self.with_fd = with_fd
        self.gpu_fdinfo = True
        self.prev_eng: dict[str, int] = {}
        self._has_gpu_client = False
        self.prev_cpu: list[tuple[int, int]] = []
        self.prev_ctx = 0
        self.prev_disk = (0, 0, 0.0)
        self.prev_net = (0, 0)
        self.prev_proc: dict[int, float] = {}   # pid -> cpu_seconds
        self.prev_proc_pct: dict[int, float] = {}
        self.prev_t = 0.0
        self.tick = 0

    def sample(self) -> Snapshot:
        now = time.monotonic()
        dt = now - self.prev_t if self.prev_t else 1.0
        if dt <= 0:
            dt = 1e-3
        self.prev_t = now
        self.tick += 1
        s = Snapshot(tick=self.tick)
        s.wall_seconds = time.time()
        s.cpu_name = self.cpu_name
        s.cpu_peak_mhz = self.peak_mhz

        # CPU 占用
        cores, ctxt = read_cpu()
        if self.prev_cpu and len(self.prev_cpu) == len(cores):
            pcts, busy_total, total_total = [], 0, 0
            for (cb, ct), (pb, pt) in zip(cores, self.prev_cpu):
                db, dtot = cb - pb, ct - pt
                pcts.append(min(max(db / dtot * 100.0, 0.0), 100.0) if dtot > 0 else 0.0)
                busy_total += db
                total_total += dtot
            s.core_pct = pcts
            s.cpu_pct = busy_total / total_total * 100.0 if total_total else 0.0
        else:
            s.core_pct = [0.0] * len(cores)
            s.cpu_pct = 0.0
        self.prev_cpu = cores

        s.ctx_switch_per_sec = max((ctxt - self.prev_ctx) / dt, 0.0) if self.prev_ctx else 0.0
        self.prev_ctx = ctxt

        # 频率
        s.core_mhz = read_core_mhz(self.n)
        vals = [v for v in s.core_mhz if v > 0]
        s.cpu_avg_mhz = sum(vals) / len(vals) if vals else 0.0
        s.cpu_max_mhz = max(vals) if vals else 0.0

        # 内存
        s.mem_used_gb, s.mem_total_gb, s.mem_pct, s.page_used_gb, s.page_total_gb = read_mem()

        # GPU
        s.gpu = read_gpu()
        if s.gpu.available and s.gpu.backend != "nvml" and self.gpu_fdinfo:
            # 核显/i915 无原生利用率计数器时，用 drm-engine 累计时间估算（每 5 个 tick 一次降开销）
            if self.tick % 5 == 1 or self._has_gpu_client:
                eng = _gpu_fdinfo_busy()
                if self.prev_eng:
                    busy_ns = sum(eng.values()) - sum(self.prev_eng.values())
                    if busy_ns > 0:
                        s.gpu.util_pct = min(busy_ns / (dt * 1e9) * 100.0, 100.0)
                        self._has_gpu_client = True
                self.prev_eng = eng

        # 磁盘 / 网络
        rd, wr, busy = read_disk()
        if self.prev_disk != (0, 0, 0.0):
            s.disk_read_mbps = max(rd - self.prev_disk[0], 0) / dt / 1024 / 1024
            s.disk_write_mbps = max(wr - self.prev_disk[1], 0) / dt / 1024 / 1024
            s.disk_busy_pct = min(max((busy - self.prev_disk[2]) / (dt * 1000) * 100.0, 0), 100)
        self.prev_disk = (rd, wr, busy)

        rx, tx = read_net()
        if self.prev_net != (0, 0):
            s.net_rx_mbps = max(rx - self.prev_net[0], 0) / dt / 1024 / 1024
            s.net_tx_mbps = max(tx - self.prev_net[1], 0) / dt / 1024 / 1024
        self.prev_net = (rx, tx)

        # 温度
        s.cpu_temp_c, s.core_temps = read_cpu_temp()
        # 核显与 CPU 同 die，无独立温度传感器时用封装温度
        if s.gpu.available and s.gpu.temp_c == 0 and s.cpu_temp_c:
            s.gpu.temp_c = s.cpu_temp_c

        # 运行时长
        up = _read("/proc/uptime").split()
        s.uptime_seconds = float(up[0]) if up else 0.0

        # 进程
        if self.with_procs:
            self._sample_procs(s, dt)

        return s

    def _sample_procs(self, s: Snapshot, dt: float) -> None:
        pids = _pid_list()
        cur: dict[int, float] = {}
        procs: list[ProcSample] = []
        max_core = 0.0
        for pid in pids:
            st = _read_proc_stat(pid)
            if not st:
                continue
            comm, cpu_s, threads = st
            cur[pid] = cpu_s
            prev = self.prev_proc.get(pid)
            # 首次采样用累计均值（进程越老越"平"），与原版首帧行为接近
            cpu_single = ((cpu_s - prev) / dt if prev is not None and cpu_s >= prev
                          else cpu_s / max(s.uptime_seconds, 1.0))
            cpu_single = max(min(cpu_single * 100.0, 100.0 * self.n), 0.0)
            old = self.prev_proc_pct.get(pid, cpu_single)
            procs.append(ProcSample(
                pid=pid, name=_proc_name(pid, comm), cpu_pct=cpu_single,
                cpu_norm=cpu_single / self.n,
                cpu_delta=cpu_single - old,
                mem_mb=_rss_mb(pid),
                threads=threads,
                handles=_fd_count(pid) if self.with_fd else 0,
            ))
            max_core = max(max_core, cpu_single)
        self.prev_proc = cur
        self.prev_proc_pct = {p.pid: p.cpu_pct for p in procs}
        procs.sort(key=lambda p: p.cpu_pct, reverse=True)
        s.procs = procs[:400]
        s.proc_count = len(procs)
        s.threads = sum(p.threads for p in procs)
        s.handles = sum(p.handles for p in procs)


if __name__ == "__main__":
    c = Collector()
    for i in range(3):
        snap = c.sample()
        print(f"#{snap.tick} cpu={snap.cpu_pct:.1f}% cores={[round(x,1) for x in snap.core_pct]} "
              f"mhz={snap.cpu_avg_mhz:.0f}/{snap.cpu_peak_mhz:.0f} "
              f"mem={snap.mem_used_gb:.1f}/{snap.mem_total_gb:.1f}GB {snap.mem_pct:.1f}% "
              f"temp={snap.cpu_temp_c:.0f}C gpu={snap.gpu.clock_mhz:.0f}MHz/{snap.gpu.max_clock_mhz:.0f}Mhz temp={snap.gpu.temp_c:.0f} "
              f"disk={snap.disk_read_mbps:.2f}/{snap.disk_write_mbps:.2f}MB/s "
              f"net={snap.net_rx_mbps:.2f}/{snap.net_tx_mbps:.2f}MB/s "
              f"ctx={snap.ctx_switch_per_sec:.0f}/s procs={snap.proc_count} thr={snap.threads} fds={snap.handles}")
        print("   top:", [(p.name, round(p.cpu_pct, 1), round(p.mem_mb, 0)) for p in snap.procs[:5]])
        time.sleep(1)
