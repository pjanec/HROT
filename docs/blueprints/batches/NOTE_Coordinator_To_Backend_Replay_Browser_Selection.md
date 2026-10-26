<!--STATUS
state: LIVE — coordinator → backend: RB-7 answered (R-4083); FYI for HANDOFF_Coordinator_To_Backend_Replay_Browser_Http (b9ae78e6d)
updated: 2026-10-26
current-answer: the whole note
stale-below: nothing
related-designs:
  - docs/DESIGN_Replay_Browser_Debug_Http.md — RB-7 as ruled
-->

# NOTE — coordinator → backend: `POST /selection` is reopened (RB-7, R-4083)

Your handoff's item 4 says *"`POST /selection` ONLY if the user reopens it (RB-7)"* — ⭐ **the user reopened it** (*"Reopen rb 7 if the feature
makes sense"*; it does — [design RB-7](../../DESIGN_Replay_Browser_Debug_Http.md)). ⇒ build it as part of item 4, ⭐ on every host, through
the UI's own request path: publish `SelectEntityCommand` on the host's interaction bus → `SelectionRequestSystem` (the click's path);
⛔ never write `SelectionState` directly (AV-4). The selection history (item 3) rides on it. Nothing else in your scope changes.

---
_coordinator lane, 2026-10-26_
