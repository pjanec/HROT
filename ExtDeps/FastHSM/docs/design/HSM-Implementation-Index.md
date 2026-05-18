# FastHSM Implementation Package - Index

**Version:** 1.1.0  
**Date:** 2026-01-11  
**Architect Review:** 2026-01-11 ✅ APPROVED  
**Status:** ✅ FULLY APPROVED - Ready for Immediate Implementation

---

## 📚 Document Overview

This implementation package provides everything needed to build the FastHSM library from scratch. All design decisions have been finalized through a rigorous architecture review process (see `HSM-design-talk.md` for the complete discussion history).

### Document Structure

```
docs/design/
├── IMPLEMENTATION-PACKAGE-README.md     ← Start here (overview)
├── ARCHITECT-REVIEW-SUMMARY.md          ← ⭐ NEW: Quick reference for decisions
├── HSM-design-talk.md                   ← Architecture discussion (background)
├── HSM-Implementation-Design.md         ← Detailed implementation spec  
├── HSM-Implementation-Questions.md      ← Questions (ALL RESOLVED ✅)
└── HSM-Implementation-Index.md          ← This navigation document
```

**Reading Order:**
1. `IMPLEMENTATION-PACKAGE-README.md` (5 min) - Overview
2. `ARCHITECT-REVIEW-SUMMARY.md` (10 min) - Critical changes & decisions
3. `HSM-Implementation-Design.md` (2-3 hours) - Full specifications
4. `HSM-Implementation-Questions.md` (reference) - Resolved questions

---

## 🎯 Quick Start for Developers

### If You're New to This Project

1. **Read:** `HSM-design-talk.md` (end-to-end to understand all decisions)
   - Time: ~2-3 hours
   - Skip to "Final Architecture Confirmation" section if pressed for time

2. **Study:** `HSM-Implementation-Design.md` (this is your blueprint)
   - Start with Section 1 (Data Layer) to understand memory layouts
   - Section 3 (Kernel) is the most complex—budget extra time

3. **Reference:** `docs/btree-design-inspiration/` for proven patterns
   - Especially `01-Data-Structures.md` for similar fixed-size techniques

### If You're Ready to Code

**Start Here:** Section 1.2 of `HSM-Implementation-Design.md`

Implement in this order:
1. Core enumerations (StateFlags, TransitionFlags, etc.)
2. StateDef and TransitionDef structs
3. HsmDefinitionBlob with span accessors
4. HsmInstance64/128/256 structs
5. Unit tests for struct sizes and alignment

---

## 📖 Implementation Design Document Structure

### Section 1: Data Layer (ROM and RAM)

**What's Defined:**
- ✅ All core enumerations (StateFlags, TransitionFlags, EventPriority, etc.)
- ✅ ROM structures: StateDef (32B), TransitionDef (16B), RegionDef (8B)
- ✅ RAM structures: HsmInstance64/128/256 (exactly 64/128/256 bytes)
- ✅ Event structure: HsmEvent (exactly 24 bytes)
- ✅ Command buffer structures: PagedCommandWriter, CommandPage

**Key Files to Create:**
```
src/FastHSM/Data/
├── Enums.cs              // All enumerations
├── StateDef.cs           // ROM state definition
├── TransitionDef.cs      // ROM transition definition  
├── RegionDef.cs          // ROM region definition
├── HsmDefinitionBlob.cs  // Complete ROM blob
├── InstanceHeader.cs     // Common header (16B)
├── HsmInstance64.cs      // Tier 1 (64B)
├── HsmInstance128.cs     // Tier 2 (128B)
├── HsmInstance256.cs     // Tier 3 (256B)
├── HsmEvent.cs           // Fixed 24B event
├── CommandBuffer.cs      // Paged command writer
└── HsmRng.cs             // RNG wrapper
```

**Critical Invariants:**
- StateDef MUST be exactly 32 bytes
- TransitionDef MUST be exactly 16 bytes
- HsmEvent MUST be exactly 24 bytes
- Instance structs MUST be exactly their declared size (use unit tests to verify)

---

### Section 2: Compiler (Asset Pipeline)

