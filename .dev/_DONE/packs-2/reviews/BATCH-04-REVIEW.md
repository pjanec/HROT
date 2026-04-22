# BATCH-04 Review

**Batch:** BATCH-04  
**Tasks:** PACK2-E002, PACK2-E003  
**Reviewer:** GitHub Copilot (dev-lead)  
**Decision:** ✅ APPROVED

---

## Build Verification

| Check | Result |
|-------|--------|
| `dotnet build IOS-IG-SimHost.sln --no-incremental` | ✅ 0 errors, 336 warnings (all pre-existing xUnit1030) |

---

## Test Verification

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| `Hrot.IG.Tests` | 408 pass, 7 fail | 408 pass, 7 fail | 0 (pre-existing unchanged) |
| `Hrot.Map.Common.Tests` | 99 pass | 99 pass | 0 |
| `Hrot.ScenarioEditor.Tests` | 2 pass | 7 pass | +5 (ToolPresence ×2, RenderLayerPresence ×3) |

Pre-existing `Hrot.IG.Tests` failures (unchanged by this batch):
- 6× `UniqueNameGeneratorTests` subtests
- 1× `TraceLoggingTests.IngressAndRender_EmitsTraceLines`

---

## Scope Check

### PACK2-E002 — Tool Migration ✅
- 10 tool files moved from `Hrot.IG/Tools/` → `Hrot.ScenarioEditor/Tools/`
- Namespace updated: `Hrot.IG.Tools` → `Hrot.ScenarioEditor.Tools`
- All `using` directives updated in `Hrot.IG` (3 files) and `Hrot.IG.Tests` (10 files)
- `Hrot.IG.csproj` now references `Hrot.ScenarioEditor`
- `ToolPresenceTests.cs` added (2 tests)
- `InternalsVisibleTo` added for `Hrot.IG.Tests` in `Hrot.ScenarioEditor.csproj`

### PACK2-E003 — Render/Adapter Migration ✅
- 5 rendering files moved from `Hrot.IG/Systems/` → `Hrot.ScenarioEditor/Rendering/`
- 4 adapter files moved from `Hrot.IG/Adapters/` → `Hrot.ScenarioEditor/Adapters/`
- Namespaces updated, consumers in `Hrot.IG` and `Hrot.IG.Tests` updated (7 files)
- `RenderLayerPresenceTests.cs` added (3 tests)

### Prerequisite work ✅
- `SelectionState`, `CullingState/Constants`, `ResolvedStyle/Constants` moved to `Hrot.Map.Common/Components/` (keeping `Hrot.IG.Components` namespace)
- Original files in `Hrot.IG/Components/` replaced with comment stubs
- `AllowUnsafeBlocks` enabled in `Hrot.Map.Common.csproj` for `ResolvedStyle` (unsafe struct with `fixed byte`)

---

## Quality Notes

- **No new debt introduced** — all deviations from instructions were necessary and well-documented in report
- `IgCameraConstants.InitialZoom` circular dep resolved cleanly with literal `0.5f` + comment
- NED/CycloneDDS isolation preserved: `Hrot.ScenarioEditor.csproj` has no `Hrot.NED` or `CycloneDDS` references
- Additional NuGet refs (`FDP.Toolkit.ImGui`, `FDP.Toolkit.Replication`, `FDP.Toolkit.Behavior`) added with no transitive NED leak confirmed

---

## Suggested Commit Message

```
feat(packs-2): PACK2-E002 + PACK2-E003 -- Tool and Render Layer Migration to ScenarioEditor

E002: Move 10 tool files Hrot.IG/Tools/ -> Hrot.ScenarioEditor/Tools/
      (namespace: Hrot.ScenarioEditor.Tools); add Hrot.IG -> Hrot.ScenarioEditor ref
E003: Move 5 rendering systems -> Hrot.ScenarioEditor/Rendering/;
      4 adapters -> Hrot.ScenarioEditor/Adapters/
Prereq: Move SelectionState, CullingState, ResolvedStyle to Hrot.Map.Common
        (keeping Hrot.IG.Components namespace, comment-stub originals)

Tests: 7/7 ScenarioEditor, 408/415 IG (7 pre-existing), 99/99 Map.Common
```
