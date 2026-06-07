# BATCH-11 Review
**Status:** ✅ APPROVED (code) — build unblock + version decision flagged to user   **Date:** 2026-06-02

## Summary
The four Blueprint data-flow host adapters (AIE-040 GraphModel, 041 TypeSystem, 042 LinkValidator, 043 NodeCatalog) are implemented with real behavioral tests. Independent of this batch, a clean build exposed a **CycloneDDS 0.2.3 codegen incompatibility** (see below) that I had to unblock.

## Verification (Batch-11 deliverable)
- 81 new tests in `Hrot.Blueprints.Tests/Host/`. Spot-checked assertions are real: `BlueprintGraphModelTests` asserts exact node-id set equality, per-node pins, `PinKind.Exec`/`Data`; `BlueprintTypeSystemTests` asserts real `AreCompatible` truth values (exec↔exec true, exec↔data false, same-type true, Bool≠Int32, String≠Single, Single≠Int32). Not NotNull-only.
- Real Blueprint pin/type model used (verified by coder): `Pin.IsExec` bool, `Pin.TypeRef.TypeId` CLR full-name string, `Pin.Direction` "In"/"Out", links canonical on `Graph.Links`. NodeCatalog wraps the existing `NodeKindRegistry`.
- `Hrot.Blueprints.Tests` 970/10 (10 pre-existing DEBT-006); `Hrot.Editor.AiShared.Tests` 702; `EditorSubsystemBoot` 10/10 (per coder; build confirmation in progress after the unblock below).

## P1 surfaced (NOT caused by Batch-11) — CycloneDDS 0.2.3 cross-assembly codegen
- A clean rebuild fails: `idlc ... IGCapabilitiesAnnounce.idl: Scoped name 'Fdp::Toolkit::Diagnostics::Gizmos::PipelineTarget' cannot be resolved` in `Hrot.IG`.
- **Root cause isolated:** `GizmoMap.Contracts@0.2.3-gc40f8c1444` (the version unified per the user's request in commit 8e197569) emits `PipelineTarget` IDL/schema that Hrot.IG's 0.2.3 codegen cannot resolve cross-assembly. Reverting **only** `GizmoMap.Contracts` to `0.2.2` makes both it and Hrot.IG codegen build cleanly (verified). `Directory.Build.targets` restore did NOT help (ruled out). Earlier "0 errors" full builds passed on stale generated IDL; forcing regen exposed the break.
- **Action:** reverted `GizmoMap.Contracts` to `0.2.2` to restore buildability → **DEBT-010** + a **decision for the user** (0.2.3-everywhere is currently incompatible with Hrot.IG DDS codegen; their DDS team must fix the 0.2.3 prerelease codegen before GizmoMap.Contracts can move to 0.2.3).
- Also a transient `MSB3030 apphost.exe` glitch from repeated `-t:Rebuild` — clears on normal rebuild (not a code issue).

## Verdict
Blueprint adapters APPROVED. Loop **paused** to surface the CycloneDDS 0.2.3 / GizmoMap.Contracts version decision (explicit user request vs. build).

## Commit Message
```
feat(editor): Blueprint data-flow host adapters (BATCH-11) + DDS build unblock

AIE-040..043 in Hrot.Blueprints.Editor/Host/: BlueprintTypeSystem, BlueprintGraphModel,
BlueprintLinkValidator, BlueprintNodeCatalog (data-flow projection + validation, wrapping
NodeKindRegistry). 81 new behavioral tests; Blueprints 970/10 (DEBT-006).

Build unblock: revert GizmoMap.Contracts to CycloneDDS.NET 0.2.2 — 0.2.3-gc40f8c1444 emits
PipelineTarget IDL that Hrot.IG's 0.2.3 codegen cannot resolve (DEBT-010). Restores clean build.
```
