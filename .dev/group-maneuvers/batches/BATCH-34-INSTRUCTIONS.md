# BATCH-34: Squad Coordination Overlay (Phase 7)

**Batch Number:** BATCH-34
**Tasks:** TASK-SQD-P7-01, TASK-SQD-P7-02, TASK-SQD-P7-03
**Phase:** Phase 7 — Debug & Overlays (§10)
**Priority:** HIGH
**Dependencies:** BATCH-32 (SquadHsmShell), BATCH-33 (Blueprint host), Utility-AI Phase-4 overlays (already merged)

---

## Onboarding & Workflow

### Developer Instructions

You are implementing the squad coordination overlay that surfaces maneuver state in the
debug visualization layer. All three P7 tasks contribute to a single new source class:
`SquadCoordinationOverlaySource`. The tasks divide naturally into three emit groups
handled by private methods on the same class.

### Required Reading (IN ORDER)

1. **Previous review:** `.dev/group-maneuvers/reviews/BATCH-33-REVIEW.md`
2. **Task definitions:** `.dev/group-maneuvers/TASK-DETAIL.md` — search for TASK-SQD-P7-01,
   P7-02, P7-03 (they start near line 879)
3. **Design reference:** `.dev/group-maneuvers/Squad_Coordination_Design_v1_1.md` §10
4. **Overlay infra design:** `.dev/utility-ai/Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md`
   §7.5
5. **Existing overlay source to emulate:**
   `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/SquadAssignmentOverlaySource.cs`
6. **Existing overlay tests to extend:**
   `Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests/OverlaySourceTests.cs`
7. **Squad state struct:**
   `FDP/Toolkits/Fdp.Toolkits/Squad/State/SquadCognitiveState.cs`
8. **Danger-area descriptor:**
   `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/DangerAreaDescriptor.cs`
   `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/DangerAreaCognitiveBuffer.cs`
9. **Unit roster:**
   `FDP/Engine/Fdp.Core/CommandHierarchy/UnitRoster.cs`
10. **IGizmoDrawBuilder (all draw methods):**
    `FDP/ExtDeps/GizmoMap/GizmoMap.Contracts/Abstractions/IDebugDrawBuilder.cs`

### Source Code Location

- **New production file:**
  `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/SquadCoordinationOverlaySource.cs`
- **Test additions:**
  `Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests/SquadCoordinationOverlaySourceTests.cs`
  (NEW file — do NOT edit the existing `OverlaySourceTests.cs`)
- **Test project:** `Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests/`
- **Test project already references** `Fdp.Toolkits` via the production project reference.

### Report Submission

When done, submit your report to:
`.dev/group-maneuvers/reports/BATCH-34-REPORT.md`

If you have questions, create:
`.dev/group-maneuvers/questions/BATCH-34-QUESTIONS.md`

---

## Context

Phase 7 closes the "observe-and-tune" loop described in Squad_Coordination_Design_v1_1.md
§10. The Utility AI Phase-4 overlay infrastructure (`AiOverlayFlags`, `OverlayBudgetArbiter`,
`IGizmoSource`, `IGizmoDrawBuilder`) is already present and used by
`SquadAssignmentOverlaySource`.

`SquadCoordinationOverlaySource` extends the pattern to surface the full maneuver state:
per-member element coloring and role labels (P7-01), assignment-vs-actual divergence lines
and veto labels (P7-02), and the squad HSM phase label, dwell-entry tick, and merged
contact-pool markers (P7-03).

All three emit groups are methods on the same sealed class. The class is placed in
`Hrot.Diagnostics.Overlays` (same namespace and project as `SquadAssignmentOverlaySource`).

---

## Tasks

---

### Task 1 — `SquadCoordinationOverlaySource` (TASK-SQD-P7-01 + P7-02 + P7-03)

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/SquadCoordinationOverlaySource.cs`
(NEW FILE)

#### Design

The source queries entities with `DebugState.Ai & AiOverlayFlags.SquadAssignment`. For each
such entity that has both `UnitRoster` and `Blackboard1024`, it projects the squad state and
emits three groups of primitives.

**Guard check (exactly as in `SquadAssignmentOverlaySource`):**

```csharp
if (!_budget.IsPermitted(AiOverlayFlags.SquadAssignment)) return;
// ...
if ((ds.Ai & AiOverlayFlags.SquadAssignment) == 0) continue;
// inside EmitForCommander:
if (!_repo.HasComponent<UnitRoster>(commander)) return;
if (!_repo.HasComponent<Blackboard1024>(commander)) return;
```

**Projecting the squad state:**

Use `GetComponentRW` (not RO) because `SquadCognitiveState.Project` requires a mutable ref:

```csharp
ref var state = ref SquadCognitiveState.Project(
    ref _repo.GetComponentRW<Blackboard1024>(commander));
