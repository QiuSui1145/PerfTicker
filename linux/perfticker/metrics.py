"""指标目录 + tick 环形缓冲（对应原版 Metrics.cs / Catalog）。

每一项系统指标都是一只「个股」：有代码、单位、精度，并按采样点累积行情。
K 线聚合放在前端做（切换周期时即时重算，和原版 RebuildCandles 一致）。
"""
from __future__ import annotations

import time
from dataclasses import dataclass, field
from typing import Callable

from .collect import Snapshot

# 环形缓冲容量：1s 采样约 1 小时（原版 TickCap = 3600）
TICK_CAP = 3600


@dataclass
class MetricDef:
    code: str
    name: str
    group: str
    unit: str
    decimals: int
    title: str
    read: Callable[[Snapshot], float]


@dataclass
class Series:
    """一只「个股」的 tick 环形缓冲。"""
    ms: list[int] = field(default_factory=list)
    vals: list[float] = field(default_factory=list)

    def push(self, ms: int, v: float) -> None:
        self.ms.append(ms)
        self.vals.append(v)
        if len(self.ms) > TICK_CAP:
            del self.ms[0]
            del self.vals[0]

    def as_dict(self) -> dict:
        return {"ms": self.ms, "v": self.vals}


def build_catalog(cores: int) -> list[MetricDef]:
    """按本机核数生成指标目录（原版 52 项，Linux 版按实际硬件裁剪）。"""
    out: list[MetricDef] = []

    def add(code, name, group, unit, dec, title, read):
        out.append(MetricDef(code, name, group, unit, dec, title, read))

    # ── 指数条 ───────────────────────────────────
    add("000001", "CPU 综合指数", "指数", "GHz", 2, "处理器实时平均主频",
        lambda s: s.cpu_avg_mhz / 1000.0)
    add("000002", "CPU 负载", "指数", "%", 1, "整机 CPU 占用率",
        lambda s: s.cpu_pct)
    add("000003", "内存占用", "指数", "%", 1, "物理内存占用率",
        lambda s: s.mem_pct)

    # ── 处理器逐核 ───────────────────────────────
    for i in range(cores):
        idx = i
        add(f"0001{i + 1:02d}", f"核心 {i + 1:02d}", "处理器", "GHz", 2,
            f"逻辑处理器 {i + 1} 实时主频",
            lambda s, j=idx: (s.core_mhz[j] / 1000.0) if j < len(s.core_mhz) else 0.0)

    # ── 图形 ─────────────────────────────────────
    add("600519", "GPU 核心频率", "图形", "MHz", 0, "显卡核心实时频率",
        lambda s: s.gpu.clock_mhz if s.gpu.available else 0.0)
    add("600520", "GPU 最大频率", "图形", "MHz", 0, "显卡核心最高频率（上限参考）",
        lambda s: s.gpu.max_clock_mhz if s.gpu.available else 0.0)
    add("600521", "GPU 负载", "图形", "%", 1, "显卡引擎占用率",
        lambda s: s.gpu.util_pct if s.gpu.available else 0.0)
    add("600523", "GPU 温度", "图形", "°C", 0, "显卡核心温度",
        lambda s: s.gpu.temp_c if s.gpu.available else 0.0)

    # ── 内存 ─────────────────────────────────────
    add("000858", "已用内存", "内存", "GB", 2, "物理内存使用量",
        lambda s: s.mem_used_gb)
    add("000859", "交换内存", "内存", "GB", 2, "Swap 使用量",
        lambda s: s.page_used_gb)

    # ── 存储网络 ─────────────────────────────────
    add("601398", "磁盘读取", "存储网络", "MB/s", 2, "物理磁盘读取吞吐",
        lambda s: s.disk_read_mbps)
    add("601399", "磁盘写入", "存储网络", "MB/s", 2, "物理磁盘写入吞吐",
        lambda s: s.disk_write_mbps)
    add("600036", "网络下行", "存储网络", "MB/s", 2, "网络接收吞吐",
        lambda s: s.net_rx_mbps)
    add("600037", "网络上行", "存储网络", "MB/s", 2, "网络发送吞吐",
        lambda s: s.net_tx_mbps)

    # ── 系统 ─────────────────────────────────────
    add("000300", "上下文切换", "系统", "K/s", 1, "每秒上下文切换次数",
        lambda s: s.ctx_switch_per_sec / 1000.0)
    add("000301", "进程数", "系统", "", 0, "当前进程总数",
        lambda s: float(s.proc_count))
    add("000302", "线程数", "系统", "", 0, "当前线程总数",
        lambda s: float(s.threads))
    add("000303", "句柄数", "系统", "", 0, "打开的文件描述符总数",
        lambda s: float(s.handles))
    add("000304", "CPU 温度", "系统", "°C", 0, "处理器封装温度",
        lambda s: s.cpu_temp_c)
    add("000305", "磁盘繁忙", "系统", "%", 1, "物理磁盘繁忙度",
        lambda s: s.disk_busy_pct)

    return out


GROUP_ORDER = ["指数", "处理器", "图形", "内存", "存储网络", "系统"]


class Market:
    """行情中心：持有每只「个股」的 tick 序列。"""

    def __init__(self, cores: int):
        self.catalog = build_catalog(cores)
        self.series: dict[str, Series] = {m.code: Series() for m in self.catalog}
        self.started = time.time()
        self.tick = 0
        self.last: Snapshot | None = None

    def push(self, snap: Snapshot) -> None:
        self.last = snap
        self.tick = snap.tick
        ms = int(snap.wall_seconds * 1000)
        for m in self.catalog:
            try:
                v = float(m.read(snap))
            except (AttributeError, IndexError, TypeError, ZeroDivisionError):
                v = 0.0
            if v != v or v in (float("inf"), float("-inf")):
                v = 0.0
            self.series[m.code].push(ms, v)

    def meta(self) -> dict:
        return {
            "cores": len([m for m in self.catalog if m.group == "处理器"]),
            "groups": GROUP_ORDER,
            "metrics": [
                {"code": m.code, "name": m.name, "group": m.group,
                 "unit": m.unit, "decimals": m.decimals, "title": m.title}
                for m in self.catalog
            ],
            "started": self.started,
        }

    def history(self) -> dict:
        return {code: s.as_dict() for code, s in self.series.items()}
