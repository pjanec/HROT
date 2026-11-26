<!--STATUS
state: LIVE — ✅ DONE: every item built, every scenario passes (f42e520ba)
updated: 2026-11-25
current-answer: §1 Now
related-designs:
  - HANDOFF_Coordinator_To_Backend_Bridge_Laying.md — the handoff (CE-4693, BL-S3 + BL-S4), scope frozen at its dispatch
  - ../../designs/engineering/DESIGN_Bridge_Laying.md — THE design (rev 2, R-4299)
  - HANDOFF_Coordinator_To_Terrain_Bridge_Laying.md — terrain's half (CE-4692), the prerequisite
-->

# STATUS — backend: bridge laying, the engineer (CE-4693)

## 1. Now

✅ `2026-11-26` — **DONE.** Every handoff item built; **every listed scenario passes** at `f42e520ba` (`scripts/scenario-regression.sh`, the editor
tier): `bl-too-wide` 5/5 · `bl-recover` 7/7 · `bl-assault-crossing` 6/6 · `bl-floating-bridge` 7/7 · ⭐ `bl-river-crossing` 6/6 (by objectives, 2 fall
rows skipped — the bridge held) — and terrain's `bl-placed-bridge` 4/4, `bl-blow-the-bridge` 7/7 still pass after the cross-lane fixes.
Ids allocated: component **292** (`BridgeCarried`), descriptor **247** (`dtBridgeCarried`), TKB **118 / 119**, `OrderKind` **17 / 18**; tracker **CE-4694…CE-4697**.

| found by the scenarios | fixed / filed |
|---|---|
| a scenario's mission plan had no task order (a rule's `MissionCommand Jump` always failed) | ✅ fixed — `MissionPlanTranslator` |
| a bridge blown while being recovered was counted back on board | ✅ fixed — `BridgeLayingBehaviors.Recover` |
| the floating deck (0.6 m, a 2.4 m ramp) had no vehicle path | ✅ fixed — `DeckGeometry.MinRampLength` 4.8 m (cross-lane, terrain — CE-4696) |
| men walked BESIDE a ramp / deck and over the river bed (terrain's own `bl-placed-bridge` met its deadline that way) | ✅ fixed — `TerrainWorld.DeckEdgeMargin` + nothing wades into a river, `TerrainWater` the one water index (cross-lane — CE-4695) |
| the floating transporter could not turn away from the river and blocked the column | ✅ fixed — the layer backs off the approach |
| a hull cannot line up onto a narrow deck from an angle | 📋 CE-4697 filed — the scenarios cross from the axis |
| "under smoke": no smoke screen can be ordered from a scenario | 📋 CE-4694 filed (behaviors) |
| the checks grammar had no negative condition | ✅ `unless` added (`ScenarioChecks`) |

⛔ HISTORY (earlier entries of §1):

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
| `bl-assault-crossing` | ✅ 6/6 (`f42e520ba`) — was ⚠ 5/6 — bridge laid, both squads across; ONE row red: a man's path runs ON the deck's edge and he walks the river bed beside it — measured live ([CE-4695](../Blueprint_Issues_Tracker.md), terrain, [NOTE](NOTE_Backend_To_Terrain_Floating_Deck_Vehicles.md)) · ⛔ "under smoke" not buildable from a scenario ([CE-4694](../Blueprint_Issues_Tracker.md)) |
| `bl-recover` | ✅ 7/7 |
| `bl-too-wide` | ✅ 5/5 |
| `bl-floating-bridge` | ✅ 7/7 (`f42e520ba`) — was ⛔ laid and Open, the column never crosses — in the EDITOR a vehicle path never uses the floating deck, in the `--mode all` cluster it does ([CE-4696](../Blueprint_Issues_Tracker.md), [NOTE to terrain](NOTE_Backend_To_Terrain_Floating_Deck_Vehicles.md)) |
| ⭐ `bl-river-crossing` (objectives) | ✅ **PASS** 6/6 (again at `f42e520ba`) (+2 fall rows skipped: the bridge held) — every objective Achieved: laid, far bank seized, the sapper's attempt, the bridge held and RECOVERED, the span back on board. Its runs found and fixed two real defects: ① a mission plan loaded from a scenario had no task order (`MissionPlanTranslator`) · ② a bridge demolished while being recovered was counted back (`BridgeLayingBehaviors.Recover`); and the grammar gained `unless` — each rail red-proved |
