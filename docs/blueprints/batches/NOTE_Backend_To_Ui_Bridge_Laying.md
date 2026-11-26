<!--STATUS
state: LIVE — a NOTE (protocol §5), FYI + one ask · updated: 2026-11-25 · current-answer: the whole file
related-designs:
  - ../../designs/engineering/DESIGN_Bridge_Laying.md — §12 the engineer (BL-S3/S4), §12f as built
  - ../../DESIGN_Orders_Params_Entity_Classes.md — §8 / §9 the orders and their gestures
  - HANDOFF_Coordinator_To_Backend_Bridge_Laying.md — item ④: "the two-point map pick is ui's gesture (a NOTE to ui)"
-->
# NOTE — `backend` → `ui`: bridge laying (CE-4693) — two new orders, a two-point pick, and what a renderer reads

| | |
|---|---|
| the orders | `LayBridge {NearBank: PointLink, FarBank: PointLink, Speed}` and `RecoverBridge {Bridge: EntityLink (optional — none ⇒ the nearest laid bridge of the type it carries), Speed}`; `OrderKind.LayBridge` 17, `RecoverBridge` 18; ⛔ no `Gesture` (a ground click is Move's). Fulfilled by `AssaultBridgeLayerLayBridge` / `FloatingBridgeEngineerLayBridge` (+ `…RecoverBridge`) — the routing rule, so the order menu offers them on the two engineer classes only |
| ⏭ **ui's — the ask** | ⭐ **a TWO-POINT map pick for `LayBridge`**: the menu today asks for one point per `PointLink` field, one form row at a time — a gesture that picks the near bank, then the far bank (a line drawn between them, ideally with the water gap read off as you drag: `BridgeLaying.Plan(terrain, near, far, …)` gives `Gap`, `Refusal`, `Centre`, `Length` — the same function the engineer uses, so a preview cannot disagree with the order) would make it one action. HTTP and the LLM commander's seat already give two points |
| a refused order | the engineer's own replicated `BridgeCarried.Refusal` (`BridgeRefusal`: `GapTooWide`, `NearBankInWater`, `FarBankInWater`, `NoWater`, `DeckEndInWater`, `NoSpanCarried`, `NotItsBridge`; `NotRefused` = 0) and `GapM` (the measured gap). The words: `BridgeLaying.Say(refusal, gap, spanMax)` — e.g. *"the gap is 30.0 m, wider than the 24.0 m this bridge spans"*; the play also logs it. A toast / the unit card can show it |
| what a renderer reads | `BridgeLayerLook.Of(world, e)` (`Hrot.Look`): `Carried` · `Laying` · `Recovering` · `NoSpan` — from the replicated `BridgeCarried` (`dtBridgeCarried` 247, the Brain group). A LAID span is the bridge obstacle itself (TKB 8825 / 8826, a walkable deck the terrain draws — terrain's CE-4692). No progress on the wire in v1 (BL-21) |
| the types | `Engineer_AssaultBridgeLayer` 118 (class `AssaultBridgeLayer`, DIS 1.1.*.9.3, tracked — an M104 Wolverine) and `Engineer_FloatingBridge` 119 (class `FloatingBridgeEngineer`, 1.1.*.7.1, wheeled — an Improved Ribbon Bridge transporter). ⏭ ui's: their 3-D family (`VisualFamily`) — today they draw as a large tracked / wheeled utility vehicle |