```

**Using UnitRoster unsafe fixed arrays:**

The `UnitRoster.SubordinateEntities` field is a fixed array of packed `long` entity handles.
Access it in an `unsafe` block:

```csharp
unsafe
{
    long packedHandle = roster.SubordinateEntities[i];
    var member = new Entity((ulong)packedHandle);
    // ...
}
```

Mark the class or method as `unsafe` as needed.

#### P7-01: Per-member element color + role label + danger-area OBB

**Per-member element color and role label:**

For each member index `i` in `0..roster.Count-1`:
- Read element index: `byte elemIdx = state.Elements.MemberElements[i]`
  (uses `MemberElementIndexArray` InlineArray; read it via `MemoryMarshal.CreateReadOnlySpan`)
- Pick color from a fixed palette (4 entries; index wraps with `% 4` for safety):

```csharp
private static readonly Rgba32[] s_elementColors = new Rgba32[]
{
    new Rgba32(0x40, 0x80, 0xFF, 0xCC),  // 0: blue
    new Rgba32(0xFF, 0x40, 0x40, 0xCC),  // 1: red
    new Rgba32(0x40, 0xFF, 0x40, 0xCC),  // 2: green
    new Rgba32(0xFF, 0xFF, 0x00, 0xCC),  // 3: yellow
};
```

- Read role id: `byte roleId = state.Roles[i].RoleId`
  (uses `RoleAssignmentArray` InlineArray via `MemoryMarshal.CreateReadOnlySpan`)
- Emit one `DrawText(0f, 0f, new FixedString32($"E{elemIdx}R{roleId}"), color)` per member.
  Use the element color from the palette.

This produces exactly **roster.Count** draw calls for the member overlays.

NOTE: All draw positions are `(0f, 0f)` world-space. The overlay layer does not have access
to per-entity position components; world-space pinning to a real position is a future
enhancement.

**Accessing InlineArray fields:**

Both `MemberElementIndexArray` and `RoleAssignmentArray` are C# 12 InlineArray structs.
The defensive-copy rule: NEVER index them directly on a `ref readonly` field because that
creates a copy. Use `MemoryMarshal.CreateReadOnlySpan`:

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
// ...
var memberElements = MemoryMarshal.CreateReadOnlySpan(
    ref Unsafe.As<MemberElementIndexArray, byte>(
        ref Unsafe.AsRef(in state.Elements.MemberElements)), 16);
var roles = MemoryMarshal.CreateReadOnlySpan(
    ref Unsafe.As<RoleAssignmentArray, RoleSlot>(
        ref Unsafe.AsRef(in state.Roles)), 16);
```

**Danger-area OBB:**

If the commander entity has a `DangerAreaCognitiveBuffer` component and the active feature
id is set (`state.ActiveFeatureId != 0`), find the matching descriptor and draw the OBB.

```csharp
private void EmitDangerAreaObb(Entity commander, IGizmoDrawBuilder draw, ref SquadCognitiveState state)
{
    if (state.ActiveFeatureId == 0) return;
    if (!_repo.HasComponent<DangerAreaCognitiveBuffer>(commander)) return;

    ref readonly var buf = ref _repo.GetComponentRO<DangerAreaCognitiveBuffer>(commander);
    var span = buf.GetSpanRO();
    for (int d = 0; d < buf.Count; d++)
    {
        if (span[d].FeatureId != state.ActiveFeatureId) continue;
        DrawObbEdges(draw, in span[d]);
        break;
    }
}
```

**Drawing the OBB edges:**

The OBB is defined by `Center (Vector3)`, `ExtentsXY (Vector2)`, `AngleRad (float)`,
`ZFloor (float)`, `ZCeiling (float)`.

Compute the 4 XY corner offsets by rotating `(±extentsXY.X, ±extentsXY.Y)` by `AngleRad`:

```csharp
private static void DrawObbEdges(IGizmoDrawBuilder draw, in DangerAreaDescriptor desc)
{
    float cos = MathF.Cos(desc.AngleRad);
    float sin = MathF.Sin(desc.AngleRad);

    // 4 corners relative to center, in XZ plane (Y is up in world space)
    // Layout: (+x,+y), (-x,+y), (-x,-y), (+x,-y) rotated by AngleRad
    float ex = desc.ExtentsXY.X;
    float ey = desc.ExtentsXY.Y;

    // Rotate 2D extents into world XZ
    var c0 = new Vector3(desc.Center.X + cos * ex - sin * ey, 0f, desc.Center.Z + sin * ex + cos * ey);
    var c1 = new Vector3(desc.Center.X - cos * ex - sin * ey, 0f, desc.Center.Z - sin * ex + cos * ey);
    var c2 = new Vector3(desc.Center.X - cos * ex + sin * ey, 0f, desc.Center.Z - sin * ex - cos * ey);
    var c3 = new Vector3(desc.Center.X + cos * ex + sin * ey, 0f, desc.Center.Z + sin * ex - cos * ey);

    // Apply ZFloor and ZCeiling as Y offset
    var floorOffset  = new Vector3(0f, desc.ZFloor,   0f);
    var ceilOffset   = new Vector3(0f, desc.ZCeiling, 0f);

    Rgba32 obbColor = new Rgba32(0xFF, 0x80, 0x00, 0xCC);

    // Bottom face (4 edges at ZFloor)
    draw.DrawLine(c0 + floorOffset, c1 + floorOffset, obbColor);
    draw.DrawLine(c1 + floorOffset, c2 + floorOffset, obbColor);
    draw.DrawLine(c2 + floorOffset, c3 + floorOffset, obbColor);
    draw.DrawLine(c3 + floorOffset, c0 + floorOffset, obbColor);

    // Top face (4 edges at ZCeiling)
    draw.DrawLine(c0 + ceilOffset, c1 + ceilOffset, obbColor);
    draw.DrawLine(c1 + ceilOffset, c2 + ceilOffset, obbColor);
    draw.DrawLine(c2 + ceilOffset, c3 + ceilOffset, obbColor);
    draw.DrawLine(c3 + ceilOffset, c0 + ceilOffset, obbColor);

    // 4 vertical edges
    draw.DrawLine(c0 + floorOffset, c0 + ceilOffset, obbColor);
    draw.DrawLine(c1 + floorOffset, c1 + ceilOffset, obbColor);
    draw.DrawLine(c2 + floorOffset, c2 + ceilOffset, obbColor);
    draw.DrawLine(c3 + floorOffset, c3 + ceilOffset, obbColor);
}
```

