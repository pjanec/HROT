<!--STATUS
state: LIVE
build-state: DISPATCH — Axis-B first cut. UXI-30 (binary authority gate) + rotation attribute id + the
  subsystem-agnostic write helper the rotator gizmo drives. Establishes the owned→direct / unowned→request path.
updated: 2026-08-25
current-answer: pointer + autonomy. Design (with UML): DESIGN_Cgf_AxisB_Rotation_Slice.md.
known-conflict: ✅ PARALLEL-SAFE with the MCP create-recipes session (disjoint files). ⚠ ONE shared file:
  CgfSubsystem.cs — this session keeps to GIZMO REGISTRATION; MCP keeps to the asset-service dict. ⛔ Do NOT add
  an MCP route (MCP owns the generated catalog); test via integration rails + existing entity ops.
-->
# HANDOFF — **Axis-B first cut: subsystem-agnostic write path + rotation** *(engine/CGF lane — parallel with MCP)*

> 📌 **Dispatched at `<coordinator HEAD>`** *(confirm `git rev-parse origin/claude/blueprint-authoring-status-6sr5ld`)*.
> ⭐ Branch FRESH from the coordinator *(rule 7)*; **rule 1b started-marker before any code.** ⛔ No PR.
> ⭐ Allocate a **NEW id prefix `AX-`** in a **new tracker area** *(clear of `MA-`/`CE-`)*; state every id *(rule 5)*.
> ⚠ **The variable-model freeze is LIFTED** *(`2026-08-25`)*.

## 0. ⛔ THE DESIGN IS THE SOURCE
📄 **[`DESIGN_Cgf_AxisB_Rotation_Slice.md`](../../DESIGN_Cgf_AxisB_Rotation_Slice.md)** *(READY-TO-BUILD)* — §1 inventory,
§2 the routing model *(user ruling)*, §4 classDiagram, §5 sequenceDiagram, §6 items, §7 gates. ⭐ Build what §4/§5
draw; report the match *(obligation ③)*; fold deviations back into the design *(obligation ⑤)*. 📄 Ruling basis:
`UX_Feature_Authority_Aware_Writes.md` §3.3b-c · `HROT-PROGRAMMERS-GUIDE.md` Part 0 rule 8.

## 1. ⛔ AUTONOMY + BUILD RULES
§0-style autonomy *(decide-and-log; stop the item not the batch; DONE = §7 rails green)*. Build the AFFECTED
PROJECT *(`Fdp.Toolkits` · `Hrot.SimHost` · `Hrot.Network.NED` · the integration suite)*, ⛔ never the whole
solution in the fix loop; build once then `--no-build`; the system/integration suite is **T3 — background it**.

## 2. ⭐⭐⭐ WHAT TO BUILD *(design §6)*
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **UXI-30** — add the `CanWrite` gate to `BinaryInterpreter.Apply`, mirroring the JSON path *(`IEntityPatchContext.CanWrite`)* | ⛔⛔ gate **change-request** appliers ONLY. ⛔ **Do NOT touch replication ingress** *(`GeoSpatialIngressTranslator` writes unowned by design — the INVERSE; breaking it corrupts ghosts repo-wide — Part 0 rule 8)*. ⭐ zero production senders ⇒ safe to switch on |
| ⭐ **②** | **`AttributeIds.GeoHeading`** *(degrees, 0=N/90=E — the Geo* family)* + a `SimTransformHeading` installer that **reuses `SimTransformBridgeSystem.HeadingDegToRotation`** *(+ `RotationToHeadingDeg` for read-back)* | ⛔ **do NOT write new conversion math — the compass convention + conversion + wire field (`EulerOri.Heading`, `GeoSpatialEgressTranslator`) all exist.** The ONLY gap is the `AttributeIds` constant + a thin installer |
| ⭐ **③** | **`IEntityComponentWriter`** — owned→direct ECS write / unowned→`AttributeRecord(Rotation)` request | ⭐ SimTransform direct write sets **NO change flag** *(egress diffs `lastSent`)*; ⚠ the helper ASKS the component whether it needs one — ⛔ does not assume |
| ⭐ **④** | make **`EntityRotatorGizmo` subsystem-agnostic** — commit through the helper; drivable from CGF | ⛔ no SimHost-only ECS poke in the commit path |

## 3. ⭐ HOW TO TEST *(design §7)*
Rails: **the UXI-30 gate** *(a binary change-request for an UNOWNED component is skipped — red by removing the gate)* · **the replication inverse still works** *(a ghost still receives owner state — anti-regression, 29.23)* · **rotation round-trips on a real `--mode all` cluster** *(owned node rotates directly; a non-owning node's rotate becomes a request the owner applies)* — reuse the barrier harness shape *(`Hrot.ClusterRunner.Integration.Tests` / the `--mode all` conformance suite)*. ⛔ **Do NOT add an MCP route** *(the MCP session owns the catalog)* — drive via existing entity ops / integration rails.

## 4. ⭐ LANE & COLLISION
⭐ **Yours:** `Fdp.Toolkits/Replication/Patching/**` *(BinaryInterpreter, AttributeIds)* · `Hrot.Network.NED/Attributes/**` · `Hrot.SimHost/Gizmos/**` · the write helper's home · `CgfSubsystem.cs` **gizmo-registration region only** · the integration rails. ✅ **Parallel-safe with the MCP session** — it owns DebugApi/catalog + the CGF asset-service dict; you touch NEITHER. ⚠ **`CgfSubsystem.cs` is the one shared file** — keep to gizmo registration; MCP keeps to the asset-service dict. ⭐ Rule 4: re-pull coordinator before the final commit.

## 5. GATES *(rule 8 contract)* + WHEN DONE
One row per gate · counts · Δ vs the started-marker · `--no-build` column · reds by `git diff` · `tracker-counts.py` · `rulings-check.py` · `design-digest.py --check` · the `AX-` ids. **Row 8:** the UXI-30 gate rail · the replication-inverse anti-regression · the `--mode all` rotation round-trip. **When done:** fold the as-built into the design *(obligation ⑤)*; flip the gap-map UXI-30 + Axis-B rows; state the `AX-` ids; the report points at the design and carries the DECISION LOG.
