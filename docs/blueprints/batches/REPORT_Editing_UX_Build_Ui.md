<!--STATUS state: LIVE · updated: 2026-11-07 · current-answer: §1 the outcome, §3 what is open -->
# REPORT — ui, editing-UX build

[Handoff](HANDOFF_Coordinator_To_Ui_Editing_UX_Build.md) dispatched `9c50cec00` · protocol [two lanes](PROTOCOL_Two_Lanes_Self_Coordinating.md) ·
per-item state and shas: [STATUS_Editing_UX_Build_Ui.md](STATUS_Editing_UX_Build_Ui.md) (the one table; this report does not repeat it).

## 1. The outcome

| | items |
|---|---|
| ✅ done | U-1 … U-16, U-18 (RX1 – RX3), U-25 |
| 🟡 most of it | **U-23** (Unit · Crew & cargo · Shape · Scenario forms; Playable folded; ⏸ Summary kept — §3) · **U-24** (S5 Weapon · In hand · Aboard · Aimed at · Knows about (`6eb8e519e`) · S6 Character; ⏳ Carried by / Carries, Damage) |
| ➡ moved to backend by the handoff | U-17 · U-19 · U-20 · U-21 · U-22 · U-26 · U-27 · U-28 |

⭐ Fidelity (R-4146): the List view and the Details forms were rendered by their rails and compared with `rule-list-visual.svg` and
`details-forms-visual.svg`; the deviations fixed, the ones kept are written in [Rule editor §2.1.1](../../UX/DESIGN_Rule_Editor_UX.md) and
[Details forms §4.0a](../../UX/DESIGN_Details_Forms.md). Renders stay in the scratchpad (rails: `U18_…`, `TheUnitFormDrawsTests` under `HROT_RAIL_SHOTS`).

## 2. Gates (this session's items; the earlier items carry theirs in the status notes and their commits)

| command | result | red-proof (compiling mutant) |
|---|---|---|
| `Hrot.Presentation.Tests` `RulesWindowFrameRail\|RuleEditor\|RunnableRules\|LabelFirstForms` (xvfb) | 69/0 | List default → Timeline; same-version key write; kind glyph |
| `Hrot.Editor.AiShared.Tests` whole (xvfb) | 2222 / 2 red: `CE1150_TheBundle…` (red at the base `6754d2ecc`, backend's measurement) · `CE1325_TheDecision_Draws` (passes alone — order-dependent) | Rule tab offers ⇒ unit; seats role; Playable `Apply`; S5 aboard; aim-self; S6 type mark |
| `Fdp.Presentation.Tests` (no `StartGateTests` — crashes the host, pre-existing) | 742/0 | — |
| `Hrot.Presentation.Tests` `NamedPointGizmo\|EntityTypeCatalog\|AddEntity\|Gallery\|LabelFirstForms` | 83/0 | Point type check → Deploy Role |
| `Hrot.ClusterRunner.Integration.Tests` `TheRunChoiceIsOnEveryHost\|TheHostCompositionIsFrozen` | 18/0 (goldens unchanged) | CGF Rules window registration removed |
| `Hrot.ClusterRunner.Tests` `GizmoSchemaFollowsDeclaration\|TheMapIsNotSelectionGated` | 7/0 (after a restore — the first run was a stale binary) | — |
| `Hrot.ReplayBrowser.Tests` whole | 74 / 4 red: `RegisterWindowsCore_*` ×2, `CE1302`, `CE3330` — identical in a clean worktree at HEAD without our changes (the worker's base check) | — |
| `python3 scripts/tracker-counts.py --check` | ✅ | — |

⚠ Stale-binary traps met and named: a build that failed on another lane's in-flight file ran tests on the old binary (twice — caught both
times); a test project not restored after a merge (ClusterRunner.Tests). ⇒ every gate row above is from a build that printed `0 Error(s)`.

## 3. What is open — each with its design home

| open | where |
|---|---|
| U-25: a LIST of named points beside the map pick (the map click snaps today) | [Initial state §4.3](../../UX/DESIGN_Initial_State.md) |
| U-24: Carried by / Carries, Damage, aim at a POINT, the seat chooser, Weapons control / Reactions | Initial state §4.2 |
| U-23: retire **Summary** once the Unit form carries position / kind / DIS / shown-as (retiring it now loses them); the combined rows the mockup draws | [Details forms §4.0a](../../UX/DESIGN_Details_Forms.md) parts 5 + fidelity |
| U-18: live edits on CGF (it reads and controls rules; no `RuleEditorServices` yet); richer objective sentences (RX4's gallery) | [Rule editor §2.6, §2.1.1](../../UX/DESIGN_Rule_Editor_UX.md) |
| CE-4290 (the Event Timeline's lanes drawn empty) — not started | tracker |

## 4. Ids

Allocated: **CE-1465** (the Rules window rebuilt its model every frame — fixed, `01c3cec18` + `c8eaff905`). No ruling ids. CE-4245 (focus lost while typing) was
refuted on verification.

## 5. Answered for the other lane

[`NOTE_Backend_To_Ui_Two_Presentation_Reds`](NOTE_Backend_To_Ui_Two_Presentation_Reds.md) → `LabelFirstForms` fixed `404ce6f92`, `CE1063` fixed `c8744b5b4`
([answer](NOTE_Ui_To_Backend_Presentation_Reds_Answered.md)).
