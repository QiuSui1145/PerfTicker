export const C = {
  bg: '#06080C',
  bg2: '#0B0F15',
  panel: '#101620',
  line: '#232C3B',
  gold: '#F2C14E',
  goldSoft: '#FFD98A',
  goldDim: '#A8863A',
  up: '#FF4D4F',
  down: '#00C08B',
  text: '#E9EFF8',
  dim: '#8C99AC',
  fade: '#5A6472',
  blue: '#4D8DFF',
};

export const FONT_CN = '"Microsoft YaHei UI","Microsoft YaHei","PingFang SC",sans-serif';
export const FONT_NUM = '"Bahnschrift","DIN Alternate",Consolas,monospace';

/** 录屏画面 2400x1400 映射到 1920x1080 画布的系数（cover：横向铺满） */
export const VID = { w: 2400, h: 1400, k: 0.8, oy: -20 };

/** 视频坐标 -> 画布坐标（未叠加相机变换） */
export const v2s = (x: number, y: number, w: number, h: number) => ({
  left: x * VID.k,
  top: y * VID.k + VID.oy,
  width: w * VID.k,
  height: h * VID.k,
});

export type Cam = { s: number; x: number; y: number };

export const applyCam = (r: { left: number; top: number; width: number; height: number }, cam: Cam) => {
  const cx = 960;
  const cy = 540;
  return {
    left: cx + (r.left - cx) * cam.s + cam.x,
    top: cy + (r.top - cy) * cam.s + cam.y,
    width: r.width * cam.s,
    height: r.height * cam.s,
  };
};

/** 主要界面区域（录屏像素坐标） */
export const AREA = {
  title: { x: 0, y: 0, w: 2400, h: 63 },
  index: { x: 0, y: 63, w: 2400, h: 87 },
  watch: { x: 0, y: 150, w: 447, h: 875 },
  inst: { x: 447, y: 150, w: 1440, h: 147 },
  chart: { x: 447, y: 297, w: 1440, h: 584 },
  book: { x: 1887, y: 150, w: 513, h: 495 },
  tape: { x: 1887, y: 645, w: 513, h: 378 },
  proc: { x: 0, y: 1025, w: 2400, h: 333 },
  econ: { x: 447, y: 881, w: 1440, h: 144 },
  settings: { x: 15, y: 72, w: 453, h: 321 },
  chips: { x: 1630, y: 12, w: 110, h: 44 },
};
