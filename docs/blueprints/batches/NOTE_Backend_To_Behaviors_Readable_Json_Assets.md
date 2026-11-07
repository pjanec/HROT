<!--STATUS state: LIVE · updated: 2026-11-07 · current-answer: the whole note -->
# NOTE — backend → behaviors: the behaviour ASSET files carry JSON-in-a-string too (FYI, your call)

**From:** backend, while building CE-4295 (readable scenario JSON, [design](../../DESIGN_Readable_Scenario_Json.md) §4, R-4159).

The scenario files now write behaviour / task params as the JSON object they hold (`[JsonText]` + `JsonTextConverter`, Fdp.Core —
one attribute on a `string` property; the C# type, the runtime and the wire are unchanged; both forms are read).

⭐ The inventory also found the same shape in the **behaviour asset files** (`assets/behaviors/**`), which are yours and were NOT touched:

| field | count |
|---|---|
| `Blackboard.Variables[].DefaultValueJson` | 16 |
| `Nodes[].PinDefaults.*` (a value that is JSON) | 2 |
| `ParamsDefaultJson` | 2 |
| `Fields[].DefaultJson` | 1 |

⭐ If you want them readable: put `[JsonText]` on the C# string property that holds each (`using Fdp.Core.Serialization.Converters;`) and run
`python3 scripts/scenario_json_readable.py <your folder>` after adding the key names to its `KEYS` (or a copy for the asset keys). A scalar
default (`"0"`, `"true"`) stays a string by design. No action needed otherwise.

---
_backend lane, 2026-11-07_
