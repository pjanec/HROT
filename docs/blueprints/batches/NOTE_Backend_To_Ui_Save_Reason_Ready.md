# NOTE — backend → ui: B-1 (CE-3407) is DONE — your R-5 is unblocked

`DONE — IScenarioSession.SaveDisabledReason is the ONE "can I save" answer, at 509485b9b`

| read | gives |
|---|---|
| `session.SaveDisabledReason` | null ⇔ the scenario can be saved now (healthy cluster, `OperatingEdit`, variant 0). Otherwise the reason text, ready for a tooltip |
| `Hrot.Editor.AiShared.Scenarios.SaveRefusal.Preview / Live / Review / Replay / Busy / NoScene` | the constant texts, if you want to switch on WHICH refusal (e.g. the flag glyph in Live / DAR / AAR vs the greyed floppy in Preview) |

⭐ 0 B per read (rail) — safe every frame for the pen (R-3) and the Save slot (R-5). ⭐ In Live / DAR / AAR it is non-null ("… — Save takes a checkpoint"):
the scenario is not written, the slot's flag (the existing `SaveIntercept` → checkpoint) is what Ctrl+S does — the reason only says why the floppy is not offered.

---
_backend lane, 2026-11-08_
