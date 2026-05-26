# — blueprints-2

> P2/P3 deferred issues. P1 issues go directly into the next batch (never here).

| # | Priority | Source | Description | Target Batch |
|---|----------|--------|-------------|--------------|
| 1 | P2 | BATCH-16 review | `TryResolveFieldCSharpType` (Stage5) and `TryResolvePropertyType` (Stage2) are near-identical AppDomain-scan helpers with no caching. O(N×M) per compile call. Consolidate into shared utility with `ConcurrentDictionary` cache. | BATCH-17+ |
| 2 | P2 | BATCH-16 review | 99 pre-existing test failures from `BlueprintDispatchKind` JSON deserialization mismatch (numeric vs string enum in sample JSON files). Investigate enum converters and fix sample JSON. | Backlog |
