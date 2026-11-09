<!--STATUS
state: LIVE
updated: 2026-11-09
current-answer: the table
related-designs:
  - HANDOFF_Coordinator_To_Ui_Next_2.md — the items
-->
# STATUS — ui, Next_2 *(started at `818d65ab3`, merged coordinator · backend · behaviors)*

| item | state | sha | notes |
|---|---|---|---|
| A-1 | DONE | 3ad125b88 · 9d43a4aa4 | CE-4330 ui rest: chips ⑤, close question ③, delete from archive ⑦, import dialog ④ (+ CE-4331 Start / Cancel), panel job list ⑥, quit guard checked — frame rails render ③ ④ ⑤ (DESIGN_Recording_Storage §11). ⏸ Go Live greyed waits on backend: NOTE_Ui_To_Backend_GoLive_Refusal_Fact |
| A-2 | DONE | (this commit) | ONE Scenario tab — the form (sections Scenario time · Terrain · Global weather; wind levels / cloud layers in their own tables via `WeatherLayersEditor` over `WeatherEdit.Set`; Parameters under it); the raw `ScenarioEnvironmentView` retired. Frame rail `CE4346_TheScenarioTab_*`, old-tab-absent rail. Forms design Part 2 as-built |
| A-3 | DONE | (see log) | ① field-scoped open kept no buffer ELEMENTS (`path[i]` vs `path.`) — fixed in `ReflectionEditDocumentBuilder.FilterNode` ⇒ `BrainJournal.Entries` opens with its 24 slots; ② the raw tree folds `[InlineArray]` types (it drew the type name); ③ get-only maps shown read-only (`[i]` Key / Value), the managed clone copies them. Rails: both named fields + `ShotTraces.Slots` + `OutgoingGrantsPending.Descriptors`. ⚠ pre-existing red `StructEdit.Tests…Build_CircularReference…` (JSON clone cycle) |
| A-4 | DONE (measured: works) | (see log) | the shared path moves the whole selection END TO END — rail `RtsCommandToolTests.CE4342_DraggingOneOfThreeSelected_MovesAllThree_ThroughTheMapsBus` (pack + drag gizmo as every host registers it, the lifecycle's `ConstructionOrder`, the terminal's press · drags · commit): all three move by the delta, the fourth stays, the selection stays whole, ONE undo step; red-proven (`ForGrab` → null). 📐 the report (`1b7c20f9b`) was on a tree without P5 (`f13a5469d`) ⇒ most likely an older build; ⚠ not run against a live editor. Probe `DataDrivenGizmoSystem.HasActiveGizmo`. Design: `DESIGN_Map_Interaction.md` P5 as-built row |
| A-5 | DONE | (see log) | the edit toasts are laid out from the font (`EditNotificationOverlay.Measure`): the mockup's scale-1 positions × the UI scale, rows never closer than a text line, the title after the glyph and clipped before the ×. Rails: the scale-1 mockup rail unchanged + `CE4343_AtUiScale1_5_TheCardsAreLaidOutFromTheFont` (frame `edit-notifications-scale-1.5.png`) |
| A-6 | DONE | (this commit) | View ▸ RTS Command Mode carries `"checked"` from the map's RTS state (`LayerControlGizmo.RtsChecked`, wired in `MapInteractionPack.AttachMapLayers` to `RtsModeState.Armed`; the two menu strings built once). Rail `LayerControlGizmoTests.CE4347_ViewRtsCommandMode_ShowsItsCheckmark` |
| A-7 | DONE (model rail; no kit frame) | (see log) | lean taken: the wheel goes PAST the eyes for every kind (`AheadDistance`, up to `UnitCameraRig.MaxAhead` = 0.75 × scale: a flyer 6 m, clear of an A-10's nose); out again returns to the eyes before the shoulder view. Rail `CE4348_AFlyersWheel_GoesPastTheEyes_ClearOfItsNose` + the continuum rail. ⚠ the handoff's frame rail on both aircraft not built (no headless possessed-flyer kit frame) |
| A-8 | DONE | (see log) | the entity pick dropdown (`EntityField`, the ONE entity field) measures its columns: the name after the glyph, the side column after the widest name (names capped at 28 em, clipped), the popup as wide as its rows (≥ 380 × scale); the entry rows too. Rail `WidgetKitFrameRail.CE4349_ThePickDropdown_LaysOutItsColumnsFromTheText_AtUiScale1_5` (frame) |
| A-9 | DONE | (see log) | `RunModeChoice.HostPlayDisabledReason` (asked after the gate and Live · ended); the editor feeds it the Review's Merged state (`ReviewSourceTransport.MergedPlayReason`); the toolbar's ▶ tooltip says the run reason. Rail `RunControlsSwitchNowTests.NextA9_AHostsReason_GreysTheToolbarsPlay_InAnAar` |
| A-10 | TODO | | |