**What's Defined:**
- ✅ JSON authoring format
- ✅ BuilderGraph (mutable intermediate representation)
- ✅ Normalizer (stable ID assignment, depth computation)
- ✅ Validator (depth limits, budget checks, slot conflicts)
- ✅ Flattener (graph → flat arrays)
- ✅ LinkerTableBuilder (function hash → index mapping)
- ✅ HashComputer (structure + parameter hashes)
- ✅ BlobEmitter (final binary blob writer)

**Key Files to Create:**
```
src/FastHSM/Compiler/
├── BuilderGraph.cs       // Mutable IR (BuilderState, BuilderTransition, etc.)
├── Normalizer.cs         // Assign IDs, compute depths, resolve refs
├── Validator.cs          // Check all constraints
├── Flattener.cs          // Graph → flat arrays
├── LinkerTableBuilder.cs // Function hash table
├── HashComputer.cs       // Structure/parameter hashing
├── BlobEmitter.cs        // Write final blob
└── JsonParser.cs         // JSON → BuilderGraph
```

**Validation Rules to Implement:**
- Max depth ≤ 16
- Tier budget enforcement (64/128/256B limits)
- State reachability (no orphans)
- Transition validity (valid source/target)
- Slot conflict detection (timers, history)

---

### Section 3: Kernel (Runtime Execution)

**What's Defined:**
- ✅ UpdateBatch entry point (generic shim + non-generic core)
- ✅ 4-phase tick: Setup → Timers → RTC → Update
- ✅ Transition resolution algorithm (interrupts, child-first bubble, arbitration)
- ✅ LCA computation (least common ancestor for exit/entry paths)
- ✅ Atomic transition execution (exit → effect → entry)
- ✅ Event queue operations (enqueue, pop, merge deferred)
- ✅ Dispatch table (function pointer invocation)

**Key Files to Create:**
```
src/FastHSM/Runtime/
├── HsmKernel.cs          // Main entry point (UpdateBatch)
├── HsmKernel.Timers.cs   // Phase 1: Timer processing
├── HsmKernel.RTC.cs      // Phase 2: RTC loop
├── HsmKernel.Update.cs   // Phase 3: Activities
├── HsmKernel.Transition.cs // LCA, exit/entry paths
├── HsmKernel.Events.cs   // Queue operations
├── HsmDispatchTable.cs   // Function pointer tables
└── HsmKernel.Helpers.cs  // Utility functions
```

**Critical Algorithms:**
1. **ComputeLCA:** O(depth) = O(16) max
2. **ResolveTransition:** Check globals → bubble up per region → arbitrate
3. **ExecuteTransition:** Collect exit path → execute exits → effect → execute entries
4. **ProcessTimers:** Scan deadlines → enqueue timer events

**Invariants to Maintain:**
- Transitions are atomic (never partial)
- Exit/entry order is deterministic (deepest→shallowest, then shallowest→deepest)
- Budget caps are enforced (max microsteps, max events)
- RNG advances only when explicitly called

---

### Section 4: Tooling (Hot Reload & Debugging)

**What's Defined:**
- ✅ HotReloadManager (structure hash vs parameter hash)
- ✅ TraceBuffer (64KB ring buffer, binary records)
- ✅ TraceRecord (16-byte fixed format)
- ✅ TraceSymbolicator (binary → human-readable)
- ✅ HsmBootstrapper (global registry)

**Key Files to Create:**
```
src/FastHSM/Tools/
├── HotReloadManager.cs   // Hot reload logic
├── TraceBuffer.cs        // Per-thread trace ring
├── TraceRecord.cs        // Binary trace format
├── TraceSymbolicator.cs  // Convert to text
└── HsmBootstrapper.cs    // Registry + linker
```

**Hot Reload Rules:**
- Structure hash unchanged → soft reload (keep state)
- Structure hash changed → hard reset (clear state, preserve blackboard)
- Generation counter increments on reset (invalidates timers)

---

## 🔧 Implementation Roadmap

### Phase 1: Data Layer (Week 1)
**Goal:** All structs defined, unit tested, documented

**Tasks:**
- [ ] Define all enumerations
- [ ] Implement StateDef (32B)
- [ ] Implement TransitionDef (16B)
- [ ] Implement RegionDef (8B)
- [ ] Implement HsmDefinitionBlob with span accessors
- [ ] Implement InstanceHeader (16B)
- [ ] Implement HsmInstance64 (64B)
- [ ] Implement HsmInstance128 (128B)
- [ ] Implement HsmInstance256 (256B)
- [ ] Implement HsmEvent (24B)
- [ ] Implement CommandPage, HsmCommandWriter
- [ ] Implement HsmRng

