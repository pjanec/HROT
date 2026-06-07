# BP-3: Make the MSBuild blueprint generator's deserialization independent of Fdp.Toolkits
**Goal:** A Full Rebuild of a real (non-recipe) blueprint under `Blueprints/` must parse + generate, instead of
failing with `BP0002 ... FileNotFoundException: Could not load file or assembly 'Fdp.Toolkits, 0.1.1.0'`.

## Confirmed cause (BP-1 de-swallow revealed it; lead-verified)
The generator runs in the `netstandard2.0` Roslyn analyzer host (no `Fdp.Toolkits` loaded). Its
`BlueprintJsonServices.Deserialize<BlueprintAsset>` reflects over the model via `DefaultJsonTypeInfoResolver`.
The Compiler model has a **`Fdp.Toolkits` type dependency** that forces loading that assembly during
deserialization:
- `Hrot.Blueprints.Compiler/Assets/Nodes.cs:2-4,202-206`: `ConditionMetPayload.Condition` is typed
  `Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto?` under `#if NET8_0_OR_GREATER` (and `object?` under
  netstandard2.0). `using Fdp.Toolkit.ReplayBrowser.Search;` is net8-guarded.
- The Compiler multi-targets `netstandard2.0;net8.0`; `Fdp.Toolkits` is referenced only for net8.0
  (`Hrot.Blueprints.Compiler.csproj`). The analyzer host ends up needing `Fdp.Toolkits` (it loads/reflects a
  Compiler build whose `BlueprintAsset` graph references `SearchPredicateDto`), which isn't present → load fail.

## Fix (investigate + implement; verify each step)
Primary direction — **remove the `Fdp.Toolkits` type from the SERIALIZED Compiler model** so `Deserialize` never
needs it in ANY host:
1. Replace the net8 `SearchPredicateDto? Condition` member with a **TFM-agnostic** representation in BOTH TFMs
   (e.g. keep it as the raw serialized form — `System.Text.Json.Nodes.JsonNode?`/`object?`/a local mirror DTO —
   so the property's declared type lives in the Compiler/Core, not Fdp.Toolkits). The WhenNode "Condition Met"
   predicate is already round-tripped as JSON; storing it as `JsonNode?`/a local DTO preserves the data without
   the external type dependency. Confirm how the compiler/editor CONSUME `Condition` today and keep them working
   (the editor net8 side may convert to/from `SearchPredicateDto` at the edge — do that conversion at the
   editor boundary, not in the serialized model).
2. Verify the Compiler no longer references `Fdp.Toolkits` in the serialized-model/deserialize path (grep
   `Fdp.Toolkit` under `Assets/`); ideally the netstandard2.0 Compiler build has **zero** `Fdp.Toolkits` ref.
3. If a residual analyzer-TFM issue remains (the analyzer loading the net8 Compiler build), ensure the generator
   consumes the **netstandard2.0** Compiler output as its analyzer dependency (check the
   `Hrot.Blueprints.Generators.csproj` ProjectReference / `SetTargetFramework=netstandard2.0` if needed).

## NO-SWALLOW (already partly done in BP-1 — keep it)
Do NOT reintroduce swallowing. The generator's parse/compile catches must keep surfacing full `ex.ToString()`.

## Verification (restore the set-aside test file)
- Restore `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Count2.bp.json` from `Count2.bp.json.setaside`
  (`git mv`/rename back) — it's a real non-recipe blueprint under the generator glob.
- `dotnet build IOS-IG-SimHost.sln -c Debug` → **0 errors** (no BP0002 for Count2; AI.Behaviors builds; the
  generated `Count2.g.cs`/registrar appear in `obj/GeneratedFiles`). NOTE: the generated blueprint may be a
  behavioral no-op until BP-2 rehydration lands — that's fine for BP-3; BP-3 only needs it to **parse + generate
  + build**, not to tick correctly.
- Existing suites green (no new failures): `Hrot.Blueprints.Tests` (only pre-existing DEBT-006 + the 2 BP-2
  tests which fail while Stage0 is disabled — do NOT touch BP-2 files), `Hrot.Blueprints.Compiler` tests,
  `Hrot.Blueprints.Generators.Tests` if present, `EditorSubsystemBoot` 10/10.
- Report exact counts.

## Report → `.dev/blueprint-compile-fix/BP-3-REPORT.md`
The exact Fdp.Toolkits trigger you found; the model change (Condition representation) + how editor consumption
still works; confirmation the ns2.0 Compiler has no Fdp.Toolkits ref; the Count2 Full-Rebuild result; exact
build/test counts; weak points; suggested commit message. No comprehension questions.

## Constraints
Branch `blueprint-integ-1`. Do NOT touch the BP-2 files (Stage0_Rehydrate.cs, INodeRegistry/BuiltInNodeRegistry
changes, BlueprintCompiler.cs line 28 — leave Stage0 disabled). Do NOT touch the user's New-from-Recipe WIP
(RecipeCreateModal.cs, AssetBrowserWindow.cs, EditorSubsystem.cs). Do NOT commit (the lead commits). If the
running editor locks dlls, report it (don't work around).
