"""PerfTicker Linux Web 服务（FastAPI + SSE）。

把 /proc /sys 的采样结果以「证券行情」的粒度推给浏览器：
  /            → 行情终端页面
  /api/meta    → 指标目录
  /api/history → 全部 tick 历史（首次建图用）
  /api/stream  → SSE 实时行情（每个采样一条）
  /api/interval→ 切换采样档位（0.5/1/2/5/10 秒）
  /api/pause   → 暂停/恢复采样
"""
from __future__ import annotations

import asyncio
import json
import os
import time
from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import FileResponse, JSONResponse, StreamingResponse
from fastapi.staticfiles import StaticFiles

from .collect import Collector, Snapshot
from .metrics import Market

BASE = os.path.dirname(os.path.abspath(__file__))
STATIC = os.path.join(os.path.dirname(BASE), "static")

ALLOWED_INTERVALS = [500, 1000, 2000, 5000, 10000]


class Hub:
    """采样线程 + 广播中心。"""

    def __init__(self) -> None:
        self.collector = Collector(with_procs=True, with_fd=True)
        self.market = Market(self.collector.n)
        self.interval_ms = 1000
        self.paused = False
        self.subs: set[asyncio.Queue] = set()
        self.snap: Snapshot | None = None
        self.payload: dict = {}
        self.task: asyncio.Task | None = None
        self.samples = 0
        self.sample_ms_avg = 0.0

    def _proc_list(self, snap: Snapshot) -> list[dict]:
        return [
            {
                "pid": p.pid, "name": p.name,
                "cpu": round(p.cpu_pct, 2), "norm": round(p.cpu_norm, 3),
                "d": round(p.cpu_delta, 2), "mem": round(p.mem_mb, 1),
                "thr": p.threads, "fd": p.handles,
            }
            for p in snap.procs[:240]
        ]

    def build(self, snap: Snapshot) -> dict:
        g = snap.gpu
        vals = {}
        for m in self.market.catalog:
            try:
                v = float(m.read(snap))
            except Exception:
                v = 0.0
            if v != v or v in (float("inf"), float("-inf")):
                v = 0.0
            vals[m.code] = round(v, 3)
        return {
            "tick": snap.tick,
            "ts": int(snap.wall_seconds * 1000),
            "values": vals,
            "cpu_pct": round(snap.cpu_pct, 1),
            "core_pct": [round(x, 1) for x in snap.core_pct],
            "core_mhz": [round(x) for x in snap.core_mhz],
            "cpu_avg_mhz": round(snap.cpu_avg_mhz),
            "cpu_peak_mhz": round(snap.cpu_peak_mhz),
            "cpu_name": snap.cpu_name,
            "cpu_temp": round(snap.cpu_temp_c, 1),
            "core_temps": [round(x, 1) for x in snap.core_temps],
            "mem_used_gb": round(snap.mem_used_gb, 2),
            "mem_total_gb": round(snap.mem_total_gb, 2),
            "mem_pct": round(snap.mem_pct, 1),
            "swap_used_gb": round(snap.page_used_gb, 2),
            "swap_total_gb": round(snap.page_total_gb, 2),
            "disk_read": round(snap.disk_read_mbps, 2),
            "disk_write": round(snap.disk_write_mbps, 2),
            "net_rx": round(snap.net_rx_mbps, 2),
            "net_tx": round(snap.net_tx_mbps, 2),
            "ctx": round(snap.ctx_switch_per_sec),
            "proc_count": snap.proc_count,
            "threads": snap.threads,
            "handles": snap.handles,
            "uptime": round(snap.uptime_seconds),
            "gpu": {
                "ok": g.available, "name": g.name, "backend": g.backend,
                "util": round(g.util_pct, 1), "clock": round(g.clock_mhz),
                "max_clock": round(g.max_clock_mhz),
                "temp": round(g.temp_c, 1), "power": round(g.power_w, 1),
                "mem_used": round(g.mem_used_mb), "mem_total": round(g.mem_total_mb),
            },
            "procs": self._proc_list(snap),
            "cfg": {
                "interval_ms": self.interval_ms,
                "paused": self.paused,
                "sample_ms": round(self.sample_ms_avg, 1),
                "samples": self.samples,
            },
        }

    def broadcast(self, payload: dict) -> None:
        data = json.dumps(payload, separators=(",", ":"), ensure_ascii=False)
        dead = []
        for q in self.subs:
            if q.full():
                try:
                    q.get_nowait()
                except asyncio.QueueEmpty:
                    pass
            try:
                q.put_nowait(data)
            except asyncio.QueueFull:
                dead.append(q)
        for q in dead:
            self.subs.discard(q)

    async def run(self) -> None:
        while True:
            t0 = time.monotonic()
            if not self.paused:
                try:
                    snap = await asyncio.to_thread(self.collector.sample)
                    self.snap = snap
                    self.market.push(snap)
                    self.payload = self.build(snap)
                    self.samples += 1
                    self.sample_ms_avg = (self.sample_ms_avg * 0.8
                                          + (time.monotonic() - t0) * 1000 * 0.2)
                    self.broadcast(self.payload)
                except Exception as e:  # 采样异常不能让循环死掉
                    print(f"[perfticker] sample error: {e!r}", flush=True)
            elapsed = time.monotonic() - t0
            await asyncio.sleep(max(self.interval_ms / 1000.0 - elapsed, 0.05))


