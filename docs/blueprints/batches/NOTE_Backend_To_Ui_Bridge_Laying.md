<!--STATUS
state: LIVE — a NOTE (protocol §5): FYI + the ui work bridge laying still needs on the 2-D and 3-D maps · updated: 2026-11-26 (rev 2 — the whole visual picture; rev 1 2026-11-25 held the orders only) · current-answer: the whole file
related-designs:
  - ../../designs/engineering/DESIGN_Bridge_Laying.md — ⭐ THE design: §2 the diagrams, §11 BL-S1/S2 as built (terrain: the walkable deck, its destruction), §12 BL-S3/S4 the engineer (backend), §12f as built
  - ../../designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md — what a renderer is told about a vehicle (BridgeLayerLook belongs to that family)
  - ../../DESIGN_Orders_Params_Entity_Classes.md — §8 / §9 the order menu, the form, the gestures
  - ../../UX/DESIGN_Colour_Icons.md — icons in the colour of their meaning (R-4139)
  - ../../DESIGN_Add_Entity_Picker.md — §2j the thumbnail atlas (scripts/thumbnails.py)
-->
# NOTE — `backend` → `ui`: bridge laying is BUILT (CE-4692 terrain + CE-4693 backend) — how it works, and what the 2-D and 3-D maps still need

🔒 User, `2026-11-26`: *"write a note to the ui lane to add whatever is not yet done for 2d map and 3d map visualization … in relation to bridge laying and
bridge removal and bridge destruction — vehicle 3d look, bridge 3d look, thumbnail for entity picker, destroyed bridge etc. describe how it all works"*.

⭐ Every bridge scenario passes (`bl-too-wide`, `bl-recover`, `bl-assault-crossing`, `bl-floating-bridge`, `bl-river-crossing` — backend; `bl-placed-bridge`,
`bl-blow-the-bridge` — terrain). The simulation side is done; **the look is not.** ⚠ Before building any of §3, R-4143 applies: a mockup first.

## 1. How it works — the simulation, in one table

| step | what happens | who / where |
|---|---|---|
| the vehicles | **Assault Bridge Layer** (TKB **118**, class `AssaultBridgeLayer`, DIS `1.1.*.9.3`, tracked — an M104 Wolverine: carries one 26 m span, bridges ≤ 24 m of water, lays in 5 min, recovers in 10) and **Floating Bridge Engineer** (TKB **119**, class `FloatingBridgeEngineer`, DIS `1.1.*.7.1`, wheeled, 11 m — an Improved Ribbon Bridge transporter: ≤ 100 m, 20 min / 20 min, ESTIMATES). Both in the Add Entity picker (they are `TkbMasterDto` vehicles) | design §12d |
| the bridges | two TKB types, **8825** (assault span: 4 m wide, 1.2 m up) and **8826** (floating span: 4.5 m wide, 0.6 m up) — a `StaticObstacle` of kind **`WalkableDeck`**: an entity that every node BAKES into its terrain (a deck slab + a ramp at each end, ≥ 4.8 m long), its `ObstacleShape` per instance (a laid span is as long as its river + 1 m each bank). DIS kind 5 (cultural feature) ⇒ **the TERRAIN draws the deck**, not an entity kit (BL-13) | design §11, §12f |
| order: LayBridge | `LayBridge {NearBank, FarBank}` (OrderKind 17) on the vehicle; the menu offers it only on these two classes; two required points ⇒ the ORDER FORM opens (two map picks). **Refused** (nothing laid) when the gap is wider than the span, a bank / deck end is in the water, or there is no water — the reason on the vehicle's replicated `BridgeCarried.Refusal` (+ `GapM`) and in the log | `LayBridgeOrder`, `BridgeLaying.Plan` / `Say` |
| laying | the vehicle drives (through an approach point) to its STAND on the deck's line short of the near ramp, sets `BridgeCarried.Order = Lay`; the mover holds it the build seconds, then asks for the bridge entity; it is **Constructing** (not drawn in the sim, not walkable) until every node has baked it, then **Open** (Active). The vehicle counts its span off (`Count` 1 → 0) and **backs off the approach** (reverses 20 m back, 10 m aside) | `BridgeLayingBehaviors`, `BridgeWorkSystem` |
| crossing | men and vehicles path over the deck (the navmesh, the floor, sight and fire all hold it); a road ADD makes it road for our road-following vehicles while it stands; nothing wades into a river | BL-5 … BL-7, CE-4695 |
| order: RecoverBridge | `RecoverBridge {Bridge?}` (OrderKind 18; no bridge named ⇒ the nearest of the type it carries) — goes at once from the menu. The vehicle drives to the nearer deck end, `Order = Recover`; after the recover seconds the bridge entity is ended through the network destroy (**never** Health 0); the deck leaves every node's terrain at the next bake; `Count` 0 → 1 | `BridgeLayingBehaviors.Recover` |
| destruction | a charge (or any blast measured to the DECK) takes the deck's Health to 0 ⇒ the span **falls at once**: whoever stood on it goes into the river (`WaterFallSystem`: men and vehicles on the slab are destroyed — there is no water model), the deck is no floor from that frame, the navmesh and the road follow at the rebake, and **NO rubble is left** (BL-9). A bridge blown while being recovered is LOST (never counted back) | design §11 BL-8 / BL-9 |

