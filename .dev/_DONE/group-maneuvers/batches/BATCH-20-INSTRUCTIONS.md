# BATCH-20 Instructions — Squad Phase 0 prerequisites

**Batch ID:** BATCH-20
**Workstream:** group-maneuvers
**Phase tasks:** TASK-SQD-P0-01, TASK-SQD-P0-02, TASK-SQD-P0-03, TASK-SQD-P0-04, TASK-SQD-P0-05
**Design refs:**
- `Squad_Coordination_Design_v1_1.md` §3.1 (P0-01, P0-02)
- `Squad_Coordination_Design_v1_1.md` §8.0, §5.2 (P0-03, P0-04)
- `Step_1_5_TargetMemory_3D_Reconciliation.md` (already satisfied — do not re-implement)

---

## Context

This is the first batch for the Squad Coordination workstream. All Utility AI phases (P0–P6)
are complete and merged. The 3D Cognitive Spatial Awareness promotion is merged
(`TargetMemory.PositionsZ`, `EqsResult.PositionZ` are live). Step 1.5 reconciliation is
already satisfied — Utility readers use `Vector3.Distance` on `Position` components, not
on positional fields from `TargetMemory`, so no reader changes are needed.

BATCH-20 lands the five Phase-0 prerequisites:
- **P0-01** Shrink `AssignmentSlot` 64 B → 16 B and migrate call sites.
- **P0-02** Create `SquadCognitiveState` — the single 1024 B blackboard projection for squad.
- **P0-03** Add `ManeuverSelect = 3` to `DecisionKind` + UT0151 analyzer diagnostic.
- **P0-04** `DangerAreaDescriptor` + `IDangerAreaProvider` + `FakeDangerAreaProvider`.
- **P0-05** Phase-0 integration gate tests.

---

## MANDATORY READS before writing any code

Read ALL of these files in full before writing a single line:

1. `.dev/group-maneuvers/TASK-DETAIL.md` — tasks P0-01 through P0-05 with all success conditions
2. `.dev/group-maneuvers/Squad_Coordination_Design_v1_1.md` — entire document; pay attention to §3.1
3. `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentState.cs`
4. `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentSystem.cs`
5. `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputs.cs` lines 265–310 (IsAssignedToContext reader)
6. `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs` lines 280–300 (ReadAssignedTarget)
7. `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StandardInputReaderTests.cs` lines 335–420
8. `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCore.cs` lines 43–50 (DecisionKind enum)
9. `FDP/Toolkits/Fdp.Toolkits.Analyzers/SharedUtilityDiagnostics.cs`
10. `FDP/Toolkits/Fdp.Toolkits.Analyzers/UtilityAuthoringAnalyzer.cs`
11. `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` last 60 lines
12. `FDP/Toolkits/Fdp.Toolkits/Navigation/Fake/FakeNavmeshProvider.cs` first 80 lines (style reference)

**Also read** the Squad Coordination design — specifically:
- §3.1 for the full `SquadCognitiveState` layout with all sub-struct sizes
- §5.2 for `DangerAreaDescriptor` field list and `DangerAreaKind` enum

---

## Task A — P0-01: Shrink `AssignmentSlot` and migrate call sites

### A.1 Shrink `AssignmentSlot`

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentState.cs` (UPDATE)

Change `AssignmentSlot` from `[StructLayout(LayoutKind.Sequential, Size = 64)]` to a packed
16-byte layout:

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct AssignmentSlot
{
    public long  AssignedTargetHandle; // 8 B
    public float AssignmentScore;      // 4 B
    public byte  FocusFireCount;       // 1 B
    public byte  Flags;                // 1 B
    public ushort _pad;                // 2 B
    // Total: 16 B
}
```

- Remove the `Size = 64` from `LayoutKind.Sequential`. Add a `Debug.Assert(Unsafe.SizeOf<AssignmentSlot>() == 16)` inside the static constructor or at the top of the file (use a static initializer field if needed).
- `AssignmentSlotArray` stays `[InlineArray(16)]` — unchanged. New total: 16 × 16 = 256 B.

### A.2 Remove `ThreatMatrixAssignmentState.Project` and migrate call sites

`ThreatMatrixAssignmentState.Project(ref Blackboard1024)` is **removed** in this batch.
The whole-blackboard standalone projection is replaced by the embedded `.Assignment` field
inside `SquadCognitiveState` (Task B). The `ThreatMatrixAssignmentState` struct itself is
deleted; it is now just the `AssignmentSlotArray` embedded in `SquadCognitiveState`.

