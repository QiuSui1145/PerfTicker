import React from 'react';
import { AbsoluteFill, Img, staticFile } from 'remotion';
import { C, FONT_CN, FONT_NUM } from './theme';

const GRID = {
  backgroundImage: `linear-gradient(${C.line} 1px, transparent 1px), linear-gradient(90deg, ${C.line} 1px, transparent 1px)`,
  backgroundSize: '72px 72px',
  opacity: 0.26,
};

export const Cover: React.FC = () => {
  return (
    <AbsoluteFill style={{ backgroundColor: C.bg }}>
      <AbsoluteFill style={GRID} />
      <AbsoluteFill
        style={{
          background: `radial-gradient(1000px 800px at 82% 50%, rgba(242,193,78,0.18), transparent 68%),
                       radial-gradient(760px 620px at 8% 12%, rgba(77,141,255,0.10), transparent 72%),
                       linear-gradient(165deg, #090C12 0%, #06080C 58%, #0B1017 100%)`,
        }}
      />
      <div style={{ position: 'absolute', left: 0, top: 0, width: '100%', height: 4, background: `linear-gradient(90deg, transparent, ${C.gold}, transparent)`, opacity: 0.85 }} />

      {/* 右侧实机截图：留出左边界，避免压字 */}
      <div
        style={{
          position: 'absolute',
          left: 880,
          top: 190,
          width: 1180,
          height: 740,
          transform: 'perspective(2600px) rotateY(-9deg) rotateX(1.5deg) rotateZ(-0.8deg)',
          transformOrigin: 'left center',
          borderRadius: 8,
          overflow: 'hidden',
          boxShadow: '0 70px 150px rgba(0,0,0,0.92), 0 0 0 1px rgba(242,193,78,0.42), 0 0 90px rgba(242,193,78,0.14)',
        }}
      >
        <Img
          src={staticFile('hero.png')}
          style={{ width: 1180, height: 688, objectFit: 'cover', objectPosition: 'left top', display: 'block' }}
        />
        <div
          style={{
            position: 'absolute',
            inset: 0,
            background:
              'linear-gradient(95deg, rgba(6,8,12,0.92) 0%, rgba(6,8,12,0.35) 16%, rgba(6,8,12,0.0) 34%), linear-gradient(0deg, rgba(6,8,12,0.55), transparent 40%)',
          }}
        />
      </div>

      {/* 左侧文案 */}
      <div
        style={{
          position: 'absolute',
          left: 92,
          top: 168,
          width: 780,
          display: 'flex',
          flexDirection: 'column',
          gap: 24,
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 20 }}>
          <div
            style={{
              width: 72,
              height: 72,
              borderRadius: 17,
              background: 'linear-gradient(145deg, #FFE39C, #E9B33C 45%, #C2891A)',
              display: 'flex',
              alignItems: 'flex-end',
              justifyContent: 'center',
              gap: 7,
              paddingBottom: 15,
              boxShadow: '0 0 48px rgba(242,193,78,0.45)',
            }}
          >
            {[0.42, 0.62, 0.34].map((h, i) => (
              <div key={i} style={{ width: 7, height: 58 * h, background: '#111620', borderRadius: 2 }} />
            ))}
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
            <div style={{ fontFamily: FONT_NUM, fontSize: 21, color: C.gold, letterSpacing: 9 }}>PERFTICKER</div>
            <div style={{ fontFamily: FONT_CN, fontSize: 19, color: C.fade, letterSpacing: 4 }}>WINDOWS 性能行情终端</div>
          </div>
        </div>

        <div
          style={{
            fontFamily: FONT_CN,
            fontSize: 118,
            fontWeight: 900,
            lineHeight: 1.06,
            letterSpacing: 4,
            color: C.text,
            textShadow: '0 18px 64px rgba(0,0,0,0.9)',
            marginTop: 6,
          }}
        >
          性能行情终端
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
          <div style={{ width: 54, height: 4, background: C.gold, borderRadius: 2, flexShrink: 0 }} />
          <div style={{ fontFamily: FONT_CN, fontSize: 37, color: C.goldSoft, letterSpacing: 2.5, fontWeight: 700 }}>
            把任务管理器做成炒股终端
          </div>
        </div>

        <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', width: 720, marginTop: 10 }}>
          {['CPU/GPU 频率', '1 秒级 K 线', '主力动向', '逐笔成交', '算力经营', '深/浅主题', '托盘常驻'].map((t) => (
            <div
              key={t}
              style={{
                fontFamily: FONT_CN,
                fontSize: 24,
                color: C.text,
                border: '1px solid rgba(242,193,78,0.32)',
                background: 'rgba(242,193,78,0.06)',
                borderRadius: 7,
                padding: '9px 17px',
                letterSpacing: 1,
              }}
            >
              {t}
            </div>
          ))}
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: 16, marginTop: 18 }}>
          <div style={{ fontFamily: FONT_NUM, fontSize: 25, color: C.dim, letterSpacing: 0.5 }}>
            github.com/QiuSui1145/PerfTicker
          </div>
          <div style={{ width: 1, height: 20, background: C.line }} />
          <div style={{ fontFamily: FONT_CN, fontSize: 21, color: C.fade, letterSpacing: 2 }}>GPL-3.0</div>
        </div>
        <div style={{ fontFamily: FONT_CN, fontSize: 20, color: C.fade, letterSpacing: 2, marginTop: -6 }}>
          单文件 exe · 免装运行时 · QiuSui_Studio · 2026
        </div>
      </div>
    </AbsoluteFill>
  );
};
