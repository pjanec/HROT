# BATCH-01 Review

**Batch:** BATCH-01 — Phase 0 Infrastructure
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Summary

All three Phase 0 tasks are implemented correctly.  29 tests pass, full solution builds
with 0 errors and 0 warnings.  The JSON polymorphism workaround is well-reasoned and
properly documented.

---

## Scope Check

- TASK-P0-001: COMPLETE. All four projects created, solution updated, `Hrot.AI.Behaviors.csproj`
  updated with generator reference + `AdditionalFiles` glob, `Fdp.Toolkits/Blueprints/` stub
  files in place. Generator targets `netstandard2.0`. No `Hrot.Blueprints.Engine` project.
- TASK-P0-002: COMPLETE. All 19 concrete `Node` subclasses present and sealed.
  `BlueprintJsonServices` uses the `CreateExtended` workaround for frozen `DefaultRelaxed`.
  All enums and record types present.
- TASK-P0-003: COMPLETE. `AssetJsonRoundTripTests` and `SchemaReflectionTests` cover all SCs.
  Round-trip comparison is byte-identical string comparison (correct).
  All 19 node types exercised.

---

## Design Alignment

All types, namespaces, and constraints match Architecture v1.2 §3 and §5 requirements.

The `CreateExtended` workaround for `[JsonPolymorphic]` + frozen `FdpJsonOptionsRegistry.DefaultRelaxed`
is the correct approach per TASK-P0-002 spec.

---

## Test Quality Assessment

Tests are HIGH QUALITY:

- `SchemaReflectionTests.ConcreteNodeSubtypeCount_Is19` uses reflection to assert the
  exact count — this will catch any accidental missing or extra subclass.
- `SchemaReflectionTests.DiscriminatorRoundTrip_EachNodeKind` tests all 19 node types
  individually via `[Theory]` — each type round-trips through JSON and comes back as
  the correct runtime type.
- `AssetJsonRoundTripTests` tests all three dispatch kinds with realistic `BlueprintAsset`
  objects covering `AiPrimitiveDecl`, `Parameters`, `WorkingState`, new v1.2 node types
  (`ChannelCommandNode`, `WaitForChannelNode`, `WaitForEventNode`), and the polymorphic
  19-node graph.
- Missing-field and unknown-field tolerance tests cover the relaxed deserialization contract.
- All assertions check values and types, not just "no exception".

---

## Issues Found

### Minor (P3 — track as debt)

1. **Generators cannot reference Hrot.Blueprints.Core** — `netstandard2.0` cannot restore
   a `net8.0` project reference. The generator currently validates `.bp.json` files only
   structurally (checks that content starts with `{`). Full schema validation requires a
   shared `netstandard2.0` contracts assembly. Deferred to Phase 3.

2. **Pre-existing `WorldResetEvent` test** — `Hrot.Presentation.Tests/WorldResetTests.cs`
   calls `Assert.NotNull(evt)` on a value-type struct, generating xUnit2002. The developer
   suppressed it with `<NoWarn>xUnit2002</NoWarn>`. The test itself is logically pointless
   (`Assert.NotNull` on a struct is always true) but the suppression keeps the build clean.
   Fix the test properly in a future housekeeping pass.

---

## Approved Git Commit Message

```
feat(blueprints): Phase 0 infrastructure -- project skeleton + asset schema + JSON tests

Add four new Blueprint subsystem projects:
- Hrot.Blueprints.Core (net8.0): BlueprintAsset schema types, BlueprintJsonServices
- Hrot.Blueprints.Generators (netstandard2.0): incremental generator stub
- Hrot.Blueprints.Editor (net8.0): editor placeholder
- Hrot.Blueprints.Tests (net8.0, xUnit): 29 passing tests

Add Fdp.Toolkits/Blueprints/ stub folder (placeholder runtime types, M8+).
Wire Hrot.AI.Behaviors.csproj with generator reference and AdditionalFiles glob.

All 19 concrete Node subclasses present with [JsonDerivedType] discriminators.
BlueprintJsonServices uses CreateExtended workaround for frozen DefaultRelaxed.
Round-trip tests cover Library/AiPrimitive/Instance dispatch and all 19 node types.
Build: 0 errors, 0 warnings. Tests: 29 passed, 0 failed.

Resolves: TASK-P0-001, TASK-P0-002, TASK-P0-003
```