**Migration is mandatory and atomic — all call sites must be migrated in the same PR.**

Call sites to migrate (confirmed by grep):
- `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputs.cs` line 289:
  `ref var state = ref ThreatMatrixAssignmentState.Project(ref bb);`
  → `ref var state = ref SquadCognitiveState.Project(ref bb).Assignment;`
  The reader only uses `state.GetSlot(i)` — the method is on `AssignmentSlotArray`
  (see `GetSlot` helper below).
- `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentSystem.cs` line 56:
  same pattern → `ref var state = ref SquadCognitiveState.Project(ref bb).Assignment;`
  The system calls `state.GetSlot(i)`, `state.SetAssignment(...)`, `state.GetAssignedTarget(...)`.
  These methods must now live on `AssignmentSlotArray` (or a thin helper — see below).
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs` line 291:
  same pattern.
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StandardInputReaderTests.cs` lines 343, 362, 402:
  same pattern.

**Move `GetSlot`/`GetAssignedTarget`/`SetAssignment` to `AssignmentSlotArray`** (or add them
as extension methods in the same file). Example for `AssignmentSlotArray`:

```csharp
public ref AssignmentSlot GetSlot(int index)
    => ref MemoryMarshal.CreateSpan(
        ref Unsafe.As<AssignmentSlotArray, AssignmentSlot>(ref this), 16)[index];

public long GetAssignedTarget(int index) => GetSlot(index).AssignedTargetHandle;

public void SetAssignment(int index, ulong targetHandle, float score = 0f)
{
    ref var slot = ref GetSlot(index);
    slot.AssignedTargetHandle = (long)targetHandle;
    slot.AssignmentScore      = score;
}
```

After deleting `ThreatMatrixAssignmentState`, update `ThreatMatrixAssignmentSystem` to
operate on `ref AssignmentSlotArray state` (the call-site change handles this via
`SquadCognitiveState.Project(ref bb).Assignment`).

**SC-P0-01-1:** `sizeof(AssignmentSlot) == 16`  
**SC-P0-01-2:** `sizeof(AssignmentSlotArray) == 256`  
**SC-P0-01-3:** All existing `ThreatMatrixAssignmentSystem` and `LeaderAssignmentDecision` tests pass  
**SC-P0-01-4:** Round-trip test (see Task E)  
**SC-P0-01-5:** `ThreatMatrixAssignmentState.Project` symbol removed

---

## Task B — P0-02: `SquadCognitiveState`

### B.1 New sub-struct types

**File:** `FDP/Toolkits/Fdp.Toolkits/Squad/State/SquadCognitiveState.cs` (NEW FILE)

Create all types in this one file (they are small). Namespace: `Fdp.Toolkit.Squad`.

**ElementPartition** (32 bytes):
```csharp
[StructLayout(LayoutKind.Sequential)]
public struct ElementPartition
{
    // [InlineArray(16)] byte MemberElementIndex — 16 B
    public MemberElementIndexArray MemberElementIndex;
    public uint LastRepartitionTick; // 4 B
    // 12 B explicit pad to reach 32 B
    private uint _pad0;
    private uint _pad1;
    private uint _pad2;
}

[InlineArray(16)]
public struct MemberElementIndexArray { private byte _element; }
```

**SlotState** (8 bytes):
```csharp
[StructLayout(LayoutKind.Sequential)]
public struct SlotState
{
    public byte  ElementIndex;
    public byte  SlotKind;
    public ushort Flags;
    public uint  LastTransitionTick;
}
```

**SlotAssignmentArray** (96 bytes = 12 × 8):
```csharp
[InlineArray(12)]
public struct SlotAssignmentArray { private SlotState _element; }
```

**RoleSlot** (2 bytes):
```csharp
[StructLayout(LayoutKind.Sequential)]
public struct RoleSlot
{
    public byte RoleId;
    public byte _pad;
}
```

**RoleAssignmentArray** (32 bytes = 16 × 2):
```csharp
[InlineArray(16)]
public struct RoleAssignmentArray { private RoleSlot _element; }
```

**SquadContact** (32 bytes):
```csharp
[StructLayout(LayoutKind.Sequential)]
public struct SquadContact
{
    public long   EntityId;          // 8 B — network-stable entity id
    public float  PositionX;         // 4 B
    public float  PositionY;         // 4 B
    public float  PositionZ;         // 4 B  (3D promotion)
    public float  ThreatScore;       // 4 B
    public uint   LastSeenTick;      // 4 B
    public ushort SourceMembersMask; // 2 B  — bitmask of roster slots that contributed
    public ushort Flags;             // 2 B
    // Total: 32 B
}
```

