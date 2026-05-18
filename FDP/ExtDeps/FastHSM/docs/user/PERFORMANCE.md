# Performance Guide

FastHSM is designed for speed, but performance depends on how you use it. This guide covers best practices.

---

## 1. Batch Processing

The single biggest optimization is **Batch Processing**. Calling `Update` on one instance is fast; calling it on 10,000 sequentially in a loop is extremely fast due to CPU cache locality.

### Standard Loop vs Batch API

**Good:**
```csharp
foreach(var machine in machines)
{
    HsmKernel.Update(blob, ref machine, ctx, dt);
}
```

**Better (using Batch API):**
```csharp
// Processes the array in unrolled, optimized internal loops
HsmKernel.UpdateBatch(blob, myInstanceArray, ctx, dt);
```

Ensure your `HsmInstance` structs are stored in a contiguous array (`HsmInstance64[]` or `NativeArray<HsmInstance64>`).

---

## 2. Allocation Optimization

### Context
Pass your Context as a struct reference or a pointer. Avoid passing large classes which cause pointer chasing.

### Actions
- **Don't use lambdas** or captures in user code if possible (FastHSM enforces static actions anyway).
- **Avoid boxing**. The `void* context` pattern is unsafe but allocation-free. Be careful casting.

### Event Payloads
The `HsmEvent` struct has a `ushort Payload`. Use this for checking small data (like "Damage Amount") to avoid allocating a separate event object or looking up external maps.

---

## 3. Graph Design

### Hierarchy Depth
Deep hierarchies require walking up and down the tree for transitions.
- **Depth 1-3:** Ideal.
- **Depth 4-8:** Acceptable, minimal impact.
- **Depth >16:** Not supported by default implementations (stack limit).

### Minimizing Transitions
Transitioning is more expensive than staying in a state.
- **Bad:** Transitioning `Idle` <-> `Walk` every frame due to jittery input.
- **Fix:** Use hysteresis / deadzones in your Guards.

---

## 4. Selecting the Right Tier

Don't use `HsmInstance256` if `HsmInstance64` suffices.
- 64 bytes fits exactly in one x64 Cache Line.
- 128/256 bytes spill into multiple lines, increasing memory bandwidth usage.

Most AI behaviors (Patrol, Idle, Chase, Attack) fit comfortably in the 64-byte layout.

---

## 5. Trace Logging

Tracing (`HsmTraceBuffer`) is incredibly useful for debugging but has a cost.
- **Development:** Enable all traces inside your debug builds.
- **Production:** Compile out tracing calls if possible, or ensure the `HsmKernelCore.SetTraceBuffer(null)` is called. The kernel explicitly checks for `null` to skip overhead.
- **Filtering:** Use `InstanceFlags.DebugTrace` to enable high-detail tracing only on the specific entity you are debugging, rather than globally.

---

## 6. Profiling

When profiling FastHSM usage:

1.  **Look at `HsmActionDispatcher`:** If this is hot, your user logic (Actions/Guards) is the bottleneck, not the HSM overhead.
2.  **Look at `ProcessEventPhase`:** If this is hot, you might be spamming events. Check if you are sending events every frame that get rejected.

---

## 7. Init Time

`HsmBuilder.Build()` and implicit compiler steps can be slow (milliseconds).
**Always cache your `HsmDefinitionBlob`.**
- Build it once on startup.
- Store it in a static field.
- Do NOT build the graph every time you spawn an entity.

---

## 8. Benchmark Results

FastHSM achieves the following performance on typical hardware:

| Configuration | Instances | Time (ns/instance) | Throughput |
|--------------|-----------|-------------------|-----------|
| Tier 64, Idle | 10,000 | 15 ns | 66M updates/sec |
| Tier 64, Transition | 10,000 | 45 ns | 22M updates/sec |
| Tier 128, Idle | 10,000 | 17 ns | 58M updates/sec |

**Key Takeaways:**
- **15 nanoseconds per instance** for idle updates (Tier 64)
- **Zero allocations** - No GC pressure
- **Linear scaling** - 10,000 instances takes 10x time of 1,000
- **Cache-friendly** - Tier 64 is fastest (fits in single cache line)

See `benchmarks/BENCHMARK-RESULTS.md` for full results.
