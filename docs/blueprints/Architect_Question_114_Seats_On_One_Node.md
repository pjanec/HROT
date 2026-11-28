<!--STATUS
state: LIVE
updated: 2026-11-21
current-answer: §2 the sub-questions as revised by the independent review (§2a; rev 2, 2026-11-21) — ✅ APPROVED (R-11011, user `2026-11-21`: "ok approved, do")
stale-below: nothing
related-designs:
  - ../designs/devices/DESIGN_Simulated_Devices.md — simulated devices: several stations on one node, each drawing only its own device HUD
  - ../DESIGN_Entity_Control.md — OWNS C-8 ("one operator per node — the station id is the node id"), which A amends
  - ../UX/DESIGN_Seats_And_Hats.md — OWNS the seat (one person) and its hats; §2.1 "a fused server … still ONE seat" is what A amends for the editor
  - ../DESIGN_Player_Join.md — OWNS the deploy server and the deploy flow that B, C and D reuse in the editor
  - ../DESIGN_Node_Roles_And_Policies.md — OWNS composition by role; B changes the editor's role table ("serves no players")
  - ../designs/IG/DESIGN_Linked_Client_Director.md — the linked client; every load waits for it (CE-11026) — unchanged by this question (one linked client per node, the gateway waits for all its clients)
  - SimNetGateway docs/design/DD-023-Dps-Multiple-Players.md (branch ccr-458b1360-afzl8f) — OWNS the gateway half (seats, partitions, the DPS mapping)
-->

# Architect Question 114 — several player seats on one node (the editor as the all-in-one server)

**Asked by:** `ui` lane (the linked-client programme), `2026-11-21`. **Decides:** what HROT changes so that one networkless editor serves
several players at once, each through the gateway extension, each deploying through HROT's own deploy server and deploy flow.
⚠ Blast radius: possession's station identity (`Fdp.Toolkits` Possession, every station-keyed rule, the recorded `PossessedBy`, the NED
possession payloads and the gameplay topics), the editor's composition (role table), `Hrot.Gameplay`'s wire.

## 0. The user's direction (R-11010)

🔒 User, `2026-11-21`: *"i like the idea of multiple players served by a single gateway"* · *"we plan to have just a few nodes so even 16/16
split is perfectly fine"* · *"reuse HROT's DeployFlowSystem per seat"* · *"having deploy server in editor fits the idea that the editor is
all-in-one networkless (except the gateway extension)"*. The editor's CPU with many players is *"an independent story"*. The sub-questions
below await approval.

## 1. Why this is a question, not a build

