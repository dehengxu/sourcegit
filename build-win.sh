#!/usr/bin/env bash
# build-win.sh — 一键交叉编译 SourceGit Windows .exe（macOS / Linux 上也能跑）
#
# 用法：
#   ./build-win.sh                                # 默认 win-x64, 多文件, 输出到 build/
#   ./build-win.sh win-arm64                      # 切到 ARM64
#   MODE=single ./build-win.sh                    # 打成单个 .exe（19M 左右）
#   RUNTIME=win-arm64 MODE=single OUTPUT_DIR=dist ./build-win.sh
#   VERSION=2026.16 ./build-win.sh                # 覆盖版本号
#
# 模式：
#   multi（默认）— 自包含多文件包，跟官方 CI 一致，最终打成 zip
#   single       — PublishSingleFile + 压缩嵌入，单 .exe 双击即用
#
# 体积优化：
#   1) DisableAOT=true                            → 跨平台 AOT 不支持，绕开走 JIT
#   2) PublishTrimmed=true + TrimMode=link        → 裁剪未用代码
#   3) single 模式下 DebugType=embedded           → 调试符号塞进 .exe，不产 pdb
#   4) single 模式下 EnableCompressionInSingleFile=true → 单文件内嵌压缩
#
# 已知限制：
#   - Windows NativeAOT .exe 必须在 Windows 上编（mac 上 ILCompiler 报
#     "Cross-OS native compilation is not supported"）
#   - 这里是 JIT + self-contained 模式，用户机器不需要装 .NET 运行时
#   - 首次启动会比 AOT 慢 0.5–1s（运行时 JIT 编译 + 单文件解压）
#
# 跟 mac 脚本对齐：
#   - 同样的参数风格（RUNTIME / MODE / OUTPUT_DIR / VERSION）
#   - 同样的输出格式（human_size、Top 5、产物清单）
#   - 同样的清理策略（mavis-trash 代替 rm -rf）

set -euo pipefail

# ============ 参数 ============
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

RUNTIME="${1:-${RUNTIME:-win-x64}}"
OUTPUT_DIR="${OUTPUT_DIR:-build}"
CONFIG="${CONFIG:-Release}"
MODE="${MODE:-multi}"  # multi | single

# 只支持 win-* RID
case "$RUNTIME" in
  win-x64|win-arm64|win-x86|win-*) ;;
  *) echo "❌ 不支持的 Runtime: $RUNTIME（需要 win-x64 / win-arm64）" >&2; exit 1 ;;
esac

case "$MODE" in
  multi|single) ;;
  *) echo "❌ 不支持的 MODE: $MODE（需要 multi / single）" >&2; exit 1 ;;
esac

# 读版本号
if [[ -n "${VERSION:-}" ]]; then
  APP_VERSION="$VERSION"
elif [[ -f VERSION ]]; then
  APP_VERSION="$(tr -d '[:space:]' < VERSION)"
else
  echo "❌ 无法确定版本号，请设置 VERSION 环境变量或确保 VERSION 文件存在" >&2
  exit 1
fi

# ============ 路径 ============
PUB_DIR="$OUTPUT_DIR/SourceGit_${RUNTIME}"
ZIP_NAME="sourcegit_${APP_VERSION}.${RUNTIME}.zip"
ZIP_PATH="$OUTPUT_DIR/$ZIP_NAME"
EXE_NAME="SourceGit_${APP_VERSION}_${RUNTIME}_singlefile.exe"
EXE_PATH="$OUTPUT_DIR/$EXE_NAME"

echo "🛠  SourceGit Windows 交叉编译"
echo "   Runtime : $RUNTIME"
echo "   Config  : $CONFIG"
echo "   Mode    : $MODE"
echo "   Output  : $OUTPUT_DIR/"
echo "   Version : $APP_VERSION"
echo

# ============ 工具函数 ============
human_size() {
  local bytes=$1
  awk -v b="$bytes" 'BEGIN {
    if (b >= 1073741824) printf "%.2f GB", b/1073741824
    else if (b >= 1048576) printf "%.1f MB", b/1048576
    else printf "%.0f KB", b/1024
  }'
}

dir_bytes() {
  du -sk "$1" 2>/dev/null | awk '{print $1*1024}'
}

file_bytes() {
  stat -f%z "$1" 2>/dev/null || stat -c%s "$1" 2>/dev/null || echo 0
}

safe_trash() {
  for p in "$@"; do
    [[ -e "$p" ]] && mavis-trash "$p" 2>/dev/null || true
  done
}

