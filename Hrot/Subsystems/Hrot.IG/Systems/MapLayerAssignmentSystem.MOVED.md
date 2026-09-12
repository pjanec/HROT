# Moved — the map-layer mechanism is shared (UXI-23 `S1`, 2026-08-28)

`MapLayerAssignmentSystem`, `MapLayerRegistry` and `MapLayerDefinition` moved from
`Hrot.IG/Systems/` to **`Hrot.Presentation/Map/`**, namespace `Hrot.IG.Systems` →
**`Hrot.Presentation.Map`**.

## Why here and not `Hrot.Core`

`MapLayerAssignmentSystem` writes `MapDisplayComponent`, which lives in `Fdp.Presentation`.
`Hrot.Core` references `Fdp.Toolkits` but **not** `Fdp.Presentation` (the dependency runs
`Fdp.Presentation → Fdp.Toolkits`, one way), so `Hrot.Core` cannot see the component.
`Hrot.Presentation` references both, already owns `MapLayerState`, and is referenced by
IG, CGF, Editor and SimHost.

## Why it moved at all

It was **already shared and in the wrong place**: `Hrot.Editor/EditorSubsystem.cs` registered
`new MapLayerAssignmentSystem()` directly, reaching cross-assembly into `Hrot.IG.Systems`.
Meanwhile SimHost — which cannot reference `Hrot.IG` — could not run it at all, so its
TKB-built entities never got a `MapDisplayComponent` and the shared entity gizmos drew nothing.

The namespace was renamed (unlike the `MapOverlayStyle` move, which kept its own to avoid
churn) because the `Hrot.IG.*` name had already misled two consumers into thinking this was
IG-private code.

See `docs/UX/UX_Feature_Map_Parity.md` §3.0a (the measured root cause) and §3.9a (this design).
