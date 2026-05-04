#!/usr/bin/env sh
# CC-DESC: POSIX launcher for Context Control's cc command from the contextcontrol/ tool folder.
set -u

SCRIPT_NAME="cc.ps1"
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" 2>/dev/null && pwd -P)
if [ -z "$SCRIPT_DIR" ]; then
    SCRIPT_DIR=.
fi
TARGET="$SCRIPT_DIR/$SCRIPT_NAME"
PROJECT_ROOT="${CC_PROJECT_ROOT:-$(CDPATH= cd -- "$SCRIPT_DIR/.." 2>/dev/null && pwd -P)}"
DEFAULT_OUTPUT="$SCRIPT_DIR/cc_code_export.md"

if [ ! -f "$TARGET" ]; then
    echo "Context Control launcher error: $SCRIPT_NAME was not found next to $(basename -- "$0")." >&2
    exit 1
fi

if [ -z "$PROJECT_ROOT" ] || [ ! -d "$PROJECT_ROOT" ]; then
    echo "Context Control launcher error: project root was not found. Expected parent of: $SCRIPT_DIR" >&2
    echo "Set CC_PROJECT_ROOT to override." >&2
    exit 1
fi

HAS_OUTPUTFILE=0
for arg in "$@"; do
    case "$arg" in
        -OutputFile|-outputfile|--OutputFile|--outputfile|/OutputFile|/outputfile)
            HAS_OUTPUTFILE=1
            ;;
    esac
done

if [ "$HAS_OUTPUTFILE" -eq 0 ]; then
    set -- -OutputFile "$DEFAULT_OUTPUT" "$@"
fi

export CC_CONTEXTCONTROL_DIR="$SCRIPT_DIR"
export CC_CONTEXTCONTROL_PROJECT_ROOT="$PROJECT_ROOT"
cd "$PROJECT_ROOT" || exit 1

if command -v pwsh >/dev/null 2>&1; then
    exec pwsh -NoLogo -NoProfile -File "$TARGET" "$@"
fi

if command -v powershell >/dev/null 2>&1; then
    exec powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "$TARGET" "$@"
fi

cat >&2 <<'CCEOF'
Context Control requires PowerShell 7+ (`pwsh`).

macOS:
  Install PowerShell 7, then run this launcher again.
  Homebrew example: brew install --cask powershell
  Run: sh ./contextcontrol/ccStart

Windows:
  Use the matching .cmd launcher, for example: contextcontrol\ccStart.cmd
CCEOF
exit 127