**SquadContactPoolSlots** (512 bytes = 16 × 32):
```csharp
[InlineArray(16)]
public struct SquadContactPoolSlots { private SquadContact _element; }
```

**SquadContactPool** (592 bytes):
```csharp
[StructLayout(LayoutKind.Sequential)]
public struct SquadContactPool
{
    public int  Count;               // 4 B
    public uint LastMergeTick;       // 4 B
    // 80 B headroom reserved for future fields — declared as explicit padding:
    private ulong _res0, _res1, _res2, _res3, _res4; // 5 × 8 = 40 B
    private ulong _res5, _res6, _res7, _res8, _res9; // 5 × 8 = 40 B
    public SquadContactPoolSlots Contacts; // 512 B
    // Total: 4 + 4 + 80 + 512 = 600 B  -- see note below
}
```

> **Important layout note:** The exact sub-struct sizes must be verified at test time. The
> task success condition SC-P0-02-2 pins specific offsets. Work out the final sizes so that:
> - `Elements` is at offset 16
> - `Slots` is at offset 48
> - `Roles` is at offset 144
> - `Assignment` is at offset 176
> - `Contacts` is at offset 432
> - `sizeof(SquadCognitiveState) == 1024`
>
> If the sub-struct sizes above don't add up precisely, adjust `SquadContactPool`'s padding
> fields to fill the remaining budget: `1024 - 432 = 592 B` for `SquadContactPool`.
> If the pool as shown is 600 B, reduce padding by 1 `ulong` (8 B) to get 592 B.
>
> **Pin the offsets in a single place** — a `const int` table at the top of the file:
> ```csharp
> public static class SquadCognitiveStateOffsets
> {
>     public const int Elements   = 16;
>     public const int Slots      = 48;
>     public const int Roles      = 144;
>     public const int Assignment = 176;
>     public const int Contacts   = 432;
>     public const int TotalSize  = 1024;
> }
> ```

### B.2 `SquadCognitiveState`

```csharp
[StructLayout(LayoutKind.Sequential)]
[DataPolicy(DataPolicyKind.NoSave)]
public struct SquadCognitiveState
{
    // --- maneuver scalars (16 B) ---
    public ushort ManeuverKind;        // 2 B
    public ushort PhaseId;             // 2 B
    public uint   ActiveFeatureId;     // 4 B
    public uint   PhaseEnteredTick;    // 4 B
    public uint   Flags;               // 4 B
    // Total scalars: 16 B, @0

    // --- element partition (32 B) @16 ---
    public ElementPartition Elements;

    // --- slot assignment (96 B) @48 ---
    public SlotAssignmentArray Slots;

    // --- role assignment (32 B) @144 ---
    public RoleAssignmentArray Roles;

    // --- fire/threat assignment (256 B) @176 ---
    public AssignmentSlotArray Assignment;

    // --- shared-awareness sub-region (592 B) @432 ---
    public SquadContactPool Contacts;

    /// <summary>
    /// Projects this struct as an overlay onto the leader's <see cref="Blackboard1024"/>.
    /// </summary>
    public static ref SquadCognitiveState Project(ref Blackboard1024 bb)
        => ref Blackboard1024.Project<SquadCognitiveState>(ref bb);
}
```

Check: does `DataPolicy` / `DataPolicyAttribute` exist in the codebase? Search for it before adding it.
If not, omit the attribute and add a code comment: `// NoSave: transient squad working state.`

### B.3 `SquadStateMarker` component and GlobalComponentIds

**File:** `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` (UPDATE)

Add a new block:
```csharp
// ---- Squad coordination components (256–299) ----------------------------
/// <summary><c>SquadStateMarker</c> — zero-byte tag on squad commander entities. NoSave.</summary>
public const int SquadStateMarker = 256;
```

Also add `256–299 Squad coordination` to the block table in the class doc comment.

**File:** `FDP/Toolkits/Fdp.Toolkits/Squad/State/SquadCognitiveState.cs` (UPDATE — same file)

Add the tag struct (at the bottom of the same file is fine):
```csharp
/// <summary>
/// Zero-byte tag component attached to squad commander entities.
/// Enables cheap ECS queries to find squad-state-bearing commanders.
/// </summary>
[ComponentId(GlobalComponentIds.SquadStateMarker)]
[StructLayout(LayoutKind.Sequential, Size = 1)]
public struct SquadStateMarker { }
```

