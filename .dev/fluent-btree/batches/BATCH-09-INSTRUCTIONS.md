# BATCH-09: Phase 5 Sample Project — Headless Logic (FBT-040, 041, 042, 044)

**Batch Number:** BATCH-09
**Tasks:** FBT-040, FBT-041, FBT-042, FBT-044
**Phase:** Phase 5 — Sample Project (headless/logic parts)
**Estimated Effort:** 4–6 hours
**Dependencies:** BATCH-08 complete (FastBTree commit 5c192c3, parent 4221cc7)

---

## Mandatory Reading (in order)

Read these files BEFORE writing any code:

1. `.dev/fluent-btree/TASK-DETAIL.md` — §TASK-FBT-040 through TASK-FBT-044
2. `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BehaviorTreeState.cs` — understand `AsyncData` property
3. `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs` — understand `Condition<TValue>`, `Action<TValue>`, `Compile`, `GetRegistry`
4. `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/ReusableDelegates.cs` — `ReusableConditionDelegate<TValue, TContext>`, `ReusableActionDelegate<TValue, TContext>`
5. `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/NodeLogicDelegate.cs` — `NodeLogicDelegate<TBlackboard, TContext>`
6. `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Attributes/BTreeActionAttribute.cs`, `BTreeConditionAttribute.cs`, `BTreeDefinitionAttribute.cs`
7. `FDP/ExtDeps/FastBTree/src/Fbt.SourceGen/BTreeDefinitionGenerator.cs` — the `[BTreeDefinition]` method MUST return `BehaviorTreeBlob` and have zero parameters
8. `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.Console/Fbt.Examples.Console.csproj` — reference for project file format
9. `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj` — reference for test project format
10. `FDP/ExtDeps/FastBTree/FastBTree.sln` — you must add new projects to this solution

---

## Mandatory Workflow

1. Read all files above.
2. Implement in the order shown (Step 1 → Step 5).
3. Build after each step; fix errors before proceeding.
4. Run tests after Step 5; all must pass.
5. Write the report, then commit.

---

## Step 1: Create `Fbt.Examples.FluentBTree` project

**File:** `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/Fbt.Examples.FluentBTree.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Fbt.Kernel\Fbt.Kernel.csproj" />
    <ProjectReference Include="..\..\src\Fbt.Compiler\Fbt.Compiler.csproj" />
    <ProjectReference Include="..\..\src\Fbt.SourceGen\Fbt.SourceGen.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

</Project>
```

Create a minimal `Program.cs` (entry point — visual app is FBT-043, this batch is headless):

**File:** `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/Program.cs`

```csharp
using System;
using Fbt.Examples.FluentBTree;

var interpreter = AmbushTree.CreateInterpreter();
var bb = new CombatBlackboard { AmmoCount = 5, ThreatVisible = true, EngagementRange = 50f };
var state = new Fbt.BehaviorTreeState();
var ctx = new CombatContext { DeltaTime = 0.016f };

Console.WriteLine("Ambush_BT demo (5 ticks):");
for (int i = 0; i < 5; i++)
{
    var result = interpreter.Tick(ref bb, ref state, ref ctx);
    Console.WriteLine($"  Tick {i + 1}: {result}  AmmoCount={bb.AmmoCount}");
}
```

**Update `FastBTree.sln`:** Add the project to the `examples` solution folder. Open `FDP/ExtDeps/FastBTree/FastBTree.sln` and add entries in the same style as `Fbt.Examples.Console`. Use a new GUID for the project. Add it both to the project list and `GlobalSection(ProjectConfigurationPlatforms)`.

---

## Step 2: FBT-040 — `CombatBlackboard` and `CombatContext`

**File:** `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/CombatBlackboard.cs`

