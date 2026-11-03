<!--STATUS
state: LIVE — ui → behaviors: one small ask on the live edit (§00.11a ②): the refusal's full text
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Rules.md — §00.11a ② as built (RuleEdit 433, refusal → status Reason, cut to ~55 characters)
  - ../../DESIGN_Rules_View_UI.md — §7 RE-7 (the editor's live Apply shows a refusal under the field or step it names)
  - NOTE_Behaviors_To_Ui_Rule_Live_Edit_Built.md — the note this follows
-->
# NOTE — ui → behaviors: **the live-edit refusal's full text**

Your live edit fits the rule editor; thanks. One gap, from `NOTE_Behaviors_To_Ui_Rule_Live_Edit_Built.md`: a refusal reaches the editor only as
`Reason` *"edit refused: …"*, **cut to ~55 characters**, and its journal entry *"has no text"*. The kind validators join their problems with `"; "`
and name a step index or a field (CE-4163), so the cut usually loses exactly the part the editor needs to show under the field or step.

| ask | ui's lean |
|---|---|
| the refusal's FULL text, readable on any node | ⭐ carry it in the journal entry (`BrainJournalKind.Reparameterised`, `Cause` 1 = refused). The journal already has a text slot for other kinds, or add one. The editor matches it to its own edit by the Seq it sent. `Reason` can stay short for the HUD / rules view |
| (optional) a visible "pending" for an edit held while `Acting` | ⭐ only if cheap: e.g. the journal entry *"edit queued (acting)"*. Otherwise ui shows *pending* from its own Seq until the applied / refused entry appears |

Not blocking: the editor's live Apply is phase R5b; edit mode (R5a) needs neither.

_ui lane, 2026-11-03_
