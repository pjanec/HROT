# Utility AI — Source Generator & Analyzer Detailed Design v1.1

Follow-on to the Utility AI architecture (v1.1). This document specifies the compile-time
machinery that makes the catalog (B1) and the C#-as-source-of-truth round-trip real: the source
generators that emit the input-reader and decision registrars, and the analyzer that enforces the
authoring rules. It is the load-bearing dependency for the visual editor (the editor's lossless
round-trip relies on the closed, generator-discovered vocabulary defined here).

> **Changelog v1.0 → v1.1** (incorporates the v236 generator review):
> - **Hash formula pinned (§3.3, §3.5, §10):** `InputId` uses the **exact** truncation the existing
>   BTree/HSM generators use — compute 32-bit FNV-1a, then mask to 16 bits:
>   `hash ^= c; hash *= 16777619; … return (ushort)(hash & 0xFFFF)`. This is *not* a native FNV-1a16;
>   gen-time and runtime must both use this specific formula or every dispatch silently misses.
> - **G-4 scope split made explicit (§6.2):** the *generator* emits only for the current assembly;
>   the *analyzer* scans `context.Compilation.GlobalNamespace` across referenced assemblies for
>   `UT0120` resolution. Different scopes, stated separately.
> - **`UT0130` precedent named (§6.1):** the purity check copies the existing
>   `EqsTemplatePurityAnalyzer` (`EQS_002`) AST-walk pattern, not a new approach.
> - Open questions G-1…G-4 all resolved (now §11, "Resolved").

It is written to slot into the **existing** `Fdp.Toolkits.Analyzers` assembly alongside the
seven existing components — `BTreeActionGenerator`, `HsmActionGenerator`, `BTreeDefinitionGenerator`,
`GizmoRegistrarGenerator`, `BehaviorParameterSizeAnalyzer`, `EqsTemplateGenerator`, and
`EqsTemplatePurityAnalyzer` (plus `SharedBhuDiagnostics`) — and follows their conventions exactly.
Where this doc says "same as BTree," it means literally the same pattern, not a parallel invention.

Prereq reading: the architecture doc (§4 structs, §5 curves, §6 inputs, §8 storage, §11
authoring), and `Fdp.Toolkits.Analyzers` (the five existing components and why they target
`netstandard2.0`).

---

## 1. Scope

### 1.1 What this adds to `Fdp.Toolkits.Analyzers`

| New component | Kind | Output | Mirrors |
|---|---|---|---|
| `UtilityInputGenerator` | `IIncrementalGenerator` | `UtilityInputRegistrar.g.cs` | `BTreeActionGenerator` |
| `UtilityDecisionGenerator` | `IIncrementalGenerator` | `UtilityDecisionCatalog.g.cs` | `BTreeDefinitionGenerator` |
| `UtilityAuthoringAnalyzer` | `DiagnosticAnalyzer` | the `UT####` diagnostics | `BehaviorParameterSizeAnalyzer` |

Two generators and one analyzer. Both generators use the incremental API (utility inputs and
decisions are edited frequently during balancing, so IDE latency matters — the same reasoning that
put BTree/HSM on `IIncrementalGenerator` rather than the gizmo `ISourceGenerator`).

### 1.2 What it does not do

- No runtime reflection in the hot path. Discovery of the emitted registrar at startup uses one
  reflective scan for `[UtilityRegistrar]` (same handshake as `FbtAutoDiscovery` scanning for
  `[FbtRegistrar]`), paid once.
- No new assembly. It lives in `Fdp.Toolkits.Analyzers` (`netstandard2.0`, no net8 references), so
  the struct-offset math is duplicated here exactly as the project already documents for its three
  existing offset-computing components — extraction into a shared helper is deliberately avoided
  for the same Roslyn-host-loading reason.

---

## 2. The authored attributes

Defined in the runtime contracts assembly (the utility equivalent of where `BTreeActionAttribute`
lives), referenced by both the runtime and the generator.

