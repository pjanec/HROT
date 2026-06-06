# BF-NODESTATUS: emit `Fbt.NodeStatus` in generated code (fix CS0234 + ordinal inversion)

**Architect-confirmed, lead-verified.** Generated AiPrimitive/function-graph C# currently references
`global::Hrot.Blueprints.Core.Assets.NodeStatus` — a **compiler-only** type the runtime game assembly
(`Hrot.AI.Behaviors`) does NOT reference → `CS0234`. Also `Fbt.NodeStatus` (Failure=0, Success=1, Running=2)
and the compiler enum (Success=0, Failure=1, Running=2) have **inverted** ordinals, so the existing
`(Fbt.NodeStatus)(int)TickCore(...)` cast silently swaps Success/Failure.

**Fix = unify ALL emitted code on `global::Fbt.NodeStatus`.** The compiler's internal
`Hrot.Blueprints.Core.Assets.NodeStatus` enum STAYS (used by the IR/analysis) — it must simply never appear in
*generated* C#. No runtime mirror type is created. Named members (`.Success`/`.Failure`/`.Running`) exist in
both enums, so switching the emitted FQN is ordinal-safe.

## Exact edits (emit sites only — do NOT change the compiler's own enum definition)

1. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/AiPrimitiveEmitter.cs`
   - **:105** `public static global::Hrot.Blueprints.Core.Assets.NodeStatus TickCore(` → `...global::Fbt.NodeStatus TickCore(`
   - **:125** `return global::Hrot.Blueprints.Core.Assets.NodeStatus.Failure;` → `return global::Fbt.NodeStatus.Failure;`
   - **:191** `return (global::Fbt.NodeStatus)(int)TickCore(ref p, ref ws, ctx.Self, ctx.World, ctx.World.SimulationTime);`
     → `return TickCore(ref p, ref ws, ctx.Self, ctx.World, ctx.World.SimulationTime);`  (TickCore now already returns Fbt.NodeStatus — drop the inverting cast)
   - **:230** `... == global::Hrot.Blueprints.Core.Assets.NodeStatus.Success;` → `... == global::Fbt.NodeStatus.Success;`
   - **:288** `... == global::Hrot.Blueprints.Core.Assets.NodeStatus.Success;` → `... == global::Fbt.NodeStatus.Success;`
   - **:297** `public static global::Hrot.Blueprints.Core.Assets.NodeStatus Call(` → `...global::Fbt.NodeStatus Call(`

2. `.../Compiler/Emit/LibraryEmitter.cs`
   - **:35** `: hasStatusReturn ? "global::Hrot.Blueprints.Core.Assets.NodeStatus"` → `: hasStatusReturn ? "global::Fbt.NodeStatus"`

3. `.../Compiler/Emit/TerminatorEmitter.cs`
   - **:30** `e.WriteLine($"return global::Hrot.Blueprints.Core.Assets.NodeStatus.{t.Status};");` → `...global::Fbt.NodeStatus.{t.Status};`

4. `.../Compiler/Emit/StatementEmitter.cs`
   - **:32-34** the WaitLowering literal qualification:
     ```
     var literal = op.CSharpLiteral.StartsWith("NodeStatus.", StringComparison.Ordinal)
         ? $"global::Hrot.Blueprints.Core.Assets.{op.CSharpLiteral}"
         : op.CSharpLiteral;
     ```
     → change the FQN prefix to `global::Fbt.` so `NodeStatus.Success` becomes `global::Fbt.NodeStatus.Success`:
     ```
     var literal = op.CSharpLiteral.StartsWith("NodeStatus.", StringComparison.Ordinal)
         ? $"global::Fbt.{op.CSharpLiteral}"
         : op.CSharpLiteral;
     ```
   - **:825-833** the `typeSuffix == "NodeStatus"` int-cast comparison branch: **LEAVE THE CODE AS-IS** (both
     sides are now the same `Fbt.NodeStatus`, so `((int)a == (int)b)` is still correct — leaving it avoids
     golden churn). **Only** update the now-stale comment (lines ~824-827) to reflect that both operands are now
     `global::Fbt.NodeStatus` (so the int-cast is defensive/redundant, not bridging two enums). Comment-only
     change → zero golden impact.

**Do NOT** touch `GraphTypes.cs` (the `enum NodeStatus { Success, Failure, Running }` definition stays — it's
the compiler's internal enum). **Do NOT** rename or remove the compiler enum.

## Golden snapshots (regenerate — intentional, sanctioned)
These AiPrimitive goldens currently encode the buggy text and WILL change (only the NodeStatus FQN swaps + the
removed cast at :191):
- `Hrot.Blueprints.Tests/Snapshots/Emit/MoveToAndFire.cs.txt`
- `Hrot.Blueprints.Tests/Snapshots/Emit/HasVisibleTarget.cs.txt`
- `Hrot.Blueprints.Tests/Snapshots/Demos/MoveToAndFire.cs.txt`
Regenerate ONLY via `BLUEPRINT_REGENERATE_SNAPSHOTS=1` on the Blueprints test run, then **`git diff` the .txt
files and confirm the ONLY changes are `Hrot.Blueprints.Core.Assets.NodeStatus` → `Fbt.NodeStatus` and the
dropped `(global::Fbt.NodeStatus)(int)` cast**. If ANY other text changed, STOP and report — do not accept it.

## Verification (all required; report exact numbers)
1. `dotnet build IOS-IG-SimHost.sln -c Debug` → **0 errors / 0 new warnings**. This is the REAL proof: the
   MSBuild generator runs on `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Loco1.bp.json` (an AiPrimitive
   blueprint) + the recipes, in the game assembly that does NOT reference the compiler. The prior CS0234 in
   `Loco1_A9036715_Bp.g.cs` must be gone. (If the editor app is running and locks DLLs, STOP and report —
   don't work around it.)
2. Inspect the regenerated `obj/.../Loco1_A9036715_Bp.g.cs`: TickCore returns `global::Fbt.NodeStatus`,
   the BTreeTick thunk returns `TickCore(...)` with no `(int)` cast, and the graph body returns
   `global::Fbt.NodeStatus.Success`.
3. Blueprints suite: failures a **subset of the 7 pre-existing** (0 new). List the final failing set by name.
   The `MoveToAndFire_*`/`HasVisibleTarget` snapshot + end-to-end tests must pass with the new goldens.
4. `EditorSubsystemBoot` 10/10; `Hrot.Editor.AiShared.Tests` green.

## Report
`.dev/blueprint-finalize/reports/BF-BATCH-NODESTATUS-REPORT.md`: the edits made, the golden diff (paste the
relevant lines proving FQN-swap-only), the solution-build result (0/0), the Loco1 .g.cs inspection, and the
suite numbers (before/after failing-set comparison).

## Constraints
Branch `blueprint-integ-1`. Projection-only invariant intact. Do NOT commit (the lead commits). Do NOT touch
user WIP (RecipeCreateModal/AssetBrowserWindow/EditorSubsystem) or the Count*.bp.json files. Do NOT change the
compiler's internal `NodeStatus` enum. Sub-agent model: sonnet.