# ============ 步骤 1：清理旧产物 ============
echo "📦 步骤 1/3  清理旧产物"
safe_trash "$PUB_DIR" "$ZIP_PATH" "$EXE_PATH"
echo

# ============ 步骤 2：dotnet publish ============
echo "⚙️  步骤 2/3  dotnet publish ($RUNTIME, $CONFIG, $MODE)"

# 基础参数（multi 和 single 共用）
PUB_ARGS=(
  -c "$CONFIG"
  -r "$RUNTIME"
  -o "$PUB_DIR"
  --self-contained true
  -p:DisableAOT=true                # 跨平台 AOT 不支持，绕开
  -p:PublishTrimmed=true
  -p:TrimMode=link
)

# single-file 模式额外加几个
if [[ "$MODE" == "single" ]]; then
  PUB_ARGS+=(
    -p:PublishSingleFile=true
    -p:EnableCompressionInSingleFile=true
    -p:DebugType=embedded           # 调试符号塞进 .exe
    -p:IncludeNativeLibrariesForSelfExtract=true
  )
fi

dotnet publish "${PUB_ARGS[@]}" src/SourceGit.csproj 2>&1 | tail -10
echo

# ============ 步骤 3：打包 ============
echo "🪟 步骤 3/3  打包产物"

# 清掉 PDB（multi 模式产物里会有）
find "$PUB_DIR" -type f -name "*.pdb" -delete 2>/dev/null || true

if [[ "$MODE" == "single" ]]; then
  # single-file 模式：把 SourceGit.exe 拷贝到顶层、加上版本号
  cp "$PUB_DIR/SourceGit.exe" "$EXE_PATH"
  EXE_SIZE=$(file_bytes "$EXE_PATH")
  rm -rf "$PUB_DIR"
  echo "   $EXE_PATH  →  $(human_size $EXE_SIZE)"
  ART1_PATH="$EXE_PATH"; ART1_SIZE=$EXE_SIZE
  ART1_LABEL=".exe"
else
  # multi 模式：打成 zip（在 OUTPUT_DIR 内用相对路径，zip 行为更可控）
  (cd "$OUTPUT_DIR" && zip -r "$(basename "$ZIP_PATH")" "$(basename "$PUB_DIR")" > /dev/null 2>&1)
  ZIP_SIZE=$(file_bytes "$ZIP_PATH")
  PUB_SIZE=$(dir_bytes "$PUB_DIR")
  echo "   $PUB_DIR  →  $(human_size $PUB_SIZE)"
  echo "   $ZIP_PATH  →  $(human_size $ZIP_SIZE)"
  ART1_PATH="$PUB_DIR"; ART1_SIZE=$PUB_SIZE
  ART2_PATH="$ZIP_PATH"; ART2_SIZE=$ZIP_SIZE
fi
echo

# ============ 报告 ============
echo "✅  完成"
echo
echo "产物："
if [[ "$MODE" == "single" ]]; then
  printf "  %-6s  %-60s  %s\n" "$ART1_LABEL" "$ART1_PATH" "$(human_size $ART1_SIZE)"
else
  printf "  %-6s  %-60s  %s\n" "dir"   "$ART1_PATH" "$(human_size $ART1_SIZE)"
  printf "  %-6s  %-60s  %s\n" "zip"   "$ART2_PATH" "$(human_size $ART2_SIZE)"
fi
echo
echo "publish 目录 Top 5："
if [[ -d "$PUB_DIR" ]]; then
  TOP_DIR="$PUB_DIR"
elif [[ "$MODE" == "single" ]]; then
  # single 模式已经删了 PUB_DIR，重新构建只为显示（不重做 IL）
  echo "  (single-file 模式，目录已清理，仅产 .exe)"
  TOP_DIR=""
fi
if [[ -n "${TOP_DIR:-}" ]]; then
  find "$TOP_DIR" -type f -exec ls -l {} \; 2>/dev/null \
    | awk '{print $5, $NF}' | sort -rn | head -5 \
    | while read -r size path; do
        printf "  %8s  %s\n" "$(human_size $size)" "$path"
      done
fi
echo
echo "💡 提示：交叉编译出的 .exe 没法在 mac 上跑，丢到 Windows 机器（或者 VM/UTM）实测一下。"
echo "   single 模式首次启动会解压到 %LOCALAPPDATA%\\Temp\\.net，第二次起就快了。"
