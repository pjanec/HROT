# BATCH-04 Review

**Status: APPROVED**

**Date:** 2026-04-29

---

## Summary

BATCH-04 approved. 128/128 tests pass. FBT-011 (BTreeActionGenerator) and FBT-013 (FbtAutoDiscovery) implemented.

---

## Code Review

### FBT-011 — BTreeActionGenerator
✅ `Fbt.SourceGen.csproj` — netstandard2.0, IsRoslynComponent=true, correct Roslyn package versions matching FastHSM.
✅ `BTreeActionGenerator : IIncrementalGenerator` — uses `SyntaxProvider.CreateSyntaxProvider` pipeline correctly.
✅ Attribute detection by name (`"BTreeActionAttribute"`, `"BTreeConditionAttribute"`) — correct, no hard type reference to Fbt.Kernel.
✅ 3-param vs 4-param detection by `symbol.Parameters.Length` — clean.
✅ 3-param paths emit `BTree001` Warning diagnostic — correct severity (not error).
✅ 4-param paths emit typed `RegisterAll` in namespace `{AssemblyName}.Generated`.
✅ Generated class tagged `[FbtRegistrar]` — tested by reflection.
✅ Generator skips assemblies with no marked methods — no empty registrar emitted.

### Design Acceptance: Non-generic `RegisterAll`
The generated `RegisterAll` is typed to concrete `TBlackboard`/`TContext` types extracted from the method parameters (not generic), because generic registration would not compile when delegates are typed to specific struct types. `FbtAutoDiscovery` catches type-mismatch silently. This is correct and pragmatic.

### FBT-013 — FbtAutoDiscovery.ScanAndRegister
✅ Scans `AppDomain.CurrentDomain.GetAssemblies()`.
✅ Matches `[FbtRegistrar]` by `IsDefined` check.
✅ Two-level `try/catch` — outer for assembly reflection, inner for `RegisterAll` invocation.
✅ No FDP-specific types — generic `<TBlackboard, TContext>`.

---

## Test Quality Review
✅ `FbtRegistrarAttribute_IsAppliedToGeneratedClass` — confirms generator emitted and annotated the registrar.
✅ `ScanAndRegister_FindsGeneratedRegistrar_InTestAssembly` — full round-trip: generator emits → auto-discovery finds → registry populated.
✅ Existing 123 tests unaffected.

---

## Known Gaps (Technical Debt Added)
| ID | Description | Priority |
|----|-------------|----------|
| DT-004 | FBT-011 generator skips 3-param reusable delegates; requires BTreeBuilder expression binding for registration. Future: add `[BTreeAction(BlackboardType, FieldName)]` attribute fields. | P2 |

---

## Decision: APPROVED — Proceed to BATCH-05