This emits exactly **12 DrawLine** calls per active danger-area descriptor.

NOTE: The coordinate convention used here (`DangerAreaDescriptor.Center.X`, `.Z` for the
OBB footprint, `.Y` from ZFloor/ZCeiling for the height) follows the 3D-native convention
established in Phase 2 (ZFloor/ZCeiling are distinct per SC-P2-03-4).

#### P7-02: Assignment-vs-actual divergence lines + veto label

For each member:
1. Always emit a solid "assignment line" stub (representing the squad-leader assignment):
   `draw.DrawLine(Vector3.Zero, Vector3.Zero, assignColor, style: LineStyle.Solid)`
2. If the member entity has a `UtilityTraceWorkingMemory1024` component **and** the component
   has at least one record (`mem.RecordCount > 0`), emit:
   - A dashed line (divergence indicator):
     `draw.DrawLine(Vector3.Zero, Vector3.Zero, vetoColor, style: LineStyle.Dashed)`
   - A text label naming the dominant consideration:
     `draw.DrawTextLong(0f, 0f, "VETO:" + <winner option id>, vetoTextColor)`

"Dominant consideration" in the overlay context means the most recently selected option id from
the member's utility trace — use `mem.LatestSelected().OptionId` (copy-to-mutable first).

Colors:
```csharp
private static readonly Rgba32 s_assignColor = new Rgba32(0x00, 0xFF, 0x00, 0xCC);
private static readonly Rgba32 s_vetoColor   = new Rgba32(0xFF, 0x80, 0x00, 0xCC);
private static readonly Rgba32 s_vetoText    = new Rgba32(0xFF, 0xCC, 0x00, 0xCC);
```

The label is emitted using `DrawTextLong` (because the option id string may exceed 31 chars
in combination with the "VETO:" prefix). The string is `$"VETO:{optId}"` where `optId` is
the `WinnerOptionId` from the latest trace record.

Per-member loop (same loop as for element colors above — combine into one loop body):
- Solid line: always (cost = 1 DrawLine per member)
- Dashed line + label: only when member has a UtilityTraceWorkingMemory1024 with RecordCount > 0

NOTE: `UtilityTraceWorkingMemory1024` is already registered in `CreateTestRepo()` in the
existing `OverlaySourceTests.cs`. The new test class must register it again in its own helper.

#### P7-03: Phase label + dwell-entry tick + contact pool markers

**Phase label and dwell-entry tick:**

```csharp
draw.DrawTextLong(0f, 0f, $"Phase:{state.PhaseId} T0:{state.PhaseEnteredTick}",
    new Rgba32(0xFF, 0xFF, 0xFF, 0xCC));
```

This emits exactly **1 DrawTextLong** per commander.

**Contact pool markers:**

For each contact in `state.Contacts` (count = `state.Contacts.Count`):
```csharp
var contact = contactSpan[c];
draw.DrawSphere(
    new Vector3(contact.PositionX, contact.PositionY, contact.PositionZ),
    1.5f,
    new Rgba32(0xFF, 0x40, 0xFF, 0xCC));
```

This emits exactly **state.Contacts.Count** `DrawSphere` calls. These are the "squad pool"
markers — distinct from per-member `TargetMemoryOverlaySource` markers (which draw spheres
of radius `1.0f` with a different color).

**Accessing SquadContactPoolSlots:**

Use `MemoryMarshal.CreateReadOnlySpan`:

```csharp
var contactSpan = MemoryMarshal.CreateReadOnlySpan(
    ref Unsafe.As<SquadContactPoolSlots, SquadContact>(
        ref Unsafe.AsRef(in state.Contacts.Contacts)), 16);
```

#### Complete emission budget per commander