**SC-P0-02-1:** `sizeof(SquadCognitiveState) <= 1024`  
**SC-P0-02-2:** Pinned offset-table test  
**SC-P0-02-3:** Aliasing test (`Project` write → raw `Blackboard1024.Memory` read)  
**SC-P0-02-4:** `default(SquadCognitiveState)` zero-initializes  
**SC-P0-02-5:** Offset-collision check (omit if no existing squad system uses the blackboard — the first Phase-0 commit is trivially collision-free; document this in a comment)

---

## Task C — P0-03: `ManeuverSelect` + UT0151

### C.1 Extend `DecisionKind`

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCore.cs` (UPDATE)

```csharp
public enum DecisionKind : byte
{
    ThreatRanking,   // = 0
    WeaponSelection, // = 1
    PostureSelect,   // = 2
    ManeuverSelect,  // = 3 — squad-tier commander decision
}
```

The source generator (`UtilityDecisionGenerator`) already handles any `DecisionKind` value
generically (it reads the enum member name at compile time). **No generator changes needed.**

### C.2 Add UT0151 to `SharedUtilityDiagnostics`

**File:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/SharedUtilityDiagnostics.cs` (UPDATE)

Add after UT0150:
```csharp
// UT0151: ManeuverSelect uses non-Self context (no candidate set on commander)
internal static readonly DiagnosticDescriptor UT0151_ManeuverSelectInvalidContext =
    new DiagnosticDescriptor(
        id: "UT0151",
        title: "ManeuverSelect must use Self context",
        messageFormat: "Class ''{0}'' is a ManeuverSelect decision but option uses ''{1}'' input context; ManeuverSelect runs on the commander without a candidate set — scope all inputs to Self or Leader",
        category: "Fdp.UtilityAI",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
```

### C.3 Add UT0151 check to `UtilityAuthoringAnalyzer`

**File:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/UtilityAuthoringAnalyzer.cs` (UPDATE)

1. Add `SharedUtilityDiagnostics.UT0151_ManeuverSelectInvalidContext` to `SupportedDiagnostics`.

2. In `AnalyzeNamedTypeStructural`, after the `CheckZeroOptions` call, add:
   ```csharp
   CheckManeuverSelectContextBinding(context, type, buildMethod, buildSyntax);
   ```

3. Add the helper method:
   ```csharp
   private static void CheckManeuverSelectContextBinding(
       SymbolAnalysisContext context,
       INamedTypeSymbol type,
       IMethodSymbol buildMethod,
       SyntaxNode buildSyntax)
   {
       if (!IsManeuverSelectDecision(type)) return;
   
       // Syntactic scan: any MemberAccessExpressionSyntax whose member name
       // is "Candidate" or "Target" indicates a non-Self context binding.
       foreach (var ma in buildSyntax.DescendantNodes()
                                     .OfType<MemberAccessExpressionSyntax>())
       {
           var name = ma.Name.Identifier.Text;
           if (name == "Candidate" || name == "Target")
           {
               context.ReportDiagnostic(Diagnostic.Create(
                   SharedUtilityDiagnostics.UT0151_ManeuverSelectInvalidContext,
                   buildMethod.Locations.FirstOrDefault(),
                   type.Name,
                   name));
               return; // one diagnostic per method is enough
           }
       }
   }
   ```

4. Add the helper predicate (mirror of `IsPostureSelectDecision`):
   ```csharp
   private static bool IsManeuverSelectDecision(INamedTypeSymbol type)
   {
       foreach (var attr in type.GetAttributes())
       {
           if (attr.AttributeClass == null) continue;
           if (attr.AttributeClass.Name != "UtilityDecisionAttribute") continue;
           if (attr.ConstructorArguments.Length >= 3)
           {
               var kindArg = attr.ConstructorArguments[2];
               // ManeuverSelect == 3
               if (kindArg.Value is byte bv && bv == 3) return true;
               if (kindArg.Value is int iv && iv == 3) return true;
               if (kindArg.Value is short sv && sv == 3) return true;
               if (kindArg.Value != null && kindArg.Value.ToString() == "3") return true;
           }
       }
       return false;
   }
   ```

**SC-P0-03-1:** `DecisionKind.ManeuverSelect == 3`; prior values unchanged  
**SC-P0-03-2:** Source-gen test: stub `ManeuverSelect` decision lands in catalog with correct Kind  
**SC-P0-03-3:** UT0151 fires on `ManeuverSelect` + `Ctx.Candidate` or `Ctx.Target`  
**SC-P0-03-4:** Pre-existing analyzer tests green (no UT0151 regressions on other kinds)

---

## Task D — P0-04: `FakeDangerAreaProvider`

### D.1 New types

**New folder:** `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/`

**File:** `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/DangerAreaDescriptor.cs` (NEW)

```csharp
using System.Numerics;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Squad.DangerArea
{
    public enum DangerAreaKind : byte
    {
        OpenGround     = 0,
        StreetCrossing = 1,
        Intersection   = 2,
        ChokePoint     = 3,
        CrestLine      = 4,
    }

    /// <summary>
    /// Descriptor for one tactical danger area, as identified by the navmesh or hand-authored
    /// via <see cref="FakeDangerAreaProvider"/>. Size is pinned at
    /// <see cref="PinnedSize"/> bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DangerAreaDescriptor
    {
        /// <summary>Pinned size assertion — verified by a unit test.</summary>
        public const int PinnedSize = 72;

        public uint          FeatureId;       // FNV-1a-32 of the stable key string
        public float         ThreatRating;    // 0..1 normalised
        public DangerAreaKind Kind;
        private byte         _pad0;
        private ushort       _pad1;
        public Vector3       Center;          // world-space centre (XYZ)
        public Vector2       ExtentsXY;       // half-extents on X and Y axes
        public float         AngleRad;        // orientation of the major axis
        public float         ZFloor;          // lower Z bound
        public float         ZCeiling;        // upper Z bound
        public Vector3       NearSideHandle;  // entry waypoint
        public Vector3       FarSideHandle;   // exit waypoint
    }
}
```

Verify `sizeof(DangerAreaDescriptor) == 72` (field-by-field: 4+4+1+1+2+12+8+4+4+4+12+12 = 68 B...
The exact size depends on alignment. If natural alignment gives a different number, adjust the
`_pad` fields or the `PinnedSize` constant to match actual `sizeof`. **Run the size test first,
then pin it.**

**File:** `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/IDangerAreaProvider.cs` (NEW)

```csharp
using System;
using Fdp.Core;