```csharp
using System.Runtime.InteropServices;
using Fbt;
using System.Numerics;

namespace Fbt.Examples.FluentBTree
{
    [StructLayout(LayoutKind.Sequential)]
    public struct CombatBlackboard
    {
        public int AmmoCount;
        public bool ThreatVisible;
        // Padding: 3 bytes to align EngagementRange at offset 8
        public byte _pad0, _pad1, _pad2;
        public float EngagementRange;
    }

    public struct CombatContext : IAIContext
    {
        public float DeltaTime { get; set; }
        public float Time { get; set; }
        public int FrameCount { get; set; }

        public int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance) => 0;
        public RaycastResult GetRaycastResult(int requestId) => new RaycastResult { IsReady = true };
        public int RequestPath(Vector3 from, Vector3 to) => 0;
        public PathResult GetPathResult(int requestId) => new PathResult { IsReady = true, Success = true };
        public float GetFloatParam(int index) => 0f;
        public int GetIntParam(int index) => 0;
    }
}
```

**Constraints:**
- `CombatBlackboard` must be `[StructLayout(LayoutKind.Sequential)]` so `Marshal.OffsetOf` returns reliable results.
- `Marshal.OffsetOf<CombatBlackboard>("AmmoCount")` must == 0.
- `Marshal.OffsetOf<CombatBlackboard>("EngagementRange")` must == 8 (int=4 + bool=1 + pad=3).
- Check `IAIContext`, `RaycastResult`, `PathResult` by reading `Fbt.Kernel` before implementing.

---

## Step 3: FBT-041 — `CombatActions`

**File:** `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/CombatActions.cs`

Key design decisions (read before implementing):

- `CheckAmmo`, `HasThreat`, `AimAndFire` use the **3-parameter reusable** delegate signatures (`ReusableConditionDelegate<TValue, CombatContext>` / `ReusableActionDelegate<TValue, CombatContext>`). The source generator will emit a BTree001 informational diagnostic for these (skipping them from auto-registration); that is expected and correct.
- `HoldPosition` uses the **4-parameter** `NodeLogicDelegate<CombatBlackboard, CombatContext>` signature so it IS auto-registered by the source generator and can be passed directly to `BTreeBuilder.Action(delegate)`.
- `HoldPosition` uses `state.AsyncData` (a `ulong` property on `BehaviorTreeState`) as a tick counter. `AsyncData` is accessible from safe code via its property wrapper.

```csharp
using System;
using Fbt;
using Fbt.Compiler;

namespace Fbt.Examples.FluentBTree
{
    public static class CombatActions
    {
        // 3-param: ReusableConditionDelegate<int, CombatContext>
        // Returns Success if ammo > 0, Failure otherwise.
        [BTreeCondition]
        public static NodeStatus CheckAmmo(ref int ammo, ref BehaviorTreeState state, ref CombatContext ctx)
        {
            return ammo > 0 ? NodeStatus.Success : NodeStatus.Failure;
        }

        // 3-param: ReusableConditionDelegate<bool, CombatContext>
        // Returns Success if threat is visible, Failure otherwise.
        [BTreeCondition]
        public static NodeStatus HasThreat(ref bool threatVisible, ref BehaviorTreeState state, ref CombatContext ctx)
        {
            return threatVisible ? NodeStatus.Success : NodeStatus.Failure;
        }

        // 3-param: ReusableActionDelegate<int, CombatContext>
        // Decrements ammo by 1 and returns Success.
        [BTreeAction]
        public static NodeStatus AimAndFire(ref int ammo, ref BehaviorTreeState state, ref CombatContext ctx)
        {
            ammo--;
            Console.WriteLine($"[AimAndFire] Shot fired. Ammo remaining: {ammo}");
            return NodeStatus.Success;
        }

        // 4-param: NodeLogicDelegate<CombatBlackboard, CombatContext>
        // Returns Running for the first tick, Success on the second tick.
        // Uses state.AsyncData as a per-node tick counter.
        [BTreeAction]
        public static NodeStatus HoldPosition(
            ref CombatBlackboard bb,
            ref BehaviorTreeState state,
            ref CombatContext ctx,
            int param)
        {
            ulong tick = state.AsyncData + 1;
            state.AsyncData = tick;
            if (tick < 2)
            {
                Console.WriteLine("[HoldPosition] Holding...");
                return NodeStatus.Running;
            }
            state.AsyncData = 0;
            Console.WriteLine("[HoldPosition] Done holding.");
            return NodeStatus.Success;
        }
    }
}
```