**Unit Tests:**
```csharp
[Fact]
public void StateDef_Is_Exactly_32_Bytes()
{
    Assert.Equal(32, Marshal.SizeOf<StateDef>());
}

[Fact]
public void HsmInstance64_Is_Exactly_64_Bytes()
{
    Assert.Equal(64, Marshal.SizeOf<HsmInstance64>());
}

// ... (similar for all fixed-size structs)
```

**Deliverable:** All data structures compile, pass size tests

---

### Phase 2: Compiler (Weeks 2-3)
**Goal:** JSON → HsmDefinitionBlob pipeline working

**Week 2 Tasks:**
- [ ] Implement BuilderGraph classes (BuilderState, BuilderTransition, etc.)
- [ ] Implement JsonParser (JSON → BuilderGraph)
- [ ] Implement Normalizer (assign stable IDs, compute depths)
- [ ] Write tests for normalization

**Week 3 Tasks:**
- [ ] Implement Validator (all rules from spec)
- [ ] Implement Flattener (graph → flat arrays)
- [ ] Implement LinkerTableBuilder
- [ ] Implement HashComputer
- [ ] Implement BlobEmitter
- [ ] Write integration tests (sample machines)

**Integration Test:**
```csharp
[Fact]
public void Compile_SimpleMachine_Produces_ValidBlob()
{
    string json = LoadTestJson("simple-combat.json");
    var compiler = new HsmCompiler();
    var blob = compiler.Compile(json);
    
    Assert.Equal(0x4653484D, blob.Header.Magic); // 'FHSM'
    Assert.True(blob.Header.StateCount > 0);
    Assert.True(blob.Header.TransitionCount > 0);
    
    // Can read states without throwing
    var states = blob.States;
    Assert.NotEmpty(states);
}
```

**Deliverable:** Can compile test machines to valid blobs

---

### Phase 3: Kernel (Weeks 4-5)
**Goal:** UpdateBatch executes simple machines correctly

**Week 4 Tasks:**
- [ ] Implement UpdateBatch entry point
- [ ] Implement Phase 0 (setup, validation)
- [ ] Implement Phase 1 (timer processing)
- [ ] Implement event queue operations (enqueue, pop, merge)
- [ ] Write tests for event queue

**Week 5 Tasks:**
- [ ] Implement Phase 2 (RTC loop)
- [ ] Implement transition resolution (global interrupts, bubble-up, arbitration)
- [ ] Implement ComputeLCA
- [ ] Implement ExecuteTransition (exit/entry paths)
- [ ] Implement Phase 3 (update/activities)
- [ ] Implement dispatch table invocation
- [ ] Write comprehensive kernel tests

**Kernel Test:**
```csharp
[Fact]
public void Kernel_ExecutesSimpleSequence_Correctly()
{
    var blob = CompileTestMachine("sequence-test");
    var instance = new HsmInstance128();
    var context = new TestContext();
    var commands = new HsmCommandWriter();
    
    // Tick 1: Should enter initial state
    HsmKernel.UpdateBatch(blob, 
        new Span<HsmInstance128>(ref instance),
        context, ref commands, 0.016f, 0.0);
    
    // Assert active state
    Assert.Equal(ExpectedStateId, instance.ActiveLeafIds[0]);
}
```

**Deliverable:** Can execute test machines, transitions work

---

### Phase 4: Tooling (Week 6)
**Goal:** Hot reload and tracing functional

**Tasks:**
- [ ] Implement HotReloadManager
- [ ] Implement TraceBuffer
- [ ] Implement TraceRecord
- [ ] Implement TraceSymbolicator
- [ ] Implement HsmBootstrapper
- [ ] Write example user actions (Attack, Patrol, etc.)
- [ ] Write golden-run tests

**Golden-Run Test:**
```csharp
[Fact]
public void GoldenRun_Combat_Deterministic()
{
    var recording = LoadRecording("combat-golden.bin");
    var replay = ReplayRecording(recording);
    
    Assert.Equal(recording.FinalHash, replay.FinalHash);
    Assert.Equal(recording.CommandStream, replay.CommandStream);
}
```