hub = Hub()


@asynccontextmanager
async def lifespan(app: FastAPI):
    hub.task = asyncio.create_task(hub.run())
    yield
    if hub.task:
        hub.task.cancel()
        try:
            await hub.task
        except asyncio.CancelledError:
            pass


app = FastAPI(title="PerfTicker Linux", lifespan=lifespan)


@app.get("/api/meta")
async def api_meta():
    meta = hub.market.meta()
    meta["host"] = {
        "cpu_name": hub.collector.cpu_name,
        "cores": hub.collector.n,
        "peak_mhz": round(hub.collector.peak_mhz),
        "uname": os.uname().sysname + " " + os.uname().release,
        "node": os.uname().nodename,
    }
    return JSONResponse(meta)


@app.get("/api/history")
async def api_history():
    return JSONResponse({"meta": hub.market.meta(), "series": hub.market.history()})


@app.get("/api/now")
async def api_now():
    return JSONResponse(hub.payload or {})


@app.get("/api/stream")
async def api_stream(request: Request):
    q: asyncio.Queue = asyncio.Queue(maxsize=4)
    hub.subs.add(q)
    if hub.payload:
        q.put_nowait(json.dumps(hub.payload, separators=(",", ":"), ensure_ascii=False))

    async def gen():
        try:
            while True:
                if await request.is_disconnected():
                    break
                try:
                    item = await asyncio.wait_for(q.get(), timeout=15)
                except asyncio.TimeoutError:
                    yield ": keepalive\n\n"
                    continue
                yield f"data: {item}\n\n"
        finally:
            hub.subs.discard(q)

    return StreamingResponse(gen(), media_type="text/event-stream",
                             headers={"Cache-Control": "no-cache",
                                      "X-Accel-Buffering": "no",
                                      "Connection": "keep-alive"})


@app.post("/api/interval")
async def api_interval(request: Request):
    body = await request.json()
    ms = int(body.get("ms", 1000))
    if ms not in ALLOWED_INTERVALS:
        raise HTTPException(400, f"ms must be one of {ALLOWED_INTERVALS}")
    hub.interval_ms = ms
    return {"ok": True, "interval_ms": ms}


@app.post("/api/pause")
async def api_pause(request: Request):
    body = await request.json()
    hub.paused = bool(body.get("paused", True))
    return {"ok": True, "paused": hub.paused}


@app.get("/api/health")
async def api_health():
    return {"ok": True, "samples": hub.samples, "subs": len(hub.subs),
            "interval_ms": hub.interval_ms, "paused": hub.paused}


@app.get("/")
async def index():
    return FileResponse(os.path.join(STATIC, "index.html"))


app.mount("/static", StaticFiles(directory=STATIC), name="static")


def main() -> None:
    import argparse
    import uvicorn

    p = argparse.ArgumentParser(description="PerfTicker Linux Web")
    p.add_argument("--host", default="0.0.0.0")
    p.add_argument("--port", type=int, default=8770)
    p.add_argument("--interval", type=int, default=1000, help="采样间隔 ms")
    args = p.parse_args()
    hub.interval_ms = args.interval
    uvicorn.run(app, host=args.host, port=args.port, log_level="warning")


if __name__ == "__main__":
    main()
