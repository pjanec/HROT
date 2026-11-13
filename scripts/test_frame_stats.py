#!/usr/bin/env python3
"""CE-4505 (F-5) rail — the measuring scripts read the editor's [FrameStats] lines in ANY locale: a Czech Windows prints "8,9 ms", which the old
"[0-9.]+" read as 8 (the Windows run's UI numbers were truncated). Run: python3 scripts/test_frame_stats.py"""
import importlib.util
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("ats", os.path.join(HERE, "access-trace-session.py"))
ats = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ats)

text = "\n".join([
    "[FrameStats] 120 frames  mean 8.9 ms  p50 8.1  p95 12.3",      # an English locale
    "[FrameStats] 120 frames  mean 8,9 ms  p50 8,1  p95 12,3",      # a Czech one
    "[FrameStats]   per window (ms/frame, calls/frame): Map 3,2 (1)",   # a per-window line: not a frame line
    "[FrameStats] 60 frames  mean 146 ms  p50 140  p95 160",        # whole numbers
])
got = ats.frame_stats(text)
want = [(8.9, 12.3), (8.9, 12.3), (146.0, 160.0)]
if got != want:
    print(f"FAIL: {got} != {want}"); sys.exit(1)
print("ok: 3 frame lines, a decimal comma read as a point")