**Deliverable:** Hot reload works, can trace and replay

---

### Phase 5: Polish (Week 7)
**Goal:** Production-ready quality

**Tasks:**
- [ ] Performance profiling (target: <0.1ms per tick)
- [ ] Memory profiling (verify zero-GC)
- [ ] Write API documentation
- [ ] Write example machines (Soldier, Orc, Vehicle)
- [ ] Write user guide

**Deliverable:** Complete v1.0 ready for use

---

## 🧪 Testing Strategy

### Unit Tests (Per-Phase)
- **Data Layer:** Struct sizes, alignment, field offsets
- **Compiler:** Normalization, validation rules, hash stability
- **Kernel:** Event queues, LCA computation, transition execution
- **Tooling:** Hot reload logic, trace decoding

### Integration Tests
- **End-to-End:** JSON → Blob → Execute → Commands
- **Multi-Tick:** State persistence across ticks
- **Regions:** Orthogonal region behavior

### Regression Tests
- **Golden Runs:** Deterministic replay verification
- **Budget Limits:** Clamp behavior under stress
- **Hot Reload:** Structure changes vs parameter changes

---

## 📋 Architect Questions & Resolutions

See `HSM-Implementation-Questions.md` and `ARCHITECT-REVIEW-SUMMARY.md` for complete details.

**Status:** ✅ ALL 10 QUESTIONS RESOLVED

| # | Question | Resolution |
|---|----------|------------|
| Q1 | Event queue layout | ✅ Tier-specific hybrid |
| Q2 | Command page size | ✅ 4KB fixed |
| Q3 | History slots | ✅ Compiler pool + stable sort |
| Q4 | RNG in guards | ✅ Allow with declaration |
| Q5 | Sync transitions | ✅ Simple v1.0 |
| Q6 | Transition cost | ✅ Structural only |
| Q7 | Global transitions | ✅ Separate table |
| Q8 | Trace filtering | ✅ All modes |
| Q9 | Action signatures | ✅ Thin shim + AggressiveInlining |
| Q10 | Local storage | ✅ Scratch registers |

**Critical Changes:**
- ⚠️ Tier 1 uses single queue (math constraint)
- ⚠️ History slots MUST be sorted by StableID
- ⚠️ AggressiveInlining attribute MANDATORY for shims
- ⚠️ ID-only events require validation

---

## 🎓 Key Design Patterns from BTree Inspiration

### Pattern 1: Flat "Bytecode" Arrays
- **BTree:** `NodeDefinition[]` with `SubtreeOffset` for skipping
- **HSM:** `StateDef[]` with parent/child indices for hierarchy

### Pattern 2: Fixed-Size Blittable State
- **BTree:** `BehaviorTreeState` (64 bytes)
- **HSM:** `HsmInstance64/128/256` (tiered sizes)

### Pattern 3: Resumable Execution
- **BTree:** `RunningNodeIndex` to skip finished nodes
- **HSM:** Active leaf IDs + RTC cursor for mid-tick pause

### Pattern 4: Delegate Caching
- **BTree:** Pre-bind delegates via registry
- **HSM:** Function hash → pointer table (linker)

### Pattern 5: Hot Reload via Hashing
- **BTree:** Structure hash vs param hash
- **HSM:** Same pattern, structure vs parameter hashes

---

## 📚 Reference Documents

### Design Documents (This Project)
- `HSM-design-talk.md` - Complete architecture discussion
- `HSM-Implementation-Design.md` - Detailed implementation spec
- `HSM-Implementation-Questions.md` - Open questions

### Inspiration (BTree Library)
- `docs/btree-design-inspiration/00-Architecture-Overview.md`
- `docs/btree-design-inspiration/01-Data-Structures.md`
- `docs/btree-design-inspiration/02-Execution-Model.md`

### External References
- UML State Machine specification (for semantics)
- SCXML specification (for reference)
- Unity DOTS (for ECS patterns)

---

## 🚀 Getting Started Checklist

### Environment Setup
- [ ] .NET 8/9 SDK installed
- [ ] IDE configured (Visual Studio, Rider, or VSCode)
- [ ] Git repository initialized
- [ ] xUnit test framework added

