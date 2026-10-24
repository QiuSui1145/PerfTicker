import React from 'react';
import {
  AbsoluteFill,
  OffthreadVideo,
  Sequence,
  interpolate,
  spring,
  staticFile,
  useCurrentFrame,
  useVideoConfig,
  Easing,
  Img,
} from 'remotion';
import { C, FONT_CN, FONT_NUM, VID, AREA, Cam, v2s, applyCam } from './theme';

const CLAMP = { extrapolateLeft: 'clamp', extrapolateRight: 'clamp' } as const;
const EASE = Easing.bezier(0.22, 1, 0.36, 1);

const lerp = (a: number, b: number, t: number) => a + (b - a) * t;

/* ────────────────────────── 基础组件 ────────────────────────── */

const Grid: React.FC<{ opacity?: number }> = ({ opacity = 0.5 }) => (
  <AbsoluteFill style={{ opacity }}>
    <AbsoluteFill
      style={{
        backgroundImage: `linear-gradient(${C.line} 1px, transparent 1px), linear-gradient(90deg, ${C.line} 1px, transparent 1px)`,
        backgroundSize: '80px 80px',
        opacity: 0.35,
      }}
    />
    <AbsoluteFill
      style={{
        background: `radial-gradient(ellipse at 50% 45%, rgba(242,193,78,0.10), transparent 55%),
                     radial-gradient(ellipse at 50% 50%, transparent 30%, ${C.bg} 78%)`,
      }}
    />
  </AbsoluteFill>
);

const LogoMark: React.FC<{ size?: number; progress?: number }> = ({ size = 96, progress = 1 }) => (
  <div
    style={{
      width: size,
      height: size,
      borderRadius: size * 0.24,
      background: `linear-gradient(145deg, #FFE39C 0%, #E9B33C 45%, #C2891A 100%)`,
      boxShadow: `0 0 ${size * 0.55}px rgba(242,193,78,${0.45 * progress}), inset 0 0 0 1px rgba(255,255,255,0.35)`,
      display: 'flex',
      alignItems: 'flex-end',
      justifyContent: 'center',
      gap: size * 0.11,
      paddingBottom: size * 0.22,
      position: 'relative',
      overflow: 'hidden',
    }}
  >
    {[0.42, 0.62, 0.34].map((h, i) => (
      <div
        key={i}
        style={{
          width: size * 0.1,
          height: size * h * progress,
          background: '#111620',
          borderRadius: size * 0.02,
        }}
      />
    ))}
    <div
      style={{
        position: 'absolute',
        inset: 0,
        background: 'linear-gradient(105deg, transparent 30%, rgba(255,255,255,0.55) 48%, transparent 62%)',
        transform: `translateX(${(progress - 1) * 160}%)`,
        opacity: progress > 0.2 ? 1 : 0,
      }}
    />
  </div>
);