For a commander with `memberCount=2` and `contactCount=1` and active danger area,
where 1 member is vetoing:
- Member labels: 2 × DrawText
- OBB: 12 × DrawLine
- Assignment lines: 2 × DrawLine (solid)
- Veto dashed line: 1 × DrawLine
- Veto label: 1 × DrawTextLong
- Phase/entry label: 1 × DrawTextLong
- Contact sphere: 1 × DrawSphere

Total = 20 primitives.

#### Signature summary

```csharp
internal sealed class SquadCoordinationOverlaySource : IGizmoSource
{
    // ctor: (EntityRepository repo, OverlayBudgetArbiter budget)
    // public void Emit(float deltaTime, IGizmoDrawBuilder draw)
    // private void EmitForCommander(Entity commander, IGizmoDrawBuilder draw)
    // private void EmitMemberOverlays(Entity commander, IGizmoDrawBuilder draw, ref SquadCognitiveState state, ref UnitRoster roster)
    // private void EmitDangerAreaObb(Entity commander, IGizmoDrawBuilder draw, ref SquadCognitiveState state)
    // private void EmitPhaseAndContacts(IGizmoDrawBuilder draw, ref SquadCognitiveState state)
}
```

#### Required usings

```csharp
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.DangerArea;
using Fdp.Toolkit.Utility;
using FixedString32 = Fdp.Toolkit.Diagnostics.Gizmos.FixedString32;
```

---

### Task 2 — Tests (TASK-SQD-P7-01/P7-02/P7-03)

**File:** `Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests/SquadCoordinationOverlaySourceTests.cs`
(NEW FILE)

Create a new test class `SquadCoordinationOverlaySourceTests` (public sealed). Do NOT add
tests to the existing `OverlaySourceTests.cs`.

#### Test helpers

**`CreateTestRepo()` helper** — registers all components needed for squad coordination tests:

```csharp
private static EntityRepository CreateTestRepo()
{
    var repo = new EntityRepository();
    repo.RegisterComponent<DebugState>();
    repo.RegisterComponent<UnitRoster>();
    repo.RegisterComponent<Blackboard1024>();
    repo.RegisterComponent<SquadStateMarker>();
    repo.RegisterComponent<DangerAreaCognitiveBuffer>();
    repo.RegisterComponent<BehaviorState>();
    repo.RegisterComponent<UtilityTraceWorkingMemory1024>();
    return repo;
}
```

**`CountingDrawBuilder`** — copy the existing inner class from `OverlaySourceTests.cs` into
the new test class (it's `internal sealed` so it can't be shared between files directly).
Alternatively, mark it with `internal` and move it to a shared file — but the simplest option
is to duplicate it in the new file.

Actually, since both test classes live in the same assembly, you can extract it to a separate
file (e.g. `TestHelpers.cs` in the test project). But if you do, mark the existing one with
`// shared via TestHelpers.cs` comment. For this batch, it is acceptable to duplicate it.

**`LineCapturingDrawBuilder`** — extended draw builder for SC-P7-01-3 and SC-P7-02-1/2:

```csharp
internal sealed class LineCapturingDrawBuilder : IGizmoDrawBuilder
{
    public int EmitCount;
    public readonly List<(Vector3 start, Vector3 end, LineStyle style)> Lines = new();
    public readonly List<string> LongTexts = new();
    public readonly List<Vector3> SpherePositions = new();

    public void DrawLine(Vector3 start, Vector3 end, Rgba32 color,
        float thickness = 1f, SizeMode sizeMode = SizeMode.ScreenPixels,
        PipelineTarget target = PipelineTarget.All, byte layer = 0,
        LineStyle style = LineStyle.Solid)
    { EmitCount++; Lines.Add((start, end, style)); }

    public void DrawLineGradient(Vector3 start, Vector3 end, Rgba32 sc, Rgba32 ec,
        float thickness = 1f, SizeMode sizeMode = SizeMode.ScreenPixels,
        PipelineTarget target = PipelineTarget.All, byte layer = 0,
        LineStyle style = LineStyle.Solid)
    { EmitCount++; }

    public void DrawSphere(Vector3 center, float radius, Rgba32 color,
        float thickness = 0f, SizeMode sizeMode = SizeMode.WorldMeters,
        PipelineTarget target = PipelineTarget.All, byte layer = 0,
        Rgba32 fillColor = default, LineStyle style = LineStyle.Solid)
    { EmitCount++; SpherePositions.Add(center); }

    public void DrawArrow(Vector3 from, Vector3 to, Rgba32 color,
        float headSize = 1f, byte layer = 0) => EmitCount++;

    public void DrawText(float x, float y, FixedString32 text, Rgba32 color,
        CoordinateSpace space = CoordinateSpace.World, byte layer = 0) => EmitCount++;

    public void DrawTextLong(float x, float y, string text, Rgba32 color,
        CoordinateSpace space = CoordinateSpace.World, byte layer = 0)
    { EmitCount++; LongTexts.Add(text); }
}
```

#### Helper: SetupSquadCommander

