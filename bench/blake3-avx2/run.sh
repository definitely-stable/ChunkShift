#!/usr/bin/env bash
# Compares upstream Blake3 master at the PR base commit (base) with the AVX2 d-row
# patch (patched) and Blake3.Native from the same commit (native reference) on the
# current machine. Base and patched are also measured on .NET 9 and .NET 8 with a
# shorter size set, and Tier-1 JIT listings of the 8-way kernels are recorded on
# every runtime.
# Usage: run.sh <work-dir> <results-file>
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
work="$1"
results="$2"
rm -rf "$work"
mkdir -p "$work"

base_ref=add258a0789ae4119745b81c12307cdd71f562f7 # xoofx/Blake3.NET master, the PR base
git init -q "$work/base"
git -C "$work/base" fetch -q --depth 1 https://github.com/xoofx/Blake3.NET "$base_ref"
git -C "$work/base" checkout -q FETCH_HEAD
cp -R "$work/base" "$work/patched"
git -C "$work/patched" apply "$here/blake3-avx2-drow.patch"

frameworks=(net10.0 net9.0 net8.0)
build() { # name project framework
  cp -R "$here/Harness" "$work/h-$1"
  # A -p:TargetFramework override does not reach the implicit restore; edit the copy instead.
  sed -i.bak "s#<TargetFramework>net10.0</TargetFramework>#<TargetFramework>$3</TargetFramework>#" "$work/h-$1/Harness.csproj"
  dotnet build "$work/h-$1/Harness.csproj" -c Release -nologo -v q -p:Blake3Project="$2" -o "$work/out-$1"
}
for tfm in "${frameworks[@]}"; do
  build "base-$tfm" "$work/base/src/Blake3/Blake3.csproj" "$tfm"
  build "patched-$tfm" "$work/patched/src/Blake3/Blake3.csproj" "$tfm"
done
build native-net10.0 "$work/base/src/Blake3.Native/Blake3.Native.csproj" net10.0
# A project reference does not list the native asset in deps.json; place it next to the app.
rid="$(dotnet --info | sed -n 's/^[[:space:]]*RID:[[:space:]]*//p' | head -1)"
cp "$work/base/src/Blake3.Native/runtimes/$rid/native/"* "$work/out-native-net10.0/"

isa="$(dotnet "$work/out-base-net10.0/Harness.dll" isa)"
echo "$isa"
modes=(default)
if [[ "$isa" == *"avx512f=True"* ]]; then
  modes+=(avx512-off)
fi

: > "$results"
tests="$work/patched/src/Blake3.Tests/Blake3.Tests.csproj"
dotnet build "$tests" -c Release -nologo -v q
run_tests() { # label
  local log="$work/tests-$1.txt"
  dotnet test "$tests" -c Release --no-build -nologo > "$log" 2>&1 || { cat "$log"; return 1; }
  echo "TESTS $1 $(grep 'Blake3.Tests.dll (' "$log" | tail -1 | tr -s ' ')" | tee -a "$results"
}
echo "::group::upstream tests on the patched source"
run_tests default
DOTNET_TieredCompilation=0 run_tests tiered-compilation-off
if [[ "$isa" == *"avx512f=True"* ]]; then
  # AVX-512 hardware runs Compress8; also cover the AVX2 kernel there.
  DOTNET_EnableAVX512F=0 DOTNET_EnableAVX512=0 run_tests avx512-off
  DOTNET_EnableAVX512F=0 DOTNET_EnableAVX512=0 DOTNET_TieredCompilation=0 run_tests avx512-off-tiered-compilation-off
fi
echo "::endgroup::"

harness() { # mode build-name harness-args...
  local mode="$1" name="$2"
  shift 2
  if [[ "$mode" == avx512-off ]]; then
    DOTNET_EnableAVX512F=0 DOTNET_EnableAVX512=0 dotnet "$work/out-$name/Harness.dll" "$@"
  else
    dotnet "$work/out-$name/Harness.dll" "$@"
  fi
}
label() { # mode framework
  if [[ "$2" == net10.0 ]]; then echo "$1"; else echo "$1@$2"; fi
}

py=python3
"$py" -c "" 2> /dev/null || py=python # skips a python3 stub that cannot run
echo "::group::Tier-1 disassembly"
for tfm in "${frameworks[@]}"; do
  for mode in "${modes[@]}"; do
    tag="$(label "$mode" "$tfm")"
    for variant in base patched; do
      DOTNET_JitDisasm="Compress8 Compress8Avx2" DOTNET_JitStdOutFile="$work/jit-$variant-$tag.txt" \
        harness "$mode" "$variant-$tfm" "$variant" "disasm-$tag" > /dev/null
    done
    "$py" "$here/disasm.py" "$tag" "$work/jit-base-$tag.txt" "$work/jit-patched-$tag.txt" | tee -a "$results"
  done
done
echo "::endgroup::"

for mode in "${modes[@]}"; do
  for round in 1 2 3; do
    for variant in base patched native; do
      harness "$mode" "$variant-net10.0" "$variant" "$mode" | tee -a "$results"
    done
  done
done

for tfm in net9.0 net8.0; do
  for mode in "${modes[@]}"; do
    for round in 1 2 3; do
      for variant in base patched; do
        harness "$mode" "$variant-$tfm" "$variant" "$(label "$mode" "$tfm")" short | tee -a "$results"
      done
    done
  done
done
