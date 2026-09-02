# BATCH-03: JSON→C# IncrementalGenerators (BTree+HSM) + migration-equivalence
**Tasks:** PU-201, PU-202, PU-205  **Phase:** 2 (build-time generation)  **Est:** ~14h
**Dependencies:** BATCH-01 (DTOs + JSON services), BATCH-02 (emit core). **Deferred to BATCH-04:** PU-203 (`[BlueprintRegistrar]` self-registration bridge) + PU-204 (Hrot.AI.Behaviors csproj wiring) — this batch builds + tests the generators in isolation (Roslyn `GeneratorDriver`), not yet wired into the real build.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `.dev/_DONE/persistence-unification/BTree_HSM_JSON_Persistence_Detailed_Design.md` — **§6.2** (new IncrementalGenerator; **generated `.cs` = `CreateBuilder()` + thunk ONLY, NO `[*Layout]`** — layout lives in JSON), **§6.4** (migration-equivalence test), **§14 item 3** (byte-identical scope = the topology core: `CreateBuilder` + thunk + `[*Layout]`; the additive bridge is excluded — but for the *generator* output we also exclude `[*Layout]`, so compare only `CreateBuilder`+thunk). Cite it.
3. `.dev/_DONE/persistence-unification/TASK-DETAIL.md` — PU-201, PU-202, PU-205 success conditions.
4. `.dev/_DONE/persistence-unification/reviews/BATCH-01-REVIEW.md` + `BATCH-02-REVIEW.md`.
5. Codebase Memory MCP first; never `search_code`.

## Reference pattern to mirror EXACTLY (read first)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Generators/Hrot.Blueprints.Generators.csproj` — `netstandard2.0`, `<IsRoslynComponent>true</IsRoslynComponent>`, `<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>`; `Microsoft.CodeAnalysis.CSharp` 4.8.0 + `Microsoft.CodeAnalysis.Analyzers` + `System.Collections.Immutable` all `PrivateAssets="all"`; **`ProjectReference` to the emit lib with `PrivateAssets="all" ExcludeAssets="runtime"`**. Mirror this for the new generator project's reference to `Hrot.AiEditor.Persistence`.
- `Hrot.Blueprints.Generators/BlueprintIncrementalGenerator.cs` — `[Generator(LanguageNames.CSharp)]`, `AdditionalTextsProvider.Where(.bp.json)` → deserialize → emit → `RegisterSourceOutput`; deserialize/compile failure → `ReportDiagnostic` (never throws, never fails sibling assets). Mirror this control flow.

## Tasks (complete in sequence; do NOT start the next until the current's tests pass.)

### Task 1 — emit-core layout-excluding mode — file: `Hrot.AiEditor.Persistence/Emit/BTreeEmitCore.cs` + `HsmEmitCore.cs` (UPDATE)
The generated `.cs` must contain `CreateBuilder()` + the `[BTreeDefinition]`/`[HsmDefinition]` thunk but **NOT** the `[BTreeLayout]`/`[HsmLayout]` method (§6.2 — layout is JSON-only, read by the future JSON loader). Add a layout-excluding emit entry point to the core (e.g. `Emit(dto, includeLayout: false)` or `EmitTopologyCore(dto)`). The existing full `Emit(dto)` (with layout, byte-identical to the committed `.cs`) stays for the editor adapter + the BATCH-02 gate. Keep both deterministic.
**Tests required:** `EmitTopologyCore(dto)` for SampleScout/SampleGuard contains `CreateBuilder()` + the thunk attribute and does **NOT** contain `[BTreeLayout(`/`[HsmLayout(`; the existing full-emit byte-identical gate (BATCH-02) still passes unchanged.

