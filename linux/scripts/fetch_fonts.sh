#!/usr/bin/env bash
# 取「霞鹜文楷 / 霞鹜文楷等宽」字体到 static/fonts/
# 优先用本机系统字体；有 fonttools 就压成 woff2（体积 25MB → 7.8MB）。
set -euo pipefail
cd "$(dirname "$0")/.."
DEST="static/fonts"
mkdir -p "$DEST"

find_src() {
  local name="$1"
  for d in /usr/share/fonts/truetype/lxgw /usr/share/fonts /usr/local/share/fonts ~/.local/share/fonts; do
    [ -d "$d" ] || continue
    local hit
    hit=$(find "$d" -iname "${name}.ttf" -o -iname "${name}.otf" 2>/dev/null | head -1)
    [ -n "$hit" ] && { echo "$hit"; return 0; }
  done
  return 1
}

to_woff2() {
  local src="$1" out="$2"
  if command -v python3 >/dev/null && python3 -c "import fontTools" 2>/dev/null; then
    python3 - "$src" "$out" <<'PY'
import sys
from fontTools.ttLib import TTFont
f = TTFont(sys.argv[1])
f.flavor = "woff2"
f.save(sys.argv[2])
PY
  elif command -v uv >/dev/null; then
    uv run --with fonttools --with brotli python - "$src" "$out" <<'PY'
import sys
from fontTools.ttLib import TTFont
f = TTFont(sys.argv[1])
f.flavor = "woff2"
f.save(sys.argv[2])
PY
  else
    return 1
  fi
}

for n in LXGWWenKai-Regular LXGWWenKaiMono-Regular; do
  if [ -f "$DEST/$n.woff2" ]; then
    echo "✓ $n.woff2 已存在"
    continue
  fi
  if src=$(find_src "$n"); then
    echo "· 找到系统字体 $src"
    if to_woff2 "$src" "$DEST/$n.woff2"; then
      echo "✓ 已转换 $n.woff2"
    else
      cp "$src" "$DEST/$n.ttf"
      echo "✓ 已复制 $n.ttf（未装 fonttools，体积较大）"
    fi
  else
    echo "✗ 未找到 $n.ttf"
    echo "  安装方式：sudo apt install fonts-lxgw-wenkai   或从 https://github.com/lxgw/LxgwWenKai/releases 下载后放到 $DEST/"
  fi
done

echo
echo "没有字体也能运行，界面会回退到系统字体。"