It overturns a standing rule (C-8, and Seats and Hats §2.1's "one seat per node") and touches a recorded component and two wires.

### INVENTORY

- `search_graph` `name_pattern=".*(Deploy|GameplayWire|StationClaims|StationReservations|PossessionSystem|PossessionStation|PlayerSlot).*"`,
  `label=Class`: **total 49, has_more false** — the deploy server (`DeployProcessorSystem`, `ServerPlayerSlotReaderSystem`,
  `DeployServerElection`), the flow (`DeployFlowSystem`, `DeployFlowView`, `DeployFlows`, `DeployFlowReadout`, `DeployFlowEnvironment`),
  the wire (`GameplayWire`, the four `Deploy*Topic` / `PlayerSlotTopic`), the arbitration (`PossessionSystem`, `StationClaims`,
  `StationReservations`), the editor's station (`PossessionStation`), the screens (`DeployScreen`, `DeployAreaMapLayer`, `DeployAreaChoice`);
  plus unrelated `WeaponDeploy*`.
- `search_graph` `name_pattern=".*(Deploy|Gameplay|Station|Possess).*"`, `label=Interface`: **total 1** (`IDeployFlow`).
- grep `StationNodeId` in production C# (generated code excluded): 14 files (rev 1 said 15; the review's count); the possession types `Possession.cs:44, 57, 73, 130`.
- ⚠ Coverage could not be checked (the CLI has no `check_index_coverage`).

| the lean rests on | code — how it IS | design — how it was MEANT |
|---|---|---|
| possession arbitrates by station NUMBER only; nothing checks the number is a cluster node | ✅ `Possession.cs:221-263`; `StationClaims.ForPossess` `:97-104` | ⚠ C-8: the station id IS the node id (`DESIGN_Entity_Control.md` C-8) |
| the only link to real nodes: release on `NodeDeparted` | ✅ `Possession.cs:289-294`; the deploy server `DeployProcessorSystem.cs:391-400` | ✅ Player Join (a station leaves with its node) |
| a station is injected, not derived, where it matters | ✅ `EntityOrders.Station` (`EntityOrders.cs:276`); the gateway already sets it (SimNetGateway `HrotPlayerStation.cs:89`) | ✅ CE-3174 |
| the deploy flow is per station by construction | ✅ `DeployFlowSystem` ctor `station` (`:88-93`), requests stamped (`:202`), status filtered (`:306`), `Possess` injected (`DeployFlowEnvironment`, `:32`), own slot injected (`SlotLookup`, `:24`) | ✅ Player Join §9.2 |
| ⛔ one flow per world in its registry | ✅ `PerWorld.AddOrUpdate(world, this)` in the ctor (`:55, :94`); `DeployFlowSystem.Of(world)` used by `/deploy` and the screen | ✅ CE-3446 (one flow per node, then) |
| ⛔ the gameplay wire is DDS only; offers and status are TAKEN | ✅ `GameplayWire.cs:25-39` (null without a participant); `Take` at `DeployFlowSystem.cs:256, :298`, `DeployProcessorSystem.cs:156` | ✅ DD-017 Step 3 finding: no deploy in the offline editor |
| the editor composes no deploy server, by its role table and a rail | ✅ `EditorNodeBootstrapper.cs:278` ("serves no players"); `GameplayPacksAreComposedOnTheRightHostsTests` | ✅ Player Join D-2 (Brain serves) |
| the deploy server's spawn recipe runs on any node with an `EntityCreation` | ✅ `GameplayServerPack.SpawnerOf` `:65-84` (`AllocateNetworkId` + `EntityCreationRequest`); the editor builds `EntityCreation` (`EditorNodeBootstrapper.cs:363`) and spawns through `EntityCreationRequest` (`EditorSubsystem`) | ✅ Entity Creation Unification (one creation path) · ⚠ not run in the editor yet |
| the proven solution for this problem class | — | a listen server / dedicated server hosting many player connections, each a "player controller" with its own id inside one authoritative world (the common game-engine model) — adopted: the editor is that server, a seat is the player controller, the deploy server is the game mode |

## 2. The sub-questions — each with a recommended answer

| # | question | ⭐ recommended | why | rejected |
|---|---|---|---|---|
| **A** | what identifies a station when one node hosts several players? | **a `StationId` value type beside `NodeId` (Fdp.Core): one `int`, node in the low 16 bits, seat in the high 16; `StationId.Of(node, seat)`, `.Node`, `.Seat`; seat 0 = the node id itself.** The possession fields (`PossessRequestEvent`, `ReleasePossessionEvent`, `OperatorControlFrameEvent`, `PossessedBy`) and the gameplay topics' `StationNodeId` become `StationId`; **C-8 becomes "one operator per STATION; a node hosts seat 0 and may host more"**. Retyped with Roslyn, never a text replace | the user's 16/16; same 4 bytes ⇒ recordings and both wires byte-identical, every value written today stays valid (seat 0); a seat knows its node, so the node-departed release covers every seat of the node | a reserved number range (a seat would not know its node); seat ids in a `NodeId` (SV-3: `NodeId` is only a cluster node) |
| **B** | where does the deploy server run for the editor? | **compose `GameplayServerPack` on the editor node** (no election: it serves alone), with the editor's `EntityCreation` as the spawner; the role table's editor row and the composition rail change; feature `gameplay.deploy-server` gates it as on the Brain | the user: the editor is all-in-one; one server, one rule set | a deploy copy in the gateway (two implementations of one concept) |
| **C** | how do the server and the flows talk with no DDS participant? | **an `IGameplayWire` interface over `GameplayWire`'s eight ends, with two implementations: the existing DDS one (cluster, unchanged) and a LOCAL one (networkless): requests into one queue for the server; offers, status and slots BROADCAST — each client end gets its own copy, as DDS gives each reader** | one server and one flow work over either transport; a shared take-queue would let flows steal each other's answers (`Take`, `DeployFlowSystem.cs:256, :298`) | a loopback DDS participant inside the editor (a network stack inside the networkless node, discovery for in-process traffic) |
| **D** | how do several `DeployFlowSystem`s live in one world? | **a ctor flag `register` (default true): only the node's own flow enters the `PerWorld` registry; a seat's flow is not registered and is ticked by its owner** (the gateway calls `Execute`) | `/deploy` and the deploy screen keep finding the node's own flow; seats come and go at run time | keying the registry by station (every caller of `Of(world)` would need a station) |
| **E** | how does a seat leave? | **an appended `Leave` flag on `DeployRequestTopic`: the server frees the station's slot and answer at once (as `ReadDepartures` does for a node); plus `NodeDeparted` frees every station whose `.Node` departed** | a seat's player leaving is not a node departing (`DeployProcessorSystem.cs:391-400`); appended ⇒ the wire stays compatible | the server timing out a silent seat (slow; the gateway knows at once from DDS liveliness) |
| **F** | the editor's own user | **stays seat 0 and keeps possessing through the map; deploying through the same flow is a later, separate item** | nothing changes for today's single-user editor | moving the editor user onto the deploy screen now (UI work unrelated to the gateway) |

**Build order once approved:** A (the type, the retype, C-8 amended, the possession rails with two stations on one node) → C + D (the local
wire, the flag; the existing deploy rails run over the local wire too) → B + E (the editor composes the server; a seat leaves; a role's man
spawns in the editor — the first run of that path) → the gateway's DD-023. Tracker: CE-11027.

## 2a. Rev 2 — the independent review (`2026-11-21`), folded

🔒 User, `2026-11-21`: *"approved, but pls run an independent review before committing to it"*. A read-only reviewer checked every claim
against the code. What it changed, per sub-question (the table above stays as the rev-1 record; where this section differs, THIS wins):

| # | the finding (verified) | the revised answer |
|---|---|---|
| **A** | ⛔ *"byte-identical, so recordings are unchanged"* is FALSE: the recording schema hash folds in each field's TYPE NAME (`ComponentLayoutHasher.cs:61`) and an EVENT whose hash differs refuses playback (`SchemaValidator.cs:141-147`; components only warn, `:88-93`) ⇒ retyping the three possession events breaks every existing recording | **a layout alias:** `[LayoutAs(typeof(NodeId))]` on `StationId`, honoured by `ComponentLayoutHasher`, so a field retyped to it hashes as before; a rail plays a recording made before the retype. The member for the station's index on its node is **`Index`** (not `Seat`: the possession code already has vehicle seats, `FreeSeatOf`, `SeatSwitch`); `StationId` gets the JSON and type converters `NodeId` has (`NodeId.cs:24-25`); a node hosts at most 1024 stations |
| **A′** | ⛔ the node-departed release compares the WHOLE station with the node id (`Possession.cs:294`, `DeployProcessorSystem.cs:397`) ⇒ a departed node would keep its seats | **both compare `station.Node`** — then "a node leaving frees every station on it" holds |
| **B** | the editor passes no election ⇒ it always serves | ✅ kept: the editor is networkless, so no second server can exist by construction; a rail pins that the editor composes no election |
| **C** | ⚠ the readers rely on DDS behaviour: disposed instances' keys from the native sample (`DeployFlowSystem.cs:267-269`, `PlayerSlotReaderSystem.cs:50-54`), "writer gone ⇒ an invalid sample" (`PlayerSlotReaderSystem.cs:12`), transient-local history for an end created late (`DeployFlowSystem.cs:238, :256`); and the flow has no `Side` readout | **the local wire is specified, not a wrapper:** last value per key with dispose (a disposed key delivered once as not-alive), "writer gone" when a client end closes, and the current last values handed to an end created late; offers, status AND slots broadcast (each end its own copy); the existing `PlayerSlotReaderSystem` reads slots over it (rev 1's "`TryGetSlot` directly" dropped — it returns only state + unit, `DeployProcessorSystem.cs:120`); the flow gains a `Side` readout. Rejected (added): the ECS event bus (per-frame, not last-value); reading the server's tables directly (a second reader shape beside the wire's) |
| **D** | ⚠ `/deploy` would 404 for seats (`DeployFlowApi.cs:128`) | the flag stays, plus **`DeployFlowSystem.Of(world, station)`** over a per-world list of all flows; `/deploy?station=` reads a seat's |
| **E** | ⛔ leave and death release ONE unit (`HrotPlayerStation.cs:632-637` — SimNetGateway; `Possession.cs:274-280`) ⇒ a seat that rode and drove leaves the vehicle held for good | **`PossessionSystem.ReleaseStation(station)`**: every unit whose `PossessedBy` names it (the shape of `ReleaseDeparted`, keyed by station) — used by the leave verb AND when a station's Riding man is killed (today's cluster has the same hole: a node does not depart when its man dies) |
| **G** | ⚠ two seats can overfill a role: `RoleFull` counts finished spawns only (`DeployProcessorSystem.cs:226, :274`) | **count pending spawns too**; a rail requests one role from two stations in one frame |
| **H** | ⛔ the server's tables are session state nothing resets (`DeployProcessorSystem.cs:63-78`), on the old clock after a load; and it runs during a replay (no `IReplayRestoreGated`) | **forget the tables at the world boundary (`WorldEpoch.Moved`); gate the server off while replaying** |
| **I** | `PlayerMessage` per station must reuse `PlayerMessages.Addressed` (`Hrot.Presentation/Map3D/PlayerMessages.cs:45-52`), which the gateway cannot reference | **move the addressing rule below `Hrot.Presentation`** (Hrot.Core), one rule for the map and the gateway |
| **J** | role spawn points are unseeded (`DeployProcessorSystem.cs:105`, `GameplayServerPack.cs:191`) ⇒ scenarios differ run to run | **seed the spawn choice from the scenario**, so the feature scenarios (DD-023 D-16) are regression tests |
| **K** | the editor's edit gate applies to every station (`Edits.cs:27-32`) | **to measure in the build:** that a seat's possess / release, which are player actions, are not refused by the editor's edit mode — or route them past it as the editor user's already are |

**Build order (rev 2):** A + A′ (with the old-recording rail) → C + D → E + G + H → B + I + J → the gateway's DD-023. Tracker: CE-11027.

## 3. What would change the recommendation

A deployment with node ids above 65 535 (the split would move, A); a measured reason the editor must not host the deploy server (B); a
need for seats on cluster Player nodes too (still covered by A — a Player node is seat 0 of its node; nothing else changes).