namespace Fdp.Toolkit.Squad.DangerArea
{
    public interface IDangerAreaProvider
    {
        void Refresh(EntityRepository repo, Entity squadCommander,
                     Span<DangerAreaDescriptor> dest, out int count);
    }
}
```

**File:** `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/Fake/FakeDangerAreaProvider.cs` (NEW)

```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Fdp.Core;

namespace Fdp.Toolkit.Squad.DangerArea.Fake
{
    /// <summary>
    /// In-memory danger-area provider for unit tests.
    /// Features are authored via <see cref="Builder"/> and written into the caller's span
    /// on <see cref="Refresh"/> with zero managed allocations.
    /// </summary>
    public sealed class FakeDangerAreaProvider : IDangerAreaProvider
    {
        private readonly List<DangerAreaDescriptor> _features;

        private FakeDangerAreaProvider(List<DangerAreaDescriptor> features)
            => _features = features;

        public void Refresh(EntityRepository repo, Entity squadCommander,
                            Span<DangerAreaDescriptor> dest, out int count)
        {
            count = Math.Min(_features.Count, dest.Length);
            for (int i = 0; i < count; i++)
                dest[i] = _features[i];
        }

        // ── Fluent builder ───────────────────────────────────────────────────────

        public sealed class Builder
        {
            private readonly List<DangerAreaDescriptor> _list = new();

            public Builder AddStreetCrossing(string key, Vector3 near, Vector3 far,
                float threatRating = 0.7f)
                => Add(key, DangerAreaKind.StreetCrossing, near, far, threatRating);

            public Builder AddCrestLine(string key, Vector3 near, Vector3 far,
                float threatRating = 0.5f)
                => Add(key, DangerAreaKind.CrestLine, near, far, threatRating);

            public Builder AddIntersection(string key, Vector3 near, Vector3 far,
                float threatRating = 0.8f)
                => Add(key, DangerAreaKind.Intersection, near, far, threatRating);

            public Builder AddChokePoint(string key, Vector3 near, Vector3 far,
                float threatRating = 0.9f)
                => Add(key, DangerAreaKind.ChokePoint, near, far, threatRating);

            public Builder AddOpenGround(string key, Vector3 near, Vector3 far,
                float threatRating = 0.3f)
                => Add(key, DangerAreaKind.OpenGround, near, far, threatRating);

