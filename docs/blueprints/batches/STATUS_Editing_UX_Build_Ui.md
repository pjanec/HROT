<!--STATUS state: LIVE · updated: 2026-11-06 · current-answer: the table -->
# STATUS — ui, editing-UX build ([handoff](HANDOFF_Coordinator_To_Ui_Editing_UX_Build.md), dispatched `9c50cec00`)

| item | state | sha | what the other lane can now use / notes |
|---|---|---|---|
| U-1 | ✅ DONE | `38adee14d` | ⭐ **use them:** `NodeEditor.Core.Widgets` — `RowIcon` (image or glyph+colour) · `TreeRow.Draw(id, label, icon, depth, hasChildren, ref open, selected)` · `SectionHeader.Draw(id, label, icon, flags, HeaderColors?)` (slate; override for authority green) · `FormGrid.Begin(id, labels?, statusSlot)` / `Row(label)` / `Status()` / `End()` (label column = min(widest+14, max(25 %, 90 px))) · `IconGlyph` · `IconPalette`. Rails `Hrot.Presentation.Tests/Panels/UiConventionsFrameRail` (5, xvfb), red-proven. ⚠ `PickerState.TreeRow` (a data row) shares the name — qualify it |
| U-6 | ✅ DONE | `c2add5b8d` + this | `StructEdit.Core.Attributes.DisplayNames` — `Of(member)` · `Of(enum)` · `UnitOf` · `IconOf` · `Humanize`; `[EditDisplayName]` on class/struct/enum; `[EditIcon(key)]`. Rail `DisplayNamesTests` (5), red-proven. `EntityRoleLine.Words` folded (Add Entity suites 78/0). ⚠ deviation: `AiPrimitiveNaming.DisplayNameFromTickCoreFqn` kept (not a humanizer — recovers an authored name), Condition editor §8.4 |
| U-2 | IN PROGRESS | — | `Icons` key map, `GlyphButton` / `GlyphToggle`, the icon tab, CE-4286 — the backend's B-items wait on it |
