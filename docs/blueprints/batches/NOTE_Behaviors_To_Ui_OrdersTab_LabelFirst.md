<!--STATUS
state: LIVE
updated: 2026-11-07
current-answer: the whole file
stale-below: nothing
related-designs:
  - ../../UX/DESIGN_Mission_Plan_Editor.md — §5.11 P-7 (ui's OrdersTab); §5.12 P-8 (behaviors, the picker's rule entries next)
-->

# NOTE — behaviors → ui: `OrdersTab` reddens `LabelFirstFormsFrameRail`

`Hrot.Presentation.Tests` · `LabelFirstFormsFrameRail.NoProductionWidgetCarriesAVisibleLabel_ExceptTheNamedSection31Families` is red at
`ui@bb24d3774` on five widgets in `Hrot/Editor/Hrot.Editor.AiShared/Shell/OrdersTab.cs` (P-7 7b): `Combo("then"` :216 · `DragFloat("min##then"` :241 ·
`DragFloat("%##then"` :247 · `Checkbox("give up after"` :256 · `DragFloat("min##giveup"` :261. ⭐ Not touched by behaviors — yours (FormGrid, or an
`Allowed` entry with its reason). ⚠ Heads-up: W-1 / P-8 8b (the picker's *when a phase begins ▸ · when a rule fires ▸ · New rule…*) will edit
`OrdersTab.DrawThen` next — if you are rebuilding the picker for the mockup's popup (mission-plan-detail-visual.svg A), say so in your status and
behaviors will put 8b's entries into yours instead.

The sixth hit (`ActionListFieldDrawer.cs:291`, `"order##kind"`) was behaviors' (V-3) and is fixed (label first).

---
_behaviors lane, 2026-11-07_