const Caption: React.FC<{
  tag: string;
  title: string;
  sub?: string;
  delay?: number;
  align?: 'left' | 'center';
}> = ({ tag, title, sub, delay = 12, align = 'left' }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const sp = spring({ frame: frame - delay, fps, config: { damping: 200, mass: 0.6 } });
  const op = interpolate(frame, [delay, delay + 14], [0, 1], CLAMP);
  return (
    <div
      style={{
        position: 'absolute',
        left: align === 'center' ? 0 : 96,
        right: align === 'center' ? 0 : undefined,
        bottom: 78,
        display: 'flex',
        flexDirection: 'column',
        alignItems: align === 'center' ? 'center' : 'flex-start',
        gap: 10,
        transform: `translateY(${(1 - sp) * 26}px)`,
        opacity: op,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
        <div style={{ width: 26, height: 3, background: C.gold, borderRadius: 2 }} />
        <div style={{ fontFamily: FONT_CN, fontSize: 22, color: C.gold, letterSpacing: 4 }}>{tag}</div>
      </div>
      <div
        style={{
          fontFamily: FONT_CN,
          fontSize: 52,
          fontWeight: 800,
          color: C.text,
          letterSpacing: 2,
          textShadow: '0 8px 30px rgba(0,0,0,0.8)',
          lineHeight: 1.15,
        }}
      >
        {title}
      </div>
      {sub ? (
        <div style={{ fontFamily: FONT_CN, fontSize: 26, color: C.dim, letterSpacing: 1.5, lineHeight: 1.5 }}>
          {sub}
        </div>
      ) : null}
    </div>
  );
};

const SceneTag: React.FC<{ index: string; label: string; delay?: number }> = ({ index, label, delay = 6 }) => {
  const frame = useCurrentFrame();
  const op = interpolate(frame, [delay, delay + 12], [0, 1], CLAMP);
  return (
    <div
      style={{
        position: 'absolute',
        top: 54,
        right: 96,
        display: 'flex',
        alignItems: 'center',
        gap: 14,
        opacity: op,
        background: 'rgba(6,8,12,0.72)',
        border: '1px solid rgba(242,193,78,0.22)',
        padding: '8px 18px 8px 10px',
        borderRadius: 999,
        backdropFilter: 'blur(6px)',
      }}
    >
      <div
        style={{
          fontFamily: FONT_NUM,
          fontSize: 20,
          color: C.bg,
          background: C.gold,
          padding: '3px 10px',
          borderRadius: 3,
          fontWeight: 700,
        }}
      >
        {index}
      </div>
      <div style={{ fontFamily: FONT_CN, fontSize: 22, color: C.dim, letterSpacing: 3 }}>{label}</div>
    </div>
  );
};

/* 相机：把录屏画面做推拉摇移 */
const Camera: React.FC<{ cam: Cam; children: React.ReactNode }> = ({ cam, children }) => (
  <AbsoluteFill
    style={{
      transform: `scale(${cam.s}) translate(${cam.x / cam.s}px, ${cam.y / cam.s}px)`,
      transformOrigin: '50% 50%',
    }}
  >
    {children}
  </AbsoluteFill>
);

const Highlight: React.FC<{ area: keyof typeof AREA; cam: Cam; from: number; dur: number; label?: string }> = ({
  area,
  cam,
  from,
  dur,
  label,
}) => {
  const frame = useCurrentFrame();
  const t = interpolate(frame, [from, from + 14, from + dur - 14, from + dur], [0, 1, 1, 0], CLAMP);
  if (frame < from - 1 || frame > from + dur + 1) return null;
  const a = AREA[area];
  const r = applyCam(v2s(a.x, a.y, a.w, a.h), cam);
  const tick = 18;
  const stroke = `2px solid rgba(242,193,78,${0.85 * t})`;
  return (
    <div style={{ position: 'absolute', left: r.left, top: r.top, width: r.width, height: r.height, opacity: t }}>
      <div style={{ position: 'absolute', inset: 0, border: stroke, borderRadius: 6, boxShadow: `0 0 24px rgba(242,193,78,${0.25 * t})` }} />
      {[
        { l: 0, t: 0, bl: true, bt: true },
        { l: 'auto', t: 0, br: true, bt: true },
        { l: 0, t: 'auto', bl: true, bb: true },
        { l: 'auto', t: 'auto', br: true, bb: true },
      ].map((p, i) => (
        <div
          key={i}
          style={{
            position: 'absolute',
            left: p.l as any,
            right: (p as any).br ? 0 : undefined,
            top: p.t as any,
            bottom: (p as any).bb ? 0 : undefined,
            width: tick,
            height: tick,
            borderLeft: (p as any).bl ? `3px solid ${C.gold}` : undefined,
            borderRight: (p as any).br ? `3px solid ${C.gold}` : undefined,
            borderTop: (p as any).bt ? `3px solid ${C.gold}` : undefined,
            borderBottom: (p as any).bb ? `3px solid ${C.gold}` : undefined,
          }}
        />
      ))}
      {label ? (
        <div
          style={{
            position: 'absolute',
            left: 10,
            top: 10,
            fontFamily: FONT_CN,
            fontSize: 18,
            color: C.gold,
            background: 'rgba(6,8,12,0.86)',
            border: '1px solid rgba(242,193,78,0.55)',
            padding: '4px 10px',
            borderRadius: 3,
            whiteSpace: 'nowrap',
            fontWeight: 700,
          }}
        >
          {label}
        </div>
      ) : null}
    </div>
  );
};

/* ────────────────────────── 实机镜头 ────────────────────────── */

type Shot = {
  clip: string;
  from: number;
  dur: number;
  camFrom: Cam;
  camTo: Cam;
  camDur?: number;
  tag: string;
  title: string;
  sub?: string;
  index?: string;
  label?: string;
  highlights?: { area: keyof typeof AREA; from: number; dur: number; label?: string }[];
};

const ShotScene: React.FC<Shot> = (s) => {
  const frame = useCurrentFrame();
  const { fps, durationInFrames } = useVideoConfig();
  const camDur = s.camDur ?? Math.max(30, durationInFrames - 10);
  const t = interpolate(frame, [0, camDur], [0, 1], { ...CLAMP, easing: EASE });
  const cam: Cam = {
    s: lerp(s.camFrom.s, s.camTo.s, t),
    x: lerp(s.camFrom.x, s.camTo.x, t),
    y: lerp(s.camFrom.y, s.camTo.y, t),
  };
  const fadeIn = interpolate(frame, [0, 12], [0, 1], CLAMP);
  const fadeOut = interpolate(frame, [durationInFrames - 14, durationInFrames], [1, 0], CLAMP);
  return (
    <AbsoluteFill style={{ opacity: Math.min(fadeIn, fadeOut), backgroundColor: C.bg }}>
      <Camera cam={cam}>
        <OffthreadVideo
          src={staticFile(s.clip)}
          startFrom={s.from}
          muted
          style={{ width: '100%', height: '100%', objectFit: 'cover' }}
        />
      </Camera>
      {s.index ? <SceneTag index={s.index} label={s.label ?? ''} /> : null}
      {(s.highlights ?? []).map((h, i) => (
        <Highlight key={i} area={h.area} cam={cam} from={h.from} dur={h.dur} label={h.label} />
      ))}
      <div
        style={{
          position: 'absolute',
          inset: 0,
          background: 'linear-gradient(to top, rgba(3,5,9,0.97) 0%, rgba(3,5,9,0.80) 16%, rgba(3,5,9,0.25) 32%, transparent 48%)',
          pointerEvents: 'none',
        }}
      />
      <Caption tag={s.tag} title={s.title} sub={s.sub} />
    </AbsoluteFill>
  );
};

/* ────────────────────────── 片头 ────────────────────────── */

const Intro: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps, durationInFrames } = useVideoConfig();
  const logoSp = spring({ frame: frame - 6, fps, config: { damping: 14, mass: 0.9, stiffness: 90 } });
  const titleOp = interpolate(frame, [22, 40], [0, 1], CLAMP);
  const titleY = interpolate(frame, [22, 46], [26, 0], { ...CLAMP, easing: EASE });
  const subOp = interpolate(frame, [38, 56], [0, 1], CLAMP);
  const lineW = interpolate(frame, [34, 60], [0, 260], { ...CLAMP, easing: EASE });
  const sweep = interpolate(frame, [30, 62], [-40, 140], CLAMP);
  const out = interpolate(frame, [durationInFrames - 12, durationInFrames], [1, 0], CLAMP);

  return (
    <AbsoluteFill style={{ backgroundColor: C.bg, opacity: out }}>
      <Grid opacity={interpolate(frame, [0, 20], [0, 1], CLAMP)} />
      <AbsoluteFill style={{ alignItems: 'center', justifyContent: 'center', flexDirection: 'column', gap: 26 }}>
        <div style={{ transform: `scale(${lerp(0.6, 1, logoSp)}) rotate(${lerp(-10, 0, logoSp)}deg)` }}>
          <LogoMark size={128} progress={Math.min(1, logoSp * 1.1)} />
        </div>
        <div style={{ position: 'relative', overflow: 'hidden', padding: '0 20px' }}>
          <div
            style={{
              fontFamily: FONT_CN,
              fontSize: 96,
              fontWeight: 900,
              letterSpacing: 10,
              color: C.text,
              opacity: titleOp,
              transform: `translateY(${titleY}px)`,
              textShadow: '0 12px 48px rgba(0,0,0,0.9)',
            }}
          >
            性能行情终端
          </div>
          <div
            style={{
              position: 'absolute',
              top: 0,
              bottom: 0,
              width: 180,
              left: `${sweep}%`,
              background: 'linear-gradient(100deg, transparent, rgba(255,225,150,0.55), transparent)',
              filter: 'blur(6px)',
              mixBlendMode: 'screen',
            }}
          />
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 18, opacity: subOp }}>
          <div style={{ width: lineW, height: 2, background: `linear-gradient(90deg, transparent, ${C.gold})` }} />
          <div style={{ fontFamily: FONT_NUM, fontSize: 30, letterSpacing: 14, color: C.goldSoft }}>PERFTICKER</div>
          <div style={{ width: lineW, height: 2, background: `linear-gradient(90deg, ${C.gold}, transparent)` }} />
        </div>
        <div style={{ fontFamily: FONT_CN, fontSize: 28, color: C.dim, letterSpacing: 6, opacity: subOp }}>
          把任务管理器，做成炒股终端
        </div>
      </AbsoluteFill>
    </AbsoluteFill>
  );
};