---

## Step 4: FBT-042 — `AmbushTree`

**File:** `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/AmbushTree.cs`

```csharp
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;

namespace Fbt.Examples.FluentBTree
{
    public static class AmbushTree
    {
        // Creates a pre-wired builder with all delegates registered.
        // Call Compile() + GetRegistry() on the returned builder to get a usable interpreter.
        public static BTreeBuilder<CombatBlackboard, CombatContext> CreateBuilder()
        {
            return new BTreeBuilder<CombatBlackboard, CombatContext>()
                .Selector(s => s
                    .Sequence(seq => seq
                        .Condition(dto => dto.ThreatVisible, CombatActions.HasThreat)
                        .Condition(dto => dto.AmmoCount, CombatActions.CheckAmmo)
                        .Action(dto => dto.AmmoCount, CombatActions.AimAndFire)
                    )
                    .Action(CombatActions.HoldPosition)
                );
        }

        // Source-generator entry point. Must return BehaviorTreeBlob with zero parameters.
        // Called by the generated FbtTreeCatalog.GetAmbush_BT().
        [BTreeDefinition("Ambush_BT")]
        public static BehaviorTreeBlob BuildAmbushTree()
        {
            return CreateBuilder().Compile("Ambush_BT");
        }

        // Creates a fully-wired Interpreter ready for ticking.
        // Use this in the sample app and in tests.
        public static Interpreter<CombatBlackboard, CombatContext> CreateInterpreter()
        {
            var builder = CreateBuilder();
            var blob = builder.Compile("Ambush_BT");
            return new Interpreter<CombatBlackboard, CombatContext>(blob, builder.GetRegistry());
        }
    }
}
```

**Success Conditions to verify (from TASK-DETAIL.md):**
- SC1: `AmbushTree.BuildAmbushTree().TreeName == "Ambush_BT"` ← verify via the `BehaviorTreeBlob.TreeName` property.
- SC2: 6 nodes in order: Selector(0), Sequence(1), Condition(2), Condition(3), Action(4), Action(5).

---

## Step 5: FBT-044 — Tests

Add tests to the EXISTING `Fbt.Tests` project. You must:
1. Add a `<ProjectReference>` to `Fbt.Examples.FluentBTree` in `Fbt.Tests.csproj`.
2. Create `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/SampleProjectTests.cs`.

**`Fbt.Tests.csproj` addition:**
```xml
<ProjectReference Include="..\..\examples\Fbt.Examples.FluentBTree\Fbt.Examples.FluentBTree.csproj" />
```

**`SampleProjectTests.cs`:**

