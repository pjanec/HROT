# Changelog

## v1.0.0 (2026-01-12)

**Initial Release**

### Features
- ✅ Data-oriented HSM with zero-allocation runtime
- ✅ 3 instance tiers (64B, 128B, 256B)
- ✅ Compiler pipeline (Builder → Normalizer → Validator → Flattener → Emitter)
- ✅ Source generation for action/guard dispatch
- ✅ Hot reload support (soft/hard reset)
- ✅ Deterministic RNG for replay
- ✅ Timer cancellation on state exit
- ✅ Deferred event queue
- ✅ Deep history support
- ✅ Global transitions
- ✅ Command buffer integration
- ✅ Debug trace system
- ✅ JSON parser for state machines
- ✅ XxHash64 hashing for performance

### Performance
- **15 ns/instance** for idle updates (Tier 64)
- **Zero allocations** in hot path
- **Linear scaling** to 10,000+ instances

### Known Limitations
- Orthogonal region arbitration is basic (first region wins on conflict)
- Deep history not fully tested in production
- P2 tasks (trace symbolication, paged allocator, registry) deferred to v1.1

### Breaking Changes
- Action signature changed: `void* instance, void* context, HsmCommandWriter* writer`
- `HsmGraphValidator.Validate` returns `List<ValidationError>` (not `bool` + `out`)