/* ────────────────────────── 片尾 ────────────────────────── */

const Outro: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps, durationInFrames } = useVideoConfig();
  const sp = spring({ frame: frame - 4, fps, config: { damping: 18, mass: 0.8 } });
  const op1 = interpolate(frame, [0, 14], [0, 1], CLAMP);
  const op2 = interpolate(frame, [18, 34], [0, 1], CLAMP);
  const op3 = interpolate(frame, [36, 52], [0, 1], CLAMP);
  const out = interpolate(frame, [durationInFrames - 12, durationInFrames], [1, 0], CLAMP);
  return (
    <AbsoluteFill style={{ backgroundColor: C.bg, opacity: out }}>
      <Grid opacity={0.85} />
      <AbsoluteFill style={{ alignItems: 'center', justifyContent: 'center', flexDirection: 'column', gap: 30 }}>
        <div style={{ transform: `scale(${lerp(0.8, 1, sp)})`, opacity: op1 }}>
          <LogoMark size={110} progress={1} />
        </div>
        <div style={{ fontFamily: FONT_CN, fontSize: 78, fontWeight: 900, letterSpacing: 8, color: C.text, opacity: op1 }}>
          性能行情终端
        </div>
        <div style={{ display: 'flex', gap: 16, opacity: op2 }}>
          {['CPU / GPU 频率', 'K 线行情', '主力动向', '算力经营', '深浅主题', '托盘常驻'].map((t) => (
            <div
              key={t}
              style={{
                fontFamily: FONT_CN,
                fontSize: 22,
                color: C.dim,
                border: `1px solid ${C.line}`,
                background: 'rgba(255,255,255,0.03)',
                borderRadius: 999,
                padding: '8px 20px',
              }}
            >
              {t}
            </div>
          ))}
        </div>
        <div
          style={{
            marginTop: 14,
            display: 'flex',
            alignItems: 'center',
            gap: 14,
            opacity: op3,
          }}
        >
          <div style={{ fontFamily: FONT_NUM, fontSize: 34, color: C.goldSoft, letterSpacing: 2 }}>
            github.com/QiuSui1145/PerfTicker
          </div>
        </div>
        <div style={{ fontFamily: FONT_CN, fontSize: 22, color: C.fade, letterSpacing: 3, opacity: op3 }}>
          GPL-3.0 · QiuSui_Studio · 单文件 exe 免安装
        </div>
      </AbsoluteFill>
    </AbsoluteFill>
  );
};

