#!/usr/bin/env bash
set -euo pipefail

RID="${1:-}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CSPROJ_DIR="$REPO_ROOT/LiteAPI"
RUST_DIR="$CSPROJ_DIR/liteapi_rust"

if [[ -z "$RID" ]]; then
  OS="$(uname -s)"
  ARCH="$(uname -m)"
  if [[ "$OS" == "Linux" ]]; then
    RID="linux-x64"
  elif [[ "$OS" == "Darwin" ]]; then
    RID="osx-${ARCH/arm64/arm64}"
    if [[ "$ARCH" == "arm64" ]]; then RID="osx-arm64"; else RID="osx-x64"; fi
  else
    echo "Unsupported OS: $OS" >&2
    exit 1
  fi
fi

case "$RID" in
  linux-x64) TARGET="x86_64-unknown-linux-gnu"; OUT="libliteapi_rust.so";;
  linux-arm64) TARGET="aarch64-unknown-linux-gnu"; OUT="libliteapi_rust.so";;
  osx-x64) TARGET="x86_64-apple-darwin"; OUT="libliteapi_rust.dylib";;
  osx-arm64) TARGET="aarch64-apple-darwin"; OUT="libliteapi_rust.dylib";;
  *) echo "Unsupported RID for this script: $RID" >&2; exit 1;;
 esac

echo "Building Rust native for RID=$RID (target=$TARGET)"
( cd "$RUST_DIR" && rustup target add "$TARGET" && cargo build --release --target "$TARGET" )

OUT_DIR="$CSPROJ_DIR/runtimes/$RID/native"
mkdir -p "$OUT_DIR"
cp "$RUST_DIR/target/$TARGET/release/$OUT" "$OUT_DIR/$OUT"

echo "Copied native to $OUT_DIR"