```csharp
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Examples.FluentBTree;

namespace Fbt.Tests.Unit
{
    public class SampleProjectTests
    {
        // FBT-040 SC1
        [Fact]
        public void CombatBlackboard_AmmoCount_IsAtOffset0()
        {
            Assert.Equal(0, (int)Marshal.OffsetOf<CombatBlackboard>("AmmoCount"));
        }

        // FBT-040 SC2
        [Fact]
        public void CombatBlackboard_EngagementRange_IsAtOffset8()
        {
            Assert.Equal(8, (int)Marshal.OffsetOf<CombatBlackboard>("EngagementRange"));
        }

        // FBT-041 SC1
        [Fact]
        public void CheckAmmo_ZeroAmmo_ReturnsFailure()
        {
            int ammo = 0;
            var state = new BehaviorTreeState();
            var ctx = new CombatContext();
            Assert.Equal(NodeStatus.Failure, CombatActions.CheckAmmo(ref ammo, ref state, ref ctx));
        }

        // FBT-041 SC1 (positive case)
        [Fact]
        public void CheckAmmo_NonZeroAmmo_ReturnsSuccess()
        {
            int ammo = 3;
            var state = new BehaviorTreeState();
            var ctx = new CombatContext();
            Assert.Equal(NodeStatus.Success, CombatActions.CheckAmmo(ref ammo, ref state, ref ctx));
        }

        // FBT-041 SC2
        [Fact]
        public void AimAndFire_DecrementsAmmo()
        {
            int ammo = 5;
            var state = new BehaviorTreeState();
            var ctx = new CombatContext();
            CombatActions.AimAndFire(ref ammo, ref state, ref ctx);
            Assert.Equal(4, ammo);
        }

        // FBT-041 SC3
        [Fact]
        public void HoldPosition_ReturnsRunningThenSuccess()
        {
            var bb = new CombatBlackboard();
            var state = new BehaviorTreeState();
            var ctx = new CombatContext();

            var r1 = CombatActions.HoldPosition(ref bb, ref state, ref ctx, 0);
            var r2 = CombatActions.HoldPosition(ref bb, ref state, ref ctx, 0);

            Assert.Equal(NodeStatus.Running, r1);
            Assert.Equal(NodeStatus.Success, r2);
        }

        // FBT-042 SC1
        [Fact]
        public void BuildAmbushTree_HasCorrectTreeName()
        {
            var blob = AmbushTree.BuildAmbushTree();
            Assert.Equal("Ambush_BT", blob.TreeName);
        }

        // FBT-042 SC2
        [Fact]
        public void BuildAmbushTree_HasCorrectNodeStructure()
        {
            var blob = AmbushTree.BuildAmbushTree();
            Assert.Equal(6, blob.Nodes.Length);
            Assert.Equal(NodeType.Selector,  blob.Nodes[0].Type);
            Assert.Equal(NodeType.Sequence,  blob.Nodes[1].Type);
            Assert.Equal(NodeType.Condition, blob.Nodes[2].Type);
            Assert.Equal(NodeType.Condition, blob.Nodes[3].Type);
            Assert.Equal(NodeType.Action,    blob.Nodes[4].Type);
            Assert.Equal(NodeType.Action,    blob.Nodes[5].Type);
        }

        // FBT-044 integration: threat visible + ammo available → AimAndFire runs
        [Fact]
        public void AmbushTree_ThreatVisibleWithAmmo_ExecutesAimAndFire()
        {
            var interpreter = AmbushTree.CreateInterpreter();
            var bb = new CombatBlackboard { ThreatVisible = true, AmmoCount = 3 };
            var state = new BehaviorTreeState();
            var ctx = new CombatContext();

            interpreter.Tick(ref bb, ref state, ref ctx);

            // AimAndFire should have decremented ammo
            Assert.Equal(2, bb.AmmoCount);
        }

        // FBT-044 integration: no threat → selector falls back to HoldPosition
        [Fact]
        public void AmbushTree_NoThreat_FallsBackToHoldPosition()
        {
            var interpreter = AmbushTree.CreateInterpreter();
            var bb = new CombatBlackboard { ThreatVisible = false, AmmoCount = 10 };
            var state = new BehaviorTreeState();
            var ctx = new CombatContext();

            // Tick 1: HoldPosition returns Running
            var r1 = interpreter.Tick(ref bb, ref state, ref ctx);
            // Tick 2: HoldPosition returns Success
            var r2 = interpreter.Tick(ref bb, ref state, ref ctx);

            Assert.Equal(NodeStatus.Running, r1);
            Assert.Equal(NodeStatus.Success, r2);
            // Ammo must be unchanged (sequence never reached AimAndFire)
            Assert.Equal(10, bb.AmmoCount);
        }

        // FBT-044 integration: threat visible + no ammo → selector falls back to HoldPosition
        [Fact]
        public void AmbushTree_ThreatVisibleNoAmmo_FallsBackToHoldPosition()
        {
            var interpreter = AmbushTree.CreateInterpreter();
            var bb = new CombatBlackboard { ThreatVisible = true, AmmoCount = 0 };
            var state = new BehaviorTreeState();
            var ctx = new CombatContext();

            var r1 = interpreter.Tick(ref bb, ref state, ref ctx);
            var r2 = interpreter.Tick(ref bb, ref state, ref ctx);

            Assert.Equal(NodeStatus.Running, r1);
            Assert.Equal(NodeStatus.Success, r2);
        }
    }
}
```

