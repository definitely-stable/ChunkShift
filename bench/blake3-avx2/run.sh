#!/usr/bin/env bash
# Compares upstream Blake3 3.0.2 (base) with the AVX2 d-row patch (patched) and
# Blake3.Native 3.0.2 (native reference) on the current machine.
# Usage: run.sh <work-dir> <results-file>
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
work="$1"
results="$2"
rm -rf "$work"
mkdir -p "$work"

git clone -q --depth 1 --branch 3.0.2 https://github.com/xoofx/Blake3.NET "$work/base"
cp -R "$work/base" "$work/patched"
git -C "$work/patched" apply "$here/blake3-avx2-drow.patch"

build() { # variant project
  cp -R "$here/Harness" "$work/h-$1"
  dotnet build "$work/h-$1/Harness.csproj" -c Release -nologo -v q -p:Blake3Project="$2" -o "$work/out-$1"
}
build base "$work/base/src/Blake3/Blake3.csproj"
build patched "$work/patched/src/Blake3/Blake3.csproj"
build native "$work/base/src/Blake3.Native/Blake3.Native.csproj"

echo "::group::upstream tests on the patched source"
dotnet test "$work/patched/src/Blake3.Tests/Blake3.Tests.csproj" -c Release -nologo 2>&1 | tail -5
echo "::endgroup::"

isa="$(dotnet "$work/out-base/Harness.dll" isa)"
echo "$isa"
modes=(default)
if [[ "$isa" == *"avx512f=True"* ]]; then
  modes+=(avx512-off)
fi

: > "$results"
for mode in "${modes[@]}"; do
  for round in 1 2 3; do
    for variant in base patched native; do
      if [[ "$mode" == avx512-off ]]; then
        DOTNET_EnableAVX512F=0 DOTNET_EnableAVX512=0 dotnet "$work/out-$variant/Harness.dll" "$variant" "$mode" | tee -a "$results"
      else
        dotnet "$work/out-$variant/Harness.dll" "$variant" "$mode" | tee -a "$results"
      fi
    done
  done
done
