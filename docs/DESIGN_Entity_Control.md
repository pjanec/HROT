<!--STATUS
state: LIVE
updated: 2026-10-16
build-state: BUILDING — S1 built 2026-10-11 (§9); S2 built — attack (CE-3170, §10) and menu orders (CE-3169, §11); S3 built (CE-3171, §8a design, §12 as-built); S4 built (§8b/§8c design, §13 as-built); S5 built (§8d design, §13.3 as-built, (V) screenshot §13.3); S6 operator weapons BUILT (§8e design, §13.7 as-built); S7 the pilot station BUILT (§8f); S8 SIGHT / LOCK and the part-box crosshair BUILT 2026-10-16 (§8g); S9 the FPS use key BUILDING — engine half (§8h.7, backend), the brain half (§8h.8+, behaviors) and the station (§8h.9, ui) built 2026-10-16
current-answer: §2 decisions · §2.3 messaging (FDP events; the networkless editor) · §3 classes · §4 sequences · §5 modules · §6 manual motion · §7 slices · §9 as-built (S1) · §10 as-built (S2 attack) · §11 as-built (S2 menu orders) · §8a + §12 (S3 RTS tool) · §8b + §13 (S4 possession, manual drive) · §8d + §13.3 (S5 FPS station) · §13.4 (M-5 navmesh check) · §13.5 (the operator's stance, the RTS tool live, first-person input) · §13.6 (the menu's keys on the map, the RTS right button) · §8e + §13.7 (the operator's weapons: click = one shot, weapon/ammo keys, HUD) · §8f (the pilot station) · §8g (SIGHT or LOCK; the crosshair on the body parts — CE-1126 / CE-1127) · §8h (the FPS use key — CE-1144; as built: §8h.7 the engine half, §8h.8+ the brain half (behaviors), §8h.9 the station)
stale-below: nothing
known-rot: ⭐ CE-2143 (2026-10-12) — the SENDER-side mapping in §3.1 / §10 / §11 / §12 (Move → MoveToLocation, Engage/Attack → CombatPosture, Stop → Idle; the fixed Move Here · Engage · Stop menu ids 1-3) is SUPERSEDED by docs/DESIGN_Orders_Params_Entity_Classes.md §9: EntityOrders sends ORDERS (Order.<name>), the menu lists the orders the unit's class fulfils (ids 400+), RTS gestures come from [Order(Gesture)], HTTP kind = any order; the unit resolves its own behaviour (PersonMove / InfantrySquadMove …). The one-service shape, picks, possession and the rest here stand. · the station side of §3.2, §4.3, §5 and the §7 S5 row (PossessionController + PossessionTool + PossessionHudLayer) is SUPERSEDED by §8d / §13.3 — one PossessionStation layer plus FpsCameraDriver; the camera's own input pass is the exclusive focus · §3.1 and §10 map an order to a soldier behaviour inside EntityOrders (the SENDER) — OVERRULED 2026-10-12 by R-273 (behaviors): the receiver picks; how is AQ90 A1–A6, awaiting the user
known-conflict: docs/UX/UX_Feature_Entity_Commanding.md §1 maps Engage → FireAtTarget; this design maps it to the attack order (CombatPosture with a designated target, C-5). That doc's §1 is marked accordingly.
related-designs:
  - docs/DESIGN_Building_Interiors.md — OWNS the door verbs and DoorActionExecutor that §8h's use key starts (§3j)
  - docs/designs/hit-location/DESIGN_Body_Parts_And_Hit_Location.md — OWNS PersonBody / VehicleBody, the parts a round meets; §8g's crosshair meets the same parts through one function (BodyPartRay)
  - docs/DESIGN_Visual_Effects.md — §10 OWNS the impacts (entry, exit, stop; wall marks) asked in the same exchange as §8g
  - docs/designs/audio/DESIGN_FPS_Audio.md — sound exists only while PossessionStation holds a unit; the ear is the FPS camera (AU-3)
  - docs/blueprints/Architect_Question_91_Player_Nodes.md — §4-F: PossessionSystem also releases a unit when its station's node DEPARTS (today only Esc or death).
  - docs/designs/weapons/DESIGN_Weapon_Systems.md — OWNS the gunner seat a player may later possess to fire a host's station weapon (R-2016)
  - docs/designs/air-attack/DESIGN_Air_Operations.md — OWNS the simple pilot models that answer M-4 (mouse-aim plane, helicopter hover hold) and ControlScheme.FixedWing (R-2015)
  - docs/designs/motion-models/DESIGN_Motion_Domains.md — makes manual drive per locomotion class (a Vertical axis, a control scheme per class at the station) so every motion model is possessable (MD-D)
  - docs/DESIGN_Orders_Params_Entity_Classes.md — D4 (CE-2143): EntityOrders / HTTP / menu / RTS become generic over the params-DTO registry
  - docs/blueprints/Architect_Question_90_Unit_Orders_Carry_Capacity.md — OWNS R-273 (behaviors): EntityOrders must publish order kinds, not behaviour names (A1); §3.1/§10 sender-side mapping is overruled
  - docs/UX/UX_Feature_Entity_Commanding.md — OWNS the right-click order vocabulary, the args channel, TKB command sets and ClearsPriorIntent/IntentClearRegistry (§2.5b-d); S1-S2 here build its §2.5b-d and §2.6 and bind its three ids
  - docs/blueprints/Architect_Question_83_Doctrine_And_Order_Origin.md — OWNS the origin rank (Operator > Superior > SOP) and "no SOP = brain-dead"; possession is an origin, never an autonomy switch
  - docs/DESIGN_Decision_Layer.md §4 — OWNS task / SOP / reaction and the paused task (R-199, R-203) that possession pauses and resumes
  - docs/DESIGN_Entity_Interactions.md — OWNS the one interaction topic (R-221); possession adds three union cases
  - docs/designs/tactical-intent/DESIGN.md — OWNS the intent pipeline (any node publishes, the owner resolves) every order here rides
  - docs/DESIGN_Weapon_Layers.md — OWNS the three weapon layers (R-261); operator fire enters at the behaviour layer
  - docs/designs/vehicle-tactics/DESIGN_Missiles_And_Ammo.md — OWNS the ammo loadout, AmmoChoice and the one-reload switch (D-I) that the operator's F key cycles (§8e)
  - docs/DESIGN_Map_3D_Mode.md — OWNS MapCamera3D, Picker3D and the scenario camera entity (R-260) the FPS view drives and must not overwrite
  - docs/UX/UX_Feature_Tool_Model.md — OWNS ToolController / exclusive focus (R-144) the RTS and FPS tools register through
  - docs/designs/ai-debug-api/DESIGN.md + docs/MCP_Integration.md — OWN the HTTP debug API the order routes extend
  - FDP/Docs/projects/toolkits/FDP.Toolkit.CarKinem.md — OWNS ground kinematics (HumanGait R-247, BicycleModel) that manual drive extends
  - docs/DESIGN_Ownership_Groups_And_Grants.md — OWNS the Brain / MuscleGround groups the new components join
  - docs/designs/gizmos-1/gizmo-input-focus-design.md §6.2d — OWNS the raw-input route to backend gizmos (the key queue's one reader, CE-3175)
  - docs/DESIGN_Peek_And_Fire.md §10.9 G4 — OWNS SquadAssignment.TargetOf, the ONE reader of "the target this unit was told to fight"; the attack order's DesignatedTarget (CE-3170) answers first there
  - docs/blueprints/Architect_Question_93_Sumo_Traffic_Node.md — R-4023 hands a vehicle to SUMO by a follow mode (KinematicsMode.Traffic), following M-6's rejection of an ownership move and M-1's Manual-mode shape.
-->

# DESIGN — **entity control** *(orders over HTTP, RTS clicks, the menu, and FPS possession — one path)*

> 🔒 **User, `2026-10-11`** (the request, an attached proposal + audio transcript): *"enable control of these entities … by
> taking control over a single chosen entity … possession … First-Person Shooter … Real-Time Strategy … an entity can be
> possessed from exactly one operator station … without losing our current capabilities."* → *"make the entity control
> available via http debug api … replace the human operator with ai debug http api allowing you to play the game"* →
> *"Your leans accepted. … Shouldnt we control the motion channel directly … Vehicles cant strafe so they should turn their
> steering wheel … Fps view should run on our 3d map view. The generic entity command http way should be fixed … menu items …
> need some entity/point picker followup operation. Write design please."* → *"Approved. All the messaging needs to work in the
> networkless editor so ig needs using fdp bus events translated to network and back, accepting the latency."*

**The idea in one line:** taking control of a unit is **giving it an order of a high enough origin** — never an "AI off" switch
(AQ83 rejected *"a separate autonomy on/off mode"*). HTTP, RTS clicks and the menu send **high-level orders** through **one
order service**; FPS sends a **stream of control frames** to a behaviour of the highest origin. Every path ends on the node
that owns the unit's brain.

## 1. INVENTORY — measured `2026-10-11`

| query | result |
|---|---|
| `search_graph` `.*(Possess\|Manual\|Operator\|PlayerInput\|Teleop\|Joystick\|Hud\|Crosshair\|Reticle).*` Class | 7 — none about control; `Stride/…/Player/PlayerInput` is the Stride template, **removed at boot** (`StrideHrotGame.cs:1493`) |
| `search_graph` `.*Camera.*` Class | 23 — `MapCamera3D` (**sealed**, `MapCamera3D.cs:28`), `MapCamera`, `MapCameraLayer`, Stride `BasicCameraController`/`ThirdPersonCamera`; no follow/attach camera |
| `search_graph` `.*DebugApi.*` | 194 — `DebugApiHost` routes incl. `POST /entities/command`, `/missions/{id}/task`, `/behaviors`, `/entities/{id}/state\|memory\|trace\|weapons` |
| grep handlers for `GlobalActionIds.MoveHere\|Engage\|Stop` | **0** — on every unit's menu (`ContextMenuProjectorGizmo.cs:54-62`), handled nowhere |
| grep `DisableCursor\|HideCursor\|SetMousePosition` | 0 — no cursor lock |
| grep `AimYaw\|DesiredHeading\|YawRate`(field)`\|LocomotionClass\|Flying`(consumer) | 0 — no body-independent aim, no commandable yaw, no aircraft motion model |
| grep `StationId\|OperatorId\|ControlledBy\|possess` (code + docs) | 0 — no station identity, no possession design anywhere in `docs/` or `.dev/` |

⚠ The codebase-memory MCP server dropped mid-session; later inventory ran through its CLI and grep. `check_index_coverage` was
not run (not available via the CLI) — the zero rows are grep-and-graph zeros, coverage not checked.

## 2. Decisions

### 2.1 ✅ Accepted — user, `2026-10-11` (*"Your leans accepted."*) → `R-270`

| # | decision |
|---|---|
| **C-1** | **FPS runs in our 3-D map view** (the shared `MapCanvas` 3-D mode on every map host), the camera behind a driver hook so a final engine can take it over (R-250) |
| **C-2** | **The operator overrides the ROE** — a possessed unit fires when told, even under `HoldFire`; it is not held by its own seen-now gate either (the operator is the eyes) |
| **C-3** | **One station per unit, FPS only.** A second station's possess request takes the unit over; RTS / menu / HTTP orders to a unit another station holds in FPS are refused |
| **C-4** | **FPS release resumes the unit's earlier task** (the paused-task slot); an RTS / menu / HTTP order **replaces** it and clears the mission plan (Entity Commanding ruling 50) |
| **C-5** | **"Attack" = close in and fight** — one behaviour for RTS, menu and HTTP: CombatPosture with a designated target and `assault` (AQ89) — the unit picks route, cover and weapon |
| **C-6** | Network lag on FPS movement is accepted for v1 and **measured on the cluster**; looking around is local |
| **C-7** | **Ammo state crosses the network** (a new per-mount descriptor) so a HUD on any station can show it |
| **C-8** | **One operator per node** — the station id is the node id |
| **C-9** | The AI player sees **only what its side knows** (the sitrep is built from its units' target memory); `truth=true` for debugging |
| **C-10** | The AI player may be **locked to one side** per session; orders to the other side's units are then refused |
| **C-11** | HTTP orders carry origin **Operator**; build order = **HTTP orders first**, then menu/RTS, then FPS |
| **C-12** | *(this message)* FPS motion is a **direct manual-drive command**, not direct-to-point; vehicles steer instead of strafing, with free look and an optional view locked to the hull where the mouse steers; the generic `/entities/command` route is fixed; the menu orders get a follow-up point / entity pick |

### 2.2 ✅ Approved — the motion and plumbing decisions (user, `2026-10-11`: *"Approved."* → `R-271 (backend)`)

| # | question | ⭐ lean | rejected — one line each |
|---|---|---|---|
| **M-1** | how manual motion reaches the muscle | a NEW Brain-group component **`ManualDrive`** + its own descriptor, consumed under a NEW **`KinematicsMode.Manual`** (entered once via `NavigationMode.Manual`) | a manual mode inside `NavigationIntent` — its egress sends and the bridge applies **only when `IntentId` changes** (`NavigationIntentEgressTranslator.cs:138`, bridge `:127`), and every change resets nav status · reusing `KinematicsMode.None` — "manual control" in its comment but treated as Direct (`CarKinematicsSystem.cs:226`, CE-3176) |
| **M-2** | person axes | **forward/back + strafe in the body frame; body yaw = the camera's yaw**, turned at `HumanGait.TurnRate` (turn on the spot = mouse only). Back and strafe at **60 %** of the gait speed; sprint forward only; stance from the existing `StanceIntent` | a yaw-RATE axis — drifts under lag; an absolute yaw is latency-tolerant |
| **M-3** | vehicle axes | **throttle (− = brake, then reverse) + steer**; default **free look**, A/D steer; **`V` toggles hull-locked view where mouse X steers**. Reverse needs the integrators' `speed ≥ 0` clamp lifted (`BicycleModel.cs:34` — ⚠ an earlier draft said `:179`) | strafe for vehicles — not physical |
| **M-4** | aircraft / pilot stick | **deferred** — no aircraft motion model exists (`DESIGN_Body_Geometry_And_Ground_Contact.md:154`); the stick axes join `ManualDrive` with that model (PLAN_3D_World_And_Realism D1) | reserving pitch/roll fields now — speculative wire |
| **M-5** | walls | a manual **person's step is refused where the navmesh is not walkable** (`INavmeshProvider.IsWalkable` / `ProjectToNavmesh`, slide along the projection); vehicles keep today's grounding + avoidance (known limit: they can drive into a building) | free movement — walks through walls |
| **M-6** | how control frames reach the brain | three **interaction union cases** on the one topic (R-221): `Possess{mode}`, `Release`, `ControlFrame{seq,…}`; the brain keeps the newest by `seq` | a new topic per message · moving the unit's ownership to the station (moves its brain and muscle) · frames straight to the muscle (two writers of motion; R-180 keeps the request on the brain) |
| **M-7** | possession's place in the decision layer | a NEW **`BehaviorOrigin.Possession`**, ranked above Operator: it **pauses** whatever runs (as a reaction does), no reaction may interrupt it, and clearing it **resumes** the paused task in all tiers (R-203) | Operator origin — an Operator order **drops** the paused task (`BehaviorIngressSystem.cs:388`), so C-4 could not hold |
| **M-8** | operator fire | the **existing** `AimAndFireExecutor` gains a point target and an **operator mode** (no seen-now gate, no aim time, ROE bypass): a turret mount gets `WeaponTask{Track, Point}` + granted rounds (the muscle already aims at `Point`, `WeaponTaskDispatcherSystem.cs:54`); a per-shot mount spends through the per-shot path | a new operator weapon executor — a second weapon brain (R-174) |
| **M-9** | the FPS camera | `MapCamera3D` gains an optional **`ICameraDriver`** (not unsealed — gizmos and labels draw only for `MapCamera3D`, `DebugGizmoLayer.cs:140,169`); while a driver is set `MapCameraLayer` **does not save the pose** into the scenario camera (R-260, `MapCameraLayer.cs:149`). `IInputProvider` gains **`SetCursorLocked`** | a second camera class — loses every 3-D gizmo, label and picker |
| **M-10** | the menu orders | handlers for the existing `MoveHere / Engage / Stop` ids, registered **once in `MapInteractionPack`** (every map host): Move → **location follow-up pick**, Engage → **entity follow-up pick**, Stop → immediate. The args channel and TKB command sets (Entity Commanding §2.2-2.4) stay later work there | an editor-only registration — the IG and CGF menus stay dead |

### 2.3 ✅ Messaging — FDP bus events first; the network is only a translator *(user, `2026-10-11` → `R-271 (backend)`; R-134)*

🔒 *"All the messaging needs to work in the networkless editor so ig needs using fdp bus events translated to network and back,
accepting the latency."* ⇒ **every message in this design is a typed FDP event or a component on the local world.** A sender
publishes on its own bus and never knows whether the receiver is local; the **owner's handler reads its own bus**, whichever
way the event arrived; an **egress translator forwards only what this node does not own**, and an ingress translator
republishes it on the owner's bus (the door pattern, `DoorCommands.cs:138-142`). ⇒ in the **editor** — which composes the
station, the brain (`CgfLogicPack`) and the muscle (`SimHostCoreLogicPack`) in ONE world (`EditorSubsystem.cs:1602-1632`) —
nothing is translated and everything works with no network; on an **IG / cluster** the same events cross by translator, and
the extra hops are accepted latency (C-6).

| message | FDP form *(what the sender publishes / the reader reads)* | owner-side reader | across nodes *(translator only)* |
|---|---|---|---|
| an order (HTTP · menu · RTS) | `AssignTacticalIntentEvent` *(existing; + `ClearsPriorIntent`)* | `TacticalIntentResolutionSystem` | `TacticalIntentRequest` *(existing)* |
| possess / release | **NEW** `PossessRequestEvent{Entity, Mode, StationNodeId, IsRemote}` · `ReleasePossessionEvent{…}` | `PossessionSystem` | interaction union cases `Possess` · `Release` |
| a control frame | **NEW** `OperatorControlFrameEvent{Entity, Seq, Forward, Lateral, Steer, Yaw, AimPoint, AimTarget, Trigger, Mount, Gait, Stance, IsRemote}` | `PossessionSystem` ⇒ `OperatorInput` | interaction union case `ControlFrame` |
| who holds a unit | **NEW** component `PossessedBy` *(Brain group)* | every station reads it | its descriptor |
| the manual drive demand | **NEW** component `ManualDrive` *(Brain group)* | `ManualDriveSystem` | its descriptor |
| ammo for the HUD | **NEW** component per mount `WeaponAmmo` *(Brain group)* | `PossessionHudLayer` | its descriptor |

| ⭐ rule | why |
|---|---|
| ⛔ **no sender or reader touches a DDS type** | R-134; and it is what makes the editor path the SAME code, not a second one |
| ⭐ **every slice's FIRST rail runs in the networkless editor world**; the cluster rail comes second | the editor is the path a broken translator cannot hide |
| ⭐ **`StationNodeId` is the local node id**, also in the editor | C-8; a second station is simulated in rails by a second id |

### 2.3a ✅ BUILT — a DIRECTIVE rides the order's road *(`CE-3251`, behaviors' BR-13, `2026-10-18`)*

📐 **Measured:** an order crossed nodes, an ROE change did not — `SetRoeEvent` was applied by `RoeSystem` on the node that RAISED
it, so whenever a superior's brain and a subordinate's brain ran on different nodes the ROE (and, by the same copy, Command Chain
CC-2's reporting requirements and CC-11's initiative) did nothing on the owner and only changed the raiser's copy. ⛔ No rule keeps a
hierarchy's brains on one node (searched `DESIGN_Ownership_Groups_And_Grants.md`: the brain group is placed per unit).

```mermaid
sequenceDiagram
  participant A as node A (raises it: HTTP, a superior's play, the editor panel)
  participant EA as TacticalIntentEgressTranslator (A)
  participant B as node B (owns the unit's brain)
  participant R as RoeSystem (B)
  A->>A: SetRoeEvent {Entity, Fire, Reactions, Window, Force, Origin}
  Note over A: RoeSystem on A: a networked unit whose brain is not here ⇒ left to the owner (ForwardedCount)
  EA->>EA: brain not here ⇒ TacticalIntentRequest Kind 2 (SetRoe), fields as JSON, Origin beside them — AFTER this frame's orders
  EA-->>B: DDS "TacticalIntentRequest"
  B->>B: TacticalIntentIngressTranslator: Kind ≥ 2 ⇒ raise SetRoeEvent again (unknown kind ⇒ counted, never an order)
  B->>R: SetRoeEvent
  R->>R: the origin gate (a lower origin than SetBy is refused) ⇒ Roe written by its ONE writer
```
*What the picture shows that prose hid:* the directive takes **the same writer** as the order, so an order and its ROE sent in one
frame arrive in that order, and **only the owner writes `Roe`** — the raiser's copy is never touched, so there is no second writer to
correct later.

| as built | where |
|---|---|
| `TacticalIntentWireKind` — 0 Assign, 1 Clear, ⭐ 2 SetRoe, 3 SetReporting (reserved for `CE-2291`; not decoded yet) | `Hrot.Network.NED/TacticalIntentMessages.cs` |
| `RoeDirectiveJson` — `{"Fire","Reactions","Window","Force"}`; an unset field stays unset | `Hrot.Network.NED/SimHost/RoeDirectiveJson.cs` |
| egress forwards `SetRoeEvent` when the brain is not here (the order's gate), after the orders | `TacticalIntentEgressTranslator.ForwardRoe` |
| ingress: kind ≥ 2 is a directive; SetRoe ⇒ `SetRoeEvent` with its origin; else `UnhandledDirectiveCount` | `TacticalIntentIngressTranslator.ProcessSample` |
| `RoeSystem` leaves a NETWORKED unit whose brain is elsewhere to the owner (`ForwardedCount`) — ⚠ "networked" = has `NetworkIdentity`: a world with no network sets no authority bits, so authority alone cannot mean "replica" (two snapshot rails caught that) | `RoeSystem.Execute` |
| rails: `TacticalIntentEgressTranslatorTests.CE3251_*` (2), `TacticalIntentIngressTranslatorTests.CE3251_*` (2), `RoeAndRecentSensesTests.CE3251_AReplicasRoe_IsLeftToTheBrainsOwner`, cluster `EntityOrdersAcrossNodesTests.CE3251_AnRoeSetOnAnotherNode_ReachesTheBrainsOwner_AndItsGate` (SimHost raises, CGF applies; a lower origin is refused on CGF) | — |

⭐ **The owner's ROE comes back to every other brain node** (BR-13's *"A's replica follows"*, and what `CE-2289`'s chain walk reads for
a subordinate whose brain is on another node than its commander's): `BrainIntentIngressTranslator` writes the unit's `Roe` from the
owner's `EntityBrainIntent` (it already carried the ROE, for a hand-over). With `RoeSystem` leaving the replica alone, that ingress is
the replica's one writer. Rail: `BrainIntentTranslatorTests.CE3251_TheOwnersRoe_BecomesThisNodesCopy`. ⚠ A node with no brain (SimHost,
IG) holds no `EntityBrainIntent` ingress and no `Roe` — no reader there needs one.

⭐ **`SetReportingEvent` (CC-2) joins by one `case`:** behaviors adds the event (`CE-2291`); the egress forwards it as Kind 3 with its
fields as JSON and the ingress raises it again — the same two places as SetRoe.

## 3. CLASSES

### 3.1 The order path *(HTTP · menu · RTS — one service)*

```mermaid
classDiagram
    direction LR
    class EntityOrders { <<NEW, Hrot.Common>> Move(netId, point, speed) · Attack(netId, targetNetId) · Stop(netId) · Hold(netId) · Behavior(netId, name, json) · Clear(netId) }
    class OrderRefusal { <<NEW>> Unknown · PossessedElsewhere · WrongSide · Dead }
    class AssignTacticalIntentEvent { <<existing>> Entity · IntentId · JsonParams · Origin · + Kind (Assign/Clear) · + ClearsPriorIntent }
    class OperatorOrderOutcome { <<NEW component, brain node>> BehaviorHash · Outcome · FaultCode · FinishedAt }
    class TacticalIntentEgressTranslator { <<existing>> non-owner ⇒ TacticalIntentRequest }
    class TacticalIntentResolutionSystem { <<existing, owner>> mapper or pass-through ⇒ AssignBehaviorEvent }
    class IntentClearRegistry { <<NEW, Entity Commanding §2.5d>> Register(IIntentClearProvider) }
    class MissionControlExecutionSystem { <<existing, + IIntentClearProvider>> ClearFor = CMD_ABORT_ALL's plan clear, no behaviour clear }
    class CombatPosture { <<existing C# BTree>> + designated target (C-5) }
    class OrderRoutes { <<NEW, DebugApiHost>> POST GET DELETE entities/ID/order · POST orders · POST orders/side-lock · GET sitrep }
    class MenuOrderHandlers { <<NEW, MapInteractionPack>> MoveHere · Engage · Stop }
    class RtsCommandTool { <<NEW, ToolController tool>> LMB select · RMB ground = Move · RMB hostile = Attack }
    class IMapPickService { <<existing>> PickLocationAsync · PickEntityAsync }
    OrderRoutes --> EntityOrders
    MenuOrderHandlers --> EntityOrders
    MenuOrderHandlers --> IMapPickService : follow-up pick
    RtsCommandTool --> EntityOrders
    EntityOrders --> AssignTacticalIntentEvent : publishes, Origin=Operator
    EntityOrders ..> OrderRefusal
    AssignTacticalIntentEvent --> TacticalIntentEgressTranslator
    AssignTacticalIntentEvent --> TacticalIntentResolutionSystem
    TacticalIntentResolutionSystem --> IntentClearRegistry : ClearsPriorIntent
    IntentClearRegistry o-- MissionControlExecutionSystem : provider
    TacticalIntentResolutionSystem ..> CombatPosture : Attack
    OrderRoutes ..> OperatorOrderOutcome : GET order reads it
```

*What the picture shows that prose hid:* three front-ends, **one** publish — the RTS tool, the menu and the AI player cannot
drift apart, and every rail written for the HTTP route covers the human paths too.

### 3.2 FPS possession and manual drive

```mermaid
classDiagram
    direction LR
    class PossessionController { <<NEW, station>> Request(netId) · Release() · State: Idle/Requesting/Possessed }
    class PossessionTool { <<NEW, exclusive-focus gizmo>> swallows clicks + menus · Esc = Release }
    class FpsInputSampler { <<NEW, station>> IsKeyDown W/A/S/D · MouseDelta · buttons · wheel ⇒ ControlFrame }
    class FpsCameraDriver { <<NEW>> ICameraDriver: eye / turret pose · free or hull-locked view }
    class ICameraDriver { <<NEW, Vis3D>> Drive(MapCamera3D, input, dt) }
    class MapCamera3D { <<existing, sealed>> + Driver }
    class PossessionHudLayer { <<NEW, IMapLayer.DrawOverlay>> crosshair · health · ammo per mount · selected weapon }
    class PossessRequestEvent { <<NEW FDP event>> Entity · Mode · StationNodeId · IsRemote }
    class OperatorControlFrameEvent { <<NEW FDP event>> Entity · Seq · axes · AimPoint · Trigger · Mount · IsRemote }
    class InteractionTransport { <<existing, R-221>> + Possess · Release · ControlFrame cases — only between nodes }
    class PossessionSystem { <<NEW, brain owner>> arbitrates (C-3) · death ⇒ release }
    class PossessedBy { <<NEW component, Brain group, replicated>> StationNodeId · Mode · Epoch }
    class OperatorInput { <<NEW component, brain-local>> newest ControlFrame }
    class OperatorControl { <<NEW behaviour>> origin Possession (M-7) }
    class ManualDriveExecutor { <<NEW, locomotion>> writes ManualDrive · Nav Mode=Manual once }
    class AimAndFireExecutor { <<existing>> + point target · operator mode (M-8) }
    class ManualDrive { <<NEW component, Brain group>> Forward · Lateral · Steer · Yaw · Gait · Seq }
    class ManualDriveSystem { <<NEW, muscle, GroundKinematicsModule>> person: strafe + yaw · vehicle: throttle + steer }
    class WeaponAmmo { <<NEW descriptor, C-7>> per mount: Ammo · MaxAmmo }
    PossessionController --> PossessRequestEvent : publishes on the local bus
    FpsInputSampler --> OperatorControlFrameEvent : publishes on the local bus
    PossessRequestEvent --> PossessionSystem : owner reads its bus
    OperatorControlFrameEvent --> PossessionSystem
    InteractionTransport ..> PossessRequestEvent : carries when not owned here
    InteractionTransport ..> OperatorControlFrameEvent
    PossessionController --> PossessionTool
    PossessionController --> FpsCameraDriver
    FpsCameraDriver ..|> ICameraDriver
    MapCamera3D o-- ICameraDriver
    PossessionSystem --> PossessedBy : writes
    PossessionSystem --> OperatorInput : writes
    PossessionSystem ..> OperatorControl : assigns / clears
    OperatorControl --> ManualDriveExecutor
    OperatorControl --> AimAndFireExecutor
    ManualDriveExecutor --> ManualDrive
    ManualDrive --> ManualDriveSystem : DDS
    PossessionController ..> PossessedBy : reads (replicated)
    PossessionHudLayer ..> WeaponAmmo
```

*What the picture shows that prose hid:* the station **never writes a unit component** — it publishes FDP events (§2.3), and reads back
`PossessedBy`, health and ammo. Exactly-one-station is enforced by **one writer** (the brain owner), and "station A lost it"
is A reading `PossessedBy` — no revoke message exists to get lost.

## 4. SEQUENCES

### 4.1 An AI player orders an attack over HTTP *(the same path a menu Engage takes after its pick)*

```mermaid
sequenceDiagram
    participant A as AI agent
    participant H as DebugApiHost (any node)
    participant O as EntityOrders
    participant E as TacticalIntentEgress
    participant R as TacticalIntentResolution (brain owner)
    participant C as IntentClearRegistry
    participant I as BehaviorIngressSystem
    A->>H: POST /entities/1000/order {attack:{targetId:2000}}
    H->>O: Attack(1000, 2000)
    O->>O: refuse if PossessedBy other station / wrong side / dead
    O->>E: AssignTacticalIntentEvent(Origin=Operator, ClearsPriorIntent)
    alt this node owns the brain
        O->>R: same event, resolved locally
    else
        E->>R: TacticalIntentRequest over DDS
    end
    R->>C: ClearFor(unit) — mission plan cleared, synchronously
    R->>I: AssignBehaviorEvent(CombatPosture, target=2000, assault)
    A->>H: GET /entities/1000/order
    H-->>A: {behavior, origin, state: running | finished | failed | paused}
```

### 4.2 Menu *Engage* with its follow-up pick

```mermaid
sequenceDiagram
    participant U as User
    participant M as Context menu
    participant G as MenuOrderHandlers
    participant P as IMapPickService
    participant O as EntityOrders
    U->>M: right-click unit → Engage
    M->>G: GlobalActionRequestedEvent(Engage, unit)
    G->>P: PickEntityAsync()
    U->>P: click the enemy (Esc / right-click cancels)
    P-->>G: target network id
    G->>O: Attack(unit, target)
```

### 4.3 FPS — possess, drive, fire, release

```mermaid
sequenceDiagram
    participant S as Station (map host)
    participant B as Brain owner (CGF)
    participant K as Muscle (SimHost)
    S->>B: PossessRequestEvent{FPS} on the bus, translated only if B is another node
    B->>B: PossessionSystem: PossessedBy = S · assign OperatorControl (origin Possession) — task paused
    B-->>S: PossessedBy replicates
    S->>S: PossessionTool + FpsCameraDriver + HUD on · cursor locked
    loop every frame
        S->>B: OperatorControlFrameEvent{seq, forward, lateral, yaw | steer, aim point, trigger, mount}
        B->>B: OperatorControl ⇒ ManualDrive · fire via AimAndFireExecutor (operator mode)
        B->>K: ManualDrive / WeaponTask (DDS)
        K->>K: ManualDriveSystem moves the body · turret slews
        K-->>S: SimTransform, TurretPose, Health, WeaponAmmo
    end
    alt Esc
        S->>B: ReleasePossessionEvent
    else unit killed (Health ≤ 0)
        B->>B: PossessionSystem releases on its own
    else the station's node departs (Q91 §4-F, CE-2231)
        B->>B: NodeDeparted{S} ⇒ PossessionSystem releases every unit S holds (no timer, R-4001)
    end
    B->>B: clear OperatorControl ⇒ the paused task resumes (R-203)
    B-->>S: PossessedBy cleared ⇒ station leaves FPS, camera stays where it was
```

## 5. MODULES — who registers what, and who calls it each frame

```mermaid
graph TD
    subgraph Station["Every map host — MapInteractionPack.Build / AttachMapLayers"]
        MH[MenuOrderHandlers]:::new
        RT[RtsCommandTool]:::new
        PC[PossessionController + PossessionTool]:::new
        CD[FpsCameraDriver via MapCamera3D.Driver]:::new
        HUD[PossessionHudLayer]:::new
    end
    subgraph Api["DebugApiHost — editor, and each cluster perspective"]
        OR[OrderRoutes + /sitrep]:::new
    end
    subgraph Brain["Brain node — CgfLogicPack (CGF; the editor composes it solo)"]
        TIR[TacticalIntentResolutionSystem]
        PS[PossessionSystem]:::new
        BTS[BrainTickSystem ⇒ OperatorControl / CombatPosture]
    end
    subgraph Muscle["Muscle node — SimHost GroundKinematicsModule"]
        MDS[ManualDriveSystem]:::new
    end
    RB[ReplayBrowser map host]:::dead
    ST[Stride motors]:::dead
    MH --> EO[EntityOrders]:::new
    RT --> EO
    OR --> EO
    EO --> TIR
    PC --> PS
    PS --> BTS
    BTS --> MDS
    RB -. no live brain .-> EO
    ST -. not wired in v1 .-> MDS
    classDef new fill:#dff5e1,stroke:#2e7d32
    classDef dead fill:#fde2e2,stroke:#c62828,stroke-dasharray:4 3
```

*In the **editor** the three subgraphs are ONE world* — the station, the brain and the muscle exchange the §2.3 events and
components on one bus, with no translator. *On a cluster* each arrow that crosses a subgraph is an egress → DDS → ingress pair.
*The red edges are the ones nothing reaches:* the **ReplayBrowser** also builds `MapInteractionPack`, but a replay has no live
brain — orders, RTS and possession are **absent** there (not greyed). The **Stride** motors take `VehicleState`/`CrowdMotorIntent`
as their inputs (`KinematicVehicleMotor.cs:184`, `BulletCharacterMotor.cs:211`) and will need their own `ManualDrive`
consumer — not v1. *Who calls each frame:* the canvas' `Update` drives the station items; `BrainTickSystem` ticks
`OperatorControl`; `GroundKinematicsModule` (PostSimulation, before `CarKinematicsSystem`) runs `ManualDriveSystem`.

## 6. Manual motion *(M-1 … M-5)*

| | person | ground vehicle |
|---|---|---|
| **keys** | W/S forward/back · A/D strafe · Shift sprint · C crouch · Z prone | W throttle · S brake → reverse · A/D steer (free look) |
| **mouse X** | turns the body (camera yaw = body yaw) — on the spot when no key is held | looks around (free) · **steers when `V` locks the view to the hull** |
| **mouse Y** | aim pitch (camera only) | look / turret elevation |
| **`ManualDrive` fields** | `Forward`, `Lateral` ∈ [−1,1] · `Yaw` (absolute) · `Gait` | `Forward` = throttle ∈ [−1,1] · `Steer` ∈ [−1,1] |
| **muscle rule** | velocity = R(yaw)·(Forward, Lateral·0.6, back ·0.6) × gait speed × stance cap (`CarKinematicsSystem.cs:146`); facing turns toward `Yaw` at `HumanGait.TurnRate`; a step onto unwalkable navmesh slides or stops (M-5) | accel / brake from `VehicleParams`, steer angle slews to `Steer·MaxSteerAngle` at `MaxSteerRate` (set by the TKB, never enforced today); `BicycleModel` integrates — with reverse allowed |
| **leaves** | the crowd agent (`CrowdAgentUpdateSystem` would fight it) | Pure Pursuit (not run in `Manual`) |
| **turret** | — | mouse aim point ⇒ `WeaponTask{Track, Point}`; the muscle slews at the TKB traverse rate |

⚠ **Today's person model cannot strafe** (`vel.Linear = fwd * Speed`, `CarKinematicsSystem.cs:410`) and **nothing can
reverse** (`speed ≥ 0` clamps, `BicycleModel.cs:179`, `HumanGait.cs:66`) — M-2/M-3 add both inside the `Manual` branch only;
autonomous motion is unchanged.

## 7. SLICES

| slice | builds | acceptance (headless unless marked; ⭐ each slice railed FIRST in the networkless editor world, then on the cluster — §2.3) |
|---|---|---|
| **S1** `CE-3167` + `CE-3168` | `EntityOrders`; `ClearsPriorIntent` + `IntentClearRegistry` + `MissionPlanClearProvider` (Entity Commanding §2.5b-d); `POST/GET/DELETE /entities/{id}/order`, `POST /orders`, `GET /sitrep?side=&truth=`; the side lock; **`/entities/command` fixed** — field-aware options (the CE-225 shape, `DebugApiService.cs:1546`) + an `Entity` given as a network id | an HTTP move reaches the brain from a non-owner node · an order clears the mission plan and never leaves the unit brain-dead · `GET …/order` reports running → finished · the sitrep lists only contacts the side knows · a `/entities/command` payload's fields arrive (the rail that was missing, `DebugApiBatch04Tests.cs:168`) |
| **S2** `CE-3169` + `CE-3170` | the three menu handlers + follow-up picks on every map host; the location picker's **cancel completes the task** (today it never does, `FdpLocationPickerGizmo`); Attack = CombatPosture with a designated target | Engage → pick → the unit closes and fires on THAT target, ignoring a nearer threat · Esc cancels the pick · Stop idles it · IG and CGF menus work, the replay menu has no orders |
| **S3** `CE-3171` | `RtsCommandTool` (2-D and 3-D picks) | RMB ground moves the selection · RMB hostile attacks · menus suppressed while active · Esc leaves |
| **S4** `CE-3172` + `CE-3173` | possession interactions, `PossessionSystem`, `PossessedBy`, `BehaviorOrigin.Possession`, `OperatorControl`, operator fire (M-8), `ManualDrive` + `KinematicsMode.Manual` + `ManualDriveSystem`; **HTTP `POST /entities/{id}/possess` + control frames** so it is railable before any camera exists | a person strafes and turns on the spot · a vehicle steers and reverses · a second station takes over · death releases · release resumes the paused task · ROE `HoldFire` does not stop operator fire |
| **S5** `CE-3174` | `ICameraDriver` + `FpsCameraDriver`, cursor lock, `PossessionTool`, `PossessionHudLayer`, `WeaponAmmo` descriptor; "Possess ▸ FPS / RTS" on the unit menu | the scenario camera is unchanged after an FPS session · the HUD shows the unit's ammo from a non-brain station · **(V)** an Xvfb screenshot of the FPS view + HUD |

## 8a. S3 DETAIL — the RTS command tool (`CE-3171`, designed `2026-10-11` after the prior-art sweep)

📐 **What the sweep measured, and what it decides:**

| fact | where | so |
|---|---|---|
| an EXCLUSIVE-focus gizmo already suppresses every map context menu (entity and canvas) | `GizmoMap…/DebugGizmoLayer.cs` :160 / :265 / :576 | "menus suppressed while active" needs **no new code** — the tool is exclusive |
| …and it suppresses the map's own LMB selection and the rubber band too | `DebugGizmoLayer.cs` :207 / :244; `SelectionInteractionSystem.cs:84` | the tool does its own click-select — **through `SelectionChangeRequest`**, the one write path (UX_Feature_Selection §2.7.3 rule 1), ⛔ never a second store |
| in 3-D the gizmo's `worldPos` is `MapCamera3D.Pick` (entity box → terrain → ground) and an entity hit lands ON the entity | `Vis2D/Layers/DebugGizmoLayer.cs:230`; `MapCamera3D.cs:257` | one hit test serves both views: `MapCanvas.PickTopmostEntity(xy)` |
| the canvas hit test lives in the station's `CanvasMapPickAdapter` | `CanvasMapPickAdapter.cs:101` | ⭐ `IMapPickService.EntityAt(xy)` (a default-`-1` interface member) — the tool reaches it through the pick the hosts already hand `MenuOrders` (§11): **no new per-host wiring** |
| tools register on the pack's `ToolController`; `ToolDescriptor` + an activation delegate | `ToolController.cs:78`; `ScenarioToolRegistrations.cs:107` (Measure — the analogue) | the pack registers `ScenarioToolIds.RtsCommand` on every live host |
| no toolbar exists outside the Editor; the every-host menu is the map's **View** main menu | `LayerControlGizmo.cs:154` | "View ▸ RTS command mode" (`GlobalActionIds.ToggleRtsCommand` = 254), only where `OrdersOffered` is set |

```mermaid
classDiagram
    direction LR
    class MapInteractionPack { <<existing>> registers RtsCommand on Tools + action 254 }
    class ToolController { <<existing>> Activate(RtsCommand) · Esc ends }
    class RtsCommandGizmo { <<NEW, exclusive + raw>> LMB-up · RMB-down · Shift · Esc }
    class RtsCommands { <<NEW, Hrot.Presentation>> Select(xy, additive) · Command(xy) }
    class IMapPickService { <<existing>> + EntityAt(xy) : netId (default -1) }
    class CanvasMapPickAdapter { <<existing>> EntityAt = canvas.PickTopmostEntity }
    class SelectionChangeRequest { <<existing, the one write>> Replace · Add · Clear }
    class EcsSelectionState { <<existing>> SelectedEntities }
    class EntityOrders { <<existing>> Move · Attack }
    MapInteractionPack --> ToolController
    ToolController ..> RtsCommandGizmo : arms
    RtsCommandGizmo --> RtsCommands
    RtsCommands --> IMapPickService : hit test
    CanvasMapPickAdapter ..|> IMapPickService
    RtsCommands --> SelectionChangeRequest : LMB
    RtsCommands ..> EcsSelectionState : reads the selection
    RtsCommands --> EntityOrders : RMB, one order per selected unit
```

```mermaid
sequenceDiagram
    participant U as User
    participant G as RtsCommandGizmo
    participant R as RtsCommands
    participant P as IMapPickService
    participant O as EntityOrders
    U->>G: View ▸ RTS command mode (tool armed, menus off)
    U->>G: LMB on a unit (Shift = add)
    G->>R: Select(xy, additive)
    R->>P: EntityAt(xy)
    R-->>R: publish SelectionChangeRequest
    U->>G: RMB on the ground
    G->>R: Command(xy)
    R->>P: EntityAt(xy) = none
    R->>O: Move(unit, point) for every selected unit
    U->>G: RMB on a hostile
    R->>O: Attack(unit, hostile) for every selected unit
    U->>G: Esc
    G-->>U: tool ends, menus back
```

*What the pictures show that prose hid:* the tool owns **no** state of its own — selection is the map's, orders are
`EntityOrders`', the hit test is the station's pick. It is a router from three mouse gestures to three existing writes.

| ⭐ rule | |
|---|---|
| **hostile** = the clicked unit's `ForceId` differs from the ordering unit's and is not `Neutral` | a friendly or neutral under the cursor is a MOVE to its position (v1) |
| each selected unit gets its own order; a refused one (dead, other side under the side lock) is skipped | the same `EntityOrders` checks the HTTP route makes |
| ⚠ v1 has **no box select** inside the tool | the rubber band is suppressed with the menus; click / Shift-click select only. A box would be a second marquee — deferred, not duplicated |

## 8b. S4 DETAIL — possession and manual drive (`CE-3172` + `CE-3173`, designed `2026-10-11` after the inventory sweep)

📐 **What the inventory measured, and what it decides** *(file:line in the sweep's report, summarised)*:

| fact | where | so |
|---|---|---|
| rank is `Sop 1 · Superior 2 · everything else 3`; a Reaction PAUSES a non-SOP task into `PausedTask`; an order of any other origin DROPS the paused task | `BehaviorOrigin.cs:52`; `BehaviorIngressSystem.cs:238-260`, `:388-401` | `Possession` = rank **4**, and it behaves like a reaction toward the running task (**pause**), never an order (**no drop**); a Reaction over a running Possession is **refused** |
| a reaction resumes the paused task by clearing itself with `Self`, or by finishing (`BrainTickSystem.cs:445-453`) | `BehaviorIngressSystem.cs:131`, `:442` | releasing possession = a **clear with origin Possession that resumes** (the same `ResumePausedTask`) |
| the motion request crosses as `NavigationIntent` (Brain), applied by the bridge only on an `IntentId` change; `KinematicsMode.None` is driven as Direct | `NavigationIntentBridgeSystem.cs:127`, `CarKinematicsSystem.cs:226` | M-1 holds: `NavigationMode.Manual` entered ONCE, and a separate `ManualDrive` component carries the per-frame axes |
| a person's velocity is `fwd × speed` (no lateral); `HumanGait` and `BicycleModel` clamp speed ≥ 0 | `CarKinematicsSystem.cs:410`; `HumanGait.cs:66`; `BicycleModel.cs:34` *(the design's `:179` was wrong)* | the Manual branch is its OWN integrator — autonomous motion is untouched |
| `AimAndFireParams` has no point; `RoePermitsFire` knows no origin; the turret task already aims at `Point` | `AimAndFireExecutor.cs:35`, `:330`; `WeaponTaskDispatcherSystem.cs:53` | M-8 (S4d): a point target + an operator flag in the SAME executor |
| a descriptor joins a group in 11 steps (`dtWeaponTask`, `ed3434b4a`) | `HrotOwnershipGroups`, `NedOwnershipGroupBinding`, `AllDescriptors`, `SharedTranslatorPack` | S4e: `ManualDrive` (Brain) and `PossessedBy` (Brain) descriptors |

```mermaid
classDiagram
    direction LR
    class BehaviorOrigin { <<existing enum>> + Possession = 5, rank 4 }
    class BehaviorIngressSystem { <<existing>> Possession pauses · reactions refused under it · its clear resumes }
    class PossessionSystem { <<NEW, brain owner>> Possess · Release · frames · death releases }
    class PossessedBy { <<NEW component, Brain>> StationNodeId · Mode · Epoch }
    class OperatorInput { <<NEW component, brain-local>> the newest frame by Seq }
    class OperatorControl { <<NEW C# BTree>> ManualControl node }
    class ManualDrive { <<NEW component, Brain>> Forward · Lateral · Steer · Yaw · Gait · Seq }
    class NavigationIntent { <<existing>> Mode = Manual, once }
    class CarKinematicsSystem { <<existing>> + the Manual branch }
    class EntityOrders { <<existing>> refuses a unit another station possesses }
    class PossessRoutes { <<NEW, DebugApiHost>> POST/DELETE entities/ID/possess · POST entities/ID/control }
    PossessRoutes --> PossessionSystem : FDP events
    PossessionSystem --> PossessedBy : the one writer
    PossessionSystem --> OperatorInput
    PossessionSystem ..> BehaviorIngressSystem : AssignBehaviorEvent(OperatorControl, Possession)
    OperatorControl --> ManualDrive : copies the axes
    OperatorControl --> NavigationIntent : Manual, once
    ManualDrive --> CarKinematicsSystem : the muscle reads it
    EntityOrders ..> PossessedBy : C-3
```

```mermaid
sequenceDiagram
    participant S as Station (HTTP / FPS)
    participant P as PossessionSystem (brain owner)
    participant I as BehaviorIngressSystem
    participant C as OperatorControl
    participant K as CarKinematicsSystem (muscle)
    S->>P: PossessRequestEvent{unit, station}
    P->>P: PossessedBy = station (another station's hold is taken over)
    P->>I: AssignBehaviorEvent(OperatorControl, origin Possession)
    I->>I: the running task → PausedTask
    loop each frame
        S->>P: OperatorControlFrameEvent{seq, forward, lateral, yaw | steer}
        P->>P: OperatorInput = newest seq
        C->>C: ManualDrive ← OperatorInput · NavigationIntent.Mode = Manual (once)
        K->>K: person: R(yaw)·(fwd, lat) · vehicle: throttle + steer, reverse
    end
    S->>P: ReleasePossessionEvent (or the unit dies)
    P->>I: ClearBehaviorEvent(origin Possession)
    I->>I: ResumePausedTask — the earlier task runs again
```

*What the pictures show that prose hid:* **one writer per thing** — `PossessedBy` only `PossessionSystem`, `ManualDrive` only the
`OperatorControl` behaviour (on the brain), the body only `CarKinematicsSystem` (on the muscle). Possession is a behaviour of a
new origin, ⛔ not a switch that turns the brain off (R-270).

| step | builds | its rails |
|---|---|---|
| **S4a** | `BehaviorOrigin.Possession` (5, rank 4): pauses like a reaction, never drops the paused task, refuses reactions over it, its own clear resumes | the ingress suite |
| **S4b** | `ManualDrive` (Brain) + `NavigationMode.Manual` → `KinematicsMode.Manual` + the Manual branch: person body-frame forward/strafe (back and strafe at 60 %), body yaw turned toward `Yaw` at `HumanGait.TurnRate`; vehicle throttle (− brakes, then reverses) + steer at `MaxSteerRate` | the kinematics suite |
| **S4c** | `PossessionSystem`, `PossessedBy`, `OperatorInput`, `OperatorControl`, the three FDP events, HTTP `possess` / `control` / release, `EntityOrders` refuses a unit held by another station | the networkless editor, end to end |
| **S4d** | operator fire: `AimAndFireParams` point + operator flag (no seen-now, no aim time, ROE bypass, C-2) | the combat suite |
| **S4e** | `ManualDrive` + `PossessedBy` descriptors; `Possess` · `Release` · `ControlFrame` interaction cases | the cluster |

⚠ ~~**Deferred, stated:** the navmesh wall check (M-5) lands with S4b only if the muscle's `INavmeshProvider` is reachable there;
otherwise it is its own row.~~ SUPERSEDED — it was reachable (a world singleton); built as `CE-3181`, §13.4. Aircraft (M-4) stay deferred.

## 8c. S4e DETAIL — possession on a cluster (`CE-3172` / `CE-3173`, designed `2026-10-11` from the measured templates)

📐 **Templates measured, both reused unchanged:** a hull component in the Brain group replicated to every node =
`dtWeaponPosture` (ui's WL-6: `AllDescriptors` · `SimDescriptors` · `HrotOwnershipGroups` · `NedOwnershipGroupBinding` · an egress +
ingress translator pair in `SharedTranslatorPack`, the ingress also in `EntityStatesIngressPack` for the pure IG); a request to an
entity this node does not own = the R-221 interaction topic (`InteractionCodecs`: one codec per FDP event, the transport and the
topic do not change).

```mermaid
sequenceDiagram
    participant St as Station node (editor / IG)
    participant Br as Brain node (CGF)
    participant Mu as Muscle node (SimHost)
    St->>St: EntityOrders.Possess → PossessRequestEvent (local bus)
    St->>Br: EntityInteractionRequest{Possess, StationNodeId} (InteractionCodecs.Possess)
    Br->>Br: PossessRequestEvent{IsRemote} → PossessionSystem → PossessedBy, OperatorControl
    Br-->>St: dtPossessedBy (every node — the station learns it holds the unit)
    loop each frame
        St->>Br: EntityInteractionRequest{ControlFrame, seq, axes, aim, trigger}
        Br->>Br: OperatorInput (newest seq) → OperatorControl → ManualDrive, weapon action
        Br-->>Mu: dtManualDrive · dtNavigationIntent (Manual) · dtWeaponTask (a turret)
        Mu->>Mu: CarKinematicsSystem Manual branch · the turret muscle
    end
    St->>Br: EntityInteractionRequest{Release}
    Br-->>St: dtPossessedBy disposed — the task resumes
```

*What the picture shows that the prose hid:* the station never writes the unit — every arrow INTO the brain is a request, every
arrow OUT is replicated state, so the same code runs in the editor (one node) and on a cluster (three).

| new | id | group / case | why |
|---|---|---|---|
| `dtManualDrive` → `EntityManualDrive` | descriptor **128** | Brain (written by `OperatorControl`, read by the muscle) | M-1: per-frame axes cross to the muscle |
| `dtPossessedBy` → `EntityPossessedBy` | descriptor **129** | Brain (`PossessionSystem` the one writer), ingress on the pure IG too | C-3: every station sees who holds a unit; no revoke message |
| `Possess` · `ReleasePossession` · `ControlFrame` | interaction kinds **5 · 6 · 7** | the R-221 topic | after the reserved 2-4; the station's node id rides the payload |
| ⚠ `OperatorInput` | — | never sent | brain-local, filled from the frames |

⚠ **Stated, not hidden:** control frames ride a Reliable/KeepAll topic. M-6's *newest seq wins* makes a late or doubled frame harmless; a
lossy KeepLast topic per station would cut latency under load and is a later tuning, not a correctness change.

## 8d. S5 DETAIL — the FPS station: camera, controls, HUD (`CE-3174`, designed `2026-10-11` after the inventory)

📐 **INVENTORY** — what exists, and what it decides *(queries: `search_graph` `.*(InputProvider|CameraDriver|Hud).*` Class → 5
hits, no camera driver and no HUD exists; grep of `: IInputProvider` → 1 production + 3 test implementers; `AttachMapLayers(` → 5
hosts)*:

| fact | where | so |
|---|---|---|
| the canvas hands the WHOLE input provider to the camera BEFORE any layer, and a consumed input skips every click and context menu | `MapCanvas.cs:253`, `:256-270` | ⭐ **a camera driver that consumes input IS the exclusive focus** — §3.2's separate `PossessionTool` is not needed (rejected below) |
| every map host attaches its 3-D view through ONE function | `MapInteractionPack.AttachMapLayers` (5 callers) | the station layer is attached THERE — no per-host wiring (CE-1033's rule) |
| `MapCameraLayer` writes the scenario camera once the view is still | `MapCameraLayer.cs:149-178` | it must not save while a driver drives (M-9, acceptance "the scenario camera is unchanged") |
| one production input provider; `IInputProvider` has no cursor control | `RaylibInputProvider` | `SetCursorLocked` as a DEFAULT interface member — the test fakes need no change |
| the eye height of a person by posture | `TerrainWorldLosStrategy.EyeHeightFor` + `LogicalStance.Of` | the FPS eye is the same eye perception uses |
| a person vs a vehicle | `VehicleParams.Class == Pedestrian` (`CarKinematicsSystem.cs:327`) | the driver's axes: strafe + yaw, or throttle + steer |
| ammunition lives only on the brain (`WeaponState`, mount children) | no ammo on the wire (`SimDescriptors.cs`) | C-7: a NEW `WeaponAmmo` (Brain group) mirrored from `WeaponState` |
| health already crosses (`dtEntityDamage` → `Health`) | `SimDescriptors.cs:51` | the HUD reads `Health` as is |
| free action ids 255–259 on every lane | `GlobalActionIds.cs` (census) | `Possess` = 255 |

```mermaid
classDiagram
    direction LR
    class ICameraDriver { <<NEW, Vis3D>> Update(cam, dt) · HandleInput(cam, input) bool }
    class MapCamera3D { <<existing>> + Driver }
    class IInputProvider { <<existing>> + SetCursorLocked(bool) default no-op }
    class MapCameraLayer { <<existing>> no save while a driver drives }
    class PossessionStation { <<NEW IMapLayer, every map host>> watches PossessedBy · Enter / Leave FPS · HUD overlay }
    class FpsCameraDriver { <<NEW>> eye on the unit · mouse look · samples WASD, wheel, LMB, V, Esc }
    class EntityOrders { <<existing>> Possess · Unpossess · Control · StationFor }
    class PossessedBy { <<existing, replicated>> }
    class WeaponAmmo { <<NEW component, Brain group>> per mount Ammo · Max }
    class WeaponAmmoMirrorSystem { <<NEW, brain>> WeaponState ⇒ WeaponAmmo on change }
    class MenuOrders { <<existing>> + Possess (255) }
    FpsCameraDriver ..|> ICameraDriver
    MapCamera3D o-- ICameraDriver
    PossessionStation --> FpsCameraDriver : creates on Enter
    PossessionStation ..> PossessedBy : reads
    PossessionStation --> EntityOrders
    FpsCameraDriver --> PossessionStation : frames, release
    PossessionStation ..> WeaponAmmo : HUD reads
    WeaponAmmoMirrorSystem --> WeaponAmmo : the one writer
    MenuOrders --> EntityOrders : Possess
```

*What the picture shows that the prose hid:* the station never decides it is in FPS — it **follows the replicated `PossessedBy`**, so a
possess from the menu, from HTTP, or a take-over by another station all land the same way, and a lost unit leaves FPS by itself.

```mermaid
sequenceDiagram
    participant U as Operator
    participant M as MenuOrders
    participant B as Brain (PossessionSystem)
    participant P as PossessionStation (layer)
    participant D as FpsCameraDriver
    U->>M: unit menu › Possess (FPS)
    M->>B: EntityOrders.Possess (the bus, the interaction topic off-node)
    B-->>P: PossessedBy = this station (replicated)
    P->>P: remember the view (pose, 2-D/3-D) · switch to 3-D · Camera3D.Driver = D
    loop each frame
        D->>D: HandleInput: mouse look · WASD / wheel / LMB · cursor locked
        D->>B: EntityOrders.Control(frame)
        D->>D: Update: eye = unit + eye height, the look
        P->>P: DrawOverlay: crosshair · health · ammo per mount
    end
    U->>D: Esc
    D->>B: EntityOrders.Unpossess
    B-->>P: PossessedBy removed
    P->>P: Driver = null · cursor unlocked · the remembered view restored (never saved)
```

```mermaid
graph TD
    AML[MapInteractionPack.AttachMapLayers — every map host] --> PS[PossessionStation layer]
    AML --> MCL[MapCameraLayer]
    CAN[MapCanvas — every frame] -->|Camera.Update / HandleInput| CAM[MapCamera3D]
    CAM -->|when Driver set| FPS[FpsCameraDriver]
    CAN -->|layer Update / DrawOverlay| PS
    MIP[MapInteractionPack.Build] --> MO[MenuOrders + Possess 255]
    CGF[CgfLogicPack — brain hosts] --> WAM[WeaponAmmoMirrorSystem]
    WAM -->|dtWeaponAmmo| PS
```

*Caption:* every arrow into the station comes from code that already runs on every map host each frame — the canvas and the shared
attach — so there is no host on which FPS could be silently missing.

| new | id | why |
|---|---|---|
| `WeaponAmmo` component | **365** | per mount (up to 4) ammo + max, Brain group |
| `dtWeaponAmmo` → `EntityWeaponAmmo` | descriptor **130** | C-7 |
| `GlobalActionIds.Possess` | **255** | the unit menu item |

**Rejected — one line each:**
- *A separate `PossessionTool` (exclusive gizmo)* — the camera already takes input before every layer (`MapCanvas.cs:253`); a second
  focus owner would duplicate what consuming in the driver does.
- *The station entering FPS from the menu click* — then HTTP possession and take-overs would not show; following `PossessedBy` covers all.
- *A second camera class* — M-9: gizmos, labels and pickers draw only for `MapCamera3D`.
- *Ammo read from the brain's `WeaponState` over HTTP* — a station on another node has no `WeaponState` (C-7).

⚠ **Deferred, stated:** a vehicle's turret-mounted eye (the view from the turret pose) — v1 puts the eye on the hull; the hull-locked
`V` view steers with the mouse as M-3 says. The `(V)` screenshot acceptance needs a windowed run.

## 8e. S6 DETAIL — the operator's weapons: a click is one shot, weapon and ammo keys, the HUD (`CE-1078` · `CE-1079` · `CE-1080`, designed `2026-10-14`, ui)

🔒 **User, `2026-10-14`:** *"when possessing the tank i would expect the turret to try to follow the FPS camera aiming direction and when
i press fire trigger the tank tries aiming to the clicked target and fire once the barrel is aimed … When possessing the soldier having
grenades and rifle, i need to switch the weapon/ammo to use when firing, defaulting to the main weapon; same for tanks - switching between
MG/cannon weapon and for each weapon selecting ammo and see in the HUD what is selected, cycling weapon/ammo using dedicated keys."* —
the leans below: *"approved"*.

📐 **INVENTORY** *(queries: `search_graph .*(Ammo|Round)(Choice|Select|Loadout|Request).*` → 0 production hits; `.*(Weapon|Mount)(Select|Choice|Cycle).*` →
`WeaponChoice`, `WeaponSelectionDecision` — the AI's mount choice only; grep `PickFilter.Terrain` in the station → 1)*:

| fact | where | so |
|---|---|---|
| trigger up ⇒ the turret task is `Track` at the crosshair point — the turret already follows the look | `AimAndFireExecutor.TurretPath` (operator `atPoint`), rail `CE3172` | ⭐ CE-1078 builds on it; no new turret code |
| rounds are granted only WHILE the trigger is held, and the muscle fires only `OnTarget` ⇒ a click shorter than the slew fires nothing | `AimAndFireExecutor.cs:421-433`, `WeaponTaskDispatcherSystem.cs:84` | ⛔ the defect CE-1078 fixes |
| the brain keeps only the NEWEST frame | `PossessionSystem` (`f.Seq <= …Seq ⇒ skip`) | ⛔ a press and release inside one brain tick vanish ⇒ presses travel as COUNTERS |
| the crosshair picks terrain only | `FpsCameraDriver.AimPoint` | ⛔ aiming at a tank aims at the ground behind it (CE-1080) |
| the wheel cycles mounts; mount 0 is the default | `FpsCameraDriver.Mount`, `NextMount` | ✅ kept |
| a mount's rounds: `AmmoLoadout` (≤ 4 types, `Loaded`, `SwitchRemaining` = one reload); only the AI's `AmmoChoice.Choose` switches | `AmmoChoice.cs:108-123` | ⭐ CE-1079 adds the operator's switch beside it (same reload cost — D-I) |
| the HUD mirror carries totals and magazines, no round type | `WeaponAmmo` (`dtWeaponAmmo`) | ⭐ grows (Missiles & Ammo §1 already said it would) |
| `AimAndFireParams` is full (32 bytes) and the operator mode leaves `Target` and `Rounds` unused | `AimAndFireParams.cs` | ⭐ they keep their meaning: the shot's target, and a round cap |

```mermaid
classDiagram
    direction LR
    class FpsCameraDriver { <<existing, grows>> keys 1-4 · F ammo · LMB press = ShotSeq++ and ShotTarget · RMB = CancelSeq++ · pick All, not the own body }
    class OperatorControlFrameEvent { <<existing, grows>> + ShotSeq · CancelSeq · AmmoSeq (bytes, wrap) · ShotTarget (network id) }
    class ControlFrameInteractionPayload { <<wire, appended>> + the same four }
    class OperatorInput { <<existing, grows>> + the same four }
    class OperatorControlBehavior { <<existing, grows>> the LATCH: a new ShotSeq locks target or point, caps the rounds at fired + 1 · AmmoSeq cycles the round · single-shot weapons ignore a held trigger }
    class ManualControlState { <<existing, grows>> seen counters · Pending · PendingTarget · PendingPoint · Cap · ActionId }
    class AimAndFireExecutor { <<existing, grows>> operator: aim at Target when set · Rounds = a cap, never Success · a round switch holds }
    class AmmoChoice { <<existing, grows>> + Cycle(world, mountEntity, steps, reloadS) · Switching(world, mountEntity) }
    class WeaponBrainProfiles { <<existing, grows>> + FiresPerPress(category) }
    class WeaponAmmo { <<existing, grows>> + Load0..3 (the loaded round) · LoadRounds0..3 · Busy (reload / switch bits) }
    class EntityBoxPicker { <<existing>> skips the first-person unit }
    class PossessionStation_Hud { <<existing, grows>> names · loaded round · state · the locked target · the barrel marker }
    FpsCameraDriver --> OperatorControlFrameEvent
    OperatorControlFrameEvent ..> ControlFrameInteractionPayload : off-node
    OperatorControlFrameEvent --> OperatorInput : PossessionSystem
    OperatorControlBehavior ..> OperatorInput : reads
    OperatorControlBehavior *-- ManualControlState
    OperatorControlBehavior --> AimAndFireExecutor : AimAndFireParams
    OperatorControlBehavior ..> AmmoChoice : Cycle
    OperatorControlBehavior ..> WeaponBrainProfiles : FiresPerPress
    AimAndFireExecutor ..> AmmoChoice : Switching, LoadedAmmo
    WeaponAmmo <.. PossessionStation_Hud : reads
```

*What the picture shows that prose hid:* nothing new crosses the brain/muscle line — the latch lives in the OPERATOR BEHAVIOUR (brain), the
executor only learns two meanings it already had room for (a target, a round cap), and the turret muscle is untouched.

```mermaid
sequenceDiagram
    participant D as FpsCameraDriver (station)
    participant P as PossessionSystem (brain)
    participant O as OperatorControl (brain)
    participant X as AimAndFireExecutor (brain)
    participant M as WeaponTaskDispatcher (muscle)
    D->>D: LMB press, pick under the crosshair (a unit or the ground)
    D->>P: frame ShotSeq+1, ShotTarget, Trigger, AimPoint
    P->>O: OperatorInput (newest frame, counters inside)
    O->>O: ShotSeq moved, so Pending, target or point locked, Cap = fired + 1
    loop until the round leaves
        O->>X: Operator, Target = locked, Trigger 1, Rounds = Cap
        X->>M: WeaponTask Engage at the target, grant 1
        M->>M: slew, fire when OnTarget
        M-->>X: WeaponTaskStatus RoundsFired
    end
    X-->>O: RoundsFired reaches Cap
    O->>O: Pending cleared
    O->>X: Target none, Point = crosshair, Trigger = held and automatic
    X->>M: WeaponTask Track at the crosshair (the turret follows the look again)
```

```mermaid
graph TD
    CAN[MapCanvas each frame] -->|Camera.HandleInput| FPS[FpsCameraDriver]
    FPS -->|EntityOrders.Control| BUS[the bus / the interaction topic off-node]
    BUS --> PS[PossessionSystem, Input phase, every brain host]
    PS --> OC[OperatorControl BTree, the possessed unit's root behaviour]
    OC -->|WeaponChannel params| WD[WeaponDispatcherSystem, brain]
    WD --> X[AimAndFireExecutor]
    X -->|EntityWeaponTask| MU[WeaponTaskDispatcherSystem, muscle]
    WAM[WeaponAmmoMirrorSystem, brain] -->|dtWeaponAmmo| HUD[PossessionStation HUD]
```

*Caption:* every box already runs each frame on the hosts that run possession today (§8c) — the change adds no system, so there is no host
on which the latch or the ammo switch could silently not tick.

| decision (✅ user, `2026-10-14`) | rule |
|---|---|
| **D-1 click semantics** | a PRESS locks one shot on what is under the crosshair (a unit is followed as it moves; else the ground point) and fires once the weapon bears; then the turret follows the look again. A new press replaces it; RMB cancels it. Held trigger: automatic weapons (small arms, MG, autocannon, untagged) keep firing at the crosshair; single-shot weapons (tank gun, ATGM, mortar, grenade) fire once per press |
| **D-2 keys** | wheel or `1`–`4` = weapon (mount 0 = the main one, the default) · `F` = the next round of the selected weapon · RMB = cancel the locked shot |
| **D-3 switch cost** | one reload — the AI's rule (D-I); the HUD says `LOADING` |
| **D-4 crosshair** | picks units too (never the own body); the aim point is where the ray meets the unit's box |

**Rejected — one line each:**
- *A separate operator turret system* — the turret task already slews then fires (WL-3).
- *Fire at once wherever the barrel points* — what the user asked to avoid.
- *The trigger as a level the brain edge-detects* — the brain keeps only the newest frame; a short click is lost (counters survive coalescing and the wire).
- *The latch in the station* — the station cannot see when the round left; the brain can, by the executor's own round count.
- *The AI's `AmmoChoice.Choose` for the operator* — the user chooses the round; `Cycle` shares the switch and its cost.
- *A new params field* — 32 bytes are full; `Target` and `Rounds` already mean "who" and "how many".

## 8f. S7 DETAIL — the pilot station: fly a plane by looking, the view banks, a flyer's HUD (`CE-1095`, air hand-off U3, designed `2026-10-14`, ui)

🔒 **User, `2026-10-14`:** the air-operations plan *"Approved"* (R-2015) — its U3: *"the pilot station — `ControlScheme.FixedWing`; the camera
follows the body's attitude (bank and pitch) for both flyers (today yaw only); the HUD per scheme (speed, height, heading; later the bomb
cue)"* ([HANDOFF_Behaviors_To_Ui_Air_Operations.md](blueprints/batches/HANDOFF_Behaviors_To_Ui_Air_Operations.md) U3) → the ui plan → *"Ok go"*.

📐 **INVENTORY** *(read end to end: `PossessionStation.cs`, `FpsCameraDriver`, `MapCamera3D.cs`, `FixedWingModel.Manual`, `RotorcraftModel.Manual`;
Air Operations §6, §6b)*:

| fact | where | so |
|---|---|---|
| the scheme comes from the body's locomotion: Rotorcraft · Wheeled/Tracked ⇒ Vehicle · the rest ⇒ Person — a **FixedWing** body falls to Person (walks, C cycles a stance) | `PossessionStation.cs:354-360` | a fourth scheme |
| the plane's manual model reads the EXISTING frame axes: `Yaw` = the heading looked along, `Vertical` ∈ [−1, 1] = the climb angle asked (× 25°), `Forward` = the throttle, held | `FixedWingModel.Manual` (E5), Air Ops §6b | no wire change: the station only fills the fields |
| the camera has yaw and pitch only; its up vector is derived from them — no roll | `MapCamera3D.cs:86-87, 114-118` | a `Roll` the up vector turns by (FDP `Fdp.Presentation`, cross-lane) |
| the body's attitude is in `SimTransform.Rotation`; `RotorcraftModel.TiltOf(q)` reads its pitch and roll | Air Ops §6 rule, `RotorcraftModel` | the view's bank is read where the model writes it |
| the HUD lists health, weapons, the keys for a person or a vehicle | `PossessionStation.Hud.Lines` | a flyer's lines: speed, height above the ground, heading |

```mermaid
classDiagram
    direction LR
    class FpsCameraDriver { <<existing, grows>> Scheme + FixedWing · HandleInput: a flyer's axes · Place: + camera.Roll }
    class ControlScheme { <<existing enum, grows>> Person · Vehicle · Rotorcraft · + FixedWing }
    class MapCamera3D { <<existing, FDP, grows>> Yaw · Pitch · + Roll · Up turned by Roll }
    class Hud { <<existing, grows>> Lines + FlightLine(speed, height AGL, heading) }
    class OperatorControlFrameEvent { <<existing, unchanged>> Forward · Yaw · Vertical }
    class FixedWingModel { <<existing, backend>> Manual(Yaw, Vertical, Forward) }
    class RotorcraftModel { <<existing, backend>> TiltOf(q) }
    FpsCameraDriver --> ControlScheme
    FpsCameraDriver ..> MapCamera3D : yaw, pitch, roll
    FpsCameraDriver ..> RotorcraftModel : the body's bank
    FpsCameraDriver --> OperatorControlFrameEvent : one a frame
    OperatorControlFrameEvent ..> FixedWingModel : on the muscle
    FpsCameraDriver ..> Hud
```

```mermaid
sequenceDiagram
    participant O as operator (mouse, W / S)
    participant D as FpsCameraDriver (the station)
    participant C as MapCamera3D
    participant M as FixedWingModel.Manual (the muscle)
    O->>D: the mouse moves the LOOK (yaw, pitch), W / S held
    D->>M: frame: Yaw = look yaw, Vertical = look pitch / 25 deg, Forward = W - S
    M->>M: banks toward the look, levels as it comes on, climbs to the angle, holds the throttle
    D->>C: eye on the body, the look's yaw and pitch, Roll = the body's bank
    C-->>O: the horizon tilts as the plane banks into the turn
```

```mermaid
graph TD
    SW[MapViewSwitch / MapCanvas, every map host] --> ST[PossessionStation layer, every map host]
    ST -->|owns while possessed| DRV[FpsCameraDriver]
    DRV -->|OperatorControlFrameEvent, bus to NED| OP[OperatorControlBehavior, the brain]
    OP -->|ManualDrive| AIR[AirMotionModule: FixedWingModel / RotorcraftModel, MuscleGround]
    AIR -->|SimTransform with pitch and roll| DRV
```

*Caption:* the station adds no new message and no new system: the plane's model already reads the frame's axes, so the whole slice is how the
station FILLS them and how its camera SHOWS the body's attitude. ⚠ On a node that only receives the plane, the bank shows only once backend
replicates pitch and roll (`GeoSpatialIngressTranslator` restores the heading only — reported, §6n of the 3-D map design).

| decision (✅ user, `2026-10-14`) | rule |
|---|---|
| **P-1 scheme** | `ControlScheme.FixedWing` for a `FixedWing` body; Person no longer catches it |
| **P-2 controls** | the mouse moves the LOOK, as for everyone; the frame carries `Yaw` = the look's heading, `Vertical` = the look's pitch ÷ 25° (clamped), `Forward` = W − S (the model holds the throttle); no strafe, no stance, no climb keys (the look climbs) |
| **P-3 the view** | the eye on the body (1.5 m above its reference point); the camera looks along the operator's look; its **roll is the body's bank** — for both flyers. ⚠ **Deviation from U3's "bank and pitch":** the camera's PITCH stays the look's, because in mouse-aim the look IS the stick — pitching the view with the nose would chase the aim |
| **P-4 the HUD** | a flyer (plane or helicopter): `SPD 152 m/s  ALT 312 m  HDG 045` (ground speed, height above the ground under it, heading) and its own keys line; the weapons as before |

**Rejected — one line each:** *a stick on the keyboard (pitch / roll keys)* — U3 and B3 chose mouse-aim for a non-expert; *a new frame field
for pitch* — `Vertical` already means it to the model; *the camera locked to the nose* — the aim would be locked too.

**AS-BUILT (`CE-1095`, `2026-10-14`, ui)** — built as the three diagrams above draw it; no box moved. Details they do not carry:

| as-built detail | why |
|---|---|
| `MapCamera3D.Roll` (FDP `Fdp.Presentation`, cross-lane): `Up` is rotated about `Forward`; `Detached` resets it to 0 | the camera had no roll; one property, every map host gets it |
| the bank is `RotorcraftModel.TiltOf(rotation).Roll` for BOTH flyers | the existing decomposition of a body's attitude — no second one |
| `Hud.Lines(…, scheme)`: a flyer's `FlightLine` and its own keys line; the person / vehicle lines unchanged | the scheme picks the lines, so a person's HUD cannot grow flight fields |
| P-3's deviation (the view's PITCH stays the look's) is as argued above | — |

📐 **Rail** `PossessionStationTests.CE1095_*` (the scheme, the frame's axes, the camera's roll, the HUD line) — red-proved twice (the view does
not bank; the plane is a Person).

## 8g. S8 DETAIL — SIGHT or LOCK, the crosshair on the body parts (`CE-1126` · `CE-1127`, designed `2026-10-16`, ui)

🔒 **User, `2026-10-16`:** *"yes, let's use the body part boxes for the crosshair"* · *"for FPS shooter with hand weapons like rifle … it is
natural to shoot in the direction of aim … and letting the ballistics fly the bullet. The locking mode is good for grenade throwing and grenade
launcher and mortar and howitzer and similar. the locking mode should work for buildings and terrain points as well"* · *"locking mode for slow
turrets as well"* · *"Mounted fast weapons: OK"* (a pintle / tripod MG with no turret is SIGHT) · *"RPG-type unguided rockets: SIGHT for
simplicity of the FPS mode"*. The impacts the user asked for in the same exchange are `DESIGN_Visual_Effects.md` §10.

📐 **INVENTORY** *(graph `search_graph` Class `.*BodyPart.*` → `BodyPartHit`, `BodyPartHitResult`; `.*BodyRay.*`, `.*FireMode.*`, `.*AimMode.*` → 0;
`.*Picker.*` → one 3-D entity picker, `EntityBoxPicker`; `IPicker3D` implementations by grep → `TerrainPicker`, `EntityBoxPicker`)*:

| the lean rests on | code — how it IS | design — how it was MEANT to be |
|---|---|---|
| the crosshair meets ONE coarse box per entity (`MinPickHalf` 0.4 m), not the parts a round meets | `EntityBoxPicker.cs:47` → `EntityBodyLayer3D.TryGetBox` | §8e D-4 *"where the ray meets the unit's box"*; Map 3D §3.8 (M17) — the box for SELECTION |
| a round meets a person by `PersonBody` boxes and a vehicle by `VehicleBody` boxes, both generated from the drawing with parity rails | `RaycastSolverSystem.cs:219-262` | hit location HL-1, HL-9, §9 S2 (`CE-3209`, `CE-3211`) |
| that per-entity part test is inline in the solver's parallel loop — no function another caller can use | `RaycastSolverSystem.cs:187-263` | ⛔ searched `docs/` + `.dev/`, none found |
| the 1.5 m `AimPoint.Operator` fallback exists only because the coarse box and the parts disagree | `AimPoint.cs` (`CE-1125`) | §13.7a |
| a press ALWAYS latches (locks the point or unit, waits for the round) | `OperatorControlBehavior.cs:142-150` | §8e D-1 |
| direct-fire rounds fly straight; arc rounds fall and get an arc solve | `BallisticAim.cs:15`, `BallisticsSystem.cs:138`, `FireProcessingSystem.ArcLaunch` | Indirect Fire IF-1 |
| the turret path already slews to a unit or a ground point | `AimAndFireExecutor.TurretPath` | §8e |

```mermaid
classDiagram
    direction LR
    class BodyPartRay { <<new, Fdp.Toolkits Physics>> +TryHit(world, entity, a, b, turretPart, out BodyPartHitResult) bool +HasParts(world, entity) bool }
    class RaycastSolverSystem { <<existing, routed>> narrow phase calls BodyPartRay for persons and vehicles }
    class BodyPartHit { <<existing>> Segment · SegmentTouchesSphere }
    class PersonBody { <<existing>> Boxes(pose) · PoseOf · ScaleOf }
    class VehicleBody { <<existing>> Of · Pose · PartOf }
    class EntityPartPicker { <<new, ui Map3D>> Kind = EntityParts · parts first, the coarse box for a body with no parts · hit point = where the ray meets the part }
    class EntityBoxPicker { <<existing>> Kind = Entities · RTS selection, unchanged }
    class PickFilter { <<existing, grows>> + EntityParts · + Aim = Terrain or EntityParts }
    class MapCamera3D { <<existing>> Pick(pixel, filter) · the cache keys on the filter }
    class FpsCameraDriver { <<existing>> Aim picks PickFilter.Aim · HUD LOCKED ring only in LOCK }
    class OperatorFireModes { <<new, Fdp.Toolkits Combat>> +Of(world, unit, mount, loadedAmmo) Sight or Lock }
    class OperatorControlBehavior { <<existing, grows>> Pending 1 = LOCK (held point or unit) · 2 = SIGHT (the crosshair of each tick) }
    class AimPoint { <<existing, shrinks>> ToUnitFrame · FromUnitFrame · Middle · ⛔ Operator and OnUnitMetres removed }
    RaycastSolverSystem ..> BodyPartRay
    EntityPartPicker ..> BodyPartRay
    BodyPartRay ..> BodyPartHit
    BodyPartRay ..> PersonBody
    BodyPartRay ..> VehicleBody
    MapCamera3D o-- EntityPartPicker
    MapCamera3D o-- EntityBoxPicker
    FpsCameraDriver ..> MapCamera3D : Pick(Aim)
    FpsCameraDriver ..> OperatorFireModes : HUD
    OperatorControlBehavior ..> OperatorFireModes
    OperatorControlBehavior ..> AimPoint
```

*What the picture shows that prose hid:* the crosshair and the bullets reach the SAME parts through ONE function (`BodyPartRay`), so "the
crosshair is on his head" and "the round strikes his head" cannot drift — and the selection box is a separate picker, so RTS clicks keep their
generous box.

```mermaid
sequenceDiagram
    participant D as FpsCameraDriver (station)
    participant C as MapCamera3D
    participant K as EntityPartPicker
    participant O as OperatorControl (brain)
    participant X as AimAndFireExecutor (brain)
    D->>C: Pick(centre, Aim)
    C->>K: TryPick(eye, look)
    K->>K: BodyPartRay per entity, nearest part entered
    K-->>D: the point on the part + network id
    D->>O: frame ShotSeq+1, ShotTarget, AimPoint
    alt LOCK (turret, arc or thrown, guided)
        O->>O: Pending 1, point held in the unit's frame (CE-1125)
        O->>X: Target, Point = FromUnitFrame each tick, Rounds = Cap
    else SIGHT (hand weapon, mount with no turret, unguided rocket)
        O->>O: Pending 2, no target
        O->>X: Point = this tick's AimPoint, Rounds = Cap
    end
    X-->>O: RoundsFired reaches Cap, Pending 0
```

```mermaid
graph TD
    PACK[MapInteractionPack.AttachMapLayers, every map host] -->|Pickers.Add| EBP[EntityBoxPicker, selection]
    PACK -->|Pickers.Add, NEW| EPP[EntityPartPicker, the crosshair]
    CAN[MapCanvas each frame] -->|Camera.HandleInput| FPS[FpsCameraDriver]
    FPS -->|Pick Aim| EPP
    CAN -->|Pick All| EBP
    FPS --> BUS[frame to PossessionSystem, brain]
    BUS --> OC[OperatorControl]
    OC -->|WeaponChannel params| X[AimAndFireExecutor]
    MOD[Physics module, muscle] --> RS[RaycastSolverSystem]
    RS --> BPR[BodyPartRay]
    EPP --> BPR
```

*Caption:* the new picker is added by the SAME attach that adds `EntityBoxPicker`, so no map host can have one without the other; the only
caller that asks `PickFilter.Aim` is the FPS station — every other click still asks `All` and gets the selection box.

| # | decision (✅ user, `2026-10-16`) | rule |
|---|---|---|
| **F-1** | which mode | ⭐ **LOCK** when the weapon is fought through a turret (`UsesTurretTask`'s test: a mount naming a turret the unit has), or is a thrown / arc weapon (category Grenade, Mortar, Howitzer, Bomb, or an `Indirect` warhead), or guided (Atgm, Sam, or `NeedsLock`). **SIGHT** for everything else: hand weapons, a mount with no turret (pintle, tripod MG), an unguided rocket |
| **F-2** | SIGHT | each round goes to the point under the crosshair WHEN IT LEAVES (the frame's `AimPoint`): a press fires one round at once (cooldown permitting), a held trigger on an automatic weapon follows the look. No target, no LOCKED ring |
| **F-3** | LOCK | unchanged from §8e D-1 / `CE-1125`: the press holds the point on a unit (in his frame), a building or the ground, and fires when the weapon bears; RMB drops it |
| **F-4** | the crosshair | the nearest of the terrain and every entity's PARTS (`BodyPartRay` — the round's own test); a body with no parts (an icon unit, an unknown type) keeps its coarse box. The point is where the ray meets the part |
| **F-5** | the fallback | ⛔ `AimPoint.Operator` / `OnUnitMetres` go: the point IS on a part. The middle is used only when a shot names a unit and no point |
| **F-6** | the sight zero | ⏭ not now — direct rounds have no gravity, so a round flown muzzle → crosshair point is a sight zeroed at that range. Filed `CE-1130` |

**Rejected — one line each:**
- *The part test inside `EntityBoxPicker` behind a flag* — one picker would then answer two questions and RTS selection would lose its generous box.
- *A copy of the solver's part test in the ui* — two answers that drift (the reason `PersonBody` is generated from the drawing).
- *SIGHT as "lock with a point only"* — a latched point fires late at where the crosshair WAS; a rifleman expects the round where he looks now.
- *The mode in the station* — the brain owns the latch (§8e); the station only mirrors the mode for its HUD.

**AS-BUILT (`CE-1126`, `CE-1127`, `2026-10-16`, ui)** — the three diagrams hold; the details they do not carry, and one deviation:

| as-built | why |
|---|---|
| ⚠ DEVIATION from F-5: `AimPoint.Operator` STAYS, as *"the point, else the middle"*; only the 1.5 m test and `OnUnitMetres` went | the executor and the station's ring both call it — one rule, now trivial, instead of two inline copies |
| `BodyPartRay.Person` / `.Vehicle` (the solver's narrow phase, its counters kept) and `.TryHit` (one entity, its main turret found by `TurretParts.Find`) | the solver keeps its per-tick turret map; a picker asks per entity |
| `EntityPartPicker`: a body the node does not mark a pedestrian (no `VehicleParams` replicated) but DRAWS as a person is still met by `PersonBody` | what you aim at is what is drawn; the sim's pedestrian test needs a component a viewer may lack |
| `MapCamera3D.Pick`'s cache keys on the FILTER (was "is it a drag") | `Aim` and `All` asked for the same pixel in one frame would have shared an answer |
| SIGHT is `ManualControlState.Pending = 2` (no new field) | the blackboard keeps its size |
| the station's `FireMode` property mirrors the rule; a SIGHT press sets no `ShotPending` | the HUD shows LOCKED only for a held shot |

## 8h. S9 DESIGN — the FPS "use" key: act on what the crosshair rests on (`CE-1144`, designed `2026-10-16`, ui) — 🔨 BUILDING (`CE-1151`…`CE-1158`: backend §8h.7, behaviors §8h.8+, the station §8h.9 built)

🔒 **User, `2026-10-16`:** *"There should be a key (or keys) that allows acting on stuff aimed at - opening/closing doors, dragging a body,
possessing a vehicle (becoming a driver there) or weapon operator if the standalone or vehicle mounted weapon is clicked - this needs bigger
discussion."* → on the leans U-1…U-7 below: *"approved, but just preliminarily so you write the design where you suggest how to wire it all,
then i will need to think about that , i.e. not ready to built"*. → on the explanation, `2026-10-16`: *"sound good! … pls records into items
and write handoff to the backend node and behavior node to implement"*. ⇒ **build-state of this section: READY-TO-BUILD.** The items and lanes
are §8h.4; the brain → HUD verdict the user asked about is §8h.6.

### 8h.1 INVENTORY — what already exists (measured `2026-10-16`; graph `search_graph` Class `.*Interaction.*`, `.*Executor.*`; grep)

| the design rests on | code — how it IS | design — how it was MEANT to be |
|---|---|---|
| every action the user named is BUILT, as an interaction-channel executor with its own reach, time and networked command | `DoorActionExecutor` (ids 4–8, reach 2 m / 1.5 m, `DoorCommands.cs:82-85`), `PickUpExecutor` / `PutDownExecutor` (9 / 10, 1.5 m), `EmbarkExecutor` / `DisembarkExecutor` (1 / 2) — `BehaviorConstants.cs:111-141`, run by `ActionDispatchModule` (`:50`) | Entity Interactions §1–§7 (R-221, R-224, R-274); Building Interiors §3j |
| the possessed unit's behaviour drives ONLY the weapon channel; nothing writes its `InteractionChannel` | `OperatorControlBehavior.OperateWeapon`; `InteractionChannel` (`ChannelComponents.cs:41`) | §8e — the latch lives in the operator behaviour (brain) |
| the frame has no "use" field; `E` is unused; the crosshair's target id is computed every frame but sent only with a shot | `OperatorControlFrameEvent` (`Possession.cs:60-89`), `ControlFrameInteractionPayload` (`InteractionMessages.cs:57-79`); `FpsCameraDriver.AimTarget` | §8e D-1: presses travel as COUNTERS (a coalesced frame never loses one) |
| a casualty can be picked up only when incapacitated; the carrier cannot shoot while carrying; a stretcher needs a named assistant | `Embarkation.TryEmbark` (`Embarkation.cs:111-134`) | §7.4.8–§7.4.10 C3, C7; §7.5 T4 (R-2017) |
| a driver seat means nothing — the vehicle's brain drives | nothing reads `SeatRole.Driver` | §7.4.4 B5: *"the vehicle's brain drives (crew stay abstract)"* |
| possessing a VEHICLE already works (drive scheme by locomotion; its station gun fires as the vehicle's) | `FpsCameraDriver.Scheme`; `WeaponSystems.ControllerOf` (`WeaponSystems.cs:41-56`) | §8f; Weapon Systems W-H |
| a man in a standalone weapon's Gunner seat controls it; the operator's fire path already accepts its mount index (32+k) — but the station counts only the man's own weapons | `ManWeapon` behaviour; `AimAndFireExecutor.OperatorFire`; `FpsCameraDriver.MountCount` (`PossessionStation.cs:565`) | Weapon Systems §6, §9 WS-6; *"possession of the gunner seat — a later item"* |
| possession is never handed over: one station holds one unit, `Possess` does not release the held one, the station takes the FIRST unit it holds | `PossessionSystem` (`Possession.cs:138-256`) never reads `IsEmbarkedTag`; `EntityOrders.Possess` (`:180`); `PossessionStation.HeldHere` (`:68-81`) | ⛔ searched `docs/` + `.dev/`, no design of a hand-over |

### 8h.2 Diagrams

```mermaid
classDiagram
    direction LR
    class FpsCameraDriver { <<existing, grows>> E press: UseSeq++ · UseTarget = AimTarget · UseVerb (0 = the primary) · hold E: the verb list }
    class OperatorUseActions { <<new, Fdp.Toolkits Behavior>> +For(world, actor, target) UseAction[] · the ONE rule for the HUD prompt and the brain }
    class UseAction { <<new record>> Verb · Label · ActionId · Available · Reason }
    class OperatorControlFrameEvent { <<existing, appended>> + UseSeq · UseVerb · UseTarget }
    class ControlFrameInteractionPayload { <<wire, appended>> + the same three }
    class OperatorInput { <<existing, appended>> + the same three }
    class OperatorControlBehavior { <<existing, grows>> + OperateUse: a new UseSeq starts the chosen action on the InteractionChannel }
    class InteractionChannel { <<existing>> ActiveAction · Params · Status }
    class ActionDispatchModule { <<existing>> runs the interaction executors }
    class DoorActionExecutor { <<existing>> }
    class PickUpExecutor { <<existing>> PutDownExecutor beside it }
    class EmbarkExecutor { <<existing>> DisembarkExecutor beside it }
    class PossessionStation { <<existing, grows>> + the hand-over: Drive / Get out switch the held unit · HUD prompt line · verdict line }
    class EntityOrders { <<existing, grows>> Possess(unit, mode) · a new Fps possession releases this station's other Fps unit }
    class PossessionMode { <<existing enum, grows>> Fps · + Riding: held, task paused, frames ignored }
    class OperatorUseStatus { <<new component, Brain group, dtOperatorUseStatus 141>> UseSeq · ActionId · Outcome · Reason }
    class Embarkation { <<existing, grows>> + CanEmbark: TryEmbark's checks without seating, one rule for both }
    FpsCameraDriver ..> OperatorUseActions : HUD prompt
    FpsCameraDriver --> OperatorControlFrameEvent
    OperatorControlFrameEvent ..> ControlFrameInteractionPayload : off-node
    OperatorControlFrameEvent --> OperatorInput : PossessionSystem
    OperatorControlBehavior ..> OperatorInput : reads
    OperatorControlBehavior ..> OperatorUseActions : decides
    OperatorControlBehavior --> InteractionChannel : writes the action
    ActionDispatchModule ..> InteractionChannel
    ActionDispatchModule --> DoorActionExecutor
    ActionDispatchModule --> PickUpExecutor
    ActionDispatchModule --> EmbarkExecutor
    PossessionStation ..> EntityOrders : Riding then Possess
    OperatorControlBehavior --> OperatorUseStatus : writes the verdict
    PossessionStation ..> OperatorUseStatus : reads, replicated
    OperatorUseActions ..> Embarkation : CanEmbark
    EntityOrders ..> PossessionMode
```

*What the picture shows that prose hid:* nothing new acts on the world — the use key only CHOOSES one of five executors that already have reach,
time and a networked command; the new code is a rule (`OperatorUseActions`), three appended frame fields, and the hand-over in the station.

```mermaid
sequenceDiagram
    participant D as FpsCameraDriver (station)
    participant O as OperatorControl (soldier's brain)
    participant X as interaction executor (brain)
    participant T as target's owner (door / casualty / vehicle)
    D->>D: crosshair on a door, HUD "E Open door"
    D->>O: frame UseSeq+1, UseVerb 0, UseTarget = the door
    O->>O: OperatorUseActions.For(soldier, door), primary = Open
    O->>X: InteractionChannel = Open door, new instance
    O-->>D: OperatorUseStatus UseSeq, Running (replicated, §8h.6)
    X->>X: in reach? (2 m) else Failure
    X->>T: DoorCommandEvent (routed by the interaction topic)
    T-->>D: replicated DoorState, the leaf swings
    O-->>D: OperatorUseStatus UseSeq, Success or Refused + Reason
```

```mermaid
sequenceDiagram
    participant D as PossessionStation (station)
    participant S as soldier's brain
    participant V as vehicle's brain
    D->>S: frame UseSeq+1, target = the BMP, primary = Drive
    S->>V: EmbarkEntityCommand, Role Driver (EmbarkExecutor)
    V-->>D: replicated passenger list, the soldier aboard
    D->>S: Possess mode Riding (his task stays paused - O-2)
    D->>V: Possess mode Fps, remember Rider = the soldier
    Note over D,V: the human now drives and shoots the BMP (crew abstract, B5)
    D->>V: E pressed: Get out
    D->>S: Possess mode Fps (a take-over, the operator behaviour keeps running)
    D->>V: Release (the vehicle's own task resumes)
    D->>S: frame UseSeq+1, verb Get out
    S->>V: DisembarkEntityCommand
```

⭐ **AS-BUILT (`CE-1158`, `2026-10-16`) — the same sequence with the waits the station really makes** (§8h.9). ⛔ The diagram above is
the design; keep it for the intent, build from this one.

```mermaid
sequenceDiagram
    participant D as PossessionStation (station)
    participant O as EntityOrders
    participant S as soldier's brain
    participant V as vehicle's brain
    D->>S: frame UseSeq+1 (sent when E is let go), target = the BMP, verb 0 = Drive
    D->>D: ArmDrive: step AwaitBoard
    S->>V: EmbarkEntityCommand, Role Driver
    V-->>D: replicated IsEmbarkedTag, the soldier in the Driver seat
    D->>O: Possess(soldier, Riding)
    S-->>D: replicated PossessedBy, Riding (step AwaitRiding)
    D->>O: Possess(vehicle, Fps)
    O->>O: release this station's other Fps units (none: he rides)
    V-->>D: PossessedBy on the vehicle, HeldHere = the vehicle
    Note over D: the view stayed in first person throughout (soft leave)
    D->>O: E in the vehicle: Possess(rider, Fps)
    O->>V: Release (the vehicle was this station's other Fps unit)
    S-->>D: PossessedBy Fps, HeldHere = the soldier
    D->>S: his first frame: UseSeq+1, verb 0 = Get out
    S->>V: DisembarkEntityCommand
```

*What the picture shows that the design's hid:* possessing the vehicle as Fps releases the station's other Fps unit (GAP2's fix) — so the
station must WAIT until the soldier's possession replicates as Riding, or it would release him. And Get out needs no explicit release: the
rider's Fps possession lets the vehicle go.

*Caption:* the hand-over is ORCHESTRATED BY THE STATION, after the boarding shows in replicated state, because the soldier and the vehicle can
be owned by different brain nodes — no single brain could move possession between them atomically.

```mermaid
graph TD
    CAN[MapCanvas each frame] -->|Camera.HandleInput| FPS[FpsCameraDriver]
    FPS -->|EntityOrders.Control| BUS[interaction topic off-node]
    BUS --> PS[PossessionSystem, brain host]
    PS --> OC[OperatorControl, the possessed unit's root behaviour]
    OC -->|weapon channel, today| WD[weapon dispatch]
    OC -.->|NEW: interaction channel| AD[ActionDispatchModule, brain host]
    AD --> EX[door / pick-up / embark executors]
    EX --> IT[interaction topic to the target's owner]
    ST[PossessionStation] -.->|NEW: Riding then Possess| EO[EntityOrders]
    OC -.->|NEW: OperatorUseStatus, Brain group| ST
    classDef dead fill:#fdd,stroke:#c00
    GAP1[station weapon list counts only the man's own mounts]:::dead
    GAP2[EntityOrders.Possess does not release the held unit]:::dead
```

*Caption:* the dashed edges are the new wiring; ⚠ the red boxes are measured gaps the slices must close — today a manned ZU-23 never
reaches the operator's weapon keys, and a second `Possess` leaves the station holding two units. ✅ **Both closed by `CE-1158`** (§8h.9):
`PossessionStation.Hud.Slots` lists the controlled weapons after the man's own, and `EntityOrders.Possess(unit, Fps)` releases the station's
other Fps unit.

### 8h.3 Decisions — ✅ PRELIMINARY (user, `2026-10-16`), each open to change

| # | decision | lean |
|---|---|---|
| **U-1** | keys | ⭐ ONE key, `E`, acts on what the crosshair rests on; the HUD names it (*E Open door · E Drag · E Drive BMP · E Man ZU-23*). Holding `E` lists the other verbs (lock / unlock / breach; carry instead of drag). `E` again undoes: put the body down, get out |
| **U-2** | wire | ⭐ three fields appended to the frame / `OperatorInput` / payload: `UseSeq` (a counter, §8e), `UseVerb` (0 = the primary), `UseTarget` (network id). The BRAIN decides with `OperatorUseActions` and writes the action into the `InteractionChannel` — exactly how `OperateWeapon` writes the weapon channel. The station calls the same rule only for its HUD |
| **U-3** | reach | ⭐ out of reach ⇒ the executor refuses; no walk-to. The prompt is greyed past reach — the STATION predicts it from replicated state; the brain's own refusal comes back as `OperatorUseStatus` (§8h.6) |
| **U-4** | driving | ⭐ `E` on a vehicle boards the soldier in its Driver seat; the STATION then releases the soldier and possesses the vehicle, remembering him as the rider; `E` in the vehicle reverses it (possess him, he disembarks). Flyers alike (*Pilot*) |
| **U-5** | weapons | ⭐ a STANDALONE weapon: `E` = take its Gunner seat; possession stays on the soldier and the weapon joins his keys 1–4 (`WeaponSystems.Controlled`, index 32+k — the fire path already takes it). A VEHICLE's weapon: as U-4 (the vehicle fires its station gun; one human drives and shoots, crew abstract) |
| **U-6** | bodies | ⭐ `E` on an incapacitated man = drag (`PickUp`, Dragged); hold-`E` = carry; `E` again = put down; the HUD says the rifle is slung (C7). Stretchers (two men, T4) not in v1 |
| **U-7** | doors | ⭐ `E` toggles open / closed by the door's replicated state; lock, unlock, breach on hold-`E` |

**Rejected — one line each:** *a key per action* — five keys to remember when the target already says what is possible · *the station
raising the door / embark commands itself* — it would skip the executors' reach and time and the counters' loss-proofing · *walk to the
target* — takes the body away from the operator · *a driver / gunner split now* — crew is abstract by design (B5) · *the brain moving
possession on boarding* — the soldier and the vehicle may live on two brain nodes.

### 8h.4 Items — one per lane-sized piece (`2026-10-16`; handoffs [backend](blueprints/batches/HANDOFF_Use_Key_To_Backend.md) · [behaviors](blueprints/batches/HANDOFF_Use_Key_To_Behaviors.md) · [ui](blueprints/batches/HANDOFF_Use_Key_To_UI.md))

| id | slice | builds | lane | needs |
|---|---|---|---|---|
| `CE-1151` | S9a | the three frame fields end to end: `OperatorControlFrameEvent`, `OperatorInput`, `ControlFrameInteractionPayload`, `InteractionTransport` both ways, `PossessionSystem`'s copy, the Debug API body | backend | — |
| `CE-1152` | S9a | `OperatorUseActions.For` + `UseAction` + `UseReason`; `Embarkation.CanEmbark` (TryEmbark's checks without seating — `TryEmbark` calls it); reads ONLY replicated components | backend | — |
| `CE-1153` | S9a | `OperatorUseStatus` (Brain group) + `dtOperatorUseStatus` = 141 + both translators + ownership mapping + registration on every host | backend | — |
| `CE-1154` | S9a | `OperatorControlBehavior.OperateUse`: a new `UseSeq` ⇒ the rule ⇒ the action on the `InteractionChannel` (doors 4–8, pick up 9 / put down 10); writes `OperatorUseStatus` | behaviors | 1151–1153 |
| `CE-1155` | S9b | standalone weapon: `E` = embark into its Gunner seat; the possessed man fires it through the controlled index (32+k) | behaviors | 1154 |
| `CE-1156` | S9c | `PossessionMode.Riding`: `PossessionSystem` holds the unit, keeps its task paused, ignores frames; a mode change is a take-over | backend | — |
| `CE-1157` | S9c | the operator brain while Riding holds still and stays aboard; `E` on a vehicle = embark Driver / Pilot; Get out = disembark | behaviors | 1154, 1156 |
| `CE-1158` | S9a–c | the station: `E` counter + verb + target, hold-`E` list (O-1), the HUD prompt (local prediction) and the verdict line; the weapon list counts controlled mounts; the hand-over (`EntityOrders.Possess(unit, mode)`, `HeldHere` skips Riding) | ui | 1151–1153, 1156 |

### 8h.5 Open points — ✅ ALL SIX ACCEPTED as leaned (user, `2026-10-16`: *"O-1,2,3,4,5,6 accepted."*)

✅ `2026-10-16`: the user asked for items and handoffs — the section is **READY-TO-BUILD** (§8h.4).

| | the question | lean, if asked |
|---|---|---|
| **O-1** | hold-`E`: a small list near the crosshair, or a radial? | a short list — fewer pixels over the view |
| **O-2** | while the human drives, the soldier's own task resumes aboard — could it order him out (a squad move)? | ✅ pause it — the mark is `PossessionMode.Riding` (`CE-1156`): the station keeps holding the soldier, so his task stays paused by the possession that already pauses it (origin Possession), and frames are not sent to him |
| **O-3** | the vehicle already has an AI driving it: does the human simply take over (its task pauses, as for any possession)? | yes — possession outranks it (origin rank, Q83) |
| **O-4** | two humans board the same vehicle | one station per unit (C-3): the second becomes a passenger, not its driver |
| **O-5** | a gunner and a driver as different humans | waits for crew to stop being abstract |
| **O-6** | stretchers / two-man weapons for a human | later: naming the assistant is its own design (T4) |

### 8h.6 The verdict back to the station — the brain runs on CGF, the HUD on the player's node

🔒 User, `2026-10-16`: *"brain is on CGF, HUD is on whatever player node. How can brain action communication with its player node to show
anything on HUD?"*

| the answer rests on | code — how it IS | design basis |
|---|---|---|
| everything the rule reads is already on every node: positions, doors, health, seats, passengers, weapon systems | `HrotSharedComponentRegistry.cs:51` `SimTransform`, `:94` `DoorState`, `:116-129` `PassengerBuffer` / `CarrierSeats` / `CarrierStations` / `WeaponSystemParams` / `IsEmbarkedTag`, `:159` `Health`; the IG calls the same `RegisterAll` (`IgNodeBootstrapper.cs:206`) | Entity Interactions §7.4 (state replicated, commands routed) |
| the reach test is a pure function of two replicated transforms | `OpenDoorExecutor.InReach` (`OpenDoorExecutor.cs:102-108`), static | Building Interiors §3j |
| the executor's verdict stays on the brain: `InteractionChannel` is brain-local and says only `Failure`, never why | `ChannelComponents.cs:41-50` (`Status` only), registered by the cognitive registry, not the shared one; `OpenDoorExecutor.cs:66,76,83,85` | ⛔ searched `docs/` + `.dev/`, no design returns an interaction verdict to a station |
| the precedent for "brain state the HUD needs" is a mirrored Brain-group descriptor | `WeaponAmmoMirrorSystem` → `dtWeaponAmmo` 130 (`WeaponAmmo.cs:10`, `NedOwnershipGroupBinding.cs:116`); `dtPossessedBy` 129 | §8e module diagram (`WAM -->|dtWeaponAmmo| HUD`) |

**Decision — two layers, no new channel:**

1. **Before the press, the station predicts.** It calls the same `OperatorUseActions.For` on its own replicated copy: the prompt, the
   greyed *too far*, *locked*, *seat taken*, *he is not down* are all drawn without asking the brain. The rule may read only replicated
   components; that is the condition that makes the prediction match the brain (`CE-1152`).
2. **After the press, the brain answers in state, not in a message.** The operator behaviour writes `OperatorUseStatus { UseSeq, ActionId,
   Outcome (Running / Done / Refused), Reason }` on the possessed unit (Brain group, `dtOperatorUseStatus` = 141, `CE-1153`), exactly as the
   ammo reaches the HUD. The station shows it when `UseSeq` is its own last press. The effect itself (the leaf swings, the man is aboard)
   arrives through the state that is already replicated.

```mermaid
sequenceDiagram
    participant P as player node (station + HUD)
    participant C as CGF (the soldier's brain)
    P->>P: OperatorUseActions.For on replicated state, prompt "E Open door" or greyed "too far"
    P->>C: control frame UseSeq 7, verb 0, target door (interaction topic)
    C->>C: same rule, then InteractionChannel = Open door
    C-->>P: OperatorUseStatus 7 Running (dtOperatorUseStatus)
    C->>C: executor ends Success or Failure
    C-->>P: OperatorUseStatus 7 Done, or Refused + Reason
    P->>P: HUD line "door opened" / "too far" for 2 s
```

*What the picture shows that prose hid:* the HUD never waits on the brain to draw the prompt — only the verdict line waits, and it waits on
replicated state the station already receives for the ammo.

**Rejected — one line each:** *a reply event from brain to station* — a lost or late event leaves the HUD stale, a counter in state does not ·
*replicate the `InteractionChannel`* — its params and state blobs for every unit, to say one word to one station · *station-only
verdict* — it cannot see the brain's refusals (a door locked a tick earlier, a seat just taken) · *reasons from the executors* — they return
only `Failure`; the rule already knows why an action is unavailable, so `Reason` comes from it (an executor failure after a start is
`Reason = Failed`).

⚠ **Every map host must receive it** — the IG included. 📌 CE-1139 (`2026-10-16`) measured a shared component that never reached the IG;
`CE-1153` checks the new descriptor live on the IG the same way, and CE-1147 / CE-1148 (stance unification, retire "pure IG") remove the cause.

### 8h.7 AS-BUILT — the backend half (`CE-1151`, `CE-1152`, `CE-1153`, `CE-1156`, `2026-10-16`, backend)

Built against §8h.2's diagrams; the class names are the diagram's. Where it differs:

| item | as built | deviation, and why |
|---|---|---|
| `CE-1151` | `UseSeq`, `UseVerb`, `UseTarget` appended to `OperatorControlFrameEvent` (after `IsRemote`), `OperatorInput`, `ControlFrameInteractionPayload` (after `ShotTarget`); both codec directions; `PossessionSystem`'s copy; the Debug API body (`use`, `useVerb`, `useTarget`, documented on `control_entity`) | — |
| `CE-1152` | `OperatorUseActions.For(world, actor, target)` (array, and a `Span<UseAction>` overload for the per-frame HUD) + `UseAction` + `UseVerbKind` + `UseReason`, in `Fdp.Toolkits/Behavior/Possession/OperatorUse.cs`. **Params are carried by a WRITER**: `OperatorUseActions.WriteParams(in UseAction, Span<byte>)` packs `OpenDoorParams` / `PickUpParams` / `EmbarkParams` / `DisembarkParams` exactly as each executor reads them. `Embarkation.CanEmbark` is `TryEmbark`'s check (one private `Check`), and `TryEmbark` now only seats what `Check` allowed | ⚠ **a vehicle's reach is a CONSTANT** (`OperatorUseActions.VehicleReach` = default hull 2 + dismount gap 1.5 + slack 1 = 4.5 m), carried in `EmbarkParams.MaxBoardingRange`: `PhysicsCollider` (the hull radius `Embarkation.BoardingRange` reads) is not registered on the CGF brain, so a radius-based reach would differ between station and brain. A large aircraft may need a reach per type — a later item. ⭐ **O-4 as a rule**: a vehicle offers Drive / Pilot (by `Locomotions.IsAircraft`) AND Board; the free one goes first. Carrying a man ⇒ `Put down` is the primary whatever the crosshair rests on (U-6); aboard ⇒ `Get out` is the only action (U-4) |
| `CE-1153` | `OperatorUseStatus { UseSeq, ActionId, Outcome, Reason }` (component 396, Brain group), `dtOperatorUseStatus` = 141, `EntityOperatorUseStatusEgress/IngressTranslator` (the `dtWeaponAmmo` shape, plus CE-4007's delete-on-wipe), the ownership mapping, `HrotSharedComponentRegistry`, and the pure-IG ingress pack | ⚠ the IG check is by registration and the ingress pack only — not yet seen LIVE on an IG (the cluster run is still owed) |
| `CE-1152` (Drive) | `Drive`/`Pilot` is available only while **the driver seat itself** is free | 📐 measured by its rail: `Embarkation.FreeSeat(Driver)` falls back to a free passenger seat, so `CanEmbark(Driver)` stays `Applied` after the driver sits — the rule checks the seat's role and reports `SeatTaken` |
| `CE-1156` | `PossessionMode.Riding = 2`; `PossessionSystem` drops a frame for a unit held in Riding; Fps ↔ Riding on the same station is the existing take-over (the operator behaviour keeps running) | — |

Rails: `InteractionTransportTests.CE3172_PossessControlRelease…` (the three fields cross unchanged), `PossessionReplicationTests.CE1156_…`
and `.CE1153_…`, `OperatorUseActionsTests` (door, body, vehicle, weapon, params, `CanEmbark` ≡ `TryEmbark`).

### 8h.8 AS-BUILT — the behaviors half, S9a–c (`CE-1154`, `CE-1155`, `CE-1157`, `2026-10-16`, behaviors)

Built against §8h.2's class diagram and the door sequence: `OperatorControlBehavior.OperateUse` runs inside the one `ManualControl` node,
after `OperateWeapon`, in the same shape — a new `UseSeq` ⇒ `OperatorUseActions.For(world, soldier, target)` ⇒ the verb `UseVerb` names ⇒
the action on the `InteractionChannel` with `OperatorUseActions.WriteParams` ⇒ `OperatorUseStatus`. No new executor, no new channel.

| point | as built | deviation, and why |
|---|---|---|
| channel use | the action starts and hands back through the SAME helpers the AI's carry / embark nodes use (`EmbarkNodes.Start` stamps the run's `BehaviorInstanceId`, `OwnsInteraction` reads only OUR action instance, `Release` hands the channel back once the answer is forwarded) | — the DoorNodes rule: a finished action left on the channel would read as the next press's answer |
| verdict | `Running` when the action starts; `Done` (executor Success) or `Refused` + `Failed` (executor Failure after the start); `Refused` + the RULE's reason when the chosen action is unavailable (out of reach, locked, not down, seat taken) — then nothing starts and nothing walks (U-3) | ⭐ a `UseVerb` past the target's list ⇒ `Refused` + `NothingToDo` (the rule offers nothing at that index) |
| target | `UseTarget` resolved through the brain's `NetworkEntityMap`; 0 or unknown ⇒ no target — the rule still offers Put down (carrying) and Get out (aboard), so "E again" works wherever the crosshair rests (U-6) | — |
| repeat press | a press while an earlier use action runs REPLACES it (a new action instance ⇒ the executor starts over) | ⭐ not in §8h.2; the alternative (ignore the press) would swallow the human's intent with no verdict |
| new station | a frame whose `Seq` goes back resets the use counter to 0, as the trigger's does | — |
| buffer | the rule's answer goes to a `[ThreadStatic]` `UseAction[MaxActions]` | `UseAction` carries its label (a string), so it cannot be `stackalloc`ed; a press must not allocate |
| `CE-1155` man a weapon | `E` on a standalone weapon = the rule's *Man* (embark, Role Gunner) through the same `OperateUse`; he is its `ControllerOf`, possession stays on him | 📐 **measured gap, fixed**: the station's controlled index (32+k) reached `AimAndFireExecutor`, which already fires the weapon through it (WS-3), but `OperateWeapon` read the index ON THE MAN (`CombatTkb.MountOf`, `WeaponMountQuery.MountEntity`) ⇒ the weapon's type was lost — an ATGM fired by SIGHT with a held trigger REPEATING, `F` cycled the man's rifle. ⭐ `OperatedMount` resolves a controlled index to the weapon and its mount 0, the executor's own rule |
| `CE-1157` drive, ride, get out | `E` on a vehicle = the rule's *Drive* / *Pilot* (embark, Role Driver); while `Riding` the possession takes his frames, and the operator brain then holds him still, starts nothing, he stays aboard; back in Fps `E` is *Get out* | ⭐ **found writing the rail**: frameless ticks zeroed the last frame number, so a station RESTARTING its frames after the ride (Seq 1, counters from 0) was not seen as new and its first press — `UseSeq` 1, equal to the Drive press — was swallowed. ⭐ `ManualControl` keeps the last frame number when no frame arrives |

Rails (each red-proved by a compiling mutant): `EmbarkNodesTests.CE1154_OneUseOpensAClosedDoor_ASecondClosesIt_…`, `.CE1154_OutOfReach_IsRefusedWithItsReason_…`,
`.CE1154_UseOnAManDown_DragsHim_AndUseAgain_PutsHimDown`, `.CE1154_ANewStationsFirstPress_IsActedOn_…`,
`.CE1155_UseOnAStandaloneWeapon_MansItsGunnerSeat_AndItsControlledMountFiresAsThatWeapon`,
`.CE1157_UseOnAVehicle_DrivesIt_ARiderHoldsStillAboard_AndUseAgain_GetsOut`, `.CE1157_AStationThatRestartsItsFramesAfterTheRide_IsStillHeard`; across nodes
`EntityOrdersAcrossNodesTests.CE1154_AUsePressOnTheStation_TogglesADoorThroughTheBrain_…` (SimHost station → CGF brain → the door's
owner; the verdict on SimHost and on the pure IG — the live IG check `CE-1153` left owed).

### 8h.9 AS-BUILT — the station (`CE-1158`, `2026-10-16`, ui)

Built against §8h.2's diagrams and the [ui handoff](blueprints/batches/HANDOFF_Use_Key_To_UI.md); the driving sequence as built is the
second sequence diagram in §8h.2. All in `Hrot/Engine/Hrot.Presentation/Map3D/PossessionStation.cs` plus `EntityOrders.Possess(unit, mode)`.

| part | as built | deviation, and why |
|---|---|---|
| ① the press | `FpsCameraDriver` counts `UseSeq`; the frame carries `UseSeq` / `UseVerb` / `UseTarget` | ⚠ **counted when `E` is LET GO**, not pressed: a tap and a hold start the same, so the verb is known only at release. The target is fixed at the press |
| ② hold-`E` | held ≥ `HoldSeconds` (0.3 s) opens the list; the wheel and 1-4 pick a verb (not a weapon) while it is open; `UseVerb` = the picked index in `OperatorUseActions.For`'s list | — |
| ③ the prompt | `Hud.Prompt` — `E  Open door` · `E  Drive BMP-2` (the target's TKB name for Drive / Pilot / Board / Man) · greyed `E  Open door (too far)` with `Hud.ReasonText` | ⚠ **drawn UNDER THE CROSSHAIR, not in `Hud.Lines`** — with the list (O-1: "by the crosshair") and the verdict, it is about what the operator looks at; the bottom-left block (CE-1140) keeps the status |
| ④ the verdict | `OperatorUseStatus` on the held unit, shown when its `UseSeq` is our last press **in this possession**, Done or Refused, for `VerdictSeconds` (2 s) | — |
| ⑤ controlled mounts | `Hud.Slots` — own mounts 0…n-1, then `WeaponSystems.Controlled` as 32+k; keys 1-4 and the wheel walk it; `Hud.Resolve` gives the holder for the ammo, the fire mode and the barrel ring | — |
| ⑥ the hand-over | `ArmDrive` after a Drive / Pilot press; steps `AwaitBoard` (the replicated `IsEmbarkedTag` in a Driver seat) → `Possess(soldier, Riding)` → `AwaitRiding` → `Possess(vehicle, Fps)` → `AwaitVehicle`; `TryGetOut` on `E` or Esc with a rider → `Possess(rider, Fps)` and his first frame carries the Get-out press (`QueueUse`); the view stays in first person between the two units (a soft leave); `HandOverTimeout` 15 s | ⚠ **one wait the design did not draw**: the vehicle is possessed only once the soldier's possession REPLICATES as Riding — possessing it earlier releases him (GAP2's rule). ⚠ **Get out sends no release**: the rider's Fps possession lets the vehicle go. ⭐ **added**: a rider whose vehicle this station no longer drives is released (checked once a second) — possessing someone else from the menu while driving would otherwise strand him held |
| ⑦ orders | `EntityOrders.Possess(unit, mode)`; `Possess(unit)` = Fps (the menu and HTTP call sites unchanged); Fps releases this station's other Fps units, Riding releases nothing; `HeldHere` skips Riding and takes the newest `Epoch` | — |
| ⑧ help | the keys line ends `E use (hold E: more)` | — |

✅ **Joined with `CE-1154` (§8h.8):** every new possession starts the station's counters at 0 (a new `FpsCameraDriver`), exactly as for
`ShotSeq`; the brain resets its use counter when the frame `Seq` goes back (§8h.8 "new station"), so the first press after a hand-over
is acted on.

⭐ **Screenshots** (`Map3DFrameRail.CE1158_V_…`, Xvfb): [prompt](assets/map-3d/fps-use-1-prompt.png) ·
[greyed, too far](assets/map-3d/fps-use-2-greyed.png) · [hold-E list](assets/map-3d/fps-use-3-list.png) ·
[verdict](assets/map-3d/fps-use-4-verdict.png).

⚠ **Owed:** the live cluster run (open a door, drag, man a ZU-23, drive a BMP and get out, from a CGF and an IG station). The brain half
(`CE-1154` / `CE-1155` / `CE-1157`) is delivered; behaviors' `EntityOrdersAcrossNodesTests.CE1154_…` already shows a station press toggling a
door through the CGF brain with the verdict on SimHost and the pure IG.

Rails (`PossessionStationTests`, `CE1158_…`): the tap counts one press with the primary verb and the target · hold-E, 1-4 pick a verb
not a weapon · the prompt for a door (open, closed, locked, far), a man down and standing, a vehicle (near, far) and a weapon, drawn in its
colour · the verdict only for our press, once done or refused, for 2 s · a manned weapon joins the keys as mount 32 · Drive rides the
soldier then drives the vehicle, Get out hands back and his first frame gets out · a second Fps possess leaves one held, a rider is kept.

#### 8h.9a ⭐ `CE-1168` (`2026-10-17`, ui) — a person's HANDS in the weapon list

Backend's B-8 (`CE-3230`, [Civilians §7.9](DESIGN_Civilians_And_Crowd_Control.md)) appends a HANDS mount LAST to every person type
(`PersonHands.Mount()`: `Hands = true`, category `Grenade`, empty until he picks something up — a throw is `FireAtPoint` on it). The
station's weapon list (`Hud.Slots`, §8e) already lists every own mount, so a possessed man now has one more slot; `Hud.WeaponLine` names it
`HANDS` (`Hud.HandsKind`) instead of its category's `GRENADE`. The civilian look's armed rule (`VisualFamilies.IsSoldier`, Civilians §8.6)
skips a hands mount explicitly — it was already unarmed by its category. Rail `PossessionStationTests.CE1168_…`, red-proved.

## 9. AS-BUILT — S1 (`CE-3167` + `CE-3168`, `2026-10-11`)

| design said | built | why |
|---|---|---|
| a `MissionPlanClearProvider` class (Entity Commanding §2.5d) | ⭐ **`MissionControlExecutionSystem` IS the provider** (`IIntentClearProvider.ClearFor`), sharing one `EmptyPlan` with `CMD_ABORT_ALL` | it owns the plan's per-unit task order and version (`_taskOrder`, `_missionVersions`); a separate class would clear the components and leave those stale |
| `DELETE …/order` releases the unit | ⭐ **`AssignTacticalIntentEvent.Kind` (`Assign` / `Clear`)**, a `Kind` byte + `Flags` byte on `TacticalIntentRequest` | `DESIGN_Sensors_And_Doctrine.md` §7.7 already named the intent topic as THE one wire path *"which gains a `Kind`"*; `ClearBehaviorEvent` has no translator, so a release from a non-owner node would have gone nowhere |
| `GET …/order` reports finished / failed | ⭐ **`OperatorOrderOutcome`** (component 358, NoScenario, never sent), written by `BrainTickSystem.Finish` for an Operator run | the run's state is cleared on finish (CE-449); its outcome had nowhere to live. Read on the brain node; elsewhere the route says so |
| the service reads the geo transform | ⭐ `EntityOrders(world, map, IGeographicTransform?)` — handed in | the user's `2026-09-08` ruling: one transform, read through the interface the host resolved, never looked up by each consumer |
| `attack` in S1 | ⏳ refused with *"arrives with CE-3170"* — ⛔ SUPERSEDED by §10 (built) | the designated-target CombatPosture is S2; ordering a combat behaviour by name works meanwhile |
| — | the flags constant lives in `TacticalIntentFlags`, outside the DDS struct | the DDS generator serialises every member of a `[DdsStruct]`, constants included |
| "any node publishes, the owner resolves" (tactical-intent design) | 🔴→⭐ **`TacticalIntentEgressTranslator` on EVERY node** (`SimHostAuxiliaryTranslatorPack`), the ingress still Brain-only | 📐 found writing the cross-node rail: the egress sat inside the `NodeRole.Brain` branch, so an order published on a SimHost / map node reached nobody. ⚠ **The IG builds no auxiliary pack** (`NedIgTranslators`) — an IG station's order still needs its own egress: **S2 owns it** (the menu is the first IG-issued order) |
| — | `EditorHarness(productionBehaviors: true)` + the harness passes its `BehaviorRegistry` to the debug API | the networkless-editor rail needs real behaviours; the API names and checks them (silent-default rule) |

📐 **Rails:** `TacticalIntentResolutionSystemTests` (+3), `TacticalIntent{Egress,Ingress}TranslatorTests` (+1 each),
`MissionControlExecutionSystemTests` (+1), `CgfLogicPackTests` (+1 forwarding rail), `SimHostAuxiliaryTranslatorPackTests` (+4 cases), `DebugApiOrdersTests` (7, networkless editor), `EntityOrdersAcrossNodesTests` (1, SimHost → CGF over DDS).

## 10. AS-BUILT — S2 attack (`CE-3170`, `2026-10-11`)

```mermaid
classDiagram
    direction LR
    class EntityOrders { <<Hrot.Common>> + Attack(unit, targetNetId) }
    class CombatPostureParamsJsonDto { <<existing>> advance · assault · + target : EntityRef }
    class CombatPosture { <<C# BTree>> decide Parallel(1) + UntilTargetDown }
    class UntilTargetDown { <<NEW node, PostureNodes>> designates · tells memory once · Success when target down }
    class DesignatedTarget { <<NEW component 360, brain-local>> TargetPacked · InstanceId (the run) }
    class SquadAssignment { <<existing, the ONE reader>> TargetOf: designation FIRST, then the squad's }
    class TopThreat { <<EqsTacticsNodes>> designated + remembered + alive wins }
    class AdvanceAndAttack { <<PostureNodes>> hunting: goes to the last-known point, arrival does not end it }
    class IsAssignedTarget { <<utility input>> ranking bias }
    class LockTarget { <<PeekAndFireNodes>> fire-from-cover lock }
    EntityOrders ..> CombatPostureParamsJsonDto : target + assault
    CombatPosture *-- UntilTargetDown
    UntilTargetDown --> DesignatedTarget : writes
    SquadAssignment --> DesignatedTarget : reads first
    IsAssignedTarget --> SquadAssignment
    LockTarget --> SquadAssignment
    TopThreat --> DesignatedTarget
    AdvanceAndAttack --> DesignatedTarget
```

*What the picture shows that prose hid:* no branch of the posture was taught about orders. The designation joins the reader every
targeting step **already** asks (`SquadAssignment.TargetOf`, G4's one reader), plus `TopThreat`, which every fire, sensor and
close-to-throw step goes through. So Suppress, TakeCover's peek, Flank and the grenade close all follow the named enemy, with no
per-branch code.

| design said | built | why |
|---|---|---|
| "CombatPosture with a designated target" | ⭐ `DesignatedTarget` (component **360** — 359 at commit, moved at the ui merge, NoScenario, brain-local) keyed to the run's `BehaviorState.InstanceId`; nothing removes it | a later run carries a new instance id, so it never inherits the designation; no deactivator ordering to get wrong |
| — | ⭐ read through **`SquadAssignment.TargetOf`** (designation first, then the squad's) and **`EqsTacticsNodes.TopThreat`** (hard override while remembered + alive) | the seam law: the "target I was told to fight" reader already existed (CE-3158 G4). ⛔ No second reader. The squad's assignment stays a ranking **bias** |
| the order succeeds when the target is down | ⭐ `PostureNodes.UntilTargetDown` in the decide `Parallel(1)` — Success ⇒ the posture, so the order, **succeeds** (`OperatorOrderOutcome`) | RequireOne ends on the first Success (`Interpreter.cs` ExecuteParallel); "down" = `CombatLife.IsAlive` false (Health ≤ 0 keeps the body) |
| — | ⭐ the order **tells** the unit where the target is: once, as a memory contact with **no modality** (not "in sight") at the score of a contact just seen | an attack on a target the unit has not seen would otherwise rank nothing; with no modality `HasLineOfSight` stays 0, and fog of war applies again after that (it is forgotten ≈ 70 s unconfirmed) |
| — | 🔴→⭐ **`AdvanceAndAttack` hunts**: with a live designation its objective is the target's last-known point (re-issued when it moves more than the arrival radius), and **arriving does not end the posture** | 📐 found wiring it: the advance's arrival is a Success that ends the Parallel, so an attack would have reported "succeeded" on reaching the target's start point with the target alive |
| the HTTP `attack` kind | ⭐ `{kind:'attack', targetId}` → `EntityOrders.Attack` → `CombatPosture {"target":id,"assault":true}`; refuses self, unknown and killed targets (400) | `assault` per AQ89 D: obedience decides closing in, not bravery |

⚠ **Known limits:** 📐 **a HIDDEN named target is SUPPRESSED, not closed on** (`CE-3177`): the posture's utility decision has no "ordered to attack" input, so an unseen enemy scores Suppress (AQ89 A). The fire still goes to the named enemy, and a grenadier closes to throw under `assault`. A seen one is advanced on and hunted. Raising Advance under a live designation is a decision-scoring change for the behaviors lane. ROE `HoldFire` still holds the fire (the unit closes in without shooting), which is C-5's stated design. A target the unit
**forgets** drops out of `TopThreat`: the posture then fights whatever it ranks, and the order keeps running until the target is
down or the operator re-orders. An order from an IG station needs the IG egress (S2 `CE-3169`).

📐 **Rails:** `TacticsTreesTests` +3 (the named enemy is fought over a nearer one and the order succeeds when it is down · the designation dies with its run · an unknown target leaves the posture as authored) · `DebugApiOrdersTests` +1 (HTTP attack in the networkless editor: Operator origin, designation, `succeeded`, the three refusals).

## 11. AS-BUILT — S2 menu orders (`CE-3169`, `2026-10-11`)

```mermaid
graph TD
    subgraph Station["Every LIVE map host — MapInteractionPack.Build"]
        LAYER[map layer / ExCon menu click]
        ING[ContextActionIngressSystem]
        DISP[GlobalActionDispatchSystem]
        MO[MenuOrders — Move Here · Engage · Stop]:::new
        PICK[host's CanvasMapPickAdapter]
        EO[EntityOrders]
    end
    LAYER -->|GizmoMenuActionEvent| ING
    ING -->|GlobalActionRequestedEvent| DISP
    DISP --> MO
    MO -->|follow-up pick| PICK
    MO -->|polled each frame| EO
    EO -->|AssignTacticalIntentEvent| EGR[TacticalIntentEgress — every node, + the IG]
    IGX[IG ExCon menu: HandleContextMenuActionById] -->|registered id| DISP
    RB[ReplayBrowser]:::dead -. OrdersOffered unset: no items, no handlers .-> MO
    classDef new fill:#dff5e1,stroke:#2e7d32
    classDef dead fill:#fde2e2,stroke:#c62828,stroke-dasharray:4 3
```

*What the picture shows that prose hid:* the click → ingress → dispatcher edge. 📐 **CGF and IG scheduled the dispatcher but ran
NO ingress** (`contextIngress: null`), so on those two hosts no map-menu click ever reached an action: not the orders, and not the
pins, AI trace or layer control the pack already registered "on every map host". The ingress is the fix, not the orders.

| design said | built | why |
|---|---|---|
| `MenuOrderHandlers` in `MapInteractionPack` | ⭐ `MenuOrders` (`Hrot.Presentation`, the pack builds it, scheduled in `InteractionSystems`, a `RequiredSystem`); the host sets `MenuOrders.PickService` (Editor, CGF, SimHost window, IG — a resolver, each builds its pick after the map) | the silent-default rule: every host that has a pick passes it |
| follow-up picks | ⭐ the pick task is **polled each frame**, never awaited | `PickerToolHost` makes continuations asynchronous: an `await` would publish on a pool thread |
| — | 🔴→⭐ **`ContextActionIngressSystem` on CGF and IG** | 📐 found by the agent sweep: `contextIngress: null` on both (`CgfSubsystem`, `IgApplication`) — every map-menu action dead there |
| IG's menu | 🔴→⭐ `HandleContextMenuActionById` runs a **registered** id through the map's registry | it forwarded every non-IG id as a `ContextActionTriggered` that nothing on the IG reads and no translator sends |
| the IG station's egress (§9) | ⭐ `TacticalIntentEgressTranslator` in `NedIgTranslators`; the IG also publishes its `NetworkEntityMap` world singleton | no host builds both packs (IG / auxiliary), so it is never doubled; IG was the one map host without the singleton |
| an order on a non-Brain node | 🔴→⭐ **`EntityOrders` registers `AssignTacticalIntentEvent` on its own node** (its constructor) — `2026-10-16` (ui, a cross-lane fix) | 📐 found LIVE (`ua-air-strike`, `--mode all`): `POST /entities/{id}/order` on the SimHost perspective answered **500** — *"Managed event type 'AssignTacticalIntentEvent' was published without being explicitly registered"*. Only `CognitiveComponentRegistry` (CGF) registers it, so with production's explicit registration (`Program.cs`) the first order on a SimHost or IG node — HTTP, the unit menu, the RTS tool — threw, although the egress there was ready to forward it. `CE3167_AnOrderIssuedOnTheMuscleNode_RunsOnTheBrainNode` was BLIND: the harness leaves strict registration off; it now turns it on for its order |
| the picker's cancel | ⭐ `FdpLocationPickerGizmo(onCancelled)` → the adapter cancels the task | Esc / right-click only removed the gizmo: `PickLocationAsync` never completed |
| the replay menu has no orders | ⭐ `OrdersOffered` world singleton, set by the pack on live hosts; the unit menu offers the three items only there; a replay registers no handler | the replay records `BehaviorState`, so "has a brain" could not tell them apart |
| — | ⚠ orders are issued only where `PickService` answers (a station) — Stop too | a menu action relayed from a station to a headless node must not be ordered twice |

⚠ **Known limits:** ~~a WINDOWED node that also receives a station's relayed menu action would run it too (`CE-3178`).~~ ✅ FIXED `2026-10-11` —
measured by reading: the relay ingress accepts a batch on EVERY node whose world holds the anchor (`GizmoInteractionIngressTranslator`,
"is this id in my world"), not only the owner, so each windowed node with the unit would issue the order and open a pick. The relay
marks its `ContextActionTriggered` `Relayed`; the mark rides `GlobalActionRequestedEvent.Relayed` through the one ingress; the four
station actions register with `GlobalActionRegistry.RegisterLocal` and do not run for a relayed click (an ordinary action — Rotate —
still does). ⚠ So the external gizmo viewer's Move/Engage/Stop/Possess do nothing on the node it mirrors: it has no pick of its own
and an order needs one. The ReplayBrowser
~~runs no context ingress, so its own map-menu actions (Center View…) never fire either (`CE-3179`, pre-existing).~~ ✅ FIXED
`2026-10-11`: it runs the shared `ContextActionIngressSystem` in its world-following form (its world changes when a recording is
rebound), ticked before the dispatcher.

📐 **Rails:** `MenuOrdersTests` (7: move/engage picks → the order, cancel → nothing, Stop, headless → nothing, live vs replay menu,
the picker's cancel) · `SimHostAuxiliaryTranslatorPackTests` +2 (the IG pack carries the egress).

## 12. AS-BUILT — S3 RTS command tool (`CE-3171`, `2026-10-11`)

⭐ **Built as §8a draws it**, with no deviation in the classes or the sequence. `RtsCommands` and `RtsCommandGizmo` live in
`Hrot.Presentation/ScenarioEditor/Map/RtsCommands.cs`. The pack registers `ScenarioToolIds.RtsCommand` ("orders.rtsCommand",
Global arbiter, toggles on reactivate) and action `ToggleRtsCommand` (254) on live hosts only. `IMapPickService.EntityAt` is a
default-`-1` member; `CanvasMapPickAdapter` implements it.

| as-built detail | why |
|---|---|
| the tool arms only where `MenuOrders.PickService` answers | a headless host has no one to click; the tool reports Unserviceable (`ToolController` tells the operator) |
| the View menu item appears only with `OrdersOffered` (two `LayerControlGizmo` menu strings) | the same gate as the unit menu (§11): never in a replay |
| RMB on the clicked unit itself is skipped for that unit | a unit is never ordered to attack or walk onto itself |
| 🔴→⭐ found: `FdpPanelWindowsDumpTheirModelsTests`'s fixture used `[ComponentId(350)]` = `CoverClaim` | an order-dependent collision red; fixed with a test-only id. Two more fixtures in that suite reuse production ids (220, 221): `CE-3180` |

📐 **Rails:** `RtsCommandToolTests` (5: click / Shift / empty-ground select · RMB hostile attacks with every selected unit ·
RMB ground / friend / neutral moves · the gizmo is exclusive, routes its gestures, Esc leaves · offered and armed on a live map with
a pick, absent in a replay).
MCP tools: `order_entity` · `get_entity_order` · `release_entity` · `order_entities` · `lock_side` · `get_sitrep` (group Z).

## 13. AS-BUILT — S4a-S4c possession and manual drive (`CE-3172` part, `CE-3173` part, `2026-10-11`)

⭐ **Built as §8b's class and sequence diagrams draw them, with one moved responsibility:** the let-go after a possession ends is `PossessionSystem`'s, not `OperatorControl`'s (row below). The other deviations are additions:

| as-built detail | why |
|---|---|
| ⭐ **the possess / release / control calls live on `EntityOrders`** (`Possess` · `Unpossess` · `Control` · `HolderOf`), and the HTTP routes call them | §3.1's rule — ONE operator service (R-270): the FPS tool (S5) will call the same three, so HTTP and FPS cannot drift. §8b's `PossessRoutes` box is that service's HTTP face |
| ⭐ **`OperatorControl`'s node RE-ENTERS Manual whenever anything cleared it** (not strictly "once") | the paused task's MoveTo `OnExit` writes `Mode = None` when the possession pre-empts it; a strictly-once write could be overwritten in the same frames. Re-entering is idempotent (`IntentId++` only on a change) |
| a station is a NODE id: `EntityOrders.Station`, else the unit's `NetworkAuthority.LocalNodeId` (C-8) | every replicated unit carries this node's id, so no new singleton |
| `OrderRefusal.Possessed` (409): any order to a possessed unit, and control / release from a station that does not hold it | the ingress would refuse an Operator order under a Possession anyway — this says WHY instead of a silent drop |
| `PossessionSystem` is the first Input system of `CgfLogicPack` | its `AssignBehaviorEvent` is on the bus before the behaviour ingress reads |
| a unit with no frame yet holds still (axes 0, facing kept); once no possession runs, `PossessionSystem` zeroes `ManualDrive` and lets `NavigationIntent` go (the invariant *Manual lives only under a running possession*) | the resumed task starts from idle, never from the operator's last stick position. 🔴→⭐ **found by the rail:** the first build did it in an `OperatorControl` BTree deactivator, which never ran — a ROOT behaviour's deactivators do not fire on clear, by design (`DESIGN_Behaviour_Fault_And_Teardown.md`, rejected *"fire BTree deactivators on clear"*). The invariant also covers death and faults |
| ⚠ component ids: `DesignatedTarget` **360**, `OrdersOffered` **361**, `ManualDrive` **362**, `PossessedBy` **363**, `OperatorInput` **364** | 359 was allocated twice the same evening (ui's `WeaponPosture`); backend's three moved up one at the ui merge |
| event ids 5200-5202 (`PossessRequest` · `ReleasePossession` · `ControlFrame`) | a census of every event id on all lanes |

📐 **Rails:** `SopSlotTests` CE3172 ×3 (a possession pauses / refuses reactions / its release resumes; over a reaction; over the
idle choice, an Operator clear refused) · `CarKinematicsSystemTests` CE3173 ×4 (strafe at 60 %; turn on the spot, back at 60 %,
signed speed; a vehicle steers, brakes, reverses; no drive = hold still) · `DebugApiOrdersTests` CE3172 ×2 in the NETWORKLESS
editor world (possess pauses a hold, refuses an order, control frames walk the body east, an older frame is ignored, release stops
the body and resumes the hold; another station cannot drive or release but can take over).
MCP tools: `possess_entity` · `release_possession` · `control_entity` (group Z).

### 13.1 AS-BUILT — S4d operator fire (`CE-3172` part, `2026-10-11`)

⭐ **Built as M-8 decided it:** the existing `AimAndFireExecutor` gained an OPERATOR mode — `AimAndFireParams.Operator`, `Trigger` (in
its padding) and `Point` (fills the 32 bytes). No second weapon executor.

| as-built detail | why |
|---|---|
| ⭐ the per-shot round at a point is ONE function, `FireAtPointExecutor.ShootAtPoint`, now used by `FireAtPoint` and by operator fire | measured before building: `FireAtPointExecutor` already WAS the point-target, no-sight, no-aim-time path; operator fire differs only in skipping the ROE (C-2), so it reuses the round instead of copying it |
| a turreted mount goes through the existing turret path with `WeaponTask.Point` (Target null) | the muscle already aims and fires at `Point` when there is no target entity (`WeaponTaskDispatcherSystem.cs:55`, `:98`); a moved point re-aims WITHOUT a new epoch, so rounds in flight keep counting |
| kept for the operator: ammunition, magazine, cooldown, no DIRECT round through a friend; skipped: seen-now, aim time, ROE | C-2 lists exactly the three skipped; the friendly line is a rule, not a decision |
| `OperatorControl` starts the weapon action once and steers its params in place each frame; stamped with the run's id | ⭐ `ChannelArbitrationSystem` cancels a channel action whose run ended — so a release (or death, or fault) stops the weapon by construction, no extra teardown |
| ⚠ cooldown is a constant, `OperatorControlBehavior.OperatorCooldownSeconds` = 0.25 s | the TKB carries no cyclic rate (`WeaponMountDto` has aim time, magazine and reload only) |
| 🔴→⭐ found by the rail: a unit with no `ActorCapabilityState{CanShoot}` is never dispatched | the dispatcher's existing gate; production soldiers carry it — the rail's fixture did not |

📐 **Rails:** `AimAndFireExecutorTests` CE3172 ×3 (per-shot fires at the point under HoldFire only while held, 3 rounds in 0.5 s at a
0.2 s cooldown; the AI's aimed fire still obeys HoldFire; a turret tracks the point, engages while held, re-aims with no new epoch,
every round spent once) · `DebugApiOrdersTests` CE3172 ×1 (a possessed HoldFire rifleman fires at the aim point while the trigger is
held, holds when released, stops for good on release of the possession).

### 13.2 AS-BUILT — S4e possession on a cluster (`CE-3172` + `CE-3173`, `2026-10-11`)

⭐ **Built as §8c draws it** — `dtManualDrive` (128) and `dtPossessedBy` (129) in the Brain group, the `Possess` (5) ·
`ReleasePossession` (6) · `ControlFrame` (7) interaction cases, codecs keyed on `PossessionSystem.OwnsBrain`.

| as-built detail | why |
|---|---|
| `EntityPossessedBy.Held`: the egress sends ONE `Held = 0` when the component leaves the brain; the ingress removes it | a release must reach every station, and a TransientLocal topic keeps the last sample per key — a disposal alone would not tell a late joiner |
| ⚠ the `ManualDrive` ingress takes the latest sample, with NO `Seq` check | the one writer is the brain (samples in order); its let-go and a new station both restart the sequence, so a seq check would DROP the stop — M-6's newest-seq rule stays the brain's, on the frames it receives. (Found while writing the translator, before any test ran) |
| `PossessedBy` ingress also in `EntityStatesIngressPack` | a pure IG skips the shared pack's ingress (the WL-6 lesson): without it no IG station would see who holds a unit |
| the three possession events and both components registered in `HrotSharedComponentRegistry` | the R-221 rule: each kind's event on every host |

📐 **Rails:** `InteractionTransportTests` CE3172 (possess, frame and release cross as their cases and arrive as the same events, marked
remote, every axis intact) · `PossessionReplicationTests` ×2 (axes applied on the muscle, the let-go lands; `PossessedBy` set, taken
over, removed by one release sample) · ⭐ `EntityOrdersAcrossNodesTests` CE3172 — **a live 4-node cluster**: SimHost possesses the
Rifleman, CGF runs `OperatorControl` at origin Possession, the body walks 1.5 m on SimHost, the IG sees the hold, release resumes the
task and every node forgets it.

⏭ **S4 is complete.** M-5 (the navmesh refusal) is `CE-3181` — built, §13.4. S5 (`CE-3174`) — §13.3.

### 13.4 AS-BUILT — M-5 the navmesh wall check (`CE-3181`, `2026-10-11`)

⭐ **Built as M-5 states, people only.** The muscle reaches the navmesh after all: `INavmeshProvider` is a managed world singleton
on every host that has one (`EngineBackedNavigationModule`), so §8b's deferral ("only if the muscle's provider is reachable") is
SUPERSEDED. `ManualMotion.KeepOnNavmesh` runs after `ManualMotion.Person` in the `Manual` walker branch.

| as-built detail | why |
|---|---|
| the step's END is PROJECTED onto the mesh: within 5 cm it stands; further, it slides to the projection when that is no further from the start than the step; no projection ⇒ refused | ⛔ `IsWalkable` is not the test — Recast's answer is "a polygon lies within the query extent", so a point just inside a wall passes it. The nearest mesh point IS the slide along the wall |
| a body whose START is already off the mesh moves freely | refusing it every step would trap a unit spawned (or pushed) off the mesh |
| the layer is `NavLayerSelection.For` (a pedestrian ⇒ Infantry) | the mesh the person would be planned on |
| a host with no navmesh singleton walks freely, as before | no regression where there is no terrain mesh |

📐 Rails: `CarKinematicsSystemTests.CE3181_*` ×2 (slides along a Recast-like wall and never passes it; a step with no mesh under it is
refused, and a body already off the mesh is not trapped).

### 13.3 AS-BUILT — S5 the FPS station (`CE-3174`, `2026-10-11`)

⭐ **Built as §8d draws it** — the classes, the sequence and the module edges are the ones in §8d's three diagrams; no box moved.

| as-built detail | why |
|---|---|
| `MapCamera3D.Driver` setter: detaches the old driver, cancels any animation, clears look/pan; `Update` and `HandleInput` delegate wholly while it is set | a half-finished fly-to or a held pan from before the possess would otherwise fight the driver's eye on the first frame |
| `MapCameraLayer.Update` returns early (and forgets its last pose) while a driver drives | M-9 / R-260: the FPS eye is never saved into the scenario camera, and the first still frame after release is not mistaken for an operator's move |
| `PossessionStation.HeldHere` keys on `PossessedBy.StationNodeId == EntityOrders.StationFor(unit)` — the same node id the possess was sent with | one definition of "this station" for both sending and following; a networkless editor is node 0 on both sides |
| `WeaponAmmo` carries the magazine too (`Mag0..3`, −1 when the weapon has none); the HUD shows `[rounds in magazine]` | the HUD would otherwise show a total the operator cannot fire without a reload |
| `WeaponAmmoMirrorSystem` (CGF simulation phase, after `NearMissSensingSystem`) mirrors mount 0 + mount children for units with `BehaviorState` this node has authority over, writing only on change | one writer on the brain; replicas never write it; a write per change, not per frame, keeps the Reliable topic quiet |
| `Possess (FPS)` (action 255) is offered only on hosts that pass a `PickService` | the same rule as Move/Engage (§11): a headless node has no operator to put in first person |

📐 **Rails:** `PossessionStationTests` ×4 (the driver seam replaces update + input and is told when removed; the station follows
`PossessedBy` into FPS and back to the 2-D pose it had, saving nothing; WASD/mouse/LMB become ONE control frame, the cursor locks,
Esc releases, detaching unlocks; the HUD text) · `MenuOrdersTests` CE3174 (the menu item possesses for this station) ·
`WeaponAmmoMirrorSystemTests` (mount + children mirrored, spend updates it, replicas untouched) · `PossessionReplicationTests`
CE3174 (the brain's count reaches the station over `dtWeaponAmmo`).

✅ **(V) acceptance — `2026-10-11`**, a real frame under Xvfb (`Map3DFrameRail.CE3174_V_TheFpsStation_DrawsTheFirstPersonView_AndTheHud`):
the production canvas + switch + `PossessionStation` follow `PossessedBy` into first person on a rifleman on test-town's street — eye
at standing height, an M1 across the street under the crosshair, the HUD with health and ammo per mount (the selected one marked).

![FPS station, first frame](assets/entity-control/fps-1-station.png)

⚠ The turret-mounted eye stays deferred (§8d).

⏭ ~~**Still open in S4:** ~~S4d operator fire~~ (built, §13.1); S4e the `ManualDrive` + `PossessedBy` descriptors and the `Possess` / `Release` / `ControlFrame` interaction
cases (until then possession works where the station, brain and muscle share a world — the editor). M-5 (the navmesh wall check)
did not land: the muscle's manual branch has no navmesh reach today — it stays its own row.

### 13.5 AS-BUILT — the operator's stance, the RTS tool live, first-person input (`CE-1074`, `CE-1075`, `2026-10-14`, ui)

> 🔒 **User, `2026-10-14`:** *"I will need to be able to switch the possessed character to crouched as well as lying pose (cycling using a key
> press). In RTS mode the key shortcut do not work, not even mouse click do anything."*

```mermaid
sequenceDiagram
    participant D as FpsCameraDriver (station)
    participant O as EntityOrders.Control
    participant P as PossessionSystem (brain)
    participant C as OperatorControl (behaviour)
    participant S as StanceRequest
    D->>D: C pressed ⇒ wanted stance = next(standing → crouched → prone → standing)
    D->>O: OperatorControlFrameEvent { …, Stance = StanceId + 1 } every frame
    O->>P: the bus (the R-221 interaction case ControlFrame off-node, Stance in its payload)
    P->>P: OperatorInput.Stance (newest seq)
    C->>S: Set(unit, stance, 0.4 s) — a version bump only on a change
```

*What the picture shows that prose hid:* the stance rides the SAME frame as the stick — no new event, no new route — and reaches the body
through the brain's one stance writer (`StanceRequest`, CE-2121), so the hit model, the fire chain and perception read it the tick it
is asked for (Buildings §3f).

| as-built detail | why |
|---|---|
| `Stance` = `StanceId + 1`, 0 = no request — on the event, `OperatorInput` and the wire payload (`ControlFrameInteractionPayload.Stance`) | an HTTP or older frame that does not mention it keeps the stance the unit has |
| the station starts the cycle from the stance the unit HAS when first driven (`LogicalStance.Of`) and sends the wanted one every frame | a frame lost or a late joiner cannot leave it half-way; `StanceRequest.Set` is idempotent |
| HTTP `POST /entities/{id}/control` takes `"stance": "standing" | "crouched" | "prone"` | the AI player gets the same control |
| the HUD shows `STANCE …` and `C stance` | |
| 🔴→⭐ **the RTS tool received NO input in the editor** — its `InputCaptureBinding` was dropped from a FULL frame, so the terminal never routed a click or key to it (and the View menu lost its entries the same way) | fixed at the buffer, `CE-1071` (📄 [gizmos-1 §11.11](designs/gizmos-1/DESIGN.md)); ⭐ measured live: an XTest click on empty ground now publishes the tool's `SelectionChangeRequest` |
| 🔴→⭐ a Shift-click reached the tool as `Left \| ShiftMask` and matched no branch (`CE-1075`) | the gizmo masks the modifiers, as `SelectionInteractionSystem` and `EntityPlacementGizmo` already did; the mask IS the Shift |
| ⭐ a camera DRIVER holds the map's input: the gizmo terminal no longer polls the mouse under it, and the editor's hover card is off | every FPS shot was also a map click (a rubber band on the selection), and the card at the locked cursor was the possessed man's own |
| ~~⚠ the RTS tool's RMB is also the 3-D camera's look button~~ | ✅ answered by the user and built — §13.6 (`CE-1077`) |

📐 Rails: `DebugApiOrdersTests.CE1074_*` (networkless editor: crouched, kept, prone, standing — one version bump per change) ·
`PossessionStationTests.CE1074_*` (C cycles, every frame carries it, the HUD says it) · `RtsCommandToolTests.CE1075_*` (the armed tool
keeps its binding in a full frame and a bus click reaches it; Shift-click adds).

### 13.6 AS-BUILT — the menu's keys work on the map, and the RTS right button (`CE-1076`, `CE-1077`, `2026-10-14`, ui)

> 🔒 **User, `2026-10-14`:** *"in RTS mode a right click commands and a right drag only looks"* · *"i meant the keys shown next to the order
> name in the entity context menu (M = move, A = attack, ESC = cancel command), i supposed they would work plainly for the currently
> selected entity."*

```mermaid
sequenceDiagram
    participant K as MapCanvas (typed key)
    participant L as MenuShortcutKeyLayer
    participant S as MenuShortcuts (pack system)
    participant M as ContextMenuProjectorGizmo.MenuJsonFor
    participant I as ContextActionIngressSystem → dispatcher
    participant O as MenuOrders
    K->>L: HandleKeyInput(M) — only keys no panel took
    L->>S: MapShortcutKeyEvent{M, Ctrl/Alt?} — none in first person
    S->>S: a tool other than RTS armed? stop
    S->>M: the PRIMARY selected unit's menu
    M-->>S: the enabled item showing "M" = Move
    S->>I: GizmoMenuActionEvent{unit, Move} — what a click sends
    I->>O: Begin(Move, unit) → the location pick (Esc cancels it)
```

*What the picture shows that prose hid:* the key never names an action — it is looked up in the SAME menu the right click shows, and sent as
the SAME event a click sends, so the key, the menu and its follow-up pick cannot disagree (a disabled item stays disabled, a unit whose
class has no Attack has no A).

| as-built detail | why |
|---|---|
| ⭐ every item that shows a key works: M Move, A Attack, H Hold, P Possess, C Center View, Space Select, R Rotate (an area's E Edit Shape, a zone's L Load) | the shortcut text was a display hint only (gizmos-1 §10, Orders D4-D) — now it is what it says |
| ⭐ the target is the PRIMARY selected unit, else the only selected one | a menu click on that unit does the same; plain orders stay one unit (Orders §13.12 **O6** — "every menu order on the selection" was rejected there) |
| ⭐ Esc is not a menu item: it already cancels the pick a key opened (`CE-3175`), and leaves RTS mode | |
| not while a tool holds the map (a pick, a placement — `ToolController.ActiveModal`) — except the RTS command tool; not with Ctrl / Alt; not in first person | a tool owns its keys; Ctrl+C is the application's; in FPS W/A/S/D/C drive the unit |
| ⭐ **RTS: a right CLICK commands, a right DRAG only looks** (`CE-1077`) — the order goes on the release, at the PRESS point, when the mouse moved in at most `RtsCommandGizmo.ClickMaxMoves` (3) frames | it commanded on the press, so every 3-D look ordered a move too |
| `MapShortcutKeyEvent` = event id **8064** (after the gizmo interaction events 8061-8063; free on every lane) | |

📐 Rails: `MenuOrdersTests.CE1076_*` ×3 (the selected unit's M / A / H / P / C through the same menu action, nothing with no selection,
Ctrl, an unknown key or a busy tool, yes in RTS mode; the key layer forwards and is silent in first person; the shortcut text and a
disabled item) · `RtsCommandToolTests.CE1077_*` (a drag orders nothing, a jittery click orders at the press point) ·
`MapSelfCheckTests` (the new system is declared and scheduled).

### 13.7 AS-BUILT — the operator's weapons: a click is one shot, weapon and ammo keys, the HUD (`CE-1078`, `CE-1079`, `CE-1080`, `2026-10-14`, ui)

⭐ **Built as §8e draws it** — the class, sequence and module diagrams there are the as-built; no box moved. The details the diagrams do not
carry:

| as-built detail | why |
|---|---|
| the latch lives in `OperatorControlBehavior.ManualControlState` (`ShotSeq`/`CancelSeq`/`AmmoSeq` seen, `Pending`, `PendingTarget`, `PendingPoint`, `Cap`) | the behaviour sees the executor's round count (`AimAndFireExecutor.RoundsFiredOf`), so it knows when the locked round LEFT — the station only guesses (the mount's count dropping) for its HUD |
| a weapon action restarted while a shot waits (an out-of-arc failure, a new possession) re-arms the cap at 1 | the new action counts its rounds from 0 (`OnEnter`); keeping the old cap would let several rounds go |
| a frame `Seq` that goes BACKWARDS (a new station took the unit) zeroes the seen counters | each station's counters start at 0; without it a take-over would read as presses |
| operator `Rounds` = a CAP: `TurretPath` caps the grant (its existing `p.Rounds` line) and skips the `Success` end; the per-shot path refuses a round past it | the field already meant "how many rounds" (32 bytes are full — §8e rejected a new field) |
| operator `Target` set ⇒ `WeaponTask.Target` = the unit (the muscle aims at `AimPoint.Middle`, follows it as it moves); the epoch moves when the locked unit changes | the AI's own path — `fresh` now compares `p.Target` for both |
| the operator has no aim time / lock (`lockSeconds` only for `!op`) | C-2, unchanged |
| `FireAtPointExecutor.ShootAtPoint(…, ammo)` now stamps `WeaponFireIntent.AmmoGuid`; operator per-shot rounds also `SpendLoaded` | ⚠ §2a of Missiles & Ammo said operator fire "carries the loaded round" — true only on the turret path; the per-shot path now does too |
| `WeaponMountQuery.MountEntity` is public; `FireAtPointExecutor`'s private copy routes to it | one lookup for "the entity holding mount N's WeaponState" |
| `WeaponAmmo` + wire `EntityWeaponAmmo` grew `Load0..3`, `LoadRounds0..3`, `Busy` (appended) | the HUD on another node has no `AmmoLoadout` (Missiles & Ammo §1 — the mirror was always meant to carry the loaded round) |
| the crosshair: `FpsCameraDriver.Aim` picks `PickFilter.All`; `EntityBoxPicker` skips `EntityBodyLayer3D.FirstPerson` | measured: a ray from inside the own hull meets it on the way OUT (`RayBox` returns the exit distance), nearer than anything — the rail proves the own tank would win |
| HUD: `> 1 CANNON  120mm APFSDS 19  41/42  F: 120mm HEAT-MP  LOADING` — numbered as its key; names from the TKB (a trailing " round" dropped; no `/max` when the weapon states none); an orange box on the locked target; a yellow ring where the selected turret's gun points | ⚠ the ring follows the pose, which carries the ballistic elevation, so at long range it sits a little above a laid gun |
| 🔴→⭐ the HUD moved from the window's bottom-left corner to UNDER THE CROSSHAIR (`PossessionStation.HudOrigin`: 0.07 of the height below it, ending 0.05 of the width right of it), on a dark backing; the keys on two lines | measured live (a 2200×1200 window): the editor draws the map FIRST and ImGui after (`Program.cs`), so its docked left column and bottom log covered the corner HUD — it was never visible in the editor (the §13.3 (V) frame was a bare canvas); the crosshair sits right of the open map's centre, so a block centred on it was cut by the right panels |
| keys: `1`–`4` only select a weapon that exists; `F`, LMB press, RMB press are counters on the frame (`OperatorControlFrameEvent`, `ControlFrameInteractionPayload` appended, the HTTP control route's `shot` / `cancel` / `ammo` / `shotTarget`) | |

✅ **Seen live `2026-10-14`** (editor under Xvfb, a 2200×1200 window, XTest input; `tt-nav-los`, possessing the M1 "Hostile B"): the gun ring
settles on the crosshair after a 167° turn of the look (the turret follows); a click right after a 90° turn fires ONE round about 1.4 s later
(42 → 41, APFSDS 20 → 19) and nothing more; `F` cycles APFSDS → HEAT-MP → HE-FRAG → APFSDS with LOADING during each switch; `2` selects the
coax. ⚠ Two of eleven injected `F` presses were not seen (each was the first key after XTest pointer moves); 5 of 5 in a row were.

![FPS HUD on the M1](assets/entity-control/fps-2-tank-hud.png)

📐 **Rails** *(each in its feature's suite)*: `AimAndFireExecutorTests.CE1078_*` ×2 + `CE1079_*` (a locked shot turns the turret to the unit and
fires one round once it bears; per-shot one round at the unit; a round switch holds, then the loaded round leaves) · `AmmoChoiceTests.CE1079_*`
(next type with rounds, skip empty, wrap, one reload, the AI's choice forgotten) · `WeaponAmmoMirrorSystemTests.CE1079_*` (the breech, its rounds,
LOADING) · `PossessionStationTests.CE1078_*` + `CE1079_*` ×2 (counters, lock, RMB, F, 1-4; the HUD line; the HUD's place) ·
`Map3DPickingTests.CE1080_*` (the crosshair sees the unit, never the own hull, aims at the box hit) · `InteractionTransportTests.CE3172_*` and
`PossessionReplicationTests.CE3174_*` extended (the new frame and ammo fields cross) · `DebugApiOrdersTests.CE1078_*` ×2 (through the real
possession: one press one round, a press during a reload waits, RMB drops it; a grenade ignores a held trigger).

#### 13.7a CORRECTION — a locked shot goes to the crosshair point ON the unit, not his middle (`CE-1125`, `2026-10-16`, ui)

🔒 User: *"in FPS mode when i aim at character and fire i always hit the belly of a standing figure like if the aiming raycast ignore the
point where i am aiming."* 📐 Measured: the station sent the box hit (D-4) and the unit's id; the brain passed both on; but the per-shot
path of `AimAndFireExecutor.OperatorFire` aimed a locked shot at `AimPoint.Middle(unit)` and dropped the point — a standing man's middle
is his belly. The rail `CE1078_PerShot…` even pinned "at the unit, not at the point" (it still holds: its point is 40 m off the unit).

| ⭐ rule (supersedes D-1's "aims at its unit" for the per-shot path) | |
|---|---|
| the brain holds a locked point IN THE UNIT'S FRAME (`AimPoint.ToUnitFrame` at the press, `FromUnitFrame` each tick) | the shot still follows him as he moves and turns (D-1), now to the same spot on him |
| `AimPoint.Operator(unit, point)` — the point while it is within `OnUnitMetres` (1.5 m) + his hull height of his middle, else the middle | one rule for the shot (executor) and the HUD ring (`PossessionStation.LockedWorldPoint`) |
| ⛔ unchanged: the turret path | a turreted or guided weapon's lock keeps naming the UNIT (the muscle follows it; a missile locks on him) — fired at his middle |
| a bullet then strikes the part its ray meets | `HitResolutionSystem` (CE-3209) — so the head point is a head hit |

Rails: `AimAndFireExecutorTests.CE1125_ALockedShot_GoesToTheCrosshairPointOnTheUnit_NotHisMiddle`, `CE1125_APointOnAUnit_FollowsHim_AsHeMovesAndTurns`.

### 13.8 AS-BUILT — the manual test's fixes: the HUD, stance keys, the attack modifier, the cockpit (`CE-1131`, `CE-1132`, `CE-1139`, `CE-1140`, `CE-1143`, `2026-10-16`, ui)

🔒 User, manual test `2026-10-16` ([tracker](blueprints/Blueprint_Issues_Tracker.md) CE-1131 … CE-1145). No box in §8a/§8e moved; the details:

| as-built | why |
|---|---|
| ⛔ **§13.7's HUD placement SUPERSEDED:** the HUD sits at the **bottom-left of the map's visible area** (`OpenMapArea` — the dockspace's central node, so a docked panel never covers it), 16 px in; Roboto (`HudText`, the entity cards' font) at 20 px, 24 px lines; each kind of line its own colour (`Hud.ColourOf`: the unit, the weapon, the stance, the keys) | 🔒 *"HUD font ugly, use the entity labels/card font, bottom-left, coloured texts"* |
| stance keys: **C** toggles crouch, **Z** toggles prone, **Ctrl held** crouches (not from prone), **Shift** runs (`Gait` 0 = full speed — unchanged); the frame sends the EFFECTIVE stance; the HUD's stance line says `RUN` | 🔒 *"Shift+W run, Ctrl hold crouch, prone toggle key"* — supersedes §13.5's one cycle key |
| 🔴 **CE-1139 root cause:** the IG world carried no `StanceIntent`, so `LogicalStance.Of` answered Standing — the brain's crouch reached the brain and the muscle but never the IG's figure, and the eye rides the drawn head. ⭐ The IG now takes both stance topics in (`AnimationReplicationModule.RegisterStanceViewerOn`; [Decision Layer §3.3g](DESIGN_Decision_Layer.md)) | 🔒 *"switching FPS posture does not seem to move the camera"* |
| the RTS tool: **Ctrl + RMB attacks whatever is under the cursor** (any side); a legend (`RtsModeState.Legend`) top-left while armed, a red reticle at the cursor while Ctrl is held | 🔒 *"how to switch to attack mode … would not matter if ctrl or shift-click activated the attack"* |
| in first person in a vehicle the own vehicle's body is not drawn (`EntityBodyLayer3D` skips the FirstPerson entity unless a person) | 🔒 *"flyer cockpit view cluttered"* — ⚠ a cockpit / crew-eye position from the TKB is a follow-up |
| ⚠ **CE-1133 not reproduced:** select A+B, click C, right-click the ground orders only C through the real pack (`RtsCommandToolTests.CE1133_*`) | the order loop reads the selection the map shows; the reported case needs a repro (which view, how the units were deselected) |

Rails: `PossessionStationTests.CE1143_*`, `CE1140_*` ×2, `RtsCommandToolTests.CE1133_*`, `StanceWireTests.CE1139_*`, `IgRoleComponentRegistryTests.CE1139_*`.

## 8. Known limits and defects found while designing

| | |
|---|---|
| ⚠ mission timers keep running during possession | a phase change while possessed is refused by rank (Possession outranks Superior); on release the unit resumes its OLD task while the plan has moved on — accepted for v1 |
| ⚠ a vehicle under manual drive can enter a building | M-5 checks walkability for people only (built, §13.4) |
| ⚠ `EntityBoxPicker` returns the entity's **position**, not the hit point | the operator's aim at an entity aims at its origin; fine for v1 |
| ✅ `CE-3175` | ~~the map canvas and the gizmo layer **both drain** Raylib's typed-key queue~~ — FIXED `2026-10-11`: the canvas is the one drain and hands the keys to the gizmo layer for its terminal. ⚠ Wider than first read: no key PRESS reached any backend gizmo, so Escape cancelled none of them. 📄 `docs/designs/gizmos-1/gizmo-input-focus-design.md` §6.2d |
| ✅ `CE-3176` | ~~`KinematicsMode.None` is driven as Direct and the PathToPoint bridge writes None with a speed~~ — FIXED `2026-10-11`: the pending state holds at speed 0 and the path response restores the intent's speed. 📄 `docs/designs/navig-2/Navigation_Design_v2_0.md` §3.1 |
