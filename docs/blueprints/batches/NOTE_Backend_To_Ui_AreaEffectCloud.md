# NOTE backend → ui — `AreaEffectCloud` is built (`CE-3219` part 3), so `CE-1165` U4-a can start

<!--STATUS
state: LIVE
updated: 2026-10-17
current-answer: whole note
related-designs:
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.3b (the cloud's design and UML), §7.3c (as-built), §8 U4-a/U4-b (ui's drawing rows)
-->

🔒 The user, `2026-10-17`: *"Ui is waiting for areaeffectcloud"* · *"the ground wind could move the cloud i guess."* Design:
[§7.3b + §7.3c](../../DESIGN_Civilians_And_Crowd_Control.md).

## What you can read

| what | where |
|---|---|
| the component — `Fdp.Toolkit.Combat.AreaEffectCloud` (id 399): `Kind` (`CloudKind.TearGas` / `Smoke`), `Radius`, `Height` (m, a vertical cylinder standing on the entity's `SimTransform.Position`), `SightTransmittance`, `IrritantSeconds`, `BornAtSimSeconds`, `Seconds` | `FDP/Toolkits/Fdp.Toolkits/Combat/AreaEffectClouds.cs` |
| the fade: `cloud.Fraction(now)` (0 when made → 1 when gone) with `now = SimClock.Of(view).TotalTime`; `cloud.TransmittanceAt(now)` is how dense it is for sight (dense for 2/3 of its life, then thinning) | same file |
| every cloud a view holds, as cylinders: `AreaEffectClouds.Of(view)` → `CloudTable` (null when none) | same file |
| it MOVES: its owner drifts it on the wind each tick, so read the position every frame (it replicates as the ordinary transform) | `AreaEffectCloudSystem` |
| TKB type 7202 "Area effect cloud", `HideFromPalette` | `Hrot/Engine/Hrot.Core/Tkb/AreaEffectCloudTkb.cs` |

## What changed under your rows

- ⛔ **U4-a's "fading over `Lifespan`" no longer applies** — the cloud carries NO `Lifespan` (`LifespanSystem` would delete it on every node,
  not only its owner). Fade on `Fraction(now)` instead.
- ⭐ **U4-b stays correct but is now unused by clouds** — the cloud is not a `smoke` obstacle prism (a drifting prism is never re-baked);
  `TerrainWorldMesh.IsVolumeEffect` keeps its job for any authored see-through prism.
- Rounds that make one: tear-gas grenade 6039, 40 mm tear-gas canister 6040, smoke grenade 6041 (`MunitionTkbCatalog`); a test or demo can
  also call `AreaEffectClouds.Spawn(repo, at, cloud)` (the FDP bus spawn order) on the node that should own it.

Rails to copy from: `FDP/Toolkits/Fdp.Toolkits.Tests/Combat/AreaEffectCloudTests.cs`; cross-node:
`EntityOrdersAcrossNodesTests.CE3219_ACloudAskedForOnSimHost_ReachesEveryNode_AndEndsEverywhere`.
