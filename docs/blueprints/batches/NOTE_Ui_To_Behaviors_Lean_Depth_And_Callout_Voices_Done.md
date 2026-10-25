<!--STATUS
state: LIVE — report
updated: 2026-10-25
current-answer: the whole file.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Ui_Lean_Depth_And_Callout_Voices.md — the handoff this reports on
  - docs/designs/audio/DESIGN_FPS_Audio.md — §9 OWNS the voices as built
  - docs/DESIGN_People_Display.md — §7 (CE-1193 row) OWNS how a lean is drawn, as built
-->

# NOTE — ui → behaviors: the lean's depth is drawn; the callouts are heard

**For:** behaviors. **From:** ui. Reply to [the handoff](HANDOFF_Behaviors_To_Ui_Lean_Depth_And_Callout_Voices.md) (dispatched at `9c481363c`).

| # | item | as built | rail | sha |
|---|---|---|---|---|
| **2** | draw the lean's depth (`CE-2371` G) | `EntityBodyLayer3D.LeanShear` reads `Leans.ShiftOf(world, e)` — 0.4 m when `Lean.Depth` is unset, up to 0.6 m. [People Display §7](../../DESIGN_People_Display.md), the `CE-1193` row | `Map3DTests.CE1193_TheDrawnLean_IsTheSimsShear` (+ a `SetDepth(0.55)` case), red-proven | `74c8bc2a0`, `d391fee13` |
| **1** | a voice clip per utterance (`CE-2372` F) | `SoundSources.Voices` plays a NEW utterance's line once, at his mouth, in his side's voice; `VoiceTkbCatalog` holds the words per kind, a sound type per (voice, line); the contact call names the side from `Target` vs his facing. Placeholder shouts generated from the words (`SoundSynth.Voice`) until a recording is named in the type's `SoundDto.Files` (AU-8). [FPS Audio §9](../../designs/audio/DESIGN_FPS_Audio.md) | `AudioTests.CE2372_*` (2) | *(this commit)* |

⭐ **What you need to do for item 1: nothing beyond appending the kinds.** The lines are keyed by the kind's NAME (AQ105 A's approved names:
CallContact, CallMoving, CallCovering, CallReloading, CallFragOut, CallGrenade, CallSuppress, CmdMove, CmdHold, CmdFallBack, CallAck), so
each is heard the moment it exists in `UtteranceKind`. ⚠ Keep the new values below **48** — slots 48–51 are the contact side lines.
⚠ `Talk` has no line (not shouted, as `Utterances.IsShouted`).
