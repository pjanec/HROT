<!--STATUS
state: LIVE
updated: 2026-11-16
current-answer: the whole file
related-designs:
  - ../../designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md — OWNS the UI these findings came from (§5 as-built rows UI-1, UI-9)
  - NOTE_AggBackend_To_Agg_Contract.md — the contract the findings ask to extend (§3d, §3e)
-->

# NOTE — exp/agg-ui → exp/agg-backend: four findings from building the UI on the contract

Built against `exp/agg-backend` merged at `6876428b8` (ST-2…ST-5, CE-8015). Every slice of the UI design is built (STATUS_AggUi.md); these are the places
where the UI stops short because the contract has no seam yet. ⚠ None blocks anything — each is drawn the closest way and named in the design's §5.

| # | finding | what the UI does today | what would close it |
|---|---|---|---|
| 1 | `AggregateUnit.Width` / `Depth` / `Heading` have no attribute rows (§3e lists Resolution, Pin, Echelon, Formation, Posture, Prepared) | the Unit tab shows Frontage `200 m × 80 m` and Heading `045°` READ-ONLY (CE-8614); the mockup draws them editable | rows `Aggregate.Width`, `Aggregate.Depth`, `Aggregate.Heading` in `AttributeVocabulary` + `FormAttributes` (component + wire, as the others) |
| 2 | `ResolutionPin` has no FUSION value (§3d) | the menu's "Make fusion now" is always greyed with the reason (CE-8615) | a `PinFusion` — or a statement that fusion is never requested by hand (then the row goes) |
| 3 | no CANCEL of a resolution change | the mockup's Cancel button is not built | the operation a cancel means (unpin? a dedicated request?) and who honours it mid-split |
| 4 | no count of the posts a split will create | Progress shows the men CREATED against the men up (`AggregateChild` count / `UnitSlotTable.Present()`), not "leaves posted n/m" | the expected leaf count (or the posts list) on the unit while it splits |
