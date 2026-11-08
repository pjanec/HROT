# NOTE — backend → coordinator: CE-4099 autosave — gate A0 FAILED as designed — QUESTION

`QUESTION — A0 fails as designed; may autosave be built on the AMENDED capture (all types + header on the frame + content singletons shared + no worker extraction across a hot reload)? lean: yes; Y-5 stopped at A0`

Per the handoff (*"If A0 FAILS: stop, write the measurement into §4c and a QUESTION note"*) — the measurement is in
[AQ108 §4c.1](../Architect_Question_108_Run_Mode_Picker.md); rail `TheWorldChangeCheckOnTheEditorTests.CE4099_A0_*`.

**What failed.** On `uc-urban-battle`, `pc-defend-house`, `showcase-bridgehead`, a save of a sandbox rebuilt from the designed keyframe (Recording types,
singletons ON) is not the live save:
- the **header** loses `Description`, `TerrainName`, `Features` — they come from NoReplay content singletons a keyframe never carries;
- the **doors** section writes every door — "as authored" is decided against `TerrainWorld`, also NoReplay content;
- the Recording type set drops NoReplay saved components (`VariantForms`: a variant scenario's presence / places).
The entities are otherwise identical with every type captured.

**The amendment, measured.** All types + the header built on the frame thread + the content singletons handed to the sandbox by reference ⇒ identical.

**A second finding (A4, the worker).** The serializer holds no mutable state, but its translators read `BehaviorRegistry` (plain dictionaries) and a
behaviour hot reload writes it on the frame thread ⇒ a worker extraction can race a hot reload. Lean: an autosave round started while a hot reload is
pending is skipped (the next interval takes it).

| option | blast radius |
|---|---|
| ⭐ **(A) amend and build** (the four points above) | autosave only; the save path itself is unchanged |
| (B) extract on the frame thread (no sandbox) | one frame of ~100–300 ms every interval while editing (the save, not the capture) — what A4 was meant to avoid |
| (C) change the content singletons' policy so a keyframe carries them | ⛔ every recording and checkpoint grows; content is loaded, not recorded (R-3043) |

What would change the lean: if the user wants autosave to be byte-identical to a save BY CONSTRUCTION rather than by a rail, (B).

---
_backend lane, 2026-11-08_
