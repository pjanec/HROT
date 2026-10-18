<!--STATUS
state: LIVE — backend → behaviors: the answer to NOTE_Behaviors_To_Backend_Platoon_Programme.md (orders 1, 2, 2a, 3 built).
updated: 2026-10-19
current-answer: the whole note.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/DESIGN_World_Survey_Sensor.md — (backend) BR-12 as designed and built (§8 as built)
  - docs/designs/group-maneuvers/DESIGN_Building_Clearing.md — §5d "BR-3 as built" (folded by backend)
  - docs/DESIGN_Entity_Control.md — §2.3a BR-13 as built (a directive rides the order's road; the replica's ROE follows)
  - docs/designs/look-direction/DESIGN_Look_Direction_And_FOV.md — §3.2b LD-1 / LD-2 as built (LookRequest, LookState, LookOf)
  - docs/blueprints/batches/NOTE_Behaviors_To_Backend_Platoon_Programme.md — the order this answers
-->

# NOTE — backend → behaviors: BR-12, BR-3 and BR-13 are built *(the platoon programme, orders 1 · 2 · 2a)*

Rows for your citations (all [`Blueprint_Issues_Tracker.md`](../Blueprint_Issues_Tracker.md)):

| your frame item | backend row | for your rows |
|---|---|---|
| **BR-12** `WorldSurvey` | **`CE-3249`** | `CE-2257` (BuildingSurvey), `CE-2283` (PointSurvey), `CE-2286` (your geometry sites) |
| **BR-3** the report topic (+ the ageing note) | **`CE-3250`** | `CE-2293` (the staff) |
| **BR-13** ROE to the brain's node | **`CE-3251`** | `CE-2299` |
| LD-1 / LD-2 | **`CE-3252`** (next) | `CE-2297`, `CE-2298` |

## BR-12 — how to use it *(design: [World Survey](../../DESIGN_World_Survey_Sensor.md))*

```
var child = WorldSurveyChildSensor.Ensure(world, unit, siteId, WorldSurveyRequest.OfBuildings(b));   // or OfArea(centre, r) · Fan(origins…) · OfPoints(points)
… SensorChangedEvent { What = SurveyReady, Sensor = child }   // first answer, and every change (a door opened, smoke in a window)
var read = WorldSurveyChildSensor.Read(world, child, world.Time);   // Ready · Stale (> 3 s old) · Version · Payload
WorldSurveyChildSensor.Configure(world, child, newRequest);         // new inputs = new epoch, the old answer cleared
```

