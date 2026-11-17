#!/usr/bin/env python3
"""junit-summary.py <junit.xml> — one markdown line per test case (PASS / FAIL + the first failure line), for a CI job's step summary (RT-8)."""
import sys
import xml.etree.ElementTree as ET

path = sys.argv[1] if len(sys.argv) > 1 else "out/junit.xml"
try:
    root = ET.parse(path).getroot()
except (OSError, ET.ParseError) as e:
    print(f"- no junit at {path}: {e}")
    sys.exit(0)
for case in root.iter("testcase"):
    fail = case.find("failure")
    line = f"- **{case.get('name')}**: {'FAIL' if fail is not None else 'PASS'}"
    if fail is not None and fail.get("message"):
        line += f" — {fail.get('message')}"
    print(line)