**What a renderer reads** (all replicated, all on every node):

| question | the read | values |
|---|---|---|
| what is this engineer doing with its span? | `BridgeLayerLook.Of(world, e)` (`Hrot.Look`, the FlailActivity / RadarActivity pattern) over `BridgeCarried` (component 292, `dtBridgeCarried` 247, Brain group) | `Carried` (span on board, no order) · `Laying` · `Recovering` · `NoSpan` (laid it / not an engineer) |
| is this deck standing, being built, gone? | the bridge entity's lifecycle + `StaticObstacle.Kind == WalkableDeck` + its `Health` | Constructing → Active (Open) → removed (recovered: Health > 0) or fallen (Health 0) |
| the deck's size and pose | `SimTransform` (centre, yaw along the span) + `ObstacleShape` (Length = between the deck ends, Width, Height above the banks) + `DeckGeometry.RampLengthFor(Height)` | the ramps run beyond each end |
| why was the order refused? | `BridgeCarried.Refusal` (`BridgeRefusal`) + `GapM`; words: `BridgeLaying.Say(refusal, gap, spanMax)` | e.g. *"the gap is 30.0 m, wider than the 24.0 m this bridge spans"* |
| progress? | ⛔ none on the wire in v1 (BL-21) — the build / recover seconds are the mover's own; a progress bar would need a mover-owned field (ask backend) | — |

## 2. What is already drawn (measured in the code, `2026-11-26`)

| | state |
|---|---|
| ✅ the deck in **3-D** | the terrain mesh draws a laid deck's slab and ramps like any walkable (it is not `DrawnByEntity` — the bridge types are DIS kind 5) — ⚠ so it looks like a plain grey slab, not a bridge |
| ✅ the deck on the **2-D** plan | `TerrainPlan2D` draws every walkable: the slab filled with a "deck 1.2m" label, the ramps dashed with "ramp 0-1.2m" — the same as an authored bridge |
| ✅ the vehicles' **2-D symbol** | both carry SIDC `SFGPEVEB-------` (2525B ground equipment · vehicle · engineer · bridge) |
| ✅ order menu / form | Lay Bridge (form, two picks) and Recover Bridge (at once) are listed by class — the generic menu, nothing bridge-specific |

## 3. ⏭ ui's — what is missing

