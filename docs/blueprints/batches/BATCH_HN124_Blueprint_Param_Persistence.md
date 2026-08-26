<!--STATUS
state: LIVE
build-state: BUILT
updated: 2026-08-26
current-answer: the whole file — the report for Batch HN-124 (blueprint param persistence + the MCP wire,
  MX-030..036), branched from the coordinator at 42a6ef37. ⛔ EPHEMERAL: the durable truth is
  DESIGN_Blueprint_Param_Persistence.md (§9 AS-BUILT), the tracker (QA-023 + MX-030..036), and the code.
known-conflict: none.
-->
# BATCH HN-124 — **persisted instance-blueprint parameters + the MCP wire**

> 📄 Design: [`DESIGN_Blueprint_Param_Persistence.md`](../DESIGN_Blueprint_Param_Persistence.md) *(authored this batch, per the frame-delegation model)*.
> Frame: [`HANDOFF_Blueprint_Param_Persistence.md`](./HANDOFF_Blueprint_Param_Persistence.md). Decision trail: [`Architect_Question_61`](../Architect_Question_61_Persisted_Blueprint_Assignment_Over_Mcp.md).

Instance-blueprint params were impossible to persist by ANY path: the runtime pipeline resolves them (param
region @16) but save dropped them and load booted to `InitDefault`. This batch makes them round-trip, ships the
run-state-aware attach/detach + list MCP wire that becomes worth having once they persist, and folds `QA-023`.

## Process (frame-delegation)
① rule-7 re-sync + started-marker at `42a6ef37` · ② INVENTORY + authored the design **with class+sequence UML**
(the coordinator gave the FRAME; I designed the HOW) · ③ built affected projects only · ④ folded as-built into
the design §9 · ⑤ this report.

