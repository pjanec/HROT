# ATTR2 Debt Tracker



| ID | Priority | Description | Source | Target Batch | Status |
|---|---|---|---|---|---|
| ATTR2-DEBT-01 | P3 | `CreateUpdateDeleteEntityAck.OpaqueData` using `List<byte>?` is an allocation concern; should be fixed-size array or `Memory<byte>` | ATTR2-BATCH-01 Report | ATTR2-BATCH-05 | ✅ Resolved |
| ATTR2-DEBT-02 | P3 | `GenericMessages.cs` mixes generic IDL primitive helpers (`Vec3f` etc.) with DDS infrastructure types. Consider extracting to `GenericPrimitives.cs` | ATTR2-BATCH-01 Report | ATTR2-BATCH-05 | ✅ Resolved |
| ATTR2-DEBT-03 | P4 | Edge compiler string interning for `KindString` allocations. | ATTR2-BATCH-02 Report | ATTR2-BATCH-05 | ✅ Resolved |
| ATTR2-DEBT-04 | P4 | Refactor `Apply` scratchpad zeroing to use blanket `Span<byte>.Clear` for predictable state without `Initialized` flags. | ATTR2-BATCH-02 Report | ATTR2-BATCH-05 | ✅ Resolved |
| ATTR2-DEBT-05 | P4 | Convert `_routes` dictionary to concrete type or array-based dispatch for micro-optimizations. | ATTR2-BATCH-02 Report | ATTR2-BATCH-05 | ✅ Resolved |
| ATTR2-DEBT-06 | P3 | `UpdateEntityAttributeRequestSystem` requires `_jsonCompiler` purely for `EcsPatchContext` factory. Extract standalone factory. | ATTR2-BATCH-03 Report | ATTR2-BATCH-04 | ✅ Resolved |
| ATTR2-DEBT-07 | P2 | `IgApplication.cs` DI wiring missing for `CreationTool`'s `JsonToRecordCompiler` injection. | ATTR2-BATCH-03 Report | ATTR2-BATCH-04 | ✅ Resolved |
