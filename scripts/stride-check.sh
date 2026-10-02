#!/usr/bin/env bash
# Compile every Stride project that CAN be compiled off Windows.
#
#   bash scripts/stride-check.sh
#
# ⭐⭐⭐ WHY THIS EXISTS, and it is not a nicety. Stride is out of IOS-IG-SimHost.sln, so nothing in
# the ordinary gate table compiles it — a change to Hrot.Editor / Hrot.Core / Hrot.Common can break
# the Stride host and every green gate stays green. 📐 Measured 2026-09-05: CE-203 widened
# EditorSubsystem.TkbDatabase to ITkbDatabase, the whole solution built, ~4700 tests ran, and
# EditorStrideSubsystem.cs:996 was BROKEN the entire time. This script found it in 90 seconds.
#
# ⛔⛔ THE BOUNDARY, MEASURED — do not re-derive it, and do not claim more than this:
#
#   restore + COMPILE every library / game / test project   ✅ works, with EnableWindowsTargeting=true
#   build HrotStrideApp.Windows (the launcher)              ⛔ Stride's asset compiler wants
#                                                              Direct3D11/Windows (MSB3073, exit 150)
#   RUN any Stride test                                     ⛔ needs the Microsoft.WindowsDesktop.App
#                                                              RUNTIME, which does not exist on Linux
#   run the app                                             ⛔ Windows + GPU
#
# ⇒ ⭐ off Windows this is a COMPILE gate. It catches signature/type drift — the whole class of
#   breakage a composition refactor produces — and it catches NOTHING about behaviour. Say so.
#
# EnableWindowsTargeting=true is passed on the command line rather than written into the csproj files
# on purpose: it is a property of THIS MACHINE, not of the projects.
set -uo pipefail
cd "$(dirname "$0")/.."

PROJECTS=(
  Stride/Hrot.Stride.Core/Hrot.Stride.Core.csproj
  Stride/Hrot.Stride.Animation/Hrot.Stride.Animation.csproj
  Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj
  Stride/Hrot.Stride.Core.Tests/Hrot.Stride.Core.Tests.csproj
  Stride/Hrot.Stride.Animation.Tests/Hrot.Stride.Animation.Tests.csproj
  Stride/HrotStrideApp.Game.Tests/HrotStrideApp.Game.Tests.csproj
)

FAILED=0
for p in "${PROJECTS[@]}"; do
  printf '%-56s ' "$(basename "$p")"
  out=$(dotnet build "$p" -p:EnableWindowsTargeting=true --nologo -v q 2>&1)
  if grep -q ' error ' <<<"$out"; then
    echo "FAILED"
    grep ' error ' <<<"$out" | sort -u | head -8 | sed 's/^/    /'
    FAILED=1
  else
    echo "ok"
  fi
done

echo
if [ "$FAILED" -eq 0 ]; then
  echo "STRIDE COMPILES. ⚠ Compile only — no Stride test can RUN off Windows (WindowsDesktop runtime),"
  echo "and HrotStrideApp.Windows cannot build here (Stride asset compiler needs Direct3D11)."
else
  echo "STRIDE IS BROKEN — see the errors above."
fi
exit "$FAILED"
