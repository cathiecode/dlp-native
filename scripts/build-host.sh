#!/usr/bin/env bash
# Build the native library for the host platform (Linux / macOS).
# Outputs the shared library and copies it to unity_package/Plugins/x86_64/.
#
# Requirements:
#   - Rust toolchain (see rust-toolchain.toml)
#   - uv with Python 3.12 installed  (`uv python install 3.12`)
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

# ── Locate Python 3.12 via uv ─────────────────────────────────────────────────
echo "==> Locating Python 3.12 via uv..."
PY_EXE="$(uv python find 3.12)"
if [[ -z "$PY_EXE" ]]; then
  echo "ERROR: Python 3.12 not found via uv. Run: uv python install 3.12" >&2
  exit 1
fi
echo "    Python: $PY_EXE"
export PYO3_PYTHON="$PY_EXE"

# ── Build ─────────────────────────────────────────────────────────────────────
echo "==> Building unity_dlp_core (release, host target)..."
# V8's prebuilt uses local-exec TLS (R_X86_64_TPOFF32) which is incompatible
# with cdylib on Linux regardless of linker. Use QuickJS (bundled C source) instead.
# On Linux: bake $ORIGIN into RPATH so libpython can be found next to the plugin.
case "$(uname -s)" in
  Linux*)
    JS_FEATURES="--no-default-features --features js-quickjs"
    export RUSTFLAGS="-C link-arg=-Wl,-rpath,\$ORIGIN"
    ;;
  *) JS_FEATURES="" ;;
esac
cargo build -p unity_dlp_core --release $JS_FEATURES

# ── Stage to Unity Plugins ────────────────────────────────────────────────────
case "$(uname -s)" in
  Linux*)  LIB="target/release/libunity_dlp.so" ;;
  Darwin*) LIB="target/release/libunity_dlp.dylib" ;;
  MINGW*|MSYS*|CYGWIN*) LIB="target/release/unity_dlp.dll" ;;
  *)
    echo "ERROR: Unknown OS '$(uname -s)'" >&2
    exit 1
    ;;
esac

DEST="unity_package/Plugins/x86_64"
mkdir -p "$DEST"
cp "$LIB" "$DEST/"
echo "==> Staged: $LIB → $DEST/"

# ── Bundle libpython alongside the plugin (Linux only) ────────────────────────
# Unity loads the plugin from its Plugins dir; $ORIGIN RPATH makes ld.so look
# there for libpython. We copy the exact file the plugin's DT_NEEDED records.
if [[ "$(uname -s)" == "Linux" ]]; then
  PYLIB_DIR="$("$PY_EXE" -c "import sysconfig; print(sysconfig.get_config_var('LIBDIR'))")"
  PYLIB_NEEDED="$(objdump -p "$DEST/libunity_dlp.so" \
      | awk '/NEEDED/ && /python/ { print $2 }')"
  if [[ -n "$PYLIB_NEEDED" && -e "$PYLIB_DIR/$PYLIB_NEEDED" ]]; then
    cp -L "$PYLIB_DIR/$PYLIB_NEEDED" "$DEST/$PYLIB_NEEDED"
    echo "==> Bundled: $PYLIB_NEEDED → $DEST/"
  else
    echo "WARNING: could not bundle libpython ($PYLIB_NEEDED not found in $PYLIB_DIR)" >&2
  fi
fi

echo ""
echo "Build complete."
