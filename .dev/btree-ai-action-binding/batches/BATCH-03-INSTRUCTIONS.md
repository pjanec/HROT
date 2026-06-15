# BATCH-03: Struct-DTO size resolution in the build-time packer (S1-2b)
**Tasks:** S1-2b   **Phase:** Slice 1   **Est:** ~9h
**Dependencies:** BATCH-02 (S1-2/S1-3/S1-4 landed). **Precedes S1-G.**
**User decision (2026-06-15):** full solution, NO primitive-only simplification. The whole-DTO binding model requires managed variables whose declared type is the action's param-0 **struct** DTO (e.g. `DemoCounterParams = {int Counter; int Threshold}`, 8 B). Today the build-time packer only knows primitive/Vector sizes and throws/skips for struct DTOs. Fix it properly.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — your working contract.
2. `docs/blueprints/BTree_AiActionParameterBinding_Detailed_Design.md` ("AIB-DD") §2, §3.2 — projection model + offsets-from-compiled-struct.
3. `.dev/btree-ai-action-binding/SLICE1-DESIGN.md` §3.1 (whole-DTO binding), §3.2.
4. `.dev/btree-ai-action-binding/reviews/BATCH-02-REVIEW.md` — how S1-2/S1-3 currently work (single-offset-source via `BTreeBlackboardPackHelper`).
5. `.dev/btree-ai-action-binding/TASK-DETAIL.md` §S1-2b (success conditions / named tests).

Use codebase-memory MCP graph tools FIRST. `read_file` only for exact edit targets.

## Verified mechanism map (use this; do not re-derive)
- **Reuse this size logic:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/BTreeActionGenerator.cs` has private `GetTypeSize(ITypeSymbol)` (l.496), `GetTypeAlign` (l.523), `ComputeStructSize(INamedTypeSymbol)` (l.529: sequential = align each field to `min(size,8)`, pad struct to `maxAlign`; detects `[StructLayout(Explicit)]`), `AlignUp(v,a)` (l.571). **Identical logic also in** `FDP/Toolkits/Fdp.Toolkits.Analyzers/BehaviorParameterSizeAnalyzer.cs:81` `ComputeStructSize`. These compute **managed** (C# sequential) size: bool=1, enums=underlying, nested structs recursive. This is exactly the size the `Unsafe.As` projection assumes — use managed size, NOT `Marshal.SizeOf` (which gives bool=4).
- **Compilation availability:** `Hrot.AiEditor.Generators/BTreeJsonGenerator.cs` `Initialize` combines files with `context.CompilationProvider`; `GenerateOneAsset(spc, path, text, Compilation compilation)` HAS the `Compilation` but currently passes it ONLY to `BTreeMethodCompatibilityValidator.Validate`. The emit helpers (`BTreeEmitCore.EmitTopologyCore`/`EmitBlackboardStructSource`, `BTreeBridgeEmitCore.EmitBridge`) receive the DTO only.
- **Packer:** `Hrot.AiEditor.Persistence/Emit/BTreeBlackboardPackHelper.cs` — `KnownSizes` (primitives+Vectors), `Pack(vars, out total)` throws `NotSupportedException` for unknown TypeId, `WouldOverflow(vars, out unknownTypeId)`. The Persistence assembly is netstandard2.0 and **does NOT reference Roslyn** — so struct sizing via symbols MUST be done in the Generators assembly and injected into the packer.
- **TypeId format:** `BehaviorTreeAssetMapper` writes `v.FieldType.FullName`. For a **nested** struct this uses `+` (e.g. `Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCounterParams`); for namespace-level types it uses `.`. `compilation.GetTypeByMetadataName(typeId)` expects the metadata (`+`) form — so the resolver can look up nested types directly by the stored TypeId.
- **Validator nested-type bug (fix in this batch):** `BTreeMethodCompatibilityValidator.CheckThreeParamReusable` (l.282) derives `param0TypeFqn` via `SymbolDisplayFormat(... NameAndContainingTypesAndNamespaces)` which uses `.` for nested types, then compares `Ordinal` against the variable `TypeId` (which is `+` for nested). So a nested struct DTO variable would FAIL type-match purely on the separator. Must normalize.

## Task: S1-2b — struct-DTO size resolution + nested-type robustness

### 1. Roslyn struct-size resolver (Generators assembly)
Add an internal resolver in `Hrot.AiEditor.Generators` (e.g. `StructSizeResolver.cs`) that, given a `Compilation` and a variable `TypeId` string, returns the **managed** byte size (or null/unresolved):
- If `TypeId` is a known primitive/Vector (mirror `BTreeBlackboardPackHelper.KnownSizes`), return that.
- Else `compilation.GetTypeByMetadataName(typeId)` → if a value-type struct, compute its managed size by **porting** the `ComputeStructSize`/`GetTypeSize`/`GetTypeAlign`/`AlignUp` logic from `BehaviorParameterSizeAnalyzer` (sequential: align cap 8, pad to maxAlign; recursive nested structs; bool=1; enums=underlying). Add a header comment: `// Mirrors Fdp.Toolkits.Analyzers.BehaviorParameterSizeAnalyzer.ComputeStructSize — keep in sync.`
- Unresolvable (symbol not found / non-struct / unknown field type) → return null (caller skips the asset with BTREE0002, never a silent emit).

