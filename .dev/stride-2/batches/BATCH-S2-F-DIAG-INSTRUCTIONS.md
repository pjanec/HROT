# BATCH-S2-F — DIAGNOSTIC ONLY: pinpoint the hosted-muscle origin-teleport

**Topic dir:** `.dev/stride-2/` · **Guide:** `.dev/.guides/DEV-GUIDE_claude.md`
**Mode:** sonnet. **This is a READ-ONLY DIAGNOSTIC batch — additive logging ONLY. ZERO behavior change.**

## Goal
Hill-attack scenario entities (FDP coords ~427–668) end up near origin on the editor 2D map. We must
determine WHICH of three things happens, with numbers from one run:
- (a) the body is **created at origin** (the entity's `SimTransform` was already origin at create time), or
- (b) the body is **created correct** but the native Bullet body **anchors at origin** (first `GetBodyState`
  already reads origin), or
- (c) created+read correct for a few frames, then **reverse-sync clobbers** `SimTransform` to origin.

To see this we add per-entity logging at four points, all prefixed `DIAG-POS`, all `Log.Info`, first ~5
frames per entity only (then stop — no spam).

## HARD CONSTRAINTS (read twice)
- Touch ONLY these three files. Do NOT edit any other file for any reason:
  1. `Stride/Hrot.Stride.Core/PhysicsBodyLifecycleSystem.cs`
  2. `Stride/HrotStrideApp.Game/BulletPhysicsBodyService.cs`
  3. `Stride/Hrot.Stride.Core/BulletReverseSyncSystem.cs`
- ADD logging only. Do NOT change any existing logic, ordering, types, signatures, or existing log lines.
- Do NOT "fix" anything, refactor anything, or remove any existing TODO/diag. Pure additions.
- No new tests needed (pure logging). Do NOT run the full test suite. Just BUILD these two projects:
  `Stride/Hrot.Stride.Core/Hrot.Stride.Core.csproj` and `Stride/HrotStrideApp.Game/HrotStrideApp.Game.csproj`
  — 0 errors. Stop there.
- If anything in the spec doesn't match the current code (field/method names), match reality and note it in
  the report — but still ONLY add logging.

## Task 1 — `PhysicsBodyLifecycleSystem.cs`, creation loop
In `Execute`, in the `// ── 3. Creation` `foreach (var entity in ownedQuery)` block, AFTER the existing
`ref readonly var simTf = ref view.GetComponentRO<SimTransform>(entity);` line and BEFORE the
`_bodyService.CreateBody(...)` call, add:

```csharp
Log.Info("[DIAG-POS] LC-CREATE entity=#{0} shape={1} SimTransform.Position=({2:F3},{3:F3},{4:F3})",
    entity.Index, visualRef.ShapeKind,
    simTf.Position.X, simTf.Position.Y, simTf.Position.Z);
```

(`Log` already exists in this class — the static NLog logger used by `LogCreate`.)

## Task 2 — `BulletPhysicsBodyService.cs`, `CreateBody`
Immediately AFTER the two lines that compute the initial Stride pose:
```csharp
var initialPos = FdpStrideTransform.ToStridePosition(initialPose.Position);
var initialRot = FdpStrideTransform.ToStrideRotation(initialPose.Rotation);
```
add:
```csharp
Log.Info("[DIAG-POS] CreateBody entity=#{0} shape={1} FDP=({2:F3},{3:F3},{4:F3}) StrideInit=({5:F3},{6:F3},{7:F3})",
    entity.Index, shapeKind,
    initialPose.Position.X, initialPose.Position.Y, initialPose.Position.Z,
    initialPos.X, initialPos.Y, initialPos.Z);
```

Then, in `GetBodyState`, add an EARLY-FRAME position trace (first 5 reads per body). Reuse the existing
`DiagState` class: add ONE new field to it — `public int EarlyPosCount { get; set; }` — and in `GetBodyState`,
right after `var pos = entry.StrideEntity.Transform.Position;` / `var rot = ...` and inside the existing
`if (_diagState.TryGetValue(bodyHandle, out var diag))` block (or a new lookup if cleaner), emit for the
first 5 calls:

```csharp
if (diag.EarlyPosCount < 5)
{
    diag.EarlyPosCount++;
    Log.Info("[DIAG-POS] GetBodyState '{0}' earlyFrame={1} StridePos=({2:F3},{3:F3},{4:F3}) shape={5} kinematic={6}",
        entry.StrideEntity.Name, diag.EarlyPosCount, pos.X, pos.Y, pos.Z, entry.ShapeKind, entry.IsKinematic);
}
```
(Place this so it does not disturb the existing throttled `FrameCounter` 120-interval log — keep both.)

## Task 3 — `BulletReverseSyncSystem.cs`, `Execute`
Add a per-entity early-frame counter and log the first 5 SimTransform writes per entity. Add a private field:
```csharp
private readonly Dictionary<ulong, int> _diagEarlyWriteCount = new();
```
In `Execute`, AFTER the line `repo.SetComponent(entity, newTransform);` (the SimTransform write), add:
```csharp
ulong dkey = entity.PackedValue;
if (!_diagEarlyWriteCount.TryGetValue(dkey, out var dn)) dn = 0;
if (dn < 5)
{
    _diagEarlyWriteCount[dkey] = dn + 1;
    Log.Info("[DIAG-POS] ReverseSync entity=#{0} earlyFrame={1} StrideStatePos=({2:F3},{3:F3},{4:F3}) -> wroteSimPos=({5:F3},{6:F3},{7:F3}) kinematic={8}",
        entity.Index, dn + 1,
        state.Position.X, state.Position.Y, state.Position.Z,
        newTransform.Position.X, newTransform.Position.Y, newTransform.Position.Z,
        state.IsKinematic);
}
```
(`Log` already exists in this class — the static NLog logger.)

## Definition of done
- [ ] Exactly the four additions above, in exactly those three files. Nothing else changed.
- [ ] Both Stride projects build, 0 errors.
- [ ] Report at `.dev/stride-2/reports/BATCH-S2-F-REPORT.md`: confirm the four insertions compiled, note any
      name mismatches you had to adapt, and paste the exact final code of each inserted block. Do NOT commit.
