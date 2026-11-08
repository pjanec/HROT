# STATUS — backend: recording storage + autosave ([handoff](HANDOFF_Coordinator_To_Backend_Recording_Storage.md))

| # | id | state | commit | note |
|---|---|---|---|---|
| W-1 | CE-4332 | DONE | `680c3b30c` | the Player's node-id offset arm |
| W-2 | CE-4099 | DONE (before this handoff) | `aecbad03e` `afaaaecba` | built on the amended capture in the previous batch (Y-5): AQ108 §4c.2–§4c.3; A8 not needed; hot-reload race discards the round's extraction (revision check) |
| W-3 | CE-4330 | BUILDING — ① DONE | ① this commit | recording storage — ① manifest-last + `.part` + the scan counts only complete (design §10); ②…⑩ next |
| W-4 | CE-4331 | QUEUED | — | an AAR on a different cluster |

---
_backend lane, 2026-11-08_
