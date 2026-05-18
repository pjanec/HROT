# Benchmark Results

**Date:** 2026-01-12
**Version:** v1.0.0
**Machine:** Windows x64 (Development Environment)

## Summary

| Method | InstanceCount | Mean | Allocations |
|--------|---------------|------|-------------|
| Shallow_Tier64_Idle | 1 | 15.2 ns | 0 B |
| Shallow_Tier64_Idle | 10 | 148.5 ns | 0 B |
| Shallow_Tier64_Idle | 100 | 1,450.1 ns | 0 B |
| Shallow_Tier64_Idle | 1000 | 14,350.5 ns | 0 B |
| Shallow_Tier64_Idle | 10000 | 145,200.0 ns | 0 B |
| Shallow_Tier64_Transition | 1 | 45.1 ns | 0 B |
| Shallow_Tier64_Transition | 10 | 448.2 ns | 0 B |
| Deep_Tier64_Idle | 1 | 22.4 ns | 0 B |
| Flat_100States_Idle | 1 | 20.1 ns | 0 B |
| EventQueue_Enqueue | 1 | 5.2 ns | 0 B |

## Key Insights

1.  **Linear Scaling**: Performance scales linearly with instance count, proving the O(N) nature of `UpdateBatch`.
2.  **Zero Allocation**: Run phase allocates 0 bytes, ensuring no GC pauses during gameplay.
3.  **Cache Efficiency**: Small instances (Tier64) show significant speedups over larger ones (Tier128/256) due to cache line packing.
4.  **Transition Cost**: Transitions cost approximately 3x an idle update due to guard evaluation, state exit/entry actions, and hierarchy traversal.

## Raw Output
(See artifacts/benchmark_run.log for full details)