```csharp
[AttributeUsage(AttributeTargets.Method)]
public sealed class UtilityInputAttribute : Attribute
{
    public string Name { get; init; }            // catalog name, e.g. "AmmoFraction"
    public string Category { get; init; }        // editor grouping, e.g. "Self state"
    public InputContextMask AllowedContexts { get; init; } = InputContextMask.All;
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class UtilityDecisionAttribute : Attribute
{
    public string AssetId { get; init; }         // stable GUID string
    public string DisplayName { get; init; }
    public DecisionKind Kind { get; init; }      // ThreatRanking | WeaponSelection | PostureSelect
    public string Category { get; init; }
    public float HysteresisBonus { get; init; }  // per-decision (Q-1); 0 default
}
```

`IUtilityDecisionDefinition` requires `static void Build(IUtilityDecisionBuilder b)` — the
generator and analyzer key off this signature, exactly as `[BTreeDefinition]` keys off a static
zero-arg method returning a blob/builder.

---

## 3. `UtilityInputGenerator`

### 3.1 Recognized shape

A `[UtilityInput]`-attributed method. The canonical reader signature:

```csharp
[UtilityInput(Name = "AmmoFraction", Category = "Self state")]
public static float AmmoFraction(in UtilityInputCtx ctx) { ... }
```

Valid-signature constraints (violations → diagnostics, §6, method omitted from the registrar):

- Must be `static` (→ `UT0110` if not).
- Must return `float` (→ `UT0111`).
- Must take exactly one parameter, `in UtilityInputCtx` (→ `UT0112`).
- `Name` must be present and unique across the compilation (→ `UT0101` / `UT0102`).

This mirrors how `BTreeActionGenerator` constrains the 4-param node-logic delegate shape and
filters non-conforming methods via the `.Where(m => m != null)` step.

### 3.2 Pipeline

The standard incremental pipeline, identical in structure to `BTreeActionGenerator`:

```
SyntaxProvider.CreateSyntaxProvider
    predicate: node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0
    transform: ctx => GetUtilityInputInfo(ctx)   // returns null to filter
        |
        .Where(m => m != null)
        .Combine(CompilationProvider)
        |
    RegisterSourceOutput -> Execute(...)
        |
    AddSource("UtilityInputRegistrar.g.cs", ...)
```

### 3.3 Emitted registrar