### First Steps
- [ ] Create solution structure (`FastHSM.sln`)
- [ ] Create projects:
  - `FastHSM.Kernel` (core library)
  - `FastHSM.Compiler` (asset pipeline)
  - `FastHSM.Tools` (hot reload, tracing)
  - `FastHSM.Tests` (unit tests)
- [ ] Create folder structure (Data/, Runtime/, Compiler/, Tools/)
- [ ] Copy enumerations from Section 1.1 of implementation design
- [ ] Write first unit test (StateDef size)
- [ ] Implement StateDef
- [ ] Run test → ✅ pass

### Success Criteria for Day 1
- [ ] Project structure exists
- [ ] Can compile empty library
- [ ] First struct defined (StateDef)
- [ ] First unit test passing

---

## 💡 Implementation Tips

### Memory Layout
- Use `[StructLayout(LayoutKind.Explicit)]` for precise control
- Use `[FieldOffset(N)]` to specify exact positions
- Always verify sizes with `Marshal.SizeOf<T>()`
- Test on both 32-bit and 64-bit (though targeting 64-bit is fine)

### Performance
- Profile early, profile often
- Use `Span<T>` to avoid allocations
- Use `stackalloc` for small temporary buffers (≤ 1KB)
- Avoid `List<T>.Add()` in hot path—preallocate

### Debugging
- Use `[Conditional("DEBUG")]` for debug-only code
- Implement `ToString()` for all structs (helps debugging)
- Add validation asserts in debug builds
- Strip them in release

### Testing
- Write tests BEFORE implementation (TDD)
- Test edge cases (empty machines, max depth, budget limits)
- Use theory/data-driven tests for bulk cases
- Keep tests fast (< 1ms each)

---

## 📞 Support & Questions

### When Stuck
1. Re-read the relevant section in `HSM-Implementation-Design.md`
2. Check the BTree inspiration documents for similar patterns
3. Review the full architecture discussion in `HSM-design-talk.md`
4. Ask the architect (create an issue or discussion)

### Common Pitfalls
- **Struct alignment issues:** Always verify with `sizeof()` tests
- **Span<T> lifetime:** Don't store spans, only use locally
- **Fixed buffer syntax:** Requires `unsafe` context
- **Function pointer syntax:** Requires `/unsafe` compiler flag

---

## 🎯 Success Metrics

### Phase 1 Complete When:
- [ ] All structs compile
- [ ] All size tests pass (32B, 16B, 64B, etc.)
- [ ] Can create and access spans from blob

### Phase 2 Complete When:
- [ ] Can compile sample JSON to blob
- [ ] Blob passes all validation rules
- [ ] Structure hash is deterministic (same input → same hash)

### Phase 3 Complete When:
- [ ] Can execute simple state machine
- [ ] Transitions work correctly
- [ ] Events trigger state changes
- [ ] Commands are emitted properly

### Phase 4 Complete When:
- [ ] Hot reload works (both soft and hard)
- [ ] Can trace execution
- [ ] Can replay recordings
- [ ] Deterministic behavior verified

---

## 📈 Progress Tracking

Use this section to track implementation progress:

### Week 1: Data Layer
- [ ] Day 1: Enums + StateDef
- [ ] Day 2: TransitionDef + RegionDef
- [ ] Day 3: HsmDefinitionBlob
- [ ] Day 4: Instance structs (64/128/256)
- [ ] Day 5: Event, Command, RNG

### Week 2: Compiler Part 1
- [ ] Day 1: BuilderGraph classes
- [ ] Day 2: JsonParser
- [ ] Day 3: Normalizer
- [ ] Day 4: Validator (rules 1-5)
- [ ] Day 5: Validator (rules 6-10)

### Week 3: Compiler Part 2
- [ ] Day 1: Flattener
- [ ] Day 2: LinkerTableBuilder
- [ ] Day 3: HashComputer + BlobEmitter
- [ ] Day 4: Integration tests
- [ ] Day 5: Sample machines

### Weeks 4-5: Kernel
(Track as you go)

### Week 6: Tooling
(Track as you go)

---

**Document Version:** 1.0.0  
**Last Updated:** 2026-01-11  
**Status:** ✅ Ready for Development  
**Next Action:** Begin Phase 1 (Data Layer)

**Good luck! 🚀**