```csharp
// Creates a commander entity with DebugState (SquadAssignment flag), UnitRoster, Blackboard1024.
// Writes the provided state snapshot into the Blackboard1024.
// Returns the commander entity.
private static unsafe Entity SetupSquadCommander(
    EntityRepository repo,
    SquadCognitiveState stateSnapshot)
{
    var commander = repo.CreateEntity();
    repo.AddComponent(commander, new DebugState { Ai = AiOverlayFlags.SquadAssignment });
    repo.AddComponent(commander, new UnitRoster());
    repo.AddComponent(commander, new Blackboard1024());

    ref var bb = ref repo.GetComponentRW<Blackboard1024>(commander);
    ref var state = ref SquadCognitiveState.Project(ref bb);
    state = stateSnapshot;

    return commander;
}
```

#### SC-P7-01-1: Toggle flag visibility

```csharp
// SC-P7-01-1
[Fact]
public void FlagSet_EmitsAtLeastOne()
{
    using var repo = CreateTestRepo();
    var arbiter = new OverlayBudgetArbiter(float.MaxValue);
    var source  = new SquadCoordinationOverlaySource(repo, arbiter);

    var snap = new SquadCognitiveState();
    var commander = SetupSquadCommander(repo, snap);

    var draw = new CountingDrawBuilder();
    arbiter.BeginFrame();
    source.Emit(0.016f, draw);

    Assert.True(draw.EmitCount >= 1);
}

// SC-P7-01-1 (off)
[Fact]
public void FlagAbsent_EmitsZero()
{
    using var repo = CreateTestRepo();
    var arbiter = new OverlayBudgetArbiter(float.MaxValue);
    var source  = new SquadCoordinationOverlaySource(repo, arbiter);

    var commander = repo.CreateEntity();
    repo.AddComponent(commander, new DebugState { Ai = AiOverlayFlags.Perception }); // NOT SquadAssignment
    repo.AddComponent(commander, new UnitRoster());
    repo.AddComponent(commander, new Blackboard1024());

    var draw = new CountingDrawBuilder();
    arbiter.BeginFrame();
    source.Emit(0.016f, draw);

    Assert.Equal(0, draw.EmitCount);
}
```

#### SC-P7-01-2: Element color persistence

Test that calling Emit twice with the same element index produces the same number of draw
calls each time (no flickering — same count implies same primitives).

```csharp
// SC-P7-01-2
[Fact]
public unsafe void ElementColorPersistence_SameElementIndex_SameEmitCountAcrossTicks()
{
    using var repo = CreateTestRepo();
    var arbiter = new OverlayBudgetArbiter(float.MaxValue);
    var source  = new SquadCoordinationOverlaySource(repo, arbiter);

    var snap = new SquadCognitiveState();
    var commander = SetupSquadCommander(repo, snap);

    // Add 1 member to roster with element index 0
    ref var roster = ref repo.GetComponentRW<UnitRoster>(commander);
    UnitRoster.Add(ref roster, 9999L);

    // Set member element index to 0
    ref var state = ref SquadCognitiveState.Project(ref repo.GetComponentRW<Blackboard1024>(commander));
    var memberElemSpan = MemoryMarshal.CreateSpan(
        ref Unsafe.As<MemberElementIndexArray, byte>(ref state.Elements.MemberElements), 16);
    memberElemSpan[0] = 0; // element 0

    int count1;
    {
        var draw = new CountingDrawBuilder();
        arbiter.BeginFrame();
        source.Emit(0.016f, draw);
        count1 = draw.EmitCount;
    }

    int count2;
    {
        var draw = new CountingDrawBuilder();
        arbiter.BeginFrame();
        source.Emit(0.016f, draw);
        count2 = draw.EmitCount;
    }

    // Same primitives emitted on both ticks (color is stable/deterministic, no randomness)
    Assert.Equal(count1, count2);
    Assert.True(count1 >= 1);
}
```

#### SC-P7-01-3: Danger-area Z extent differs

Uses `LineCapturingDrawBuilder`. Verifies that lines emitted for a ground-level descriptor
(ZFloor=0, ZCeiling=2) have different Y values than for a bridge-deck descriptor
(ZFloor=10, ZCeiling=12).

