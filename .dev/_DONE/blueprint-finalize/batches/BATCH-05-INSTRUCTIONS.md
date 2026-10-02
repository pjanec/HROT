# BATCH-05 — BlueprintMath library + canvas-authorable counting demo (Task 6)

> **Coder contract:** read `.dev/.guides/DEV-GUIDE_claude.md` first. Verify-first, cite `file:line`,
> never fake a pass, implement→build→test→fix to green. **Codebase Memory MCP first**. Project
> `D-Work-IOS-IG-SimHost-FDP-2`. No `search_code`/tree grep.

## Mission

Blueprints currently have **no arithmetic primitive** (no Add node; no callable CLR add — `System.Math.Add`
doesn't exist; `LibraryMath` is a broken placeholder). Add a real `BlueprintMath` library of pure math
functions that `FunctionCallNode` can target, then author a real `.bp.json` whose Tick increments a
blackboard `Count` by 1 each tick and prove it compiles + runs + climbs (Task 6). User direction: add the
**full** math set (mirror + extend the NodeEdit demo's Math node picker).

## Part 1 — `BlueprintMath` library

Add a `public static class BlueprintMath` in **`Fdp.Toolkit.Blueprints`** (the runtime blueprint assembly
already in the Roslyn reference set — verify it is referenced by
`MetadataReferenceResolver.ForRuntimeAssemblies` / the compile path; the generated code calls
`global::<FQN>(args)` so the type must be reference-resolvable). All methods **pure, static, deterministic,
side-effect-free** (FunctionCall IsPure → `IrOp_PureCall` → `global::Fdp.Toolkit.Blueprints.BlueprintMath.X(...)`).

Implement the full set (mirror the NodeEdit demo `FakeNodeCatalog.cs:51-73` naming where sensible, and
extend). Suggested coverage:
- **Float:** `Add, Subtract, Multiply, Divide, Modulo, Abs, Negate, Min, Max, Clamp, Lerp, Floor, Ceil,
  Round, Sqrt, Pow, Sin, Cos` (all `float`-typed).
- **Int:** `AddInt, SubInt, MulInt, DivInt, ModInt, AbsInt, NegateInt, MinInt, MaxInt, ClampInt`.
- **Comparisons (return `bool`):** `GreaterThan, LessThan, GreaterOrEqual, LessOrEqual, ApproxEquals`
  (float) and `EqualsInt, GreaterThanInt, LessThanInt` (int).
- **Bool logic:** `And, Or, Not, Xor`.
- **Vector3 (`System.Numerics.Vector3`):** `AddVec, SubVec, MulVecScalar, Dot, Cross, Normalize, Length,
  Distance` (Vector3 is a known blueprint type — `BlackboardTypeHelper.DefaultKnownTypeNames` lists
  `Vector3`; verify the TypeId the type system uses, likely `System.Numerics.Vector3`).
Guard division/modulo by zero (return 0 rather than throw — these run in the sim hot path). Document each
briefly. Keep names stable (a later batch surfaces them in the node picker).

**Tests:** a `BlueprintMathTests.cs` (in the appropriate test project, e.g. `Fdp.Toolkits.Tests` or
`Hrot.Blueprints.Tests`) asserting representative results (AddInt(2,3)==5, Clamp, Lerp, Div-by-zero==0,
Dot/Cross/Normalize/Length, bool ops). These double as the spec for the picker batch.

## Part 2 — `CountingDemo.bp.json` + compile-and-run proof

Author `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/TestAssets/CountingDemo.bp.json`: Instance
dispatch, one `Count : System.Int32` variable (default 0), one Tick graph
(`EventEntry → SetVariable(Count)`), where `SetVariable.Value` is fed by a **pure** `FunctionCall`
targeting `Fdp.Toolkit.Blueprints.BlueprintMath.AddInt` with args `GetVariable(Count)` and `Literal(1)`.
Lowers to: `var a = s.Count; var b = 1; var r = global::...BlueprintMath.AddInt(a, b); s.Count = r;`.

**CRITICAL — pin authoring + projection-only invariant (verify-first):** Determine whether the compile
pipeline hydrates `node.Pins` from the canonical pin schema BEFORE Stage4/Stage5 (so the saved `.bp.json`
keeps `"Pins": []` per the projection-only invariant), or whether the compiler reads pins directly from the
node JSON (Stage5 `SetVariableNode` does `node.Pins.FirstOrDefault(...)`; Stage4 reads a pure FunctionCall's
out-pin `TypeRef.TypeId`). Find where existing compilable Instance demos (e.g. `HealthRegen.bp.json` — its
golden has a real Tick) get their pins: do they store explicit pins, or `"Pins": []` + a hydration pass?
Author `CountingDemo.bp.json` the SAME way HealthRegen does, so it honors the projection-only invariant and
the byte-stability test. If explicit pins are required for some nodes (e.g. pure FunctionCall out-pin type),
include exactly those and document why in the report. Do NOT break or re-baseline the byte-stability /
golden tests.

Register the asset name in `TestData.SampleAssets` (`TestData.cs`).

**Proof test** (mirror `Demos/StateFields_ProofTests.cs` PROOF-002; `[Collection("DebugProbe")]`):
load `CountingDemo`, `CompileAndLoad`, create entity + `AttachBlueprint` (Count starts 0), `TickFrame` ×5,
then `GetBlueprintState(...).TryGetField<int>("Count", out var c)` and `Assert.Equal(5, c)`. Also assert
Count==0 before any tick. This proves the authored increment runs and is observable by name (BATCH-04).

## Verification (paste real output)
1. `dotnet build IOS-IG-SimHost.sln` — 0 errors; 0 new warnings in touched projects.
2. `BlueprintMathTests` green; the `CountingDemo` proof test green (Count climbs 0→5).
3. Full `Hrot.Blueprints.Tests`: failures a SUBSET of the pre-existing **7**, 0 new, **no golden/byte-
   stability test newly fails** (the demo must not perturb existing goldens). List + classify. Run any
   byte-stability / projection-only test explicitly and confirm green.
4. `Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~EditorSubsystemBoot` → 10/10.

## Report
`.dev/_DONE/blueprint-finalize/reports/BATCH-05-REPORT.md`: the BlueprintMath set (file:line) + where it lives +
confirmation it's in the Roslyn reference set; the demo `.bp.json` authoring decision (pins: hydrated vs
explicit, and how it honors the projection-only invariant); the generated Tick C# (paste it); the proof
test result (0→5); full-suite classification. Note BATCH-05B (surfacing BlueprintMath in the node picker)
is a separate follow-up. **Do not commit** — lead reviews/commits.
