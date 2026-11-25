<!--STATUS
state: LIVE — BUILDING: items ①–⑥ built and committed; the scenarios running
updated: 2026-11-25
current-answer: §1 Now
related-designs:
  - HANDOFF_Coordinator_To_Backend_Bridge_Laying.md — the handoff (CE-4693, BL-S3 + BL-S4), scope frozen at its dispatch
  - ../../designs/engineering/DESIGN_Bridge_Laying.md — THE design (rev 2, R-4299)
  - HANDOFF_Coordinator_To_Terrain_Bridge_Laying.md — terrain's half (CE-4692), the prerequisite
-->

# STATUS — backend: bridge laying, the engineer (CE-4693)

## 1. Now

🔨 `2026-11-25` — **items ①–⑥ BUILT** (`caea57450`): the figures and types, `BridgeCarried` on the wire, the orders, the plays, `BridgeWorkSystem`; rails
`BridgeLayingBehaviorTests` 6/6 (red-proved), `BridgeLayerTkbTests`, the wire round trip. ✅ `bl-too-wide` 5/5 · ✅ `bl-recover` 7/7 · ⚠ `bl-assault-crossing` 5/6 (CE-4695) · ⏳ `bl-floating-bridge`, `bl-river-crossing`.
Ids allocated: component **292** (`BridgeCarried`), descriptor **247** (`dtBridgeCarried`), TKB **118 / 119**, `OrderKind` **17 / 18**.

⛔ HISTORY — ⏸ `2026-11-25` — **queued, waiting on terrain's CE-4692 BL-S1** (a placed bridge obstacle that bakes). 🔒 User: *"When terrain's CE-4692 BL-S1
… is merged, take … HANDOFF_Coordinator_To_Backend_Bridge_Laying.md (CE-4693 …). Done = every listed scenario passes, including the end-to-end
bl-river-crossing judged by objectives."* Measured: `origin/terrain` @ `a4b886397` = "started CE-4692 bridge laying (BL-S1/S2)" — not landed.
Ahead of it in the backend queue: CE-11057 ④ / CE-4691 (va-pop-smoke — the vehicle drive-out stops and fires on sight; gates running).

## 2. The items (from the handoff)

| # | item | state |
|---|---|---|
| ① | real figures (assault + floating bridge), TKB with sources | ✅ design §12d, `BridgeLayerTkbTests` |
| ② | the engineer types (DIS slot, class rows, TKB rail) | ✅ 118 / 119, 1.1.*.9.3 / 1.1.*.7.1 |
| ③ | `BridgeCarried` + descriptor + translator + appearance | ✅ 292 / 247, `BridgeLayerLook` |
| ④ | the orders `LayBridge` / `RecoverBridge`, refusals with reasons | ✅ `BridgeCarried.Refusal`; the two-point pick → [NOTE to ui](NOTE_Backend_To_Ui_Bridge_Laying.md) |
| ⑤ | the behaviours | ✅ `BridgeLayingBehaviors` |
| ⑥ | `BridgeWorkSystem` | ✅ |

## 3. Scenarios (DONE = all pass)

| scenario | result |
|---|---|
| `bl-assault-crossing` | ⚠ 5/6 — bridge laid, both squads across; ONE row red: a man's height reads the river bed at the deck's very edge ([CE-4695](../Blueprint_Issues_Tracker.md)) · ⛔ "under smoke" not buildable from a scenario ([CE-4694](../Blueprint_Issues_Tracker.md)) |
| `bl-recover` | ✅ 7/7 |
| `bl-too-wide` | ✅ 5/5 |
| `bl-floating-bridge` | ⏳ |
| ⭐ `bl-river-crossing` (objectives) | ⏳ |