```csharp
// SC-P7-01-3
[Fact]
public unsafe void DangerAreaObb_ZExtentDiffers_GroundVsBridgeDeck()
{
    using var repo = CreateTestRepo();
    var arbiter = new OverlayBudgetArbiter(float.MaxValue);

    // --- Ground-level fixture ---
    {
        var source = new SquadCoordinationOverlaySource(repo, arbiter);
        var snap = new SquadCognitiveState { ActiveFeatureId = 1u };
        var commander = SetupSquadCommander(repo, snap);
        repo.AddComponent(commander, new DangerAreaCognitiveBuffer());

        ref var buf = ref repo.GetComponentRW<DangerAreaCognitiveBuffer>(commander);
        var bufSpan = buf.GetSpanRW();
        bufSpan[0] = new DangerAreaDescriptor
        {
            FeatureId  = 1u,
            Center     = new System.Numerics.Vector3(0f, 0f, 0f),
            ExtentsXY  = new System.Numerics.Vector2(5f, 5f),
            AngleRad   = 0f,
            ZFloor     = 0f,
            ZCeiling   = 2f,
        };
        buf.Count = 1;

        var draw = new LineCapturingDrawBuilder();
        arbiter.BeginFrame();
        source.Emit(0.016f, draw);

        // Verify: Y values in lines span [0, 2]
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        foreach (var (start, end, _) in draw.Lines)
        {
            if (start.Y < minY) minY = start.Y;
            if (start.Y > maxY) maxY = start.Y;
            if (end.Y < minY) minY = end.Y;
            if (end.Y > maxY) maxY = end.Y;
        }
        Assert.True(draw.Lines.Count >= 12, "Expected at least 12 OBB lines");
        Assert.Equal(0f, minY, precision: 3);
        Assert.Equal(2f, maxY, precision: 3);
    }

    // --- Bridge-deck fixture ---
    using var repo2 = CreateTestRepo();
    {
        var source = new SquadCoordinationOverlaySource(repo2, arbiter);
        var snap = new SquadCognitiveState { ActiveFeatureId = 2u };
        var commander = SetupSquadCommander(repo2, snap);
        repo2.AddComponent(commander, new DangerAreaCognitiveBuffer());

        ref var buf = ref repo2.GetComponentRW<DangerAreaCognitiveBuffer>(commander);
        var bufSpan = buf.GetSpanRW();
        bufSpan[0] = new DangerAreaDescriptor
        {
            FeatureId  = 2u,
            Center     = new System.Numerics.Vector3(0f, 0f, 0f),
            ExtentsXY  = new System.Numerics.Vector2(5f, 5f),
            AngleRad   = 0f,
            ZFloor     = 10f,
            ZCeiling   = 12f,
        };
        buf.Count = 1;

        var draw = new LineCapturingDrawBuilder();
        arbiter.BeginFrame();
        source.Emit(0.016f, draw);

        float minY = float.MaxValue;
        float maxY = float.MinValue;
        foreach (var (start, end, _) in draw.Lines)
        {
            if (start.Y < minY) minY = start.Y;
            if (start.Y > maxY) maxY = start.Y;
            if (end.Y < minY) minY = end.Y;
            if (end.Y > maxY) maxY = end.Y;
        }
        Assert.True(draw.Lines.Count >= 12, "Expected at least 12 OBB lines");
        Assert.Equal(10f, minY, precision: 3);
        Assert.Equal(12f, maxY, precision: 3);
    }
}
```

#### SC-P7-02-1: On-task member emits only solid line

Member with no utility trace (no `UtilityTraceWorkingMemory1024` or RecordCount==0) should
emit exactly one solid DrawLine per member plus the text label.

```csharp
// SC-P7-02-1
[Fact]
public unsafe void OnTaskMember_NoDivergence_EmitsSolidLineOnly()
{
    using var repo = CreateTestRepo();
    var arbiter = new OverlayBudgetArbiter(float.MaxValue);
    var source  = new SquadCoordinationOverlaySource(repo, arbiter);

    var snap = new SquadCognitiveState();
    var commander = SetupSquadCommander(repo, snap);

    // Add 1 member to roster; member has NO UtilityTraceWorkingMemory1024
    ref var roster = ref repo.GetComponentRW<UnitRoster>(commander);
    var member = repo.CreateEntity();
    repo.AddComponent(member, new BehaviorState { ActiveBehaviorHash = 0 });
    UnitRoster.Add(ref roster, (long)member.PackedValue);

    var draw = new LineCapturingDrawBuilder();
    arbiter.BeginFrame();
    source.Emit(0.016f, draw);

    // Solid lines only (1 solid per member) — no dashed lines
    var dashedLines = draw.Lines.FindAll(l => l.style == LineStyle.Dashed);
    Assert.Empty(dashedLines);
    Assert.Empty(draw.LongTexts.FindAll(t => t.StartsWith("VETO:")));
}
```

#### SC-P7-02-2: Vetoing member emits dashed line with label

```csharp
// SC-P7-02-2
[Fact]
public unsafe void VetoingMember_EmitsDashedLineAndLabel()
{
    using var repo = CreateTestRepo();
    var arbiter = new OverlayBudgetArbiter(float.MaxValue);
    var source  = new SquadCoordinationOverlaySource(repo, arbiter);

    var snap = new SquadCognitiveState();
    var commander = SetupSquadCommander(repo, snap);

    var member = repo.CreateEntity();
    repo.AddComponent(member, new BehaviorState { ActiveBehaviorHash = 42 });
    repo.AddComponent(member, new UtilityTraceWorkingMemory1024());

    ref var mem = ref repo.GetComponentRW<UtilityTraceWorkingMemory1024>(member);
    mem.WriteWinnerRecord(tick: 1, winnerOptionId: 7, winnerDefinitionIdx: 0,
        winnerScore: 0.9f, runnerUpMargin: 0.1f);

    ref var roster = ref repo.GetComponentRW<UnitRoster>(commander);
    UnitRoster.Add(ref roster, (long)member.PackedValue);

    var draw = new LineCapturingDrawBuilder();
    arbiter.BeginFrame();
    source.Emit(0.016f, draw);

    // Must have at least one dashed line
    var dashedLines = draw.Lines.FindAll(l => l.style == LineStyle.Dashed);
    Assert.True(dashedLines.Count >= 1);

    // Must have a VETO: label with the winner option id
    var vetoLabels = draw.LongTexts.FindAll(t => t.StartsWith("VETO:"));
    Assert.True(vetoLabels.Count >= 1);
    Assert.Contains("7", vetoLabels[0]); // optionId=7 appears in label
}
```