| # | item | detail | priority |
|---|---|---|---|
| **U-1** | **3-D look of the Assault Bridge Layer** | class `AssaultBridgeLayer` maps to **no** `VisualFamily` (its parent `LargeTrackedUtilityVehicle` has none either — the Bulldozer / MineClearer each got their own) ⇒ ⛔ not checked what it falls back to. A tracked hull (≈ 13.4 × 4 × 4 m with the folded span on top). ⭐ With `BridgeLayerLook`: **Carried** — the folded scissor span on the hull; **Laying / Recovering** — the span raised / unfolding (any pose; there is no progress, so a looped or a fixed "deployed" pose); **NoSpan** — the bare launcher | ⭐ high |
| **U-2** | **3-D look of the Floating Bridge Engineer** | class `FloatingBridgeEngineer` falls back to `WheeledUtility` (a generic truck). An 11 × 2.6 × 3.5 m transporter carrying a folded ribbon-bridge bay; the same three `BridgeLayerLook` states | ⭐ high |
| **U-3** | **3-D look of a laid bridge** | today the terrain's plain slab. ⭐ Draw the deck as a BRIDGE: the assault span (a steel treadway with side girders) vs the floating span (pontoon bays on the water, a low roadway) — keyed on the bridge entity's TKB type (8825 / 8826) and its `ObstacleShape` + pose. ⚠ Decide whether the entity draws its own body (then the terrain must skip it: set the walkable's `DrawnByEntity`, BL-13 — a NOTE to terrain/backend, the bake already supports it) or the terrain mesh gets a bridge material. Lean: the entity draws itself (the IG gets the same entity) | ⭐ high |
| **U-4** | **a bridge being BUILT** (Constructing) | the deck exists only from Open; while the engineer works (minutes) nothing shows that it is laying. ⭐ At least: the engineer's `Laying` pose (U-1/U-2) + a ghost / outline of the planned deck. ⚠ The planned deck is not on the wire — recompute it with `BridgeLaying.Plan(terrain, NearBank, FarBank, …)` from the replicated `BridgeCarried.NearBank / FarBank` (the same function the engineer uses, so it cannot disagree) | medium |
| **U-5** | **a bridge being RECOVERED** | the deck stays until the recover seconds end, then vanishes at the next bake. ⭐ The engineer's `Recovering` pose; optionally the deck highlighted while `Order == Recover` targets it (`BridgeCarried.Target` = its network id) | medium |
| **U-6** | **a DESTROYED bridge** | by design there is **no rubble** (BL-9): the deck simply vanishes at the rebake; the blast is drawn as any burst. ⭐ Missing: the FALL itself — a brief collapse / splash at the deck when its Health reaches 0 (a one-shot effect at the deck's pose; read the bridge entity going to Health 0 / its removal). ⚠ No wreck object exists to keep — if ui wants a lasting sunken-span marker, it is a presentation-only decal (never terrain) | low |
| **U-7** | **thumbnails for the Add Entity picker** | the atlas (`Thumbnails/entities.atlas.json`) has entries for neither 118 nor 119 (nor the bridge types 8825 / 8826, which are not in the picker). ⭐ Render them with `scripts/thumbnails.py --pack` once U-1 / U-2 have kits (rendering the fallback body now would only bake the placeholder in) | after U-1/U-2 |
| **U-8** | **2-D map: the deck as a bridge** | today a generic slab + "deck 1.2m" label. ⭐ A bridge symbol (a span across the river, the two ramp ends), distinct for assault / floating; while Constructing a dashed outline (U-4's plan); ⚠ verify a FALLEN deck disappears from the 2-D plan when it leaves the baked world (⛔ not measured — the plan draws the world it is given) | medium |
| **U-9** | **2-D picker glyph** for the two engineers | the picker maps families to glyphs (`_tank`, `_wheeled` …); a bridge-layer glyph (assets/icons, coloured by meaning, R-4139) | low |
| **U-10** | **the two-point pick for LayBridge** (from rev 1) | ⭐ one gesture: pick the near bank, drag to the far bank — a line between them with the water gap read off as you drag (`BridgeLaying.Plan` gives `Gap`, `Refusal`, `Centre`, `Length`); red when it would be refused. Today the generic form asks for the two points one at a time | medium |
| **U-11** | **show the refusal** | a toast / the unit card line from `BridgeCarried.Refusal` worded by `BridgeLaying.Say` — today only the log and the component view show it | medium |
| **U-12** | **show the span count / state on the unit card** | "span on board" / "laying…" / "recovering…" / "no span" from `BridgeLayerLook` | low |

## 4. Rails / scenarios to watch them with

`scripts/scenario-regression.sh bl-assault-crossing` (an assault bridge laid, two squads cross) · `bl-floating-bridge` (a 72 m floating bridge, two tanks cross) ·
`bl-recover` (laid, then recovered — the deck vanishes) · `bl-blow-the-bridge` (terrain's: a charge drops a placed span, a tank on it falls) ·
`bl-river-crossing` (the whole feature by objectives) · `bl-too-wide` (refused). ⚠ The lay takes 300 s (assault) / 1200 s (floating) of sim time —
`POST /sim/run-to {seconds}` in Preview gets there fast. Look rail: `BodyRulesTests.CE4693_ABridgeEngineersLook_IsAFunctionOfItsReplicatedSpan`.

## 5. Known limits (not ui's, said so they are not mistaken for drawing bugs)

- ⚠ **CE-4697** — a vehicle cannot yet line up onto a narrow deck from an angle: it stops at the ramp's top (`FailedBlocked`). The scenarios cross from the bridge's axis.
- ⚠ **CE-4694** — no smoke screen can be ordered from a scenario (the "lay under smoke" flavour).
- ⚠ The `--mode all` cluster once crossed the floating deck where the editor did not — explained by the ramp length since (CE-4696), but that one run was never re-measured.
