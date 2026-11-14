#!/usr/bin/env sh
set -eu

repo_root="$1"
build_dir="$2"
mkdir -p "$build_dir"
quickjs_dir="$build_dir/quickjs-ng-src"

if [ ! -f "$quickjs_dir/quickjs.h" ] || [ "$(cat "$quickjs_dir/.loot-commit" 2>/dev/null || true)" != "d7215b0" ]; then
  archive="$build_dir/quickjs-ng-d7215b0.tar.gz"
  rm -rf "$quickjs_dir"
  mkdir -p "$quickjs_dir"
  curl --fail --location --retry 3 --silent --show-error \
    https://codeload.github.com/quickjs-ng/quickjs/tar.gz/d7215b0 -o "$archive"
  if command -v shasum >/dev/null 2>&1; then
    archive_hash=$(shasum -a 256 "$archive" | awk '{print $1}')
  else
    archive_hash=$(sha256sum "$archive" | awk '{print $1}')
  fi
  if [ "$archive_hash" != "7ce3155176baf5e47abc863073cdeab38f52676a0ebd4141cdf66ad9c4ca54e5" ]; then
    echo "QuickJS-NG source archive hash mismatch" >&2
    exit 1
  fi
  tar -xzf "$archive" --strip-components=1 -C "$quickjs_dir"
  rm -f "$archive"
  printf '%s\n' d7215b0 > "$quickjs_dir/.loot-commit"
fi

void_return_count=$(grep -Ec 'return js_free_cstring\((ctx->rt|rt), ptr\);' "$quickjs_dir/quickjs.c" || true)
if [ "$void_return_count" -gt 0 ]; then
  if [ "$void_return_count" -ne 4 ]; then
    echo "Expected four upstream JS_FreeCString void-return warnings, found $void_return_count" >&2
    exit 1
  fi
  sed -E 's/return js_free_cstring\((ctx->rt|rt), ptr\);/js_free_cstring(\1, ptr);/' \
    "$quickjs_dir/quickjs.c" > "$quickjs_dir/quickjs.c.patched"
  mv "$quickjs_dir/quickjs.c.patched" "$quickjs_dir/quickjs.c"
fi

if [ "$(uname -s)" = "Darwin" ]; then
  library="$build_dir/libloot_qjs.dylib"
  [ -f "$library" ] && [ "$library" -nt "$repo_root/native/quickjs/loot_qjs.c" ] && [ "$library" -nt "$repo_root/native/quickjs/build-native.sh" ] && [ "$library" -nt "$quickjs_dir/quickjs.c" ] && exit 0
  clang -dynamiclib -std=c11 -D_GNU_SOURCE -I"$quickjs_dir" \
    "$repo_root/native/quickjs/loot_qjs.c" \
    "$quickjs_dir/dtoa.c" "$quickjs_dir/libregexp.c" "$quickjs_dir/libunicode.c" "$quickjs_dir/quickjs.c" \
    -pthread -lm -Wl,-install_name,@rpath/libloot_qjs.dylib -o "$library"
else
  library="$build_dir/libloot_qjs.so"
  [ -f "$library" ] && [ "$library" -nt "$repo_root/native/quickjs/loot_qjs.c" ] && [ "$library" -nt "$repo_root/native/quickjs/build-native.sh" ] && [ "$library" -nt "$quickjs_dir/quickjs.c" ] && exit 0
  cc -shared -fPIC -std=c11 -D_GNU_SOURCE -I"$quickjs_dir" \
    "$repo_root/native/quickjs/loot_qjs.c" \
    "$quickjs_dir/dtoa.c" "$quickjs_dir/libregexp.c" "$quickjs_dir/libunicode.c" "$quickjs_dir/quickjs.c" \
    -pthread -ldl -lm -o "$library"
fi

cp "$quickjs_dir/LICENSE" "$build_dir/QuickJS-NG-LICENSE.txt"
