#!/usr/bin/env bash
# build-mac.sh — 一键构建 SourceGit macOS 优化版 .app 和 DMG
#
# 用法：
#   ./build-mac.sh                                 # 默认 osx-arm64, Release, 输出到 build/
#   ./build-mac.sh osx-x64                         # 显式指定 RID
#   RUNTIME=osx-x64 OUTPUT_DIR=dist ./build-mac.sh # 全量覆盖
#   VERSION=2026.16 ./build-mac.sh                 # 覆盖版本号
#
# 体积优化（T1+T2）：
#   1) DebugType=none + StripSymbols=true            → 不生成 dSYM / pdb
#   2) IlcOptimizationPreference=Size                → AOT 体积优先
#   3) IlcGenerateStackTraceData=false               → 砍 stack trace 元数据
#   4) IlcGenerateEmptyDllImportThunks=false         → 砍空 P/Invoke thunk
#   5) IlcInvokeDataCount=0                          → 砍 invoke 表
#
# 已知禁用的优化（会让 .app 启动失败）：
#   - InvariantGlobalization=true                   → SourceGit 启动期就需要 ICU 字符串处理，
#                                                     开启后进程秒退。已实测排除。
#
# 相对原 package.osx-app.sh 的修复：
#   - 真正删除 dSYM（之前是 SourceGit.dsym 拼错，根本没删到）
#   - 真正删除全部 *.pdb（之前 glob 在 MacOS/ 下没匹到任何文件，shell 行为依赖设置）
#   - 一并打包成 DMG（原脚本只产 .app + zip）

set -euo pipefail

# ============ 参数 ============
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# 第一个位置参数可作 RUNTIME 简写
RUNTIME="${1:-${RUNTIME:-osx-arm64}}"
OUTPUT_DIR="${OUTPUT_DIR:-build}"
CONFIG="${CONFIG:-Release}"

# 读版本号
if [[ -n "${VERSION:-}" ]]; then
  APP_VERSION="$VERSION"
elif [[ -f VERSION ]]; then
  APP_VERSION="$(tr -d '[:space:]' < VERSION)"
else
  echo "❌ 无法确定版本号，请设置 VERSION 环境变量或确保 VERSION 文件存在" >&2
  exit 1
fi

echo "🛠  SourceGit 优化构建"
echo "   Runtime : $RUNTIME"
echo "   Config  : $CONFIG"
echo "   Output  : $OUTPUT_DIR/"
echo "   Version : $APP_VERSION"
echo

# ============ 路径 ============
PUBLISH_DIR="$OUTPUT_DIR/SourceGit"
APP_DIR="$OUTPUT_DIR/SourceGit.app"
DMG_NAME="SourceGit_${APP_VERSION}_${RUNTIME}.dmg"
DMG_PATH="$OUTPUT_DIR/$DMG_NAME"
STAGING_DIR="/tmp/sourcegit_dmg_staging_${APP_VERSION}_$$"

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

safe_trash() {
  for p in "$@"; do
    [[ -e "$p" ]] && mavis-trash "$p" 2>/dev/null || true
  done
}

cleanup_staging() { safe_trash "$STAGING_DIR"; }
trap cleanup_staging EXIT

# ============ 步骤 1：清理旧产物 ============
echo "📦 步骤 1/4  清理旧产物"
safe_trash "$PUBLISH_DIR" "$APP_DIR" "$DMG_PATH"
echo

# ============ 步骤 2：dotnet publish ============
echo "⚙️  步骤 2/4  dotnet publish ($RUNTIME, $CONFIG) with size optimizations"

# AOT 优化参数(仅 PublishAot=true 时生效)
AOT_FLAGS=(
  -p:DebugType=none
  -p:StripSymbols=true
  -p:IlcOptimizationPreference=Size
  -p:IlcGenerateStackTraceData=false
  -p:IlcGenerateEmptyDllImportThunks=false
  -p:IlcInvokeDataCount=0
)

# DisableAOT=true 时退化为普通 publish,绕开 Avalonia 11.3.x + macOS 26 + NativeAOT
# 的 Dispatcher.MainLoop "Dispatcher shut down" 启动期 bug。
# 强绑 self-contained,否则 .app 启动时 LaunchServices 找不到系统 .NET runtime。
if [[ "${DisableAOT:-false}" == "true" ]]; then
  echo "   ⚠️  DisableAOT=true — 跳过 AOT,改用普通 self-contained publish(产物 ~200MB,自带 .NET 10 runtime)"
  AOT_FLAGS=(
    -p:DisableAOT=true
    --self-contained true
    -p:PublishSingleFile=false
  )
fi

dotnet publish \
  -c "$CONFIG" \
  -r "$RUNTIME" \
  -o "$PUBLISH_DIR" \
  "${AOT_FLAGS[@]}" \
  src/SourceGit.csproj 2>&1 | tail -10
echo

# ============ 步骤 3：打包 .app ============
echo "🍎 步骤 3/4  打包 SourceGit.app"
RES="$OUTPUT_DIR/resources/app"
[[ -d "$RES" ]] || { echo "❌ 找不到资源目录 $RES" >&2; exit 1; }

mkdir -p "$APP_DIR/Contents/Resources"
mv "$PUBLISH_DIR" "$APP_DIR/Contents/MacOS"
cp "$RES/App.icns" "$APP_DIR/Contents/Resources/App.icns"
sed "s/SOURCE_GIT_VERSION/${APP_VERSION}/g" "$RES/App.plist" > "$APP_DIR/Contents/Info.plist"

# 真正清理 dSYM 和 PDB（用 find 保证匹配到任何嵌套位置）
find "$APP_DIR" -type d -name "*.dSYM" -prune -exec mavis-trash {} + 2>/dev/null || true
find "$APP_DIR" -type f -name "*.pdb" -delete 2>/dev/null || true

APP_SIZE=$(dir_bytes "$APP_DIR")
echo "   $APP_DIR  →  $(human_size $APP_SIZE)"
echo

# ============ 步骤 4：生成 DMG ============
echo "💿 步骤 4/4  生成 DMG"
safe_trash "$STAGING_DIR"
mkdir -p "$STAGING_DIR"
cp -R "$APP_DIR" "$STAGING_DIR/"
ln -s /Applications "$STAGING_DIR/Applications"

hdiutil create -volname "SourceGit $APP_VERSION" \
  -srcfolder "$STAGING_DIR" \
  -ov -format UDZO \
  "$DMG_PATH" 2>&1 | tail -3

DMG_SIZE=$(dir_bytes "$DMG_PATH")
echo "   $DMG_PATH  →  $(human_size $DMG_SIZE)"
echo

# ============ 报告 ============
echo "✅  完成"
echo
echo "产物："
printf "  .app  %-60s  %s\n" "$APP_DIR"   "$(human_size $APP_SIZE)"
printf "  .dmg  %-60s  %s\n" "$DMG_PATH" "$(human_size $DMG_SIZE)"
echo
echo ".app 内 Top 5："
find "$APP_DIR" -type f -exec ls -l {} \; 2>/dev/null \
  | awk '{print $5, $NF}' | sort -rn | head -5 \
  | while read -r size path; do
      printf "  %8s  %s\n" "$(human_size $size)" "$path"
    done
echo
echo "💡 提示：第一次跑本脚本后，建议在真机上完整测一遍主要功能（clone/commit/diff/AI/图片预览/SSH push），"
echo "   确认各项 NativeAOT 体积优化没有引入回归再合到主干。"
