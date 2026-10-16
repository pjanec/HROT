<!--STATUS
state: LIVE
updated: 2026-10-16
current-answer: the whole file (four findings, each with a lean — answers go into §7 of the design)
stale-below: nothing
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7 backend detailed designs (B-1, B-2) these findings touch; §8 the ui detailed designs that raised them
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Ui_Civilians.md — the ui frame (U-1…U-5)
  - docs/blueprints/batches/HANDOFF_Behaviors_To_Backend_Civilians.md — the backend frame (B-1…B-5)
-->

# Findings: ui → backend, the civilian programme (from the ui detailed designs, §8)

**Dispatched at `4329101e9` (branch `ui`).** These are not orders. They are four things the ui's detailed design (§8 of
[the design](../../DESIGN_Civilians_And_Crowd_Control.md)) found in backend's §7. Each has a lean. Fold the answer into §7, or
reply that the lean is wrong. Nothing here is built, and nothing should be before the user rules the CVs.

| # | finding (measured) | touches | ⭐ lean |
|---|---|---|---|
| **F-1** | B1-c's cloud prism would **draw as a solid block**. `TerrainWorldMesh` meshes every prism whatever its material (`TerrainWorldMesh.cs:48,128-133`), and so does the 2-D terrain gizmo (`TerrainWorldGizmo.cs:68`). | B1-c | ui owns the render side (U4-b): terrain meshing and the terrain gizmo skip a prism whose material lets sight through and does not block movement. Backend keeps B1-c as designed. ⚠ Land U-4 with B-1, or the first gas cloud shows as a grey box. |
| **F-2** | **`EffectKind` already exists**: it is the visual-effect enum (`Fdp.Toolkits/Tkb/Domain/EffectDtos.cs:7`). B1-a draws a second `EffectKind`. | B1-a | name B-1's enum `HitEffectKind` (or similar). |
| **F-3** | **Nothing carries "cowering" on the wire.** §2.1 draws `CivilianHsm ..> EffectState : cower`, but B1-b's `EffectState` has only Stunned / Irritated / KnockedDown. | B1-b | add a `Cowering` bit to `EffectState`. The CivilianHsm (behaviors) writes it. The body layer reads it (U3-b). |
| **F-4** | **No elderly carrier.** B2-a adds an Elderly type, but nothing on a type says its age. "Child" is inferred from a height under 1.5 m (`EntityBodyLayer3D.cs:47`). | B2-a | one small descriptor on the person types (age band and sex), read by `Outfits` (U1-b). |

Also FYI, ui-side only: today a SecurityForce type (DIS category 3) would draw in civilian clothes with no weapon, because
`VisualFamilies.IsSoldier` is a hard-coded `Category == 1` (`VisualFamily.cs:104`). ui fixes that in U1-a by reading the class.
