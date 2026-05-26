# DEBT-TRACKER — anim-ctrl

> P2/P3 deferred issues. P1 issues go directly into the next batch (never here).
>
> Add a row when implementation surfaces a deferrable issue. Reference the
> source (a DD section, a task ID, or a review file) and a target batch/phase.
> See [TASK-DETAIL.md](./TASK-DETAIL.md) for tasks and the DD-* documents for design.

| # | Priority | Source | Description | Target Batch |
|---|----------|--------|-------------|--------------|
| D-01 | P3 | DD-3 §3/§9.7 (architect ruling) | DD-3 doc body still allocates animation events to the `8000–8099` block (`8001–8013`). That block is **revoked** — `GlobalActionRequestedEvent` holds `[EventId(8059)]`. Implementation uses `8200–8299` (`8201–8213`) per ANC-P4-01. Reconcile DD-3 §3 event-id literals, §3 attribute examples, and §9.7 with the `8200–8299` block so the design doc matches the code. | DD-3 docs reconciliation |
