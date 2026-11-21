#!/usr/bin/env python3
"""CE-4295 (R-4159) - behaviour / task / trigger params written as the JSON object they hold, not as an escaped string.

The C# side is `[JsonText]` + `JsonTextConverter` (FDP/Engine/Fdp.Core/Serialization/Converters/JsonTextConverter.cs): a string
that is a valid JSON object or array is written VERBATIM as that value; anything else (empty, a scalar such as a trigger's "40",
text that is not JSON) stays a string. This module applies the SAME rule to a file's text, so:

  * `python3 scripts/scenario_json_readable.py [--check] [paths...]` rewrites the scenario + recipe files once (default roots:
    scenarios/ and recipes/scenarios/), touching only the params string tokens - every other byte is kept;
  * the scenario generators call `readable(text)` on what they write, so a regenerated file is readable too.

Docs: docs/DESIGN_Readable_Scenario_Json.md section 2.
"""
import json, os, re, sys

# the [JsonText] properties as they appear in a file: SavedBrainSlot.Params (Behavior / Sop), DomainMissionTask.BehaviorParams
# (camelCase under HrotJsonOptions), DomainMissionTrigger.Params (camelCase "params")
KEYS = ("Params", "params", "behaviorParams", "BehaviorParams")
_TOKEN = re.compile(r'"(' + "|".join(KEYS) + r')"(\s*):(\s*)"((?:[^"\\]|\\.)*)"')


def structured(text):
    """Is `text` a valid JSON object or array (the converter's IsStructured)?"""
    t = text.lstrip()
    if not t or t[0] not in "{[":
        return False
    try:
        json.loads(text)
        return True
    except ValueError:
        return False


def readable(text):
    """`text` (a JSON document) with every params string token that holds an object / array replaced by that value, verbatim."""
    def sub(m):
        inner = json.loads('"' + m.group(4) + '"')
        if not structured(inner):
            return m.group(0)
        return '"' + m.group(1) + '"' + m.group(2) + ":" + m.group(3) + inner
    return _TOKEN.sub(sub, text)


def files(roots):
    for r in roots:
        if os.path.isfile(r):
            yield r
            continue
        for d, _, fs in os.walk(r):
            for f in sorted(fs):
                if f.endswith(".json"):
                    yield os.path.join(d, f)


def main(argv):
    check = "--check" in argv
    roots = [a for a in argv if not a.startswith("--")] or ["scenarios", os.path.join("recipes", "scenarios")]
    changed = 0
    for path in sorted(files(roots)):
        with open(path, encoding="utf-8") as fh:
            before = fh.read()
        after = readable(before)
        if after == before:
            continue
        # the result must still be JSON and say the same thing (each rewritten value parses to what the string held)
        if _canon(json.loads(after)) != _canon(json.loads(before)):
            print("MISMATCH", path)
            return 2
        changed += 1
        if check:
            print("escaped params:", path)
        else:
            with open(path, "w", encoding="utf-8", newline="") as fh:
                fh.write(after)
    print(("would rewrite " if check else "rewrote ") + str(changed) + " file(s)")
    return 1 if check and changed else 0


def _canon(n, key=None):
    """`n` with every params string that holds JSON parsed - so the old and the new form compare equal."""
    if isinstance(n, dict):
        return {k: _canon(v, k) for k, v in n.items()}
    if isinstance(n, list):
        return [_canon(v) for v in n]
    if key in KEYS and isinstance(n, str) and structured(n):
        return _canon(json.loads(n))
    return n


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
