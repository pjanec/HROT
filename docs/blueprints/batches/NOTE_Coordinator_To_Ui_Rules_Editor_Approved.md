<!--STATUS
state: LIVE — coordinator → ui: the rules view, the rule editor and the player HUD / debrief are APPROVED (with amendments)
updated: 2026-11-03
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Rules_View_UI.md — RV-1…RV-9 ✅; RE-1…RE-10 ✅ with §7.4's four amendments
  - ../../DESIGN_Player_HUD_And_Debrief.md — PH-1…PH-7 ✅; PH-8 ✅ amended
-->
# NOTE — coordinator → ui: **approved** — build on it

**At** `611684de3`. 🔒 **User, `2026-11-03`:** *"approved"* (after the coordinator's review).

- ✅ **RV-1 … RV-9** (rules view, mission summary) as written.
- ✅ **RE-1 … RE-10** (the rule editor) with `DESIGN_Rules_View_UI.md` **§7.4**: A1 macro-made rules READ-ONLY (SR-43) · A2 drag-to-connect predicate by the dropped row's kind (Achieved / Fired) · A3 the InsertMacro citation · A4 list the `SearchPredicateDto` fields the type registration reaches. ⭐ RE-4's live step marker and RE-7's live Apply are buildable NOW — behaviors built `StepCur` / `StepCount` and `RuleEdit` (433), `DESIGN_Scenario_Rules.md` §00.11a.
- ✅ **PH-1 … PH-7** (HUD, kill feed, debrief, game score screens) as written · **PH-8 AMENDED**: R-4115 stands — every `hud.*` / `game.*` element defaults ON; a scenario's feature preset switches the other profile's elements off; `hud.messages` ON everywhere.

_coordinator lane, 2026-11-03_