## The design call — persist the RESOLVER SHAPE, not the `Overrides` dict
The measured architectural ruling ([`EXPLAINER_Where_Parameters_And_State_Live.md`](../EXPLAINER_Where_Parameters_And_State_Live.md) §"two supply shapes,
one concept") is decisive: a name→value `Overrides` dict and the resolver's byte region are **two implementations
of one concept (ruling 9)**; the resolver shape wins. `BLUEPRINT-SCENARIO-DESIGN.md` §6's `Overrides` was
**deferred for UX reasons, not chosen**. Since no bytes→JSON inverse of `ParseParams` exists, a JSON form would
mean inventing a second representation. ⇒ persist the **resolved param bytes** — one source of truth (the live
slot), no side table, symmetric with `AttachToEntity`.

## What shipped — MX-030..036
| id | what |
|---|---|
| **MX-030** | `BlueprintAssignmentDto`: dead `Overrides` → `Params` (bytes) + `ParamsStructureHash`; `BlueprintInstanceService.{Read,Write,GetDefault}ParamsRegion` — the one param-region owner, shared by attach+save+load |
| **MX-031** | `BlueprintStateTranslator.Extract` diffs live params vs `InitDefault`, emits non-default bytes + hash |
| **MX-032** | `BlueprintMaterializationSystem` re-applies bytes after `InitDefault`, guarded by `StructureHash` |
| **MX-033** | `QA-023` — `Inject` accepts `JsonArray` / `JsonElement`(Array) / string, not `JsonArray`-only |
| **MX-034** | run-state-aware `attach_blueprint`/`detach_blueprint` — direct (frozen) vs event (advancing) |
| **MX-035** | `GET /entities/{networkId}/blueprints` — the instance blueprints on an entity |
| **MX-036** | `list_entity_blueprints` MCP tool + attach/detach RouteDoc updates; regenerated catalog/skill (98 tools) + test-catalog allow-list |

**Files:** `BlueprintAssignmentDto.cs` · `BlueprintInstanceService.cs` (Fdp.Toolkits) · `BlueprintStateTranslator.cs` ·
`BlueprintMaterializationSystem.cs` (Hrot.SimHost) · `DebugApiService.Reuse.cs` · `DebugApiHost.cs` ·
`DebugApiRouteDocs.cs` (Hrot.Editor) · `tools/ai-debug-mcp/{src/index.mjs, tool-catalog.mjs, SKILL.md, test-catalog.mjs}` ·
tests: `BlueprintAssignmentDtoTests.cs`, `BlueprintScenarioIntegrationTests.cs` (round-trip rail) · doc-comment
fixes in `BlueprintLifecycleEvents.cs`.

## DECISION LOG
| # | decision | why |
|---|---|---|
| **D1** | ⭐ **Persist resolved param BYTES, replace `Overrides`** (not populate it) | EXPLAINER §287 ruling: resolver shape > name→value dict; no bytes→JSON inverse exists, so JSON would invent a 2nd representation |
| **D2** | **Diff vs `InitDefault`; persist only non-default** | keeps scenarios clean — a default assignment stays `{AssetId}` |
| **D3** | **Layout-versioned by `StructureHash`; mismatch → defaults (logged)** | bytes are layout-bound; a recompiled blueprint must not read stale offsets. ⚠ Tradeoff: the blob is opaque in the file and recompile-fragile — accepted, the ruling decides shape |
| **D4** | **Materialization keeps its aggregate-tier low-level path + adds `WriteParamsRegion`** (does NOT delegate to `AttachToEntity`) | `AttachToEntity` picks a per-blueprint tier; Materialization must pre-provision the aggregate tier. Both write params through the ONE writer |
| **D5** | **`QA-023` fix accepts every JSON-array shape** | the value arrives as `JsonElement`(Array) on the reader path; `JsonArray`-only silently dropped the intent |
| **D6** | **Run-state-aware wire mirrors the panel's branch; no `IEntityBlueprintEditorService` facade (Q61-D)** | blueprints have no OCC/version — a facade would invent ergonomics that don't exist (ruling 9: one route matching the panel) |

## Gates *(rule 8)*
| gate | command | result |
|---|---|---|
| Fdp.Toolkits | `dotnet build Fdp.Toolkits.csproj` | ✅ 0 err |
| Hrot.SimHost | `dotnet build … --no-restore` | ✅ 0 err |
| Hrot.Editor + ClusterRunner | `dotnet build … --no-restore` | ✅ 0 err (10 pre-existing warns) |
| DTO round-trip | `dotnet test --filter BlueprintAssignmentDtoTests --no-build` | ✅ 2/0 |
| translator/materialization/genesis | `dotnet test SimHost.Tests --filter … --no-build` | ✅ 25/0 |
| Fdp.Toolkits blueprint | `dotnet test --filter Blueprint --no-build` | ✅ 38/0 |
| **round-trip rail + QA-023** | `dotnet test --filter BlueprintScenarioIntegrationTests --no-build` | ✅ 6/0 (+2 pre-existing skips) |
| route docs | `dotnet test --filter EveryRouteIsDocumented --no-build` | ✅ 4/0 |
| catalog / skill / node | `npm run gen:catalog:check · gen:skill:check · test:catalog` | ✅ 98 tools; 785/0 |
| STATUS/UML/inventory | `design-digest.py --check` | ✅ |
| tracker | `tracker-counts.py --check` | ✅ open 102 / done 346 (BP unchanged; QA/MX not BP-counted) |
| rulings | `rulings-check.py` | ✅ 25/25 (pre-existing WARN on `.claude/CLAUDE.md`, `DataBreakpointManager.cs`) |
| mermaid | `mermaid-check.mjs` on the design | ✅ 2/2 blocks parse |

⭐ **Round-trip red-proof:** the rail asserts the reloaded slot's param region equals the saved non-default bytes;
removing the `WriteParamsRegion` call in Materialization makes it read the all-zero default → red. QA-023's
`Test5b` green (`JsonArray`-only match → intent dropped, now fixed).

## Lane
Scope expanded into `Fdp.Toolkits` + `Hrot.SimHost` blueprint-serialization **as declared by the frame**; the
backend's concurrent batch is fenced OFF these exact files and its handoff hands QA-023/`BlueprintStateTranslator`
to the MCP lane, so no collision. ⛔ Did not touch UI/CGF scenario/menu/viewport code. Rule-4 re-pull before final commit.

## NOT done (out of scope, documented)
The **same blueprint twice on one entity** — slot identity is `blueprintId` alone; `(blueprintId, instanceKey)` is
a separate, larger identity change. Single-instance persist only.
