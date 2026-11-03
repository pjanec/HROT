<!--STATUS
state: LIVE — ui → backend: D24's default (a RANDOM variant) has no owner on the load path
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — D24 (run setup; ✅ user: "default random, no interaction"), D18 (start-up options), D22 (POST /scenario/load/live)
  - ../../DESIGN_Scenario_Macros.md — §15.3 (scenario args to every node), S3b B2 (the run variant as the reserved arg `$variant`)
-->
# NOTE — ui → backend: **D24 run setup — who draws the default random variant?**

ui is building D24's picker half (the run setup in the scenario picker's side pane). Measured on `ui` (`07418edad`):

| measured | where |
|---|---|
| the variant rides the scenario args as the reserved key `$variant`; absent ⇒ **0 = as authored** | `ScenarioVariants.ArgName` / `VariantOf` (`Fdp.Toolkits/Scenario/ScenarioVariants.cs:30-37`); `ScenarioLoadStep.cs:192` |
| ⛔ nothing draws a RANDOM variant: no `--variant` start-up option, no `variant` on `POST /scenario/load/live` | grep `"--variant"` → 0; grep `ArgName` → readers only |
| D24 (✅ user): *"every way into Live takes an OPTIONAL run setup: variant (`random` — the DEFAULT, drawn and SHOWN — or a number; `0` = as authored)"* · D18 gains `--variant <n\|random>` · D22's route gains `variant` (absent = default) | AQ108 §4 D24 |

| ⭐ ui's lean | why |
|---|---|
| ⭐ the DEFAULT is drawn in ONE place on the load path — where the live load intent is built (`ScenarioLoadSequence.LiveLoadIntent(.., args)`) — when the args carry no `$variant` AND the scenario uses variants (`ScenarioVariants.Uses`): draw `1..N` (or any non-zero), write it into the args as `$variant` | then the picker, ▶, the API and the CLI all get the default by construction; the drawn number is in the args, so it is RECORDED (`RecordingMetadata.ScenarioArgs`, SR-176) and can be SHOWN. ⛔ If only the picker drew it, an API / CLI start would silently run *as authored* |
| `--variant <n\|random>` on the runner and `variant` on `POST /scenario/load/live` / `/edit` map onto the same arg (`random` = absent) | D18 / D22 |
| a branch (*Go live from here*, D21) keeps the recording's own `$variant` | D24 |

**What ui does on its side meanwhile:** the picker's *Run setup* expander passes an explicit choice only (a number, or nothing for *random*), and reads the run's actual `$variant` back from the resident args to show it in the mode indicator's menu — so it needs no change when your default lands.

_ui lane, 2026-11-03_
