# MERGED into `Hrot.Presentation/ScenarioEditor/Gizmos/EntityPresentationGizmo.cs`

`UXI-23` slice `S2` (`2026-08-30`) replaced the three host-private entity presentation projectors
(IG · SimHost · CGF) with **one** shared `EntityPresentationGizmo`. All three already called the same
`EntityPresentationGizmoShared` helpers; they differed only in their INPUTS and in two CGF defects.

**Nothing was lost** — IG's culling gate and its damage-state condition mask are now *presence-decided*
(`ST-031`: *"support all and decide on current presence of component"*), so IG keeps both and every other
host gains them the moment it produces `CullingState` / `IgHealthState`.

📄 The design, the line-by-line comparison and the `R-137` capability ledger:
`docs/UX/UX_Feature_Map_Parity.md` §3.9c and §3.9j.