            private Builder Add(string key, DangerAreaKind kind, Vector3 near, Vector3 far,
                float threatRating)
            {
                var center = (near + far) * 0.5f;
                _list.Add(new DangerAreaDescriptor
                {
                    FeatureId     = Fnv1a32(key),
                    ThreatRating  = threatRating,
                    Kind          = kind,
                    Center        = center,
                    ExtentsXY     = new Vector2(
                        Math.Abs(far.X - near.X) * 0.5f,
                        Math.Abs(far.Y - near.Y) * 0.5f),
                    AngleRad      = 0f,
                    ZFloor        = Math.Min(near.Z, far.Z),
                    ZCeiling      = Math.Max(near.Z, far.Z),
                    NearSideHandle = near,
                    FarSideHandle  = far,
                });
                return this;
            }

            public FakeDangerAreaProvider Build()
                => new FakeDangerAreaProvider(new List<DangerAreaDescriptor>(_list));

            // FNV-1a-32 over UTF-8 bytes of the key string.
            public static uint Fnv1a32(string key)
            {
                uint hash = 2166136261u;
                foreach (byte b in Encoding.UTF8.GetBytes(key))
                {
                    hash ^= b;
                    hash *= 16777619u;
                }
                return hash;
            }
        }
    }
}
```

**SC-P0-04-1:** `sizeof(DangerAreaDescriptor) == PinnedSize` (whatever value the test finds)  
**SC-P0-04-2:** Builder with 3 features → `Refresh` writes exactly 3 descriptors with correct Kind and FeatureId  
**SC-P0-04-3:** `FeatureId == Fnv1a32("street-east-01")` pin  
**SC-P0-04-4:** 10^6 `Refresh` calls allocate zero managed bytes (use `GC.GetTotalAllocatedBytes` before/after)

---

## Task E — P0-05: Phase-0 integration gate tests

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/SquadPhase0IntegrationTests.cs` (NEW)

Use xUnit. Namespace: `Fdp.Toolkit.Squad.Tests`. Test class: `SquadPhase0IntegrationTests`.

**Also add** the following unit test files:

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/AssignmentSlotLayoutTests.cs` (NEW)

Tests for SC-P0-01-1, SC-P0-01-2, SC-P0-01-4:

```csharp
[Fact]
public void AssignmentSlot_SizeIs16()
    => Assert.Equal(16, Unsafe.SizeOf<AssignmentSlot>());

[Fact]
public void AssignmentSlotArray_SizeIs256()
    => Assert.Equal(256, Unsafe.SizeOf<AssignmentSlotArray>());

[Fact]
public void AssignmentSlot_RoundTrip_Slot7_NoAliasing()
{
    // Allocate on stack; write slot 7; read back; verify adjacent slots zero.
    var arr = default(AssignmentSlotArray);
    arr.GetSlot(7).AssignedTargetHandle = 0xDEAD_BEEF_0000_0001L;
    arr.GetSlot(7).AssignmentScore      = 0.42f;
    arr.GetSlot(7).FocusFireCount       = 3;
    arr.GetSlot(7).Flags                = 0x05;

    Assert.Equal(0xDEAD_BEEF_0000_0001L, arr.GetSlot(7).AssignedTargetHandle);
    Assert.Equal(0.42f, arr.GetSlot(7).AssignmentScore, 5);
    Assert.Equal(3, arr.GetSlot(7).FocusFireCount);
    Assert.Equal((byte)0x05, arr.GetSlot(7).Flags);
    Assert.Equal(0L, arr.GetSlot(6).AssignedTargetHandle); // no aliasing
    Assert.Equal(0L, arr.GetSlot(8).AssignedTargetHandle); // no aliasing
}
```

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/SquadCognitiveStateLayoutTests.cs` (NEW)

Tests for SC-P0-02-1 through SC-P0-02-4:

