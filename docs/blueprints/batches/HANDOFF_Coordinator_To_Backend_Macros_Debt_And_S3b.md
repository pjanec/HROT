<!--STATUS
state: LIVE — coordinator → backend: macros S3a debt, then S3b (InsertMacro + the director)
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Macros.md — §15.1–§15.3 (S3a as-built and its open debt) · §1 S3b row · §14 (accepted rulings)
  - ../../DESIGN_Scenario_Rules.md — rules S2 (the trigger runtime, behaviors lane) — S3b's prerequisite; SR-99 (a director is a macro), SR-100 (SetSkillAmplifier), SR-101 (run variants)
  - REPORT_Backend_Macros_S3a.md — §3 "open" lists the debt below
-->
# HANDOFF — coordinator → backend: **macros S3a debt, then S3b**

**Dispatched at** `0a4ab2f42` (`coordinator`) · ⛔ scope FROZEN at this commit (R-106 on a later invalidation).
🔒 **User, `2026-11-02`:** *"Yes, please include the debt into the next batch for the backend. And please prepare the handoff for the backend."*

## Start
Merge `coordinator` (Node Features S1 + CE-4215, CE-4154, S8, the small fixes); push `chore: started macros debt at <sha>`.

## Part A — the S3a debt (your report §3 "open"), in order
| # | item | spec / basis | done when |
|---|---|---|---|
| A1 | `@Key` args resolve only by GUID at runtime — the live world registers no `RefKey` | SR-65, SR-188 (first match, warn) | a runtime-created instance with `args: {"area": "@Bridge"}` resolves against the LIVE world's keys; ⭐ S3b's `InsertMacro` needs it |
| A2 | two instances expanded in one frame are placed against the same world and may overlap | SR-190 (avoid existing entities) | a second expansion counts the first's accepted slots as taken (or both go in one batch); rail: two instances dropped on one spot do not overlap |
| A3 | a reconcile re-applies only `SimTransform` / `EntityInfo` and does not re-place | SR-157 + SR-180 (a moved anchor = re-placement) | moving an instance re-runs placement for its children; a failed re-placement fails loudly and leaves the old placement |
| A4 | after a restore `MacroExpandedWith` is absent ⇒ no reconcile until the next expansion | SR-150 | restored from the recording or rebuilt from `MacroInstance` on restore; rail: restore → edit args → reconcile happens |
| A5 | an EDIT load takes no scenario args | SR-41 | the editor's load applies the header defaults (and given args) through the one `ScenarioParams` routine |
| A6 | a child's mobility layer comes from its file's DIS type (0 ⇒ infantry) | SR-180 ("the layer comes from the child's template mobility") | the layer from the template (TKB) mobility; rail: a vehicle child projects on the vehicle layer |
| A7 | `EveryEcsHostComposesTheLoadPhaseChainRails` ×2 on `CgfSubsystem.cs` (red before S3a — no `FromRoles`) | your report §2 | green, or a named reason it cannot be |

## Part B — S3b, ⏳ only once the behaviors lane's rules S2 (the trigger runtime) is on `behaviors` — merge it first
| # | item | spec |
|---|---|---|
| B1 | `InsertMacro` — the trigger action: creates a TRANSIENT instance (SR-171) at `at` (`PointLink`) + `heading` (`Bearing`) with its `args`, reads `MacroExpansionStatus` (SR-96: `Expanding` ⇒ Running, `Done` ⇒ Success, `Failed` ⇒ Failure with the reason); its own `Bind` (SR-94 / SR-160) | macros §1 S3b, SR-93, SR-96, SR-157 |
| B2 | run variants — `presence` on an instance / a macro choosing ONE of N, recorded so a variant replays identically | SR-101, SR-176 |
| B3 | `SetSkillAmplifier` and the counter-attack DIRECTOR as a macro used by two scenarios with different params | SR-99, SR-100 |
⛔ NOT here: the editor's Preview button (SR-189), the run-setup picker (AQ108 D24) — ui.

## Gates *(Rule 8)*
Touched classes, new rails red-proved · `DistributedScenarioLoadTests` alone · one live run per part (A: the two-teams scenario moved + restored; B: a trigger inserting a macro at a key) · the gate scripts · fold the as-built into `DESIGN_Scenario_Macros.md` §15.

_coordinator lane, 2026-11-02_
