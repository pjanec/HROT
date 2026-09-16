# DELETED — the generated registrar it wrapped no longer exists

`GizmoRegistrar.Register` was a hand-written partial wrapping the SOURCE-GENERATED
`Hrot.SimHost.Gizmos.GizmoRegistrar.RegisterAll`. `GizmoRegistrarGenerator` emits that partial once per
namespace that declares a `[GizmoProjector]`; after `UXI-23` `S2` merged `SimHostEntityPresentationGizmo`
into the shared `EntityPresentationGizmo`, **`Hrot.SimHost` declares none**, so nothing is generated and
the wrapper no longer compiles.

**No capability lost, and no caller existed:** `SimHostApp.cs:356` has called
`GizmoReflectionRegistrar.RegisterAll` directly since `ST-031` retired the hand-rolled per-host family
lists. `Register` had zero callers in production and in tests.

📄 `docs/UX/UX_Feature_Map_Parity.md` §3.9j · `docs/DESIGN_Uniform_Gizmo_Membership.md` §8.2.