```csharp
[Fact]
public void SquadCognitiveState_SizeAtMost1024()
    => Assert.True(Unsafe.SizeOf<SquadCognitiveState>() <= 1024);

[Fact]
public unsafe void SquadCognitiveState_PinnedOffsets()
{
    var s = default(SquadCognitiveState);
    fixed (SquadCognitiveState* p = &s)
    {
        // Elements at offset SquadCognitiveStateOffsets.Elements
        fixed (ElementPartition* ep = &s.Elements)
            Assert.Equal(SquadCognitiveStateOffsets.Elements,
                (int)((byte*)ep - (byte*)p));

        fixed (SlotAssignmentArray* sp = &s.Slots)
            Assert.Equal(SquadCognitiveStateOffsets.Slots,
                (int)((byte*)sp - (byte*)p));

        fixed (RoleAssignmentArray* rp = &s.Roles)
            Assert.Equal(SquadCognitiveStateOffsets.Roles,
                (int)((byte*)rp - (byte*)p));

        fixed (AssignmentSlotArray* ap = &s.Assignment)
            Assert.Equal(SquadCognitiveStateOffsets.Assignment,
                (int)((byte*)ap - (byte*)p));

        fixed (SquadContactPool* cp = &s.Contacts)
            Assert.Equal(SquadCognitiveStateOffsets.Contacts,
                (int)((byte*)cp - (byte*)p));
    }
}

[Fact]
public void SquadCognitiveState_DefaultIsZero()
{
    var s = default(SquadCognitiveState);
    Assert.Equal(0, (int)s.ManeuverKind);
    Assert.Equal(0L, s.Assignment.GetSlot(0).AssignedTargetHandle);
}

[Fact]
public unsafe void SquadCognitiveState_ProjectAliasesBlackboard()
{
    var bb = default(Blackboard1024);
    ref var s = ref SquadCognitiveState.Project(ref bb);
    s.ManeuverKind = 7;
    // Read the same bytes directly from the raw blackboard memory.
    fixed (Blackboard1024* p = &bb)
    {
        ushort raw = *(ushort*)((byte*)p);
        Assert.Equal(7, raw);
    }
}
```

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/DangerAreaProviderTests.cs` (NEW)

Tests for SC-P0-04-1 through SC-P0-04-4:

```csharp
[Fact]
public void DangerAreaDescriptor_SizeMatchesPinnedConstant()
    => Assert.Equal(DangerAreaDescriptor.PinnedSize, Unsafe.SizeOf<DangerAreaDescriptor>());

[Fact]
public void FakeDangerAreaProvider_Builder_ThreeFeatures_CorrectKindAndFeatureId()
{
    var provider = new FakeDangerAreaProvider.Builder()
        .AddStreetCrossing("street-alpha", new Vector3(0,0,0), new Vector3(10,0,0))
        .AddCrestLine("crest-bravo",      new Vector3(0,0,0), new Vector3( 0,10,5))
        .AddChokePoint("choke-charlie",   new Vector3(0,0,0), new Vector3( 5, 5,0))
        .Build();

    Span<DangerAreaDescriptor> buf = stackalloc DangerAreaDescriptor[8];
    provider.Refresh(default, default, buf, out int count);

    Assert.Equal(3, count);
    Assert.Equal(DangerAreaKind.StreetCrossing, buf[0].Kind);
    Assert.Equal(DangerAreaKind.CrestLine,      buf[1].Kind);
    Assert.Equal(DangerAreaKind.ChokePoint,     buf[2].Kind);
    Assert.Equal(FakeDangerAreaProvider.Builder.Fnv1a32("street-alpha"), buf[0].FeatureId);
}

[Fact]
public void FakeDangerAreaProvider_FeatureId_PinnedForStreetEast01()
{
    uint id = FakeDangerAreaProvider.Builder.Fnv1a32("street-east-01");
    // Pin: run once, record, paste back here as the expected value.
    Assert.Equal(id, FakeDangerAreaProvider.Builder.Fnv1a32("street-east-01")); // stable across runs
    Assert.NotEqual(0u, id);
    // Also verify it differs from a different key.
    Assert.NotEqual(id, FakeDangerAreaProvider.Builder.Fnv1a32("street-east-02"));
}