### Task 2 — PU-201/PU-202: BTree + HSM IncrementalGenerators — file: new `netstandard2.0` Roslyn project `Hrot.AiEditor.Generators` (NEW)
One project (mirroring `Hrot.Blueprints.Generators`) hosting two `[Generator]` classes: `BTreeJsonGenerator` (consumes `*.btree.json` AdditionalTexts) and `HsmJsonGenerator` (`*.hsm.json`). Each: `AdditionalTextsProvider.Where(...)` → deserialize via `BTreeJsonServices`/`HsmJsonServices` → `EmitTopologyCore(dto)` → `RegisterSourceOutput` as `{AssetName}.g.cs`. A per-asset deserialize failure becomes a reported Roslyn **diagnostic** (define a small `DiagnosticDescriptor`), never a build crash, and **never fails sibling assets** (one bad file ≠ all fail). ProjectReference `Hrot.AiEditor.Persistence` with `PrivateAssets="all" ExcludeAssets="runtime"`. Add the project to the solution.
**Tests required (Roslyn `GeneratorDriver`):** in a net8 test project, build a `CSharpGeneratorDriver` with the generator + a synthesized `.btree.json`/`.hsm.json` `AdditionalText` (produced from a fixture DTO via the JSON services), run it, and assert: (a) a `{Name}.g.cs` source is produced containing `CreateBuilder()` + the thunk and NOT `[*Layout]`; (b) a deliberately malformed `.btree.json` AdditionalText yields a generator **diagnostic** and does NOT throw and does NOT suppress a sibling valid asset's generation (drive two AdditionalTexts: one good, one corrupt → good one still emits, corrupt one reports a diagnostic).

### Task 3 — PU-205: migration-equivalence test harness — file: new test(s) (NEW)
Prove `json → generated .cs (topology core)` is **byte-identical** to today's committed `.cs` **topology core** (`CreateBuilder` + thunk, EXCLUDING the `[*Layout]` method and any bridge — §14 item 3). For SampleScout + SampleGuard: load model (reflection) → `ToDto` → `Serialize` to a `.btree.json`/`.hsm.json` string → run it through the generator (GeneratorDriver) → extract the generated topology core → compare to the committed `.cs`'s topology core (strip its `[*Layout]` method block). Document the exact extraction/strip method in the report (it must be unambiguous, not a fuzzy contains-check).
**Tests required:** the byte-identical topology-core equivalence for both fixtures; assert it FAILS loudly if the generated core diverges (i.e. the comparison is exact-string, not substring).

## Success Criteria
- [ ] PU-201/202: `Hrot.AiEditor.Generators` is `netstandard2.0`/`IsRoslynComponent`, references `Hrot.AiEditor.Persistence` (`ExcludeAssets="runtime"`), no editor/net8 ref. Two generators emit `CreateBuilder()`+thunk (no `[*Layout]`); malformed input → diagnostic, sibling-safe.
- [ ] Emit core has a layout-excluding mode; full byte-identical gate (BATCH-02) still green.
- [ ] PU-205: json→generated-core byte-identical to the committed `.cs` topology core for both fixtures (exact-string).
- [ ] Global gate: `dotnet build IOS-IG-SimHost.sln` 0 errors / 0 new warnings (touched); all new generator tests green; `Hrot.AiEditor.Persistence.Tests` green (BATCH-01/02); `EditorSubsystemBoot` 10/10; `Hrot.Editor.AiShared.Tests` green; `Hrot.Blueprints.Tests` only pre-existing (0 new). Report exact counts/classification.
- [ ] Report → `.dev/_DONE/persistence-unification/reports/BATCH-03-REPORT.md`.

## Report Requirements
The generator project layout + how it references the emit lib (analyzer packaging — does `ExcludeAssets="runtime"` suffice, as for Blueprint?); how `EmitTopologyCore` differs from full emit; the GeneratorDriver test setup; the exact topology-core extraction/strip method for PU-205 and why it's unambiguous; whether the generator emits stable output; weak points; suggested commit message. No comprehension questions.

## Constraints
Branch `blueprint-integ-1`. GizmoMap.Contracts 0.2.2. No `Hrot.IG`/DDS/`Stride/`. No `editor_stride`. **No build wiring into `Hrot.AI.Behaviors` yet** (no AdditionalFiles glob, no `.cs` decommit — that's PU-204/PU-401). **No registration bridge yet** (PU-203). Generators are built + tested in isolation here. Don't touch the Blueprint path. Do NOT commit (the lead commits).