/* ────────────────────────── 主时间线 ────────────────────────── */

const ShotA: React.FC = () => (
  <ShotScene
    clip="clips/A_hero.mp4"
    from={0}
    dur={320}
    camFrom={{ s: 1.14, x: 0, y: 4 }}
    camTo={{ s: 1.02, x: 0, y: 0 }}
    camDur={300}
    index="01"
    label="全景 / OVERVIEW"
    tag="实时行情"
    title="一屏掌握整机算力"
    sub="52 项指标 · 指数条 · 自选行情 · 主图 · 盘口 · 逐笔 · 进程列表"
    highlights={[
      { area: 'index', from: 70, dur: 90, label: '指数条' },
      { area: 'watch', from: 150, dur: 90, label: '自选 52 项' },
    ]}
  />
);

const ShotB: React.FC = () => (
  <ShotScene
    clip="clips/C_panels.mp4"
    from={0}
    dur={260}
    camFrom={{ s: 1.32, x: 500, y: 130 }}
    camTo={{ s: 1.40, x: 505, y: 120 }}
    index="02"
    label="自选行情 / WATCHLIST"
    tag="逐核行情"
    title="32 个逻辑核心，逐核报价"
    sub="红涨绿跌 · 迷你 K 线 · 现价 / 涨跌 · 点击即联动主图"
    highlights={[{ area: 'watch', from: 26, dur: 120, label: '核心 01–32' }]}
  />
);

const ShotC: React.FC = () => (
  <ShotScene
    clip="clips/B_chart.mp4"
    from={0}
    dur={300}
    camFrom={{ s: 1.04, x: 20, y: 95 }}
    camTo={{ s: 1.16, x: 31, y: 105 }}
    index="03"
    label="K 线主图 / CHART"
    tag="K 线行情引擎"
    title="把采样点，画成真正的 K 线"
    sub="2s / 5s / 10s / 30s / 1min 周期 · MA5·10·20 · 成交量 · 十字光标 OHLC"
    highlights={[{ area: 'chart', from: 30, dur: 150, label: '十字光标 / OHLC' }]}
  />
);

