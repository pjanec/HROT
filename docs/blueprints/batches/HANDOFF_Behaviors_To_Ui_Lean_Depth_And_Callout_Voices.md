<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-10-25
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/Architect_Question_105_Squad_Callouts.md — ✅ R-2080: F (the IG plays a clip per utterance kind)
  - docs/blueprints/Architect_Question_104_Area_And_Building_Combat.md — ✅ R-2079: G (the lean's depth by skill)
  - docs/DESIGN_People_Display.md — OWNS U15 (the speech marks) and how a lean is drawn
-->

# HANDOFF — ui: play the squad's callouts; draw the lean's depth

**For:** the ui lane (`ui`). **From:** the behaviours lane (`behaviors`). **Dispatched at `9c481363c`.** Scope FROZEN at that sha — later
documents are FYI; one that invalidates an item ⇒ stop and report. Designs: [AQ105](../Architect_Question_105_Squad_Callouts.md) (R-2080),
[AQ104](../Architect_Question_104_Area_And_Building_Combat.md) (R-2079). Tracker: `CE-2372`, `CE-2371`.

| # | item | basis | acceptance |
|---|---|---|---|
| **1** | ⭐ **the IG plays a voice clip per utterance** | the replicated `Utterance {Kind, Target, Until}` (`Fdp.Toolkit.Civilians.Speech`, CE-3264) — `OverheadMarkGizmo` already reads it for the mark (U15); behaviors appends the squad kinds (CallContact, CallMoving, CallCovering, CallReloading, CallFragOut, CallGrenade, CallSuppress, CmdMove, CmdHold, CmdFallBack, CallAck — `CE-2372`) and writes them | a clip per kind at the speaker, played once per new utterance (not per frame), positional, a voice set per force (a language later); CallContact may say the side ("front / left") from `Target` relative to the speaker's facing; the existing kinds (warning, order, insult, chant, the CB-27 commands) get theirs too |
| **2** | **draw the lean's depth** | backend adds a per-man depth to `Lean` (their handoff item 5); behaviors sets it by proficiency | the drawn lean follows the depth, not the fixed 0.4 m |

⭐ Report per item: as-built in People Display, the rail, the sha. Questions ⇒ a NOTE to behaviors.
