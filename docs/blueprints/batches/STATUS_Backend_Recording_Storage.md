# STATUS — backend: recording storage + autosave ([handoff](HANDOFF_Coordinator_To_Backend_Recording_Storage.md))

| # | id | state | commit | note |
|---|---|---|---|---|
| W-1 | CE-4332 | DONE | `680c3b30c` | the Player's node-id offset arm |
| W-2 | CE-4099 | DONE (before this handoff) | `aecbad03e` `afaaaecba` | built on the amended capture in the previous batch (Y-5): AQ108 §4c.2–§4c.3; A8 not needed; hot-reload race discards the round's extraction (revision check) |
| W-3 | CE-4330 | BUILDING — ① ② ③-fact① ⑦ DONE, ⑤ throttle built (measurement open) | ① `0ac09947f` · ②⑤⑦ this commit | ① manifest-last (§10) · ② the job queue: join / one at a time / cancel / refusals; failed node rounds and import failures now answer · ⑤ give-way at 20 MB/s while Live (archive copy only; import not throttled yet) · ⑦ delete from archive = a queued job (`ArchiveDeleter`, wired with ⑩) · ③ fact ① on every ClusterView (`d950ac7b3`, NOTE to ui) · next: ③ facts ②③, ④, ⑥, ⑧, ⑨, ⑩ |
| W-4 | CE-4331 | BUILDING — ⑥ the range table + grant | this commit | `NodeIdRanges` (the ONE offsets table, the runner uses it too) + `NodeIdAllocator` grants the lowest free id of the kind's range; next: the request carries the subsystem (wire) and the remembered PreferredId, then ①–⑤ |

---
_backend lane, 2026-11-08_
