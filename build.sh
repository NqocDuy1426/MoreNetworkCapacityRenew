#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

GAME_DIR="$HOME/Library/Application Support/Steam/steamapps/common/Cities_Skylines/Cities.app/Contents/Resources/Data/Managed"
HARMONY_DLL="$HOME/Library/Application Support/Colossal Order/Cities_Skylines/Addons/Mods/1680642819/0Harmony.dll"
ORIGINAL_MNC="$HOME/Library/Application Support/Steam/steamapps/workshop/content/255710/3814019901/MoreNetworkCapacity.dll"
CECIL_DLL="/Library/Frameworks/Mono.framework/Versions/6.12.0/lib/mono/gac/Mono.Cecil/0.11.1.0__0738eb9f132ed756/Mono.Cecil.dll"

MOD_NAME="MoreNetworkCapacityRenew"
MOD_DIR="$HOME/Library/Application Support/Colossal Order/Cities_Skylines/Addons/Mods/$MOD_NAME"

echo "=========================================================="
echo "🚀 Building $MOD_NAME (MoreNetworkCapacity Renew by NgocDuy)..."
echo "=========================================================="

# 0. Compile and run test suite
echo "0️⃣ Running Test Suite before build..."
mcs -r:"$CECIL_DLL" run_tests.cs -out:run_tests.exe
mono run_tests.exe
echo "✅ Test Suite passed 100%."

# 1. Use clean, stable core engine (MoreNetworkCapacity.Core.dll v1.0.19 from stable depot)
echo "1️⃣ Using stable core engine (MoreNetworkCapacity.Core.dll v1.0.19)..."
cp MoreNetworkCapacity.Core.v1019.dll MoreNetworkCapacity.Core.dll

# 2. Compile Renew.cs into MoreNetworkCapacityRenew.dll
echo "2️⃣ Compiling MoreNetworkCapacityRenew.dll..."
mcs -sdk:2 -target:library \
    -r:"$GAME_DIR/Assembly-CSharp.dll" \
    -r:"$GAME_DIR/ICities.dll" \
    -r:"$GAME_DIR/ColossalManaged.dll" \
    -r:"$GAME_DIR/UnityEngine.dll" \
    -r:"$HARMONY_DLL" \
    -resource:MoreNetworkCapacity.Core.dll \
    -out:MoreNetworkCapacityRenew.dll \
    Renew.cs

echo "✅ Compiled MoreNetworkCapacityRenew.dll successfully."

# 3. Deploy to Cities: Skylines Mods folder
echo "3️⃣ Deploying to Cities: Skylines Addons/Mods/$MOD_NAME..."
mkdir -p "$MOD_DIR"
cp MoreNetworkCapacityRenew.dll "$MOD_DIR/"
cp MoreNetworkCapacity.Core.dll "$MOD_DIR/"
[ -f preview-image.png ] && cp preview-image.png "$MOD_DIR/"

WORKSHOP_DIR="$HOME/Library/Application Support/Steam/steamapps/workshop/content/255710/3818403598"
if [ -d "$WORKSHOP_DIR" ]; then
    echo "📦 Deploying to Steam Workshop staging ($WORKSHOP_DIR)..."
    cp MoreNetworkCapacityRenew.dll "$WORKSHOP_DIR/"
    cp MoreNetworkCapacity.Core.dll "$WORKSHOP_DIR/"
    [ -f preview-image.png ] && cp preview-image.png "$WORKSHOP_DIR/PreviewImage.png"
fi

echo "📦 Deploying to SteamCMD content staging ($SCRIPT_DIR/content)..."
mkdir -p "$SCRIPT_DIR/content"
cp MoreNetworkCapacityRenew.dll "$SCRIPT_DIR/content/"
cp MoreNetworkCapacity.Core.dll "$SCRIPT_DIR/content/"

echo "=========================================================="
echo "🎉 DEPLOYMENT COMPLETE!"
echo "Files in $MOD_DIR:"
ls -lh "$MOD_DIR"
echo "=========================================================="
