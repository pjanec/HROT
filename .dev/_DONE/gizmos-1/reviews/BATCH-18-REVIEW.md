# BATCH-18 REVIEW

**Batch:** BATCH-18
**Tasks:** GZ050, GZ051, GZ052
**Reviewer:** Dev Lead
**Status:** APPROVED

---

## Build

`dotnet build IOS-IG-SimHost.sln --no-incremental` → **0 errors**.

---

## Test Results (verified)

| Task  | Tests Passed | Test Class / File |
|-------|-------------|-------------------|
| GZ050 | 6/6 | `Fdp.Diagnostics.Contracts.Tests` SC_GZ050_* |
| GZ051 | 5/5 (+ build-time SC-GZ051-2) | `Fdp.Diagnostics.Contracts.Tests` SC_GZ051_* |
| GZ052 | 5/5 | `Hrot.Network.NED.Tests` SC_GZ052_* |
| **Total** | **16/16** | |

---

## Per-Task Review

### GZ050 — Introduce Semantic and Routing Primitives

APPROVED. All three new shapes (`SemanticShape=8`, `MilStd2525=9`, `SpatialAnchor=10`) added to
the `DebugPrimitiveShape` byte enum. Payload fields added to `DebugPrimitive` explicit-layout
union at the correct offsets (24-63). The `SidcCode` (`MilStd2525`) correctly aliases
`TextContent` at offset 32 — same physical bytes, same type (`FixedString32`). The `NetworkId`
field (`SpatialAnchor`) at offset 24 correctly aliases `InspNetworkId` and `ProfileId` — all
three are the 8-byte ID field at the start of the payload union.

`Marshal.SizeOf<DebugPrimitive>() == 64` confirmed by SC_GZ050_2 and by the layout invariant
test in the regression SC_GZ050_6. The size is enforced by the `[StructLayout(LayoutKind.Explicit, Size = 64)]`
declaration.

SC_GZ050_5 (unrecognized shape silently skipped) verifies the `default: continue` behavior by
setting `Shape = (DebugPrimitiveShape)11` and iterating. This is the correct way to test for
graceful unknown-value handling.

### GZ051 — Fix ComponentInspector Abstraction Leak

APPROVED. ECS-index fields (`InspTargetIndex`, `InspTargetGen`, `InspComponentTypeId`) removed.
New fields: `InspNetworkId` (long at offset 24) and `InspSchemaHash` (uint at offset 32),
followed by `InspAnchor` (byte at 36) and `InspIsReadOnly` (byte at 37). Offsets verified by
SC_GZ051_6 using `Marshal.OffsetOf`.

SC_GZ051_3 verifies FNV-1a hash consistency by replicating the hash function locally and
comparing. This is the right approach since the test assembly may not have direct access to
`GizmoSettingsRegistry.ComputeHash` — the local reimplementation uses the same algorithm.

SC_GZ051_5 verifies that a remote viewer can build a display label from only struct fields
with no ECS dependency — this directly validates the design intent.

No external callsites existed for the removed ECS fields (confirmed by the report noting "no
other files required updating" and the build succeeding). This is consistent with the fields
being internal implementation details of the builder.

P3 note: The `SpatialAnchor` `NetworkId` field (at offset 24) and the `ComponentInspector`
`InspNetworkId` field (also at offset 24) are now aliased at the same physical location. This is
intentional and correct for a union — the `Shape` discriminator at offset 0 distinguishes them.
The naming difference (`NetworkId` vs `InspNetworkId`) is appropriate since they are documented
under different payload sections.

### GZ052 — Entity Attribute Schema Broadcast

APPROVED. `EntityAttributeSchema` DDS topic struct added to `GenericMessages.cs` with correct
QoS annotations (`TransientLocal`, `KeepLast`, `HistoryDepth=1`). The `[DdsKey]` attribute on
`NodeId` ensures late-joining subscribers receive the per-node schema immediately.

`EntityAttributeSchemaPublisherSystem` is in `Hrot.Network.NED` (not `Hrot.SimHost`) —
correct separation. `ExportSchema()` produces valid parseable JSON verified by
`JsonDocument.Parse` in SC_GZ052_4.

SC_GZ052_5 is particularly valuable: it uses the actual `AttributeCompilerFactory.Build(null)` 
(the real production compiler) and verifies the exported schema contains at least one property.
This is a behavioral integration test, not a unit test — it confirms the wiring between the
compiler builder and the schema exporter works end-to-end.

SC_GZ052_3 verifies the critical `isDefaultProcessor=false` guard: in a multi-node SimHost
cluster only one node must broadcast. This prevents the DDS broadcast storm the spec warned about.

---

## Design Alignment

All implementations align with TASK-GZ050, GZ051, and GZ052 in TASK-DETAIL.md. The `DebugPrimitive`
struct remains 64 bytes throughout all changes (size invariant enforced by four independent tests
across GZ050 and GZ051). The ComponentInspector payload is now DDS-safe — it carries no
process-local ECS indices. The attribute schema broadcast enables runtime UI discovery.

---

## Decision

**APPROVED — BATCH-18 is accepted. Proceed to BATCH-19.**

Tasks marked completed: GZ050, GZ051, GZ052.
