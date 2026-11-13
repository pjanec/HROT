#!/usr/bin/env python3
"""R-4221 (F-5, CE-4505) rail — NO OUTPUT IS EVER CULTURE-FORMATTED: every non-test HROT executable runs culture-invariant.

1. every project with OutputType Exe/WinExe that is not a test project RESOLVES InvariantGlobalization=true (an MSBuild evaluation — set once in
   Directory.Build.targets), and no test project does (a test may set a culture on purpose);
2. with --live <ClusterRunner.dll>: the editor started under a Czech locale prints its [FrameStats] mean with a decimal POINT.

Run: python3 scripts/test_invariant_culture.py [--live Hrot/Runner/Hrot.ClusterRunner/bin/Debug/net8.0/Hrot.ClusterRunner.dll]"""
import argparse
import glob
import json
import os
import re
import subprocess
import sys
import time

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def evaluate(proj):
    r = subprocess.run(["dotnet", "msbuild", proj, "-getProperty:OutputType", "-getProperty:IsTestProject",
                        "-getProperty:InvariantGlobalization"], capture_output=True, text=True, cwd=REPO)
    try:
        return json.loads(r.stdout[r.stdout.index("{"):])["Properties"]
    except (ValueError, KeyError):
        return None


def projects():
    for p in glob.glob(os.path.join(REPO, "**", "*.csproj"), recursive=True):
        if "/obj/" in p or "/bin/" in p or "node_modules" in p:
            continue
        if os.path.basename(p).endswith(".Tests.csproj"):
            yield p   # a test project: an executable too (its own runtimeconfig), whose OutputType the test SDK sets
            continue
        with open(p, errors="ignore") as fh:
            if re.search(r"<OutputType>\s*(Exe|WinExe)\s*</OutputType>", fh.read()):
                yield p


def live(dll):
    env = dict(os.environ, LANG="cs_CZ.UTF-8", LC_ALL="cs_CZ.UTF-8", HROT_FRAME_STATS="1", HROT_DEBUG_API_PORT="8140",
               DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="")   # the runtimeconfig must decide, not the environment
    log = os.path.join(REPO, ".invariant-culture-live.log")
    with open(log, "w") as fh:
        proc = subprocess.Popen(["xvfb-run", "-a", "dotnet", dll, "--mode", "editor"], env=env, stdout=fh, stderr=subprocess.STDOUT, cwd=REPO)
        try:
            for _ in range(90):
                time.sleep(2)
                with open(log, errors="ignore") as r:
                    m = re.search(r"\[FrameStats\] \d+ frames\s+mean (\S+) ms", r.read())
                if m:
                    return m.group(1)
            return None
        finally:
            proc.terminate()
            try: proc.wait(20)
            except subprocess.TimeoutExpired: proc.kill()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--live", help="a ClusterRunner.dll to start under a Czech locale")
    a = ap.parse_args()
    bad = []
    n_exe = n_test = 0
    for p in sorted(projects()):
        props = evaluate(p)
        rel = os.path.relpath(p, REPO)
        if props is None:
            bad.append(f"{rel}: MSBuild could not evaluate it"); continue
        is_test = props.get("IsTestProject") == "true" or os.path.basename(p).endswith(".Tests.csproj")
        inv = props.get("InvariantGlobalization") == "true"
        if is_test:
            n_test += 1
            if inv: bad.append(f"{rel}: a TEST project runs invariant (a culture rail could not set its culture)")
        else:
            n_exe += 1
            if not inv: bad.append(f"{rel}: an executable NOT culture-invariant")
    if a.live:
        mean = live(a.live)
        if mean is None: bad.append("live: no [FrameStats] line within 3 minutes")
        elif "," in mean or not re.fullmatch(r"[0-9]+(\.[0-9]+)?", mean): bad.append(f"live: under cs-CZ the editor printed mean {mean!r}")
        else: print(f"live: under cs-CZ the editor printed mean {mean} ms (a decimal point)")
    if bad:
        print("FAIL:\n  " + "\n  ".join(bad)); sys.exit(1)
    print(f"ok: {n_exe} executables culture-invariant, {n_test} test executables not")


if __name__ == "__main__":
    main()