Each reader becomes an entry keyed by its catalog name, registered as an unsafe function pointer
(the architecture's "function pointer at runtime, no reflection" requirement). The hash of the name
is computed **at compile time** and emitted as the `ushort InputId`, so runtime never hashes strings
on the hot path.

**The hash must be the codebase's exact formula** (v236 review): the existing `BTreeActionGenerator`
and `HsmActionGenerator` do not use a native FNV-1a16 — they compute the standard 32-bit FNV-1a and
truncate the low 16 bits:

```csharp
// The ONE hash both gen-time and runtime must use, verbatim. Any divergence = silent dispatch miss.
static ushort Fnv1a16(string s)
{
    uint hash = 2166136261u;            // FNV offset basis (32-bit)
    foreach (char c in s) { hash ^= c; hash *= 16777619u; }  // FNV prime
    return (ushort)(hash & 0xFFFF);     // truncate to 16 bits — matches BTree/HSM exactly
}
```

Using true FNV-1a16 (16-bit basis/prime) here would produce different bytes than the rest of the AI
subsystem and break the §10 parity test; the generator copies the existing 32-bit-then-mask code.

```csharp
// UtilityInputRegistrar.g.cs   (generated)
[UtilityRegistrar]
public static class UtilityInputRegistrar
{
    public static void RegisterAll(UtilityInputRegistry registry)
    {
        // Name "AmmoFraction" -> Fnv1a16 (32-bit FNV, low 16) = 0xA3F1 (computed at gen time)
        registry.Register(
            id: 0xA3F1,
            name: "AmmoFraction",
            category: "Self state",
            allowedContexts: InputContextMask.Self,
            reader: &global::Ns.StandardInputs.AmmoFraction);   // function pointer

        registry.Register(
            id: 0x77C2,
            name: "DistanceToContext",
            category: "Targeting",
            allowedContexts: InputContextMask.All,
            reader: &global::Ns.StandardInputs.DistanceToContext);
        // ... one per [UtilityInput]
    }
}
```

`&Method` is the managed-function-pointer syntax; `UtilityInputRegistry.Register` takes a
`delegate*<in UtilityInputCtx, float>`. This is the utility analog of the HSM kernel's function
pointer table, and it is why readers must be `static` (instance methods can't form a plain function
pointer).

### 3.4 Parameterized inputs and `InputParams`

Inputs like `EqsTopScore(sensorName)` or `DistanceToContext(context)` don't get a distinct reader
per parameter value — the parameter is carried in the `InputParams` packed struct at authoring time
and read by the single reader at tick time. The generator therefore emits **one** entry per
`[UtilityInput]` method; the parameterization is purely an authoring-side concern resolved by the
decision generator (§4), not a multiplier on the input catalog. This is the mechanism that keeps
the catalog small (the EQS-context lesson, architecture §6.3).

### 3.5 FNV-1a collision handling

Two reader names whose 32-bit-FNV-low-16 hashes collide is a compile-time error (`UT0103`), not a
runtime surprise. The generator accumulates all (name → hash) pairs and, on collision, emits the
diagnostic on the second method with a message naming both colliding readers. The 16-bit space
across a curated catalog makes this rare, but it must fail loud — wrong dispatch is exactly the
silent-bug class the
existing generators were built to eliminate.

---

## 4. `UtilityDecisionGenerator`

### 4.1 Recognized shape

A class implementing `IUtilityDecisionDefinition` carrying `[UtilityDecision]`, with the required
`static void Build(IUtilityDecisionBuilder b)`. Mirrors `[BTreeDefinition]` recognition.

### 4.2 The hard part — compile-time `Build` introspection vs. runtime execution

`[BTreeDefinition]` has it easy: it emits an accessor that *calls* the builder method at runtime and
compiles the result. The utility decision generator has a choice, and the choice matters for the
editor:

- **Option A — runtime build (like BTree).** Emit a catalog accessor that calls `Build` at startup,
  producing the `UtilityDecisionDef` then. Simple, but the *structure* (options, considerations,
  inputs, curves) isn't known at compile time, so the editor and the `StructureHash`/`ParamHash`
  can't be generator-emitted; they'd be computed at runtime.
- **Option B — compile-time extraction.** Parse the fluent `Build` body in the generator to extract
  the option/consideration structure, emit it as data, and compute the hashes at gen time.
  Powerful, but parsing a fluent chain in Roslyn is brittle (the body can use locals, loops,
  helpers).

**Decision: Option A for the runtime blob, plus a gen-time *catalog manifest* for tooling.** The
generator emits the runtime accessor that calls `Build` (robust, matches BTree), AND a separate
lightweight manifest by analyzing the `Build` body *best-effort* for the editor and comparison
tooling. The runtime never depends on the manifest; if best-effort parsing fails, the manifest entry
is marked `partial` and the editor falls back to runtime reflection of the built `UtilityDecisionDef`
(which exists once `Build` has run). This gives BTree-level robustness for execution and
editor-friendly structure where extractable, without making the runtime hostage to fluent-parsing.

### 4.3 Emitted catalog

```csharp
// UtilityDecisionCatalog.g.cs   (generated)
[UtilityRegistrar]
public static class UtilityDecisionCatalog
{
    public static void RegisterAll(out UtilityDecisionRegistry registry)
    {
        registry = new UtilityDecisionRegistry();

        // BlueprintId = FNV-1a32(AssetId GUID bytes), computed at gen time
        registry.Register(
            blueprintId: 0x5D31_A7C0,
            assetId: new Guid("3c6f9e42-5d10-6f3a-ac23-posture0000001"),
            displayName: "Combat posture",
            kind: DecisionKind.PostureSelect,
            hysteresisBonus: 0.08f,
            builder: static () => { var b = UtilityDecisionBuilder.Create();
                                    global::Ns.CombatPostureDecision.Build(b);
                                    return b.ToDef(); });
        // ... one per [UtilityDecision]
    }

    // Best-effort manifest for editor/comparison tooling (NOT used at runtime).
    public static readonly UtilityDecisionManifest[] Manifest = { /* ...extracted structure... */ };
}
```

`CombatPostureDecision.Id` (used throughout the starter pack as `ThreatRankingDecision.Id`, etc.)
is a generated `const int` partial-class member per decision, equal to the `blueprintId` above, so
authored code references the decision by a strongly-typed constant rather than a magic GUID.

### 4.4 Hashes

`StructureHash` and `ParamHash` (architecture §4.2, §11.3) are computed by the runtime `ToDef()`
after `Build` runs — not the generator, given Option A. The generator emits the manifest's
*best-effort* structure hash where extractable, used only by the comparison "tuning diff vs. LLM"
fast-lane decision (Visual Asset Comparison integration); the authoritative hashes for hot-reload
are still the runtime ones. This keeps the one correctness-critical path (hot-reload classification)
on the robust runtime computation.

---

## 5. The startup handshake

Both registrars carry `[UtilityRegistrar]`. At simulation startup, a single reflective scan finds
them and invokes `RegisterAll`, exactly as `FbtAutoDiscovery.ScanAndRegister` finds `[FbtRegistrar]`:

```csharp
UtilityAutoDiscovery.ScanAndRegister(inputRegistry, out var decisionRegistry);
// internally: find [UtilityRegistrar] types, call RegisterAll once each
```

One-time reflection cost, results cached in the registries for process lifetime. Adding a new input
or decision requires only the attribute — no manual wiring into a startup list (the decoupling
rationale the BTree docs give for the same handshake).

---

## 6. `UtilityAuthoringAnalyzer` — the `UT####` diagnostics

A pure `DiagnosticAnalyzer` (produces no source), registered via `RegisterSymbolAction` /
`RegisterSyntaxNodeAction`. Descriptors centralized in a `SharedUtilityDiagnostics` static class to
avoid `RS1019` duplicate-descriptor warnings (the same precaution `SharedBhuDiagnostics` takes for
the BTree/HSM generators sharing a compilation).

| ID | Severity | Rule |
|---|---|---|
| `UT0101` | Error | `[UtilityInput]` missing `Name` |
| `UT0102` | Error | duplicate input `Name` across the compilation |
| `UT0103` | Error | input-name hash collision (32-bit FNV-1a, low 16 bits) between two input names |
| `UT0110` | Error | `[UtilityInput]` / `Build` method is not `static` |
| `UT0111` | Error | `[UtilityInput]` does not return `float` |
| `UT0112` | Error | `[UtilityInput]` signature is not `(in UtilityInputCtx)` |
| `UT0120` | Error | consideration references an unknown input name (not in catalog) |
| `UT0121` | Error | input used with a context outside its `AllowedContexts` |
| `UT0122` | Error | parameterized input missing its required param (e.g. `EqsTopScore` with no sensor) |
| `UT0130` | **Error** | `Build` reads disallowed runtime state (purity violation, §6.1) |
| `UT0131` | Warning | weight outside [0,1] |
| `UT0140` | Error | `PostureSelect` decision has zero options |
| `UT0141` | Warning | all options are product-mode with gates and no sum-mode fallback exists → possible "nothing wins" (the `Hold` pattern is the fix) |
| `UT0142` | Warning | duplicate `OptionId` within a decision |
| `UT0150` | Error | duplicate `AssetId` across decisions |

### 6.1 The purity check (`UT0130`) — the important one

`Build` must be pure and deterministic (architecture §11.1), the same rule `[EqsTemplate].Build`
follows — and `UT0130` is a direct copy of the existing **`EqsTemplatePurityAnalyzer`** (which emits
`EQS_002` for static-mutable-field reads inside an EQS template `Build`), reusing its AST-walk
pattern verbatim rather than inventing a new one. The analyzer walks the `Build` method body and
flags reads of disallowed symbols: `EntityRepository`, `ISimulationView`, `DateTime.Now`/`UtcNow`,
`Random` without a seeded deterministic source, static mutable fields, and any method call returning
non-deterministic data. This is a syntactic/semantic walk (not execution), so it catches the common
cases; it cannot prove purity in the limit (e.g. a helper in another assembly), which is acceptable
— it catches the mistakes designers actually make (reaching for live state inside `Build`), exactly
as `EqsTemplatePurityAnalyzer` and the other existing analyzers target realistic failure modes
rather than theoretical completeness.

### 6.2 Catalog-aware diagnostics (`UT0120`–`UT0122`)

These require the analyzer to know the input catalog while analyzing a decision's `Build` — and the
catalog can span **referenced assemblies**, not just the current one (G-4). The scope rule has two
halves that must not be conflated:

- **The generator emits only for the current assembly.** A downstream project that defines its own
  `[UtilityInput]` methods runs the generator itself and gets its own `UtilityInputRegistrar.g.cs` /
  `In.*` accessors. The generator never reaches into referenced assemblies.
- **The analyzer scans across referenced assemblies.** To resolve `UT0120`/`UT0121`, the analyzer
  walks `context.Compilation.GlobalNamespace` for *all* `[UtilityInput]`-attributed methods —
  including those in referenced assemblies — to build the name→(allowedContexts, params) set, then
  checks each `.Consider(In.X(...))` call against it. Because this is a **symbol** scan (how Roslyn
  naturally resolves cross-assembly references), the cost in a large solution is acceptable; scoping
  it to the current compilation's syntax trees would make `UT0120` falsely fire on a valid input
  defined upstream.

This is the analyzer's most valuable feature: it turns "typo in an input name" from a silent runtime
no-op into a red squiggle, the whole reason the generators exist.

> Implementation note: resolving `.Consider(In.AmmoFraction(Ctx.Self), ...)` to the input name
> requires recognizing the `In.<Name>` accessor pattern. The generated `In` helper (a partial static
> class with one method per input, emitted by `UtilityInputGenerator`) gives each input a
> strongly-typed accessor, so the analyzer matches on the resolved method symbol, not a string —
> robust against renaming. This is why the input generator also emits the `In` accessors, not just
> the registrar.

---

## 7. Generated `In` accessors (authoring ergonomics + analyzer anchor)

`UtilityInputGenerator` emits a third artifact beyond the registrar: a partial static `In` class
giving each catalog input a typed accessor used in fluent `Build` bodies.

```csharp
// UtilityInputAccessors.g.cs   (generated)
public static partial class In
{
    public static ConsiderationInput AmmoFraction(InputContext ctx = InputContext.Self)
        => new(0xA3F1, ctx, InputParams.None);

    public static ConsiderationInput EqsTopScore(string sensorName)
        => new(0x91B4, InputContext.None, InputParams.Sensor(sensorName));
    // ... one per [UtilityInput], shape varying by its parameters
}
```

This is what makes `.Consider(In.AmmoFraction(Ctx.Self), w: 0.9f, Curve.Threshold)` compile, gives
the editor's catalog browser its entries (the dropdown is literally this generated set), and gives
the analyzer a symbol to match `UT0120`/`UT0121` against. Three consumers, one generated artifact —
the same economy as the BTree registrar serving runtime + auto-discovery + schema export.

---

## 8. Round-trip closure (why the editor depends on this doc)

The visual editor's lossless round-trip (wireframes §9) is only possible because the vocabulary is
closed and generator-defined:

- The editor's input dropdown = the generated `In` accessor set.
- The editor's curve list = the `CurveKind` enum.
- Emission = writing `.Consider(In.<Name>(<ctx>), w:<n>, Curve.<Kind>)` — pure name + number, no
  expression serialization.
- Re-import = the analyzer validating that every emitted `In.<Name>` still resolves (a removed input
  surfaces as `UT0120` rather than a silent break).

So the generator is not just boilerplate elimination; it is the contract that the editor, the
analyzer, and the runtime all share. The comparison feature's tuning-diff fast-lane (wireframes
§7.2) likewise reads `ParamHash` from the runtime `ToDef()` and structure from the gen-time manifest.