### 2. Inject resolved sizes into the packer + emit path
- Add a `Pack` overload (or parameter) to `BTreeBlackboardPackHelper` accepting an injected size map / resolver delegate: `Pack(IReadOnlyList<BlackboardVariableDto> vars, Func<string,int?> extraSizeResolver, out int total)`. Lookup order: `KnownSizes` → `extraSizeResolver(TypeId)` → throw/overflow. Keep the existing primitive-only `Pack`/`WouldOverflow` working (delegate to the new form with a null resolver) so BATCH-02 tests stay green.
- In `BTreeJsonGenerator.GenerateOneAsset`: build a `Dictionary<string,int>` (TypeId → managed size) for every non-primitive variable type by calling the resolver; if ANY managed variable's type is unresolvable, report `BTREE0002` and skip the asset (do not emit a partial struct). Pass the size map (or resolver) into `EmitBlackboardStructSource`, `EmitTopologyCore`, and `EmitBridge` (thread it down to `BuildVariableOffsets` and `EmitManagedActionThunks`/`EmitManagedConditionThunks`). Update `WouldOverflow` usage to account for struct sizes too (overflow check must use the same resolved sizes).
- Struct emitter (`EmitBlackboardStructSource`): for a struct-typed variable, emit a field of that DTO type using the fully-qualified `global::`-prefixed name with `.` separators (convert nested `+`→`.`), e.g. `public global::Hrot.AI.Behaviors.Brains.DemoCounterNodes.DemoCounterParams Counter;`. (Primitive fields unchanged; bool keeps `[MarshalAs(I1)]`.) The field's `Marshal.OffsetOf`/managed offset must equal the packer's resolved offset.

### 3. Validator nested-type normalization
In `CheckThreeParamReusable`, compare `param0TypeFqn` and `varTypeId` after normalizing nested-type separators (`+`↔`.`) on both sides (or compare by resolving both to the same symbol). A nested struct DTO variable must validate when its type equals param-0's type.

## Tests required (`Hrot.AiEditor.Generators.Tests`) — implement exactly; do not invent
Use stub struct DTOs declared in the test's compiled stub source (mirror the existing `ThreeParamStubs` pattern). Include at least one **nested** struct DTO and one **namespace-level** struct DTO of a **different** size.
- `StructDtoVariable_ResolvesManagedSize` — resolver returns the correct managed size for: a `{int;int}` struct (8), a `{int;float;bool}` struct (12: bool=1 padded), a nested struct, and a struct containing a `Vector3` (offsets/align cap 8). Assert each, AND assert it equals the runtime **managed** size for the equivalent real type (compute via the same align rules or a reference value stated in the test) — NOT `Marshal.SizeOf` for bool-containing structs.
- `StructDtoVariable_PacksAtResolvedOffsets` — managed asset with two variables of **different** struct DTO types; assert the second variable's packed offset = size-of-first (aligned), the emitted struct declares both DTO-typed fields, total ≤100 B.
- `StructDtoVariable_TopologyAndRegistrar_CarryResolvedOffsets` — run `EmitTopologyCore` + `EmitBridge`; assert the blob key AND the registrar thunk for the second variable carry the resolved non-zero offset (`{Fqn}@{offset}` and `Unsafe.AddByteOffset(..., (nint){offset})`), identical on both sides.
- `NestedStructDto_TypeMatch_Validates` — a `ThreeParamReusable` binding to a variable whose `TypeId` is the `+`-nested form, param-0 the same nested struct ⇒ `Validate` returns null (separator normalization works).
- `StructDtoVariable_AggregateOver100Bytes_SkipsWithBtree0002` — variables whose resolved struct sizes sum >100 B ⇒ generator reports BTREE0002 and emits no `.Blackboard.g.cs` (run the generator, as in BATCH-02's rewritten overflow test).
- `UnresolvableStructDto_SkipsWithBtree0002` — a managed variable whose TypeId resolves to no symbol ⇒ generator reports BTREE0002 and skips (no partial emit).

## Success Criteria
- [ ] Resolver computes managed struct sizes from the Compilation (primitives, Vectors, nested structs, bool=1, enums).
- [ ] Generator packs/emits struct-DTO-typed managed variables (struct + topology + registrar all at the resolved offset); unresolvable/over-budget → BTREE0002 skip (no silent/partial emit).
- [ ] Validator validates nested-type DTO bindings.
- [ ] All named tests pass; BATCH-02 generator/persistence tests stay green (`Hrot.AiEditor.Persistence.Tests` byte-identity 129/0; `Managed==false` unchanged); the 2 known `MigrationEquivalenceTests` excepted.
- [ ] Clean rebuild (`dotnet build-server shutdown` first) 0 errors; report submitted.

Run all tests and fix root causes to completion **without asking permission**. Only stop on a breaking design↔codebase contradiction (describe it in the report).

## Report Requirements (`.dev/btree-ai-action-binding/reports/BATCH-03-REPORT.md`)
Answer: where the resolver lives and how you kept it in sync with `BehaviorParameterSizeAnalyzer.ComputeStructSize` (port vs shared); the exact `Pack` overload signature; how the size map threads from `GenerateOneAsset` to all three emit helpers; how you normalized the nested-type separator in the validator (and which direction TypeId vs FQN); the managed-vs-Marshal size decision and why bool matters; any types you could NOT resolve and how you fail; weak points; suggested commit message. Do NOT ask comprehension questions.
