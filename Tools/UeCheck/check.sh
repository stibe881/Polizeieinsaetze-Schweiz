#!/bin/sh
# Syntax/type check of Unreal/SwissPoliceSim against a hand-written stub of the engine API (not the real engine!).
cd "$(dirname "$0")"
SRC=../../Unreal/SwissPoliceSim/Source/SwissPoliceSim
rc=0
for f in $SRC/*.cpp; do
  clang++ -std=c++17 -fsyntax-only -Wno-everything -fms-extensions -I stubs -I $SRC "$f" 2>&1 | grep -E "error" && rc=1
done
[ $rc = 0 ] && echo "stub check passed"
exit $rc