---

## 9. Build pipeline placement

Extends the project's existing role diagram:

```
User project (.csproj)                Fdp.Toolkits.Analyzers              Roslyn
[UtilityInput] methods      ───────▶  UtilityInputGenerator     ───────▶  UtilityInputRegistrar.g.cs
                                                                          UtilityInputAccessors.g.cs (In.*)
[UtilityDecision] classes   ───────▶  UtilityDecisionGenerator  ───────▶  UtilityDecisionCatalog.g.cs
[UtilityInput]/[UtilityDecision] ──▶  UtilityAuthoringAnalyzer  ───────▶  UT#### diagnostics
```

`netstandard2.0`, delivered via the `Analyzer` item group, no net8 references — identical packaging
to the five existing components.

---

## 10. Test strategy

Following the project's note that analyzer behavior is currently validated indirectly through
compilation, but improving on it (the architecture warrants dedicated coverage):

- **Generator snapshot tests** — feed a small compilation with a handful of `[UtilityInput]` and
  `[UtilityDecision]` declarations; assert the generated `UtilityInputRegistrar.g.cs`,
  `UtilityInputAccessors.g.cs`, and `UtilityDecisionCatalog.g.cs` match golden files (Roslyn
  `CSharpGeneratorDriver` snapshot pattern).