[Fact]
public void FakeDangerAreaProvider_Refresh_ZeroAllocs()
{
    var provider = new FakeDangerAreaProvider.Builder()
        .AddStreetCrossing("s1", Vector3.Zero, Vector3.One)
        .Build();

    Span<DangerAreaDescriptor> buf = stackalloc DangerAreaDescriptor[4];
    Entity dummy = default;

    // Warm up
    provider.Refresh(null!, dummy, buf, out _);

    long before = GC.GetTotalAllocatedBytes(precise: false);
    for (int i = 0; i < 1_000_000; i++)
        provider.Refresh(null!, dummy, buf, out _);
    long after = GC.GetTotalAllocatedBytes(precise: false);

    Assert.Equal(0L, after - before);
}
```

**`SquadPhase0IntegrationTests`** covers SC-P0-05-1 (three integration tests):

```csharp
[Fact]
public unsafe void Layout_WriteManeuverKind_ReadFromRawBlackboard()
{
    // Spawn a simple entity repo, add Blackboard1024 + UnitRoster, project
    // SquadCognitiveState, write ManeuverKind = 1, write an AssignmentSlot
    // for member 0, then read back from the same bb bytes.
    var repo = new EntityRepository();
    var entity = repo.CreateEntity();
    repo.AddComponent(entity, new UnitRoster { Count = 3 });
    repo.AddComponent(entity, new Blackboard1024());

    ref var bb = ref repo.GetComponentRW<Blackboard1024>(entity);
    ref var state = ref SquadCognitiveState.Project(ref bb);
    state.ManeuverKind = 1;
    state.Assignment.SetAssignment(0, targetHandle: 0xABCD_EF01UL, score: 0.5f);

    // Re-read from raw bytes.
    ref var state2 = ref SquadCognitiveState.Project(ref bb);
    Assert.Equal(1, (int)state2.ManeuverKind);
    Assert.Equal((long)0xABCD_EF01UL, state2.Assignment.GetAssignedTarget(0));
}

[Fact]
public void DangerArea_RoundTrip_4Features()
{
    var provider = new FakeDangerAreaProvider.Builder()
        .AddStreetCrossing("a", Vector3.Zero, new Vector3(5,0,0))
        .AddCrestLine("b",      Vector3.Zero, new Vector3(0,5,3))
        .AddChokePoint("c",     Vector3.Zero, new Vector3(3,3,0))
        .AddOpenGround("d",     Vector3.Zero, new Vector3(20,20,0))
        .Build();

    Span<DangerAreaDescriptor> buf = stackalloc DangerAreaDescriptor[8];
    provider.Refresh(default, default, buf, out int count);

    Assert.Equal(4, count);
    Assert.Equal(DangerAreaKind.StreetCrossing, buf[0].Kind);
    Assert.Equal(DangerAreaKind.OpenGround,     buf[3].Kind);
    // Verify FeatureId / Center / ZFloor / ZCeiling round-trip.
    Assert.Equal(FakeDangerAreaProvider.Builder.Fnv1a32("b"), buf[1].FeatureId);
    Assert.Equal(3f, buf[1].ZCeiling, 5);
}

[Fact]
public void ManeuverSelect_InCatalog_WithCorrectKind()
{
    // Verify DecisionKind.ManeuverSelect == 3.
    Assert.Equal((byte)3, (byte)DecisionKind.ManeuverSelect);
    // Verify prior values are stable.
    Assert.Equal((byte)0, (byte)DecisionKind.ThreatRanking);
    Assert.Equal((byte)2, (byte)DecisionKind.PostureSelect);
}
```

---

## Test project considerations

The Squad tests go in the existing `Fdp.Toolkits.Tests` project under a new `Squad/` folder.
Check whether `AllowUnsafeBlocks` is set in the test project csproj — if not, add it (the
pinned-offset tests use `unsafe` and `fixed`). Check the `Fdp.Toolkits.Tests.csproj`.

The production squad code goes in `Fdp.Toolkits` under a new `Squad/` folder tree. No new
.csproj files are needed — the existing projects already exist.

After completing all tasks, run:
```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --verbosity quiet
```

Expected: all new squad tests pass. Pre-existing failures (BicycleModel, SimTransformBridge,
GizmoSettingsPersistence, Navigation frustration watchdog) are unrelated — **do not count them
as regressions**. The existing `ThreatMatrixAssignmentSystem` tests (in `StarterPackIntegrationTests`)
must continue to pass after the P0-01 migration.

---

## Test count target

Minimum **13 new tests** across the 4 new test files:
- `AssignmentSlotLayoutTests`: 3
- `SquadCognitiveStateLayoutTests`: 4
- `DangerAreaProviderTests`: 4
- `SquadPhase0IntegrationTests`: 3 (or merge into the layout/provider files if preferred)

The pre-existing Utility + Squad generator analyzer tests must remain green.

---

## Report submission

Submit your batch report to: `.dev/group-maneuvers/reports/BATCH-20-REPORT.md`

If you have questions: `.dev/group-maneuvers/questions/BATCH-20-QUESTIONS.md`

Include in your report:
- Which files were created/modified
- Any layout surprises (actual `sizeof` values vs. initial estimates)
- Any issues encountered and how you resolved them
- The exact FNV-1a-32 hash you pinned for `"street-east-01"`
- Final test count (new tests passing / total project passing)
- Suggested commit message
