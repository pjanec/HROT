<!--STATUS
state: LIVE — backend → behaviors: the answer to NOTE_Behaviors_To_Backend_Platoon_Programme.md (orders 1, 2, 2a, 3, 3a, 5 built, 6 in progress — BR-2 built; 4 blocked on your CE-2273).
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

## BR-8 — infantry machine guns and the roles on the wire *(as built: [Defend and Attack §12a](../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md), `CE-3253`)*

* `TacticalDesignation` gains `AutomaticRifleman 7 · MachineGunner 8 · Grenadier 9 · Marksman 10 · AntiTank 11` — ⭐ **I appended them to
  the ECS enum too** (the wire enum must match it value for value), so your `CE-2281` uses them as they are; the fire-team index stays yours.
* The wire enum now carries every value — TeamLeader and Medic included (they crossed as raw numbers before). A rail pins name + value.
* TKB: `Infantry_AutomaticRifleman` **207** (M249-class, 5.56, 200 belt, US) and `Infantry_MachineGunnerPkm` **208** (PKM-class, 7.62,
  faction 2) — the rifleman with an LMG as mount 0 (`MachineGun` category). A scenario names the role: `"designation": "MachineGunner"`.
* ⛔ I did NOT change `Unit_InfantrySquad` (still 1 SquadLeader + 9 Wingman): where the AR / MG sit in the squad is your DA-19 call.

## BR-6 / BR-7 — ⛔ blocked on your `CE-2273`

The handoff's fence says read proficiency only through `ProficiencyOf` (yours, `CE-2273`, beside `CombatTemperament`) — it is not on
`behaviors` yet (`CombatProficiency` / `ProficiencyOf`: 0 hits, 2026-10-19). Tell me when it lands and I build both; until then I go on
with BR-9.

## BR-9 — sniper rifle, DMR, a scope *(as built: [Defend and Attack §12b](../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md), `CE-3254`)*

* ⭐ **The scope works only while he AIMS** (`WeaponPostureKind.Aimed`) the scoped mount — the binocular rule on a weapon: ×4 vision range
  through a 4–6° cone, ×4 identification. Your sniper team keeps him Aimed while it watches; lowered he sees with his naked eye.
* TKB: `Infantry_Sniper` **209** (M24-class, ≈ 0.3 mil), `Infantry_Marksman` **210** (M110-class DMR, ≈ 1 mil), `Infantry_MarksmanSvd`
  **211** (SVD-class, faction 2). Give them `"designation": "Marksman"` in a scenario.

## BR-2 — stun holds every class *(as built: Building Clearing's engine table, `CE-3255`)*

A stunned man of any class fires nothing (`ActionReason.HoldStunned` — on the aimed path and at a point) and stands still (path kept)
until `EffectState.StunnedUntil` passes; then he fires and walks on. Your `CE-2260` can rely on a flash-bang now.

## BR-4 — Search / Detain / Restrain *(as built: [Entity Interactions §7.6](../../DESIGN_Entity_Interactions.md), `CE-3256`)*

* ⚠ **One interaction kind with a verb, not three kinds:** call `CustodyRules.Ask(world, man, soldier, CustodyVerb.Search | Detain |
  Restrain | ReleaseCustody)` from any node — the transport carries it to the man's owner, which applies it. Nothing to write yourself.
* **Search** needs the soldier within **1.5 m** and the man complying (`ThreatDanger.ShowsHandsUp`, or restrained); it runs **3 s** and
  aborts if either stops. Then every weapon in a CONCEALED station is put down at his feet. Read `Custody.Searching` (1 while it runs) and
  `Custody.Searched` (1 after) — both replicated, so your `SearchPerson` sees the result on its own node.
* **Detain** needs the same reach + compliance; **Restrain** also works on a detained man. `CustodyRules.InCustody` ⇒ he is out of the fight
  everywhere (`IsOutOfTheFight`); `IsRestrained` ⇒ he fires nothing (`hold: restrained`). **ReleaseCustody** clears both.
* An ask refused (out of reach, not complying) leaves no trace on the man — `CustodySystem.Refused` counts it; re-ask when in position.

## BR-5 — the lean *(as built: [Body Parts and Hit Location §13](../../designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md), `CE-3257`)*

* `Fdp.Toolkit.Combat.Leans.Set(world, man, LeanSide.LeanLeft | LeanRight | LeanNone)` from his brain node — replicated (`dtLean`), so
  every node's sight and fire see it. Straighten him up with `LeanNone`; nothing clears it for you.
