#!/usr/bin/env bash
# 编译并打包「寝室扣分查看」ClassIsland 插件为 .cipx（Linux/macOS/CI 用）
# Windows 用户请优先使用 build-cipx.ps1
set -euo pipefail

CONFIGURATION="${1:-Release}"
OUTPUT_DIR="cipx"

cd "$(dirname "$0")"

ROOT="$(pwd)"
CSPROJ=$(find . -maxdepth 1 -name "*.csproj" | head -n 1)
if [ -z "$CSPROJ" ]; then
    echo "[失败] 未找到 .csproj 项目文件" >&2
    exit 1
fi

# 从 csproj 读取目标框架
TFM=$(grep -oPm1 '(?<=<TargetFramework>)[^<]+' "$CSPROJ" || true)
TFM="${TFM:-net10.0-windows}"
echo "项目: $CSPROJ    目标框架: $TFM"

echo "[1/3] 还原 NuGet 依赖..."
dotnet restore

echo "[2/3] 编译发布（$CONFIGURATION）..."
dotnet publish -c "$CONFIGURATION" -p:CreateCipx=true

mkdir -p "$OUTPUT_DIR"

EXISTING=$(find "$OUTPUT_DIR" -maxdepth 1 -name "*.cipx" -printf '%T@ %p\n' 2>/dev/null \
           | sort -rn | head -n 1 | cut -d' ' -f2- || true)

if [ -n "$EXISTING" ]; then
    echo "[完成] 插件包: $(pwd)/$EXISTING"
    exit 0
fi

echo "未检测到一键打包产物，改用手动打包..."

PUBLISH_DIR="bin/$CONFIGURATION/$TFM/publish"
if [ ! -d "$PUBLISH_DIR" ]; then
    PUBLISH_DIR=$(find "bin/$CONFIGURATION" -type d -name publish -print0 2>/dev/null \
                  | xargs -0 -r ls -dt 2>/dev/null | head -n 1 || true)
fi

if [ -z "$PUBLISH_DIR" ] || [ ! -d "$PUBLISH_DIR" ]; then
    echo "[失败] 未找到 publish 输出目录" >&2
    exit 1
fi

if [ ! -f "$PUBLISH_DIR/manifest.yml" ]; then
    echo "[失败] publish 输出目录中缺少 manifest.yml" >&2
    exit 1
fi

PKG="$OUTPUT_DIR/DormScoreViewer.cipx"
rm -f "$PKG"

# cipx 本质是 zip，且 manifest.yml 必须位于压缩包根目录
(cd "$PUBLISH_DIR" && zip -qr "$ROOT/$PKG" .)

echo "[完成] 插件包: $(pwd)/$PKG"
echo "在 ClassIsland 中打开【应用设置】->【插件】安装此包即可。"
