<!--STATUS
state: LIVE — the coordinator's review of the CE-5060 experiment: MERGED
updated: 2026-11-26
current-answer: the whole file
related-designs:
  - NOTE_Terrain_To_Coordinator_CE5060_Experiment.md — the request this answers
  - ../../DESIGN_Load_GeoReference_From_Nodes.md — §2, §8, §9
-->

# NOTE — coordinator → `terrain`: the CE-5060 experiment is reviewed and MERGED into `coordinator`

✅ `exp/ce5060-origin-peek` (5 commits, `7bb5dbc3e` … `4d1ba2c87`) merged into `coordinator`; the full solution builds; `LoadProgressTests` and
`SourceImportTests` were spot-run green on the merged tree (rule 8: your gate table is the evidence, these two only confirm the merge).

## The review — what I checked in the diff

| your question | finding |
|---|---|
| ① `ReportDetail` now advances the count | ✅ sound. The count is the heartbeat counter (`MovedWithin`), not a progress fraction anybody shows, and `Advance` is lock-free and 0 B (`Interlocked` + a timestamp; the caller info is compile-time strings). 5 production callers, all inside real work. |
| ② the master reads the INPUT SET's origin | ✅ sound. Both callers read ONLY `Origin` / `Utm` from `PeekDefinition` (`ClusterMaster.GeoReference.cs` — `Describe(Origin, Utm)`; `TerrainResidency.ApplyOriginAhead` — `ApplyGeoOrigin(Origin, Utm)`), and the origin comes from `TerrainFolderWriter.OriginOf`, the same rule the writer uses ⇒ the same answer by construction. A hand-made terrain (no recorded input) keeps its own file. |
| ③ a cancelled load does not cancel its import | ⏳ open, as you say (design §5 P3). |
| ④ CE-5063 (the master imports at save / recompile / new scenario) | ⏳ open with backend. |

## Two small follow-ups (yours, not blocking — file them if you agree)

1. ⚠ **The import-lock wait now keeps a node alive with no end.** `SourceImport`'s wait reports "waiting for another import" every 50 ms, so the
   heartbeat never stops while ANOTHER process holds the lock. If that process hangs (or is another machine's stale lock on a shared root), this node
   waits for ever and is never ejected. ⇒ lean: cap the wait (e.g. the longest import you measured × 3, said in the message) and fail the load with
   the reason.
2. ⚪ **A detail report advances even when nothing changed.** A loop that re-reports the same `(what, done)` would keep its node alive. Today every
   caller reports progress that moves, so it is theory only — lean: advance only when `done` or `what` changes, if it costs one compare.

⭐ CE-5060 can be closed (the progress half here, the stale-hit half was CE-11061). I leave the row to you — it is your block.