---

## Build Verification

Run after each step to catch errors early:

```powershell
# After Step 1 (project creation):
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/Fbt.Examples.FluentBTree.csproj 2>&1 | Select-String "error|warning" | Select-Object -First 10

# After all steps (full test run):
dotnet test FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Fbt.Tests.csproj 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 2
```

Expected: Fbt.Tests passes with 149 + 11 = **160** or more tests (existing 149 + 11 new SampleProjectTests).

---

## Common Pitfalls

1. **`[BTreeDefinition]` method must return `BehaviorTreeBlob`** — NOT `BTreeBuilder`. If it returns `BTreeBuilder`, the source generator will emit BTree002 warning and skip it. Read `BTreeDefinitionGenerator.cs` before writing `AmbushTree.cs`.

2. **Builder generic params are `<TBlackboard, TContext>`** — the actual type is `BTreeBuilder<CombatBlackboard, CombatContext>`, not `BTreeBuilder<CombatBlackboard>` (the latter doesn't exist).

3. **Source generator skips 3-param methods** — `CheckAmmo`, `HasThreat`, `AimAndFire` will produce BTree001 informational diagnostics; this is expected. They are NOT auto-registered; they are bound via `BTreeBuilder.Condition<TValue>(expression, logic)`.

4. **`CreateBuilder()` vs `CreateInterpreter()`** — the blob compiled by `BuildAmbushTree()` (source-gen entry) is a DIFFERENT blob instance from the one in `CreateInterpreter()`. They share the same structure but have separate `ActionRegistry` instances. In the sample app, always use `CreateInterpreter()` for live interpretation (not the catalog blob directly).

5. **`HoldPosition` 4-param** — the 4th `int param` parameter is required to match `NodeLogicDelegate<CombatBlackboard, CombatContext>`. Without it, calling `.Action(CombatActions.HoldPosition)` in the builder won't compile (type mismatch).

6. **`state.AsyncData` reset** — after HoldPosition returns Success, it resets `state.AsyncData = 0`. But the interpreter also resets state on tree restart (next tick from root). Verify tests pass as written.

7. **Adding project to FastBTree.sln** — do NOT use `dotnet sln add` in PowerShell; it may fail. Instead, edit `FastBTree.sln` manually by copying the format of the existing `Fbt.Examples.Console` project entry. Use a fresh GUID (generate one with `[System.Guid]::NewGuid().ToString("B").ToUpper()` in PowerShell).

---

## Report Requirements

Create `.dev/fluent-btree/reports/BATCH-09-REPORT.md` with:

**Q1:** Did `BTreeDefinitionGenerator.cs` require the return type to be `BehaviorTreeBlob`? Did you encounter the BTree002 diagnostic?

**Q2:** What GUIDs did you assign to the new `Fbt.Examples.FluentBTree` project in `FastBTree.sln`?

**Q3:** What was the final test count in `Fbt.Tests` after adding the `SampleProjectTests`?

**Q4:** Did any of the 11 SampleProjectTests fail? If so, what was the root cause and fix?

---

## Git Commit

After tests pass and report is written:

1. **FastBTree submodule:**
   ```powershell
   cd d:\Work\IOS-IG-SimHost-FDP-2\FDP\ExtDeps\FastBTree
   git add -A
   git commit -m "FBT-040/041/042/044: Phase 5 sample project -- CombatBlackboard, CombatActions, AmbushTree, tests"
   ```

2. **Parent repo:**
   ```powershell
   cd d:\Work\IOS-IG-SimHost-FDP-2
   git add -A
   git commit -m "FBT-040/041/042/044: BATCH-09 Phase 5 headless sample project"
   ```

---

## Success Criteria

- [ ] `Fbt.Examples.FluentBTree.csproj` builds without errors.
- [ ] All 11 new `SampleProjectTests` pass.
- [ ] No regression in existing 149 `Fbt.Tests` tests (total >= 160).
- [ ] `FastBTree.sln` includes `Fbt.Examples.FluentBTree` in the `examples` folder.
- [ ] Both git commits (FastBTree + parent repo) are made.
