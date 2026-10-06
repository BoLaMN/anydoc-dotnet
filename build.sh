#!/usr/bin/env bash
# Build the native library for this machine, stage it where the .NET project
# packs and copies it from, then run the tests.
set -euo pipefail
cd "$(dirname "$0")"

case "$(uname -s)-$(uname -m)" in
  Darwin-arm64)  rid=osx-arm64;   lib=libanydoc_ffi.dylib ;;
  Darwin-x86_64) rid=osx-x64;     lib=libanydoc_ffi.dylib ;;
  Linux-aarch64) rid=linux-arm64; lib=libanydoc_ffi.so ;;
  Linux-x86_64)  rid=linux-x64;   lib=libanydoc_ffi.so ;;
  MINGW*-x86_64|MSYS*-x86_64) rid=win-x64; lib=anydoc_ffi.dll ;;
  *) echo "unsupported platform: $(uname -s)-$(uname -m)" >&2; exit 1 ;;
esac

cargo build --release --manifest-path native/Cargo.toml
mkdir -p "src/AnyDoc/runtimes/$rid/native"
cp "native/target/release/$lib" "src/AnyDoc/runtimes/$rid/native/"

if [[ "${1:-}" != "--no-test" ]]; then
  dotnet test tests/AnyDoc.Tests
fi
