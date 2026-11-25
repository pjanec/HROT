<!--STATUS
state: LIVE — queued, NOT started (waits on terrain's CE-4692 BL-S1)
updated: 2026-11-25
current-answer: §1 Now
related-designs:
  - HANDOFF_Coordinator_To_Backend_Bridge_Laying.md — the handoff (CE-4693, BL-S3 + BL-S4), scope frozen at its dispatch
  - ../../designs/engineering/DESIGN_Bridge_Laying.md — THE design (rev 2, R-4299)
  - HANDOFF_Coordinator_To_Terrain_Bridge_Laying.md — terrain's half (CE-4692), the prerequisite
-->

# STATUS — backend: bridge laying, the engineer (CE-4693)

## 1. Now

⏸ `2026-11-25` — **queued, waiting on terrain's CE-4692 BL-S1** (a placed bridge obstacle that bakes). 🔒 User: *"When terrain's CE-4692 BL-S1
… is merged, take … HANDOFF_Coordinator_To_Backend_Bridge_Laying.md (CE-4693 …). Done = every listed scenario passes, including the end-to-end
bl-river-crossing judged by objectives."* Measured: `origin/terrain` @ `a4b886397` = "started CE-4692 bridge laying (BL-S1/S2)" — not landed.
Ahead of it in the backend queue: CE-11057 ④ / CE-4691 (va-pop-smoke — the vehicle drive-out stops and fires on sight; gates running).

## 2. The items (from the handoff)

| # | item | state |
|---|---|---|
| ① | real figures (assault + floating bridge), TKB with sources | ⏳ |
| ② | the engineer types (DIS slot, class rows, TKB rail) | ⏳ |
| ③ | `BridgeCarried` + descriptor + translator + appearance | ⏳ |
| ④ | the orders `LayBridge` / `RecoverBridge`, refusals with reasons | ⏳ |
| ⑤ | the behaviours | ⏳ |
| ⑥ | `BridgeWorkSystem` | ⏳ |

## 3. Scenarios (DONE = all pass)

| scenario | result |
|---|---|
| `bl-assault-crossing` | ⏳ |
| `bl-recover` | ⏳ |
| `bl-too-wide` | ⏳ |
| `bl-floating-bridge` | ⏳ |
| ⭐ `bl-river-crossing` (objectives) | ⏳ |
