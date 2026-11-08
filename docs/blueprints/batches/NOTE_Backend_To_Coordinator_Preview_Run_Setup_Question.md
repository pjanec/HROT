# NOTE — backend → coordinator: B-6 (ui's Preview-with-a-run-setup request) needs the user — QUESTION

`QUESTION — what does a Preview with a run setup (params / variant ≠ authored) preview: the saved scenario re-loaded with the setup, or the edit world rebuilt in place? lean (B); item B-6 skipped`

**Measured.** The setup is applied ONLY by the load: `ScenarioLoadStep.cs:182-200` binds params and the `$variant` on the scenario's JSON DOM
(`ScenarioParams.Prepare`) BEFORE the extractor creates entities. A Preview is not a load: `ReferencePreviewHandler` (`LoadingPreview`) takes a RAM
keyframe of the CURRENT edit world and the dry run runs that world in place; `UnloadingPreview` rewinds it. ⇒ there is no world-level applier, and
ui's lean ("re-run the load's apply step on the copy") has nothing to run on — the copy has no DOM.

| option | what it means | cost / risk |
|---|---|---|
| **(A)** rebuild in place | at `LoadingPreview`, after the keyframe, each node serialises its world to scenario JSON, runs the load's apply (Prepare + Extract) and replaces its entities; the rewind restores the authored world | ⛔ every node rebuilds its world mid-session — the deterministic-id rules (`DESIGN_Deterministic_Network_Ids.md` §4c: LoadingPreview must NOT touch the id authority) and the entity map must survive a full re-extract; large blast radius |
| **(B) ⭐ lean** preview the SAVED scenario with the setup | ▶ Preview with a non-default setup goes through the same door as ▶ Live: unsaved edits ⇒ *Save and preview / Discard / Cancel*, then a load of the saved scenario WITH the args into the dry run; Stop returns to Edit of the authored scenario | ⭐ one applier (the load's), no in-place rebuild; ⚠ a setup preview never shows UNSAVED edits — the same rule Live already has ("Live starts from the saved scenario") |
| (C) refuse | a run setup applies to Live only; the Preview's *Run setup…* entry greyed with the reason | cheapest; loses ui's L-10 |

Lean **(B)**: it is what ▶ Live already does, so the user meets one rule; (A)'s risk is in the id and replay machinery for a convenience.
⭐ Default (authored) Preview is unchanged under every option.

Item B-6 is SKIPPED in `STATUS_Run_Controls_Backend.md` until the user picks; ui's L-10 stays ⏳.

---
_backend lane, 2026-11-08_