#### SC-P7-02-3: Veto label updates tick-to-tick

```csharp
// SC-P7-02-3
[Fact]
public unsafe void VetoLabel_UpdatesWhenOptionIdChanges()
{
    using var repo = CreateTestRepo();
    var arbiter = new OverlayBudgetArbiter(float.MaxValue);
    var source  = new SquadCoordinationOverlaySource(repo, arbiter);

    var snap = new SquadCognitiveState();
    var commander = SetupSquadCommander(repo, snap);

    var member = repo.CreateEntity();
    repo.AddComponent(member, new BehaviorState());
    repo.AddComponent(member, new UtilityTraceWorkingMemory1024());

    ref var roster = ref repo.GetComponentRW<UnitRoster>(commander);
    UnitRoster.Add(ref roster, (long)member.PackedValue);

    // Tick 1: option 3
    ref var mem = ref repo.GetComponentRW<UtilityTraceWorkingMemory1024>(member);
    mem.WriteWinnerRecord(tick: 1, winnerOptionId: 3, winnerDefinitionIdx: 0,
        winnerScore: 0.8f, runnerUpMargin: 0.2f);

    var draw1 = new LineCapturingDrawBuilder();
    arbiter.BeginFrame();
    source.Emit(0.016f, draw1);
    var label1 = draw1.LongTexts.Find(t => t.StartsWith("VETO:"));

    // Tick 2: option 5 (different)
    mem.WriteWinnerRecord(tick: 2, winnerOptionId: 5, winnerDefinitionIdx: 0,
        winnerScore: 0.9f, runnerUpMargin: 0.1f);

    var draw2 = new LineCapturingDrawBuilder();
    arbiter.BeginFrame();
    source.Emit(0.016f, draw2);
    var label2 = draw2.LongTexts.Find(t => t.StartsWith("VETO:"));

    Assert.NotNull(label1);
    Assert.NotNull(label2);
    Assert.NotEqual(label1, label2); // label changed when optionId changed
}
```

#### SC-P7-03-1: Phase label updates on transition

```csharp
// SC-P7-03-1
[Fact]
public unsafe void PhaseLabel_UpdatesOnTransition()
{
    using var repo = CreateTestRepo();
    var arbiter = new OverlayBudgetArbiter(float.MaxValue);
    var source  = new SquadCoordinationOverlaySource(repo, arbiter);

    var snap1 = new SquadCognitiveState { PhaseId = 1, PhaseEnteredTick = 100u };
    var commander = SetupSquadCommander(repo, snap1);

    var draw1 = new LineCapturingDrawBuilder();
    arbiter.BeginFrame();
    source.Emit(0.016f, draw1);
    var phaseLabel1 = draw1.LongTexts.Find(t => t.StartsWith("Phase:"));
    Assert.NotNull(phaseLabel1);
    Assert.Contains("1", phaseLabel1);

    // Transition to phase 2
    ref var state = ref SquadCognitiveState.Project(ref repo.GetComponentRW<Blackboard1024>(commander));
    state.PhaseId = 2;
    state.PhaseEnteredTick = 200u;

    var draw2 = new LineCapturingDrawBuilder();
    arbiter.BeginFrame();
    source.Emit(0.016f, draw2);
    var phaseLabel2 = draw2.LongTexts.Find(t => t.StartsWith("Phase:"));
    Assert.NotNull(phaseLabel2);
    Assert.Contains("2", phaseLabel2);
    Assert.NotEqual(phaseLabel1, phaseLabel2);
}
```

#### SC-P7-03-2: Dwell timer resets on phase change

```csharp
// SC-P7-03-2
[Fact]
public unsafe void PhaseEntryTick_ResetsOnTransition()
{
    using var repo = CreateTestRepo();
    var arbiter = new OverlayBudgetArbiter(float.MaxValue);
    var source  = new SquadCoordinationOverlaySource(repo, arbiter);

    var snap = new SquadCognitiveState { PhaseId = 0, PhaseEnteredTick = 50u };
    var commander = SetupSquadCommander(repo, snap);

    var draw1 = new LineCapturingDrawBuilder();
    arbiter.BeginFrame();
    source.Emit(0.016f, draw1);
    var label1 = draw1.LongTexts.Find(t => t.Contains("T0:"));
    Assert.NotNull(label1);
    Assert.Contains("50", label1); // T0:50

    // Phase transition resets PhaseEnteredTick to 150
    ref var state = ref SquadCognitiveState.Project(ref repo.GetComponentRW<Blackboard1024>(commander));
    state.PhaseId = 1;
    state.PhaseEnteredTick = 150u;

    var draw2 = new LineCapturingDrawBuilder();
    arbiter.BeginFrame();
    source.Emit(0.016f, draw2);
    var label2 = draw2.LongTexts.Find(t => t.Contains("T0:"));
    Assert.NotNull(label2);
    Assert.Contains("150", label2); // T0:150 (reset to new entry tick)
    Assert.NotEqual(label1, label2);
}
```

