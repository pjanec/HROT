<!--STATUS
state: LIVE
build-state: DISPATCH — CGF/engine lane. AX-005 (the cross-node change-request egress, under STRICT NETWORK
  SEPARATION) + the drag gizmo + the low-risk cleanups CE-035/036/018. One session, independent items (R-106).
updated: 2026-08-25
current-answer: pointer + autonomy. Design (with UML + the R-134 ruling): DESIGN_Cgf_AxisB_Rotation_Slice.md §11.
known-conflict: ⚠ ONE shared file with the running MCP-diagnostics slice — EditorSubsystem.cs (CE-018 walk-up
  region vs the diagnostics log-sink pass). Different regions; rule 4 re-pull before final commit.
-->
# HANDOFF — **Axis-B cross-node egress + drag gizmo + cleanups** *(CGF/engine lane)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm `git rev-parse origin/claude/blueprint-authoring-status-6sr5ld`)*.
> ⭐ Branch FRESH from the coordinator *(rule 7)*; **rule 1b started-marker before any code.** ⛔ No PR.
> ⭐ Continue **`AX-`** *(Axis-B)* + **`CE-`** *(cgf==editor)* ids; state every id *(rule 5)*. ⚠ Freeze LIFTED.

## 0. ⛔⛔⛔ THE BINDING RULING — **R-134: STRICT NETWORK SEPARATION**
📄 `DESIGN_Cgf_AxisB_Rotation_Slice.md` **§11** *(READY-TO-BUILD)* + `RULINGS.md` **R-134**.
⭐⭐⭐ **The gizmo / write-router / FDP-bus intent speak FDP-INTERNAL types ONLY** *(`AttributeValueKind`)*.
⛔ **No DDS structure** *(`AttributeRecord`, `AttributeValueUnion`, `AttributeValueType`, `UpdateEntityAttributeRequest`)*
**anywhere in the internal path.** ⭐⭐ **The egress translator is the SOLE boundary** — it converts the internal
record + enum → the DDS message + `AttributeValueType`. Precedent: `NavigationIntentEgressTranslator` *(internal
`Fdp.Toolkits.Navigation.NavigationIntent` → wire `Hrot.NED…NavigationIntent`)*. ⛔ Enum duplication is CORRECT, not debt.

## 1. ⛔ AUTONOMY + BUILD RULES
§0-style autonomy *(decide-and-log; stop the item not the batch; DONE = §3 rails green)*. If the codebase-memory MCP
tools are not connected, use the **CLI** *(`codebase-memory-mcp cli <tool> '<json>'` — `.claude/CLAUDE.md`)*; ⛔ not grep-only.
Build the AFFECTED PROJECT *(`Hrot.Network.NED` · `Hrot.SimHost` · `Fdp.Toolkits` · `Hrot.Editor` · the integration suite)*,
⛔ never the whole solution in the fix loop; build once then `--no-build`; system/integration suite is **T3 — background it**.

## 2. ⭐⭐⭐ WHAT TO BUILD *(design §11.3 / §11.6)*
| # | task | the one thing not to get wrong |
|---|---|---|
| 🔴 **AX-005a** | **Fix the as-built coupling** *(§11.2)*: `AttributeEntityComponentWriter` uses DDS types in the FDP-internal path. Move it to an **FDP-internal** change record with `AttributeValueKind` | ⛔⛔ **R-134** — DDS types leave the internal path entirely; report which mechanism you chose for the local-owned apply *(internal repr vs convert-at-boundary)* |
| 🔴 **AX-005b** | **The FDP-bus intent** `EntityAttributeChangeIntent { networkId/entity, attributeId, value, AttributeValueKind }` + wire the router's `_publishRequest` to publish it | ⛔ FDP-internal only — no `AttributeRecord` here |
| ⭐ **AX-005c** | **The request egress translator** — subscribe to the intent, resolve `Entity → NetworkId` *(`NetworkEntityMap`)*, convert `Kind → Type`, DDS-write `UpdateEntityAttributeRequest{AttributeRecords}`; **register via the network module** | ⛔ NOT `Program.cs` *(the diagnostics slice edits it)*; mirror `UpdateEntityAttributeCommandEgressTranslator`, ⛔ not the per-tick descriptor scan |
| ⭐ **AX-005 rail** | **round-trip on a real `--mode all` cluster** — a non-owning node rotates a **SimHost-owned** entity → SimHost applies it, ownership-gated *(AX-001)* | discharges §9.4's open item; red by removing the egress |
| ⭐ **AX-007** | **`EntityDragGizmo`** *(exists)* commits **position** through the same router *(`GeoLat`/`GeoLon`)* → move + rotate on one path | reuse the router; ⛔ no new write mechanism; ruling 32 wanted drag here |
| ⭐ **CE-035** | `RequestContinue`-after-step no-op → route through `RequestResume` | neutral-assembly; keep it minimal |
| ⭐ **CE-036** | the stale `Requires CycloneDDS` skips in `Hrot.ClusterRunner.Integration.Tests` — real cause **domain id 250 out of range** | fix the domain id or re-document with evidence *(R-131: don't just filter-around)* |
| ⭐ **CE-018** | `EditorSubsystem`'s two inline `.csproj` walk-ups → `AssetRoots` | ⚠ **the MCP-diagnostics slice also edits `EditorSubsystem.cs`** *(log-sink wiring)* — keep to the walk-up region; rule 4 re-pull |

## 3. ⭐ DONE — rails *(design §11 / §7)*
- the AX-005 `--mode all` round-trip *(above)*; **a structural rail that FAILS if any DDS type appears in the FDP-internal write path** *(the R-134 guard)*; the drag round-trip *(move reaches the owner)*; CE-035 continue-after-step green; CE-036 un-skipped + green (or justified with evidence); CE-018 a deployed-shape node resolves roots.
- affected-project builds; the integration/conformance suite named + run *(T3, background)*; `git diff` proves pre-existing reds.

## 4. ⭐ LANE & COLLISION
⭐ **Yours:** `Hrot.Network.NED/**` *(the egress + the FDP-internal record)* · `Fdp.Toolkits/Replication/Patching/**` *(if `AttributeValueKind` needs extending)* · `Hrot.SimHost/Gizmos/**` · the network-module registration · `EditorSubsystem.cs` **CE-018 walk-up region only** · `Hrot.ClusterRunner.Integration.Tests/**`. ⛔ **Do NOT touch** DebugApi/catalog, `Program.cs`, or the diagnostics log-sink wiring *(the running MCP-diagnostics slice)*. ⭐ Rule 4 re-pull before the final commit.

## 5. GATES *(rule 8)* + WHEN DONE
one row per gate · counts · Δ vs the started-marker · `--no-build` column · reds by `git diff` · `tracker-counts.py` · `rulings-check.py` · `design-digest.py --check` · the `AX-`/`CE-` ids. **When done:** fold the as-built into `DESIGN_Cgf_AxisB_Rotation_Slice.md` §11 *(obligation ⑤)*; flip the gap-map Axis-B rows; state the ids; the report points at §11 and carries the DECISION LOG.