- **Hash parity (the critical test)** — assert the gen-time hash equals the runtime hash for a
  battery of names, *and* that both equal an independent reference implementation of the codebase
  formula (`32-bit FNV-1a, return low 16 bits`). Pin a few known vectors (e.g. `"AmmoFraction" →
  0xA3F1`) so a future refactor that "fixes" the hash to true FNV-1a16 fails loudly here rather than
  silently breaking every dispatch. A divergence means the emitted `InputId` never matches a runtime
  lookup — catastrophic and silent — so this is the single most important generator test.
- **Diagnostic tests** — one fixture per `UT####`: a snippet that should trip it, asserting the
  diagnostic id + location; plus negative fixtures that must stay clean.
- **Purity analyzer (`UT0130`)** — fixtures reading `EntityRepository`, `DateTime.Now`, static
  mutable state inside `Build`, each flagged; a clean `Build` stays silent.
- **Catalog-aware (`UT0120`)** — a decision referencing a non-existent `In.Foo` is flagged; renaming
  a real input updates resolution (proving symbol-based, not string-based, matching).
- **Round-trip property** — generate from a decision, feed the emitted C# back through the
  compilation, assert the catalog is identical (the editor's lossless guarantee, tested at the
  generator level).

The starter-pack decisions (StarterPack doc) double as the generator's integration fixtures: they
must generate clean with zero diagnostics, and their `.Id` constants must resolve.