#### SC-P7-03-3: Contact pool markers differ from per-member markers

Contact pool uses `DrawSphere` with radius 1.5f. Per-member `TargetMemoryOverlaySource` uses
1.0f. The test verifies that contact pool spheres ARE emitted.

```csharp
// SC-P7-03-3
[Fact]
public unsafe void ContactPool_EmitsSpheres_WhenContactsPresent()
{
    using var repo = CreateTestRepo();
    var arbiter = new OverlayBudgetArbiter(float.MaxValue);
    var source  = new SquadCoordinationOverlaySource(repo, arbiter);

    var snap = new SquadCognitiveState();
    var commander = SetupSquadCommander(repo, snap);

    // Add 2 contacts to the pool
    ref var state = ref SquadCognitiveState.Project(ref repo.GetComponentRW<Blackboard1024>(commander));
    state.Contacts.Count = 2;
    var contactSpan = MemoryMarshal.CreateSpan(
        ref Unsafe.As<SquadContactPoolSlots, SquadContact>(ref state.Contacts.Contacts), 16);
    contactSpan[0] = new SquadContact { PositionX = 10f, PositionY = 0f, PositionZ = 0f, ThreatScore = 0.8f };
    contactSpan[1] = new SquadContact { PositionX = 20f, PositionY = 0f, PositionZ = 5f, ThreatScore = 0.5f };

    var draw = new LineCapturingDrawBuilder();
    arbiter.BeginFrame();
    source.Emit(0.016f, draw);

    Assert.Equal(2, draw.SpherePositions.Count);
    Assert.Contains(draw.SpherePositions, p => MathF.Abs(p.X - 10f) < 0.01f);
    Assert.Contains(draw.SpherePositions, p => MathF.Abs(p.X - 20f) < 0.01f);
}
```

#### SC-P7-03-4: Budget shedding with 50 squads

```csharp
// SC-P7-03-4
[Fact]
public unsafe void BudgetShedding_50Squads_ChannelsShedFirst()
{
    using var repo = CreateTestRepo();
    // Budget = 1 ms; record 2 ms for Channels (shed first) before squad emit.
    var arbiter = new OverlayBudgetArbiter(1f);
    var source  = new SquadCoordinationOverlaySource(repo, arbiter);

    // Create 50 squad commander entities
    for (int i = 0; i < 50; i++)
    {
        var snap = new SquadCognitiveState();
        SetupSquadCommander(repo, snap);
    }

    arbiter.BeginFrame();
    bool channelsAllowed = arbiter.RecordAndCheck(AiOverlayFlags.Channels, 2f);
    Assert.False(channelsAllowed); // Channels shed

    // SquadAssignment is higher priority than Channels; must still be permitted
    Assert.True(arbiter.IsPermitted(AiOverlayFlags.SquadAssignment));

    var draw = new CountingDrawBuilder();
    source.Emit(0.016f, draw); // Should emit for all 50 squads (budget still allows SquadAssignment)
    Assert.True(draw.EmitCount >= 50);
}
```

---

## Testing Requirements

**Minimum test counts:**
- SC-P7-01-1: 2 tests (flag on / flag off)
- SC-P7-01-2: 1 test (color persistence)
- SC-P7-01-3: 1 test (Z extent)
- SC-P7-02-1: 1 test (on-task solid only)
- SC-P7-02-2: 1 test (vetoing dashed + label)
- SC-P7-02-3: 1 test (label update)
- SC-P7-03-1: 1 test (phase label update)
- SC-P7-03-2: 1 test (entry tick reset)
- SC-P7-03-3: 1 test (contact pool spheres)
- SC-P7-03-4: 1 test (budget shedding)

**Total minimum: 11 tests.**

**Quality bar:**
- Each test is self-contained (creates its own repo, arbiter, source)
- No test shares mutable state with another
- All tests must pass with 0 allocation warnings (the overlay source itself should not allocate
  on the hot path; the tests themselves may allocate freely)

---

## Build Verification

After implementation, run:

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests --filter "FullyQualifiedName~SquadCoordinationOverlay" --no-build
```

First build the whole solution to ensure no compilation errors:
```powershell
dotnet build IOS-IG-SimHost.sln
```

Expected: all 11+ new tests pass. No regressions in existing overlay tests.

---

## Report Requirements

Report file: `.dev/group-maneuvers/reports/BATCH-34-REPORT.md`

Include:
1. List of files created/modified
2. All test results (pass/fail counts)
3. Any deviations from the instructions (with justification)
4. Confirmation that the existing `OverlaySourceTests` tests still pass