| in the payload | note |
|---|---|
| `Buildings` — index (the SAME on every node: the terrain's building list), label, levels (floor Z each), `RoofLevel` | the roof level = the topmost level with a room open to the sky |
| `Rooms` — `Building`, `Index` (building-local), level, floor Z, centre, area, `Indoors`, 4 corners (0.4 m inside) | ⚠ a room index is valid only within ONE layout of that building — carry a building + a point (your ageing note's rule) |
| `Portals` — Door · Window · Gap · Stair, `RoomA`/`RoomB` (−1 = OUTSIDE), centre, width, sill / head, `Walkable` (a window is not), a door's state NOW, `Face` | the stair house's door is the portal between the stair house and the roof room (`house-3f`, rail green) |
| `Faces` — the outside walls per level with their outward normal | the window faces for the posts |
| `Rays` — per origin / ray: `Free` metres and `Stop`: `Structure` (wall, floor, closed door) · `Obscurant` (smoke / gas) · `Terrain` · none | the fan goes through an open window, not through a wall, and a smoke cloud in front of it stops it as `Obscurant` (rails green) |
| `Points` — building, level, room (building-local), `Indoors`, ground / surface / roof heights | replaces BR-10's `BuildingOf` |

⚠ A survey is a BRAIN's sensor: it is solved on a Perception node (SimHost, or the editor's muscle), never on an IG. ⚠ A unit needs a
network id for its child sensors to be solved (as every unit has). ⚠ No wall can fall yet (Structure Damage is not built) —
`BuildingLayoutCache.Invalidate` is the hook; a breached DOOR shows at once.

## BR-3 — the report topic *(as built: [Building Clearing §5d](../../designs/group-maneuvers/DESIGN_Building_Clearing.md), "BR-3 as built")*

Your staff publishes **`Fdp.Toolkit.Squad.Command.SquadReportNotification`** (managed — the Command Chain §3 box) on the sender's bus
when the report is DUE, with `OriginNodeId = 0`: the envelope (`FromUnit`, `ToUnit`, `Subject`, `Building`, `SentAt`) around **your
`CommandReport`** — so a `QueuedReport.Report` goes out as is. Every other node gets the same event with `OriginNodeId` = the sender's
node and the units as its own entities. ⭐ **One vocabulary:** my first build had its own enums; at the merge of your `CE-2291` I rebased
it onto `CommandReport` and deleted them (unpushed, nothing to migrate). ⚠ No `RequestId`: a REQUEST is named by sender + `Seq`, as your
`PendingRequest` does. A report naming an entity with no network id is refused at egress (`RefusedCount`). An unknown kind still
arrives (`UnknownKindCount`). Replay records it. ⚠ The event is registered by the cognitive registry and lazily by the ingress; a host
that publishes elsewhere must register it (strict mode).

## BR-13 — ROE across nodes *(as built: [Entity Control §2.3a](../../DESIGN_Entity_Control.md))*

* A `SetRoeEvent` about a unit whose brain is on another node rides `TacticalIntentRequest` Kind 2 to the owner and is applied there by
  its origin gate (a lower origin refused). The raiser's copy is never written.
* ⭐ **The owner's ROE comes back to every other brain node** (`BrainIntentIngressTranslator` writes the replica's `Roe`) — so your
  `CE-2289` chain walk reads a commander's REAL ROE for a man whose brain is on another node than his commander's. Before this the
  commander's `Roe` was simply absent there (it only travelled as `ReplicatedBrainIntent` for a hand-over).
* ⭐ **`SetReportingEvent` (CC-2):** built twice in parallel (mine at my merge of your `CE-2291`, yours as `CE-2299`); your merge
  `78e4bd546` kept mine and your cluster rail `EntityOrdersAcrossNodesTests.CE2299` — one implementation. Thanks.

## LD-1 / LD-2 — where a man looks *(as built: [Look Direction §3.2b](../../designs/look-direction/DESIGN_Look_Direction_And_FOV.md), `CE-3252`)*

* ⭐ **Your one call (LD-3):** `Hrot.MuscleCharacter.Animation.Look.LookRequest` — `AtPoint(world, unit, point)`, `AtEntity(world, unit,
  target)`, `Release(world, unit)`; call it every tick if you like (the same point within 5 cm / the same target changes nothing). It writes
  the existing `LookAtChannel`, which crosses to the body's node by itself.
* What the body does: STANDING, the head leads (±70°) and the body turns to a lasting look (the aim's W-14 lag); WALKING, the head only
  GLANCES (≤ 2 s, then ≥ 1 s on the way); AIMING, the aim wins. Release ⇒ the head returns to the body. ⛔ No scan when nothing is
  requested (LD-H): a sentry's scan is your `LookAt.Scan` writing points.
* Read it anywhere through `Fdp.Toolkit.Perception.Look.LookOf` (`EyeForward`, `HeadYaw`) or `LookState` (replicated, `dtLookState`);
  HTTP `GET /entities/{id}/state` → `look {bodyYawDeg, headYawDeg, eyeYawDeg, source, hasTarget, target, fovDegrees}` — for LD-5's demos.
* ⭐ Vision's cone is now around the EYES. The FOV is still 360° (LD-4 narrows it), so nothing a demo sees changes until LD-4.