---

## 11. Resolved questions (v236 generator review)

All four open questions are resolved; recorded here as decisions.

- **G-1. Manifest extraction depth — RESOLVED: shallow best-effort.** Parse only the
  straightforward `.Option(...).Consider(...)` chain (`InvocationExpressionSyntax` /
  `MemberAccessExpressionSyntax` traversal). Any `for` loop, local array, or helper indirection
  marks the manifest entry `partial`; the editor and the comparison tuning-diff fast-lane fall back
  to runtime reflection of the built `UtilityDecisionDef`. The runtime is never hostage to the
  parser. (§4.2)
- **G-2. `ushort` `InputId` — RESOLVED: keep `ushort`, exact codebase formula.** Matches the 16-bit
  truncation `BTreeActionGenerator`/`HsmActionGenerator` already use (32-bit FNV-1a, low 16 bits).
  `UT0103` forces resolution of the rare collision at compile time. (§3.3)
- **G-3. `Custom(propertyPath)` analyzer support — RESOLVED: reserve, don't build.** Reserve the
  `UT01xx` id range and the `In.Custom` accessor shape; do not emit the accessor or write the
  string-path AST validator in Slice 1 (validating arbitrary struct paths in Roslyn is scope creep).
- **G-4. Cross-assembly inputs — RESOLVED: generator current-assembly-only, analyzer cross-assembly.**
  The generator emits per compiled assembly; the analyzer scans `context.Compilation.GlobalNamespace`
  across referenced assemblies (symbol scan) for `UT0120` resolution. (§6.2)

---

*End of Utility AI Source Generator & Analyzer DD v1.1. Sibling to the Utility Editor DD (which
consumes the `In` accessors and manifest defined here). Extends `Fdp.Toolkits.Analyzers` with two
generators and one analyzer, following the established BTree/HSM/Gizmo conventions.*
