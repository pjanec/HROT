<!--STATUS
state: DONE 2026-11-08 — Y-1…Y-8 built or filed (STATUS_Backend_Next.md); Y-5 (CE-4099) resumes in HANDOFF_Coordinator_To_Backend_Recording_Storage as W-2
updated: 2026-11-08
current-answer: the whole file
stale-below: nothing
known-rot: none
known-conflict: none
related-designs:
  - ../../UX/DESIGN_Scenario_Presets.md — §9 Preview with a preset IN PLACE (R-4168) — item Y-1
  - ../Architect_Question_108_Run_Mode_Picker.md — §4c D23 autosave (CE-4099) — Y-5; §0 the run controls — Y-2
  - ../../UX/DESIGN_Review_Perspective.md — §6.2 M3 the stale owner (D004) — Y-7
  - ../../DESIGN_Struct_Editor.md — the follow-ups CE-3399 … CE-3404 — Y-8
  - HANDOFF_Coordinator_To_Backend_After_Mission_Plan.md — its X-2 … X-5 never started; they are Y-3 … Y-6 here (X-1 / CE-4305 was built by ui)
-->

# HANDOFF — coordinator → backend: the next batch

**For:** backend. **From:** coordinator. **Dispatched at `4600ebcd9`**. ⭐ Scope FROZEN there. Merge `coordinator` and
`ui` first (restore after), push `chore: started HANDOFF_Coordinator_To_Backend_Next at <sha>`, then the [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md):
status file after every item, fetch and merge `ui` after every item. ⛔⛔ Anything needing the user is skipped (R-4156).

🔒 User, `2026-11-08`: *"Approved, send it to backend. and prepare next batch for them"*. ⭐ This file SUPERSEDES the unstarted rest of
[HANDOFF_Coordinator_To_Backend_After_Mission_Plan](HANDOFF_Coordinator_To_Backend_After_Mission_Plan.md) (its X-2 … X-5 are folded in below).

## ⭐ Your B-6 QUESTION — answered (R-4168)

[NOTE_Backend_To_Coordinator_Preview_Run_Setup_Question](NOTE_Backend_To_Coordinator_Preview_Run_Setup_Question.md): ⛔ not (B) — 🔒 *"the preview is
largely about showing unsaved edits and quick preview/edit iterations"*; ⛔ not a save-and-reload for every preset — 🔒 *"the preview still needs to
stay fast on default settings … it should happen only if necessary."* ⭐ **The answer: apply the preset IN PLACE on the Preview's copy of the edit
world** — the edit world already holds what every preset part changes ([Presets §9](../../UX/DESIGN_Scenario_Presets.md), measured): params through
`ParamBindings`, presence / place / one-of-N macro through `VariantForms` and the live macro reconcile. Also ruled: parameters never block a save —
only a non-zero variant; ✅ **no checkpoints in AAR or in an unrecorded Live** (ui's greyed flag stands).

## The items — in order

| # | id | what | design basis | done when |
|---|---|---|---|---|
| **Y-1** | CE-4326 | **Preview with a preset, IN PLACE**: keyframe → apply the preset to the copy → run → rewind. ⭐ **Gate P1**: the applier CALLS the load's own decision functions (the presence roll, the position pick, the param source) and only writes differently — ⛔ no second copy of the rules. **P2**: a param bound into a macro's behaviour args must reach KEPT children (extend the one reconcile) — or that preset falls back to the load path, named in the Preview note. **P3**: the id authority as the rewind finds it. Default Preview UNCHANGED (0 extra cost). ⭐ Tell ui when it lands: it unblocks their L-10 (the Run setup on the mode menu for Preview) | Presets §9 (R-4168) | an equality rail per fixture scenario × preset: *load with the preset* = *in-place on the authored world*; a Preview of a preset shows an UNSAVED edit; the default Preview's timing unchanged |
| **Y-2** | CE-3411 rest | the HTTP asker on the switch out of Edit — waits on ui's answer to [your REQUEST](NOTE_Backend_To_Ui_Choose_With_Http_Asker.md); skip until it is there | AQ108 §0 | `/run-mode` out of Edit with unsaved edits answers 409 + the question; `confirm:true` goes |
| **Y-3** | CE-4298 rest | the load WARNING for a Wait task (no behaviour) with no *then* — names the entity and the task, the load goes on | Mission plan §5.9 E9 | a rail |
| **Y-4** | — (file a row in your block) | ⭐ **root-cause `EntityMission_MovesEntity`** (*"moved 0.0 m"*, red at `2b50f0b92` too) — a plan that does not move its entity is the feature's core promise; fix, or file with the measured cause and owner | Mission plan §5.4 | green, or a row naming the cause |
| **Y-5** | CE-4099 | **autosave (D23)**: ① **A0 — measure first**; ② A1–A8. If A0 FAILS: stop, write the measurement into §4c and a QUESTION note | [AQ108 §4c](../Architect_Question_108_Run_Mode_Picker.md) | §4c's rails; a killed editor reopened offers the recovery |
| **Y-6** | — | the pre-existing reds you listed (SimHost: `TheDescriptorMapIsWired`, `CE1159`, `CE4208`, `NodeRolePersistenceRails`, `CE1431`, `CE3345`; `The_registered_window_set_is_unchanged`) — each FIXED or FILED with its cause and owner; ⛔ never skipped | the test tiers | one line per red in your status file |
| **Y-7** | CE-4327 | **the stale owner after a handover** (D004): the recorder writes a cold chunk whose version moved (or `SetAuthority` stamps the change) — every recording benefits; the merged view and the federated search depend on it | Review perspective §6.2 M3, §6.4 I8 | a recorded ownership handover replays with the new owner from the handover frame |
| **Y-8** | CE-3399 · CE-3400 · CE-3402 · CE-3403 · CE-3404 · CE-3401 | your struct-editor follow-ups, small first: the condition chip's 64 B (CE-3404) · the frame harness for the raw inspector and the Orders tab (CE-3403) · the link-type parameter's pick context (CE-3402) · the HSM deferred events' picker (CE-3400) · the entity field's type-ahead honours `[MapPickableEntity]` (CE-3399) · the blackboards and Blueprint variables onto `FieldTable` (CE-3401, the large one) | [Struct editor](../../DESIGN_Struct_Editor.md) | each row's own acceptance |

⛔ **Not here:** the Review perspective (CE-3419) and its better merge (§6) — designs, not scheduled; the briefing picture book (CE-3417).

## Gates

The CLAUDE.md gate contract, rows 1–8. ⭐ Row 8: Y-1 changes every Preview on every node ⇒ the dry-run suites and a cluster Preview rail; Y-5 the
save round on every node; Y-7 the recorder ⇒ the record / replay and checkpoint suites.

---
_coordinator lane, 2026-11-08_