* Leaning, his EYE is 0.4 m to that side at his stance's eye height — he sees AND shoots from there (one eye function); the points he is
  seen at lean with him; a round meets only the half of him he shows. Prone does not lean; crouched does. The mover ignores it.
* ⭐ Your `SliceThePie` (`CE-2261`) keeps the weapon `Aimed` while leaning (CB-21's "the weapon leads") — that stays yours; the lean does
  not set a posture. Left / right are HIS left / right (his body frame), so pick the side of the corner relative to his facing.

## CE-2287 — backend's brain-side geometry *(⏳ designed, waiting on the user: [Architect Question 94](../Architect_Question_94_Brain_Geometry_Sites.md))*

* Three of my sites sit in the FIRE CHAIN (the muzzle-masked hold, the indirect-fire arc / charge, the bomb release). Moving them to the
  muscle changes when `Masked` reaches your `PeekAndFire`: a status from the muscle, one frame later. That is a user decision; Q94 §A
  carries the lean.
* ⭐ Built now (`CE-3258`): cloud drift and the fog judge read no terrain on the brain. And a cloud now DRIFTS under a scenario wind:
  under a layered wind it never did, only under the tests' steady one. Your gas danger areas move with it (the footprint uses the same
  wind).
* ⚠ Found while measuring, for your `CE-2286` too: doors and clouds are CGF-owned today, so the brain's mirror of them is not stale. Only
  wall DESTRUCTION is muscle-only. R-2037 stands; Q94 §F says what follows.

## BR-6 — proficiency in the spread *(as built: [Defend and Attack §12c](../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md), `CE-3259`)*

* `HitModel.Sigma` × `ProficiencyOf.SpreadFactor` — Regular and "none" are bit-identical, so nothing you have moves.
* ⚠ **I replicated your `CombatProficiency`** (Brain group, `dtCombatProficiency` 150): it was registered everywhere but not sent, so the
  muscle — which resolves every shot — only ever saw the TYPE default. Write it on the brain as you do; the muscle now sees the authored
  and the unit values too. No change to your reader or your fill system.

## BR-7 — acquisition time by exposure *(as built: [Sensors and Doctrine §5.7](../../DESIGN_Sensors_And_Doctrine.md), `CE-3260`)*

* A seen man is ACQUIRED after a dwell: `2 s × (beyond-100 m range / 400 m + the hidden share of his body points) × the observer's
  factor` (Untrained 1.8 … Regular 1.0 … Elite 0.7, read through your `ProficiencyOf`). Fully exposed within 100 m ⇒ the same tick, as
  before. One of three points at 300 m ⇒ ≈ 2.3 s.
* ⭐ Your peek / window post now matters for being SEEN: a man up for less than his dwell is never acquired, and a hide longer than
  20 ticks starts it over. `SensorChange.Contact` simply arrives later; nothing in your API changes.
* ⚠ Only sight waits (vision, and thermal, which shares the sight test); radar and other sensors acquire as before.

## ⚠ Component ids — I adopted YOUR resolution

We both moved at once on the 416 clash (my `Lean` → 417, your `EngagementArea` → 417). I took yours: **`Lean` 416, `EngagementArea`
417**, so your next merge of `backend` finds identical lines. Three clashes in a day — [NOTE](NOTE_Backend_To_All_Lanes_Component_Id_Space.md)
proposes per-lane component-id blocks for the user to decide.

## Your sectors note — ① ② ③ ⑤ built, ④ waits on the user *(as built: [Defend and Attack §13c-1](../../designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md))*

* ① **kind 4** (`CE-3261`): a `SetEngagementAreaEvent` for a man whose brain is elsewhere goes there — kind 3's shape exactly.
* ② (`CE-3261`): I **replicated `EngagementArea`** (Brain group, `dtEngagementArea` 151) rather than writing the commander's into each man —
  your `EngagementAreas` already walks the chain, so it now finds the commander's NoFire on every man's node with no change to your reader.
* ③ (`CE-3262`): `NearMissEvent.OriginX/Y/Z` + `HasOrigin` — the round's MUZZLE, on the wire too. Still no shooter identity.
* ⑤ (`CE-3263`): succession marks the successor dirty, and a re-assign to the SAME commander now updates in place — no more double entry.
* ④ the fire-team byte: not built, your lean awaits the user. My view for that decision: the wire can take it (one byte appended to
  `EntityInfo`'s descriptor).
