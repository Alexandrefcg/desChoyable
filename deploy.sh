#!/bin/bash
# Build and deploy DestroyChecker Blish HUD module
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly BLISH_MODULES_DIR="${BLISH_MODULES_DIR:-$HOME/.local/share/Guild Wars 2/addons/blishhud/modules}"
readonly BHM_FILE="$SCRIPT_DIR/src/DestroyChecker.BlishModule/bin/Debug/net48/DestroyChecker.BlishModule.bhm"

echo "=== Destroy Checker — Build & Deploy ==="
echo ""

# Build
echo "Building solution..."
dotnet build "$SCRIPT_DIR/DestroyChecker.sln" --verbosity quiet
echo "Build OK"
echo ""

# Check .bhm was generated
if [[ ! -f "$BHM_FILE" ]]; then
    echo "ERROR: .bhm file not found at: $BHM_FILE" >&2
    exit 1
fi

# Check modules folder exists
if [[ ! -d "$BLISH_MODULES_DIR" ]]; then
    echo "ERROR: Blish HUD modules folder not found at:" >&2
    echo "  $BLISH_MODULES_DIR" >&2
    echo "Set BLISH_MODULES_DIR to your Blish HUD modules folder." >&2
    exit 1
fi

# Deploy
cp "$BHM_FILE" "$BLISH_MODULES_DIR/"
echo "Deployed to: $BLISH_MODULES_DIR/DestroyChecker.BlishModule.bhm"
echo ""
echo "Done! Start GW2 via Steam to load the module."