const ShotD: React.FC = () => (
  <ShotScene
    clip="clips/D_procs.mp4"
    from={0}
    dur={320}
    camFrom={{ s: 1.24, x: 0, y: -470 }}
    camTo={{ s: 1.32, x: 0, y: -512 }}
    index="04"
    label="个股列表 / PROCESSES"
    tag="进程即个股"
    title="搜索 · 置顶 · 右键全抛"
    sub="按 CPU / GPU / 内存 / 线程排序 · 每行一条迷你 K 线 · 二次确认结束进程"
    highlights={[{ area: 'proc', from: 40, dur: 160, label: '搜索 / 右键菜单' }]}
  />
);

const ShotE: React.FC = () => (
  <ShotScene
    clip="clips/E_economy.mp4"
    from={0}
    dur={300}
    camFrom={{ s: 1.42, x: 20, y: -330 }}
    camTo={{ s: 1.50, x: 37, y: -344 }}
    index="05"
    label="营收中心 / REVENUE"
    tag="算力经营"
    title="负载越高，赚得越多"
    sub="每个部件按占用率结算产能 · 低于盈亏平衡点即亏损 · 金额本地固化，重启续算"
    highlights={[
      { area: 'econ', from: 26, dur: 100, label: '分项盈亏' },
      { area: 'econ', from: 150, dur: 90, label: '本地固化 + 归零' },
    ]}
  />
);

const ShotF: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const sp = spring({ frame: frame - 150, fps, config: { damping: 18, mass: 0.7 } });
  const abOp = interpolate(frame, [146, 168], [0, 1], CLAMP);
  return (
    <AbsoluteFill>
      <ShotScene
        clip="clips/F_settings.mp4"
        from={0}
        dur={330}
        camFrom={{ s: 1.44, x: 830, y: 400 }}
        camTo={{ s: 1.50, x: 892, y: 442 }}
        index="06"
        label="设置 / 托盘 / 关于"
        tag="日常可用"
        title="深色 · 浅色 · 跟随系统"
        sub="开机自启动 · 滚动信息开关 · 关闭即驻留托盘，采样与营收继续运行"
        highlights={[
          { area: 'settings', from: 24, dur: 110, label: '设置面板' },
          { area: 'chips', from: 20, dur: 60, label: '营收开关' },
        ]}
      />
      {/* 关于窗口浮层 */}
      <div
        style={{
          position: 'absolute',
          right: 150,
          bottom: 150,
          width: 690,
          opacity: abOp,
          transform: `translateY(${(1 - sp) * 40}px) scale(${lerp(0.92, 1, sp)})`,
          borderRadius: 8,
          overflow: 'hidden',
          boxShadow: '0 40px 100px rgba(0,0,0,0.85), 0 0 0 1px rgba(242,193,78,0.35)',
        }}
      >
        <Img src={staticFile('about_still.png')} style={{ width: '100%', display: 'block' }} />
      </div>
    </AbsoluteFill>
  );
};

const ProgressBar: React.FC = () => {
  const frame = useCurrentFrame();
  const { durationInFrames } = useVideoConfig();
  const w = interpolate(frame, [0, durationInFrames], [0, 100], CLAMP);
  return (
    <div style={{ position: 'absolute', left: 0, bottom: 0, height: 3, width: `${w}%`, background: C.gold, opacity: 0.85 }} />
  );
};

export const Promo: React.FC = () => {
  return (
    <AbsoluteFill style={{ backgroundColor: C.bg }}>
      <Sequence from={0} durationInFrames={140} name="Intro">
        <Intro />
      </Sequence>
      <Sequence from={120} durationInFrames={320} name="Overview">
        <ShotA />
      </Sequence>
      <Sequence from={425} durationInFrames={260} name="Watchlist">
        <ShotB />
      </Sequence>
      <Sequence from={670} durationInFrames={300} name="Chart">
        <ShotC />
      </Sequence>
      <Sequence from={955} durationInFrames={320} name="Processes">
        <ShotD />
      </Sequence>
      <Sequence from={1260} durationInFrames={300} name="Economy">
        <ShotE />
      </Sequence>
      <Sequence from={1545} durationInFrames={330} name="Settings">
        <ShotF />
      </Sequence>
      <Sequence from={1860} durationInFrames={360} name="Outro">
        <Outro />
      </Sequence>
      <ProgressBar />
    </AbsoluteFill>
  );
};
