<!--STATUS
state: LIVE
updated: 2026-11-27 (rev 3 — ONE generic output description (a display list + lamps + fields) drawn as screen-space gizmos and recorded; the device ticks in pause, never acting on the world) · 2026-11-27 (rev 2 — the device runs on the BRAIN: its state is a [UnitMemory] record in its blackboard, reachable from C#, BTree / HSM and blueprints; inputs replicate as events, outputs as replicated state)
build-state: DESIGN — ⛔ NOT READY (the independent review, §3e: two blocking findings; leans for the user's nod, then rev 4 redraws the UML) — the framing's leans ACCEPTED by the user (2026-11-27); the shape below (§3) for the user's nod; slices in §5
current-answer: ⭐ §3b rev 3 (generic outputs as a display list, the HUD as gizmos, pause) — it SUPERSEDES SD-14 and SD-7's HUD model · ⭐ §3a rev 2 (the brain, unit memory, the I/O wire) — it SUPERSEDES SD-2, SD-6 and the state block in §2 · §3 the other decisions · §5 the slices · §9 the open questions
stale-below: §2's `DeviceStateBlock` boxes and SD-2 / SD-6 in §3 — superseded by §3a (kept for the reasoning)
known-rot: §2's diagrams, §5's SD-S1 scope, §6 and Q-4 still describe rev 1 (the review, §3e RV-2 — redrawn in rev 4 once §3e is approved) · §3a's "blueprints reach it via GetShared / SetShared" is FALSE (removed with CE-440 — RV-3) · LS-10 / LS-16 (the designator as a PART) superseded by RV-4
known-conflict: none
related-designs:
  - ../../DESIGN_Entity_Control.md — OWNS the possession station (§8d), the pilot station (§8f), the sight / lock (§8g), zoom and binoculars (§8j, §8k), and ⭐ §8m: a portable weapon is an ENTITY that boards a person's Hands / Back station, is dropped and picked up — THIS attaches a device the same way
  - ../../UX/DESIGN_Seats_And_Hats.md — OWNS seats and hats (Embodied); THIS adds a device sub-mode inside the Embodied hat
  - ../../UX/DESIGN_Input_And_Commands.md — OWNS the one keymap and its scopes; THIS feeds device controls into it and adds axis sources
  - ../../DESIGN_Node_Features.md — OWNS node features (per node today); THIS asks for a per-station layer (later slice)
  - ../sensor-view/DESIGN_Sensor_View.md — OWNS the night / thermal views (map-wide; G-10 "tie to a unit's sight" deferred) — THIS ties a mode to the active device
  - ../../DESIGN_Player_Join.md — OWNS playable slots, deploy roles and kits; a slot's issued devices hang off it
  - ../../DESIGN_Node_Extensions.md — OWNS how an outside product joins a node; the external device driver is one
  - ../weapons/DRAFT_Weapon_Malfunctions_And_Physical_Devices.md — the earlier IDEAS (CE-4645) on physical devices; THIS is their design
  - ../weapons/DESIGN_Weapon_Systems.md — OWNS weapons as entities (stowed / deployed, manned); a device that controls a weapon talks to it there
  - ../../DESIGN_Player_HUD_And_Debrief.md — OWNS the player HUD; a device HUD draws beside it
  - ../vehicle-tactics/DESIGN_FPV_Drone.md — OWNS the FPV drone's simulation (battery, jamming, impact); the drone ground station device reads it
  - ../air-attack/DESIGN_Air_Combat.md — OWNS the fighter HUD (§4d) — the precedent for a HUD as a DATA MODEL
-->

# Simulated devices — a trainee's equipment with its own logic, displays and controls

🔒 User, `2026-11-27`: *"We are still missing the support for training with simulated military equipment like smart devices … they have their own
logic, they have buttons, they have little displays, which is usually running separately from the engine, but requires some API from the device
logic to be usable"* · *"the screen for the trainee is basically the 3D map of HROT in player mode on player node"* · *"we need to be able to somehow
define what kind of devices the trainee is given, and the trainee needs to be able to switch those devices at will"* · *"Leans accepted"* ·
*"the protocol is generic and supports buttons, displays, lamps and analog values like those coming from the joysticks and gamepads … we will then
provide drivers … and even the virtual devices which are controlled via keyboard or mouse … will have their own adapter to this generic
interface"* · *"The device might be an entity having some components describing its logic state … some kind of generic state storage component.
Where the information layout is defined by the device logic and the recording system doesn't know about it"* · *"the device entity should somehow
support being attached to a player avatar … a non-attached state … its own world position … pre-attached to a player avatar entity in the
scenario for our development and testing … creating and attaching at runtime is not slice 1, the infrastructure needs to support that"*.

⭐ **The decision in one line:** a device is an **ENTITY** — attached to its holder through the existing carried-item stations, or lying in the
world — whose logic state lives in a **generic fixed-size state block** the device type lays out (recorded as bytes); its **logic** runs where its
holder is operated and talks to the outside only through a **generic device I/O model** (buttons, axes, lamps, displays), served by **adapters**
(keyboard / mouse, joystick / gamepad, a physical replica); its **view** owns the camera and draws a HUD from a **data model**.

## 0. The proven solution

Training simulators separate a simulated device into **logic** (the device's state machine and menus), **I/O** behind a hardware-abstraction layer
(abstract buttons, axes, lamps and displays, with a driver per real or virtual panel) and **view** (the picture and the optic). The same logic then
serves a desktop trainee and a replica panel, and the recorder only needs the logic's state. ⭐ Adopted as is — each part maps onto a seam HROT
already has (§1).

## 1. INVENTORY — the queries run (`2026-11-27`)

| query / read | finding |
|---|---|
| `search_graph` `.*(Station|Hud|Hat|Seat|Sight|Binocular|Zoom|Camera|Kit|Device|Joystick|Gamepad|Input|Keymap|Feature).*` Class (351, paged) | the possession station (`PossessionStation`, `FpsCameraDriver` — `ICameraDriver`), hats (`Hat`, `HatArbiter`, `SeatHats`), the keymap (`Keymap`, `InputScope`, `KeyTrigger` Press / Hold / **Axis**, `IChordInput`), `SensorView`, `FighterHud` (a HUD data model), `NodeFeatures` |
| `search_graph` `.*(Carried|Lying|PickUp|Attach|Mount|Holder|Stow|Blob|Opaque).*` Class (112) | ⭐ the pick-up machinery (`PickUpGrantEvent`, `RoundPickUp`, `PickUpParams`) — and Entity Control §8m: a portable weapon is an entity that boards a person's **Hands / Back station** (`Embarkation.Check`), is taken / dropped (`PickUpExecutor` / `PutDownExecutor`) and lies in the world |
| grep `fixed byte` in Fdp.Core / Fdp.Toolkits | ⭐ `BlueprintBlackboard1024` / `4096`: a fixed-size memory component whose layout only its owner knows, in size TIERS — the precedent for a generic device state block |
| grep `Gamepad|Joystick|SDL_|DirectInput|XInput|HID` | ⛔ none in HROT (input is keyboard + mouse through `IInputProvider`) |
| grep `laser|rangefind|JTAC|9-line` | ⛔ no rangefinder / designator; the only "9-line" is a medevac request |
| per-station features | ⛔ node features are per NODE only |
| ⚠ `check_index_coverage` | not available through the CLI: absence claims rest on graph + grep |

What exists and is reused: the station's camera ownership and HUD layer, the binoculars / zoom / sight branches (to be turned into devices), the
sensor views, the fighter HUD model, possession of vehicles and aircraft, carried-item stations and pick-up / put-down, the one keymap, node
extensions, the control frame (HTTP / DDS).

## 2. The diagrams

### 2a. The module view — who runs what

```mermaid
graph TD
  subgraph SIM["every node — the simulation"]
    DEV["device ENTITY — NEW<br/>DeviceInfo · DeviceStateBlock (tiered) · Carried in a holder's station, or lying with its own position"]
    EFF["the device's world effects — through existing seams<br/>(an order, a report, a designation, a weapon's control)"]
  end
  subgraph PLAYER["the node that operates the holder (player node)"]
    RT["DeviceRuntime — NEW<br/>one IDeviceLogic per device type · writes the state block · raises effects"]
    IO["DeviceIo — NEW: the generic channels<br/>buttons · axes · lamps · displays"]
    VIEW["DeviceView — NEW<br/>owns the camera while active (FOV steps, sensor mode, reticle) · HUD from a data model"]
    KM["adapter: keyboard / mouse — via the keymap's device scope"]
    JOY["adapter: joystick / gamepad — NEW input backend"]
    STN["PossessionStation (existing) — the Embodied hat, device sub-mode"]
  end
  EXT["adapter: physical replica — a driver behind a NODE EXTENSION, one generic device protocol"]
  KM --> IO
  JOY --> IO
  EXT --> IO
  IO --> RT
  RT --> IO
  RT --> DEV
  RT --> EFF
  RT --> VIEW
  VIEW --> STN
  DEV -->|"replicated + recorded"| SIM
```

*What the picture shows that prose hid: the device logic never sees a keyboard, a joystick or a replica — only the generic channels; the recorder
never sees the logic — only the state block; and the device itself is an ordinary entity, so carrying, dropping, picking up and late join are the
existing machinery.*

### 2b. The classes

```mermaid
classDiagram
  class DeviceInfo { <<NEW, unmanaged, recorded>> DeviceType (TKB) · Power · ActiveFor (station) }
  class DeviceStateBlock256 { <<NEW, unmanaged, recorded>> header (type, layout version) · fixed byte Memory[...] }
  class DeviceStateBlock1024 { <<NEW, tier>> }
  class IDeviceLogic { <<NEW interface, one per device type>> Describe() → channels · Step(in DeviceIo, ref state, context) · View(ref state) → DeviceHudModel }
  class DeviceRuntime { <<NEW>> finds the holder's active device · runs its logic off the hot path budget · writes the block through the holder's node }
  class DeviceIo { <<NEW>> Buttons · Axes · Lamps · Displays (text grid / glyphs) — the ONE generic model }
  class IDeviceAdapter { <<NEW interface>> Read(ref DeviceIo inputs) · Write(in DeviceIo outputs) }
  class KeyboardMouseAdapter { <<NEW>> }
  class JoystickAdapter { <<NEW>> }
  class ReplicaAdapter { <<NEW, in a node extension>> one generic protocol }
  class DeviceView { <<NEW>> camera rules · DeviceHudModel → drawn by the station }
  class Embarkation { <<existing>> a person's stations: Hands, Back, … }
  class PossessionStation { <<existing>> camera owner · HUD layer · Embodied hat }
  class BlueprintBlackboard1024 { <<existing precedent>> fixed memory, layout owned by its user }
  IDeviceLogic <|.. BinocularsLrfLogic
  IDeviceAdapter <|.. KeyboardMouseAdapter
  IDeviceAdapter <|.. JoystickAdapter
  IDeviceAdapter <|.. ReplicaAdapter
  DeviceRuntime --> IDeviceLogic
  DeviceRuntime --> DeviceIo
  IDeviceAdapter --> DeviceIo
  DeviceRuntime ..> DeviceStateBlock256
  DeviceRuntime ..> DeviceInfo
  DeviceView ..> IDeviceLogic
  PossessionStation --> DeviceView
  DeviceInfo ..> Embarkation : carried in
  DeviceStateBlock256 ..> BlueprintBlackboard1024 : same pattern
```

*What the picture shows that prose hid: one interface per axis of change — a new DEVICE TYPE is one `IDeviceLogic`; a new PANEL is one
`IDeviceAdapter`; neither touches the other or the recorder.*

### 2c. The sequence — a button on a replica changes a display and the camera

```mermaid
sequenceDiagram
  participant P as physical replica (or keyboard / joystick)
  participant A as its adapter
  participant R as DeviceRuntime (player node)
  participant L as IDeviceLogic (the device type)
  participant S as device entity (state block)
  participant V as DeviceView (station)
  P->>A: button 3 pressed
  A->>R: DeviceIo inputs (button 3 down)
  R->>L: Step(inputs, ref state)
  L-->>R: new state · outputs (display lines, lamp 2 on) · effects (e.g. "measure range")
  R->>S: write the state block (replicated, recorded)
  R->>A: DeviceIo outputs
  A->>P: display text, lamp 2 on
  R->>V: the HUD model + camera rules (zoom step 2, thermal)
  Note over S: replay restores the block, the view redraws the device's display as it was
```

## 3. Decisions (leans — for the user's nod)

| # | decision | why |
|---|---|---|
| SD-1 | ⭐ **a device is an ENTITY** of a device TKB type (`DeviceInfo` + its state block); attached to a holder by **boarding one of his carried-item stations** (Hands, Back — the §8m machinery), else **lying in the world** with its own position | the user's requirement; carrying, dropping, picking up, replication and late join already exist for portable weapons (Entity Control §8m) — reused, not rebuilt |
| SD-2 | ⭐ **the generic state block**: `DeviceStateBlock` in size TIERS (e.g. 256 / 1024 bytes), unmanaged, recorded as bytes; a header names the device type and the layout version; the LAYOUT is the device logic's (a struct it overlays) — the recorder knows nothing about it | the user's call; the `BlueprintBlackboard` tier precedent; unmanaged ⇒ no GC churn in replay (CLAUDE.md "new components") |
| SD-3 | **what the world must know is not only in the block**: effects reach the world through EXISTING seams (an order, a report, a designated point, a weapon's control); the block is the device's own memory | the AI and other nodes read typed seams, never a device's private layout |
| SD-4 | ⭐ **the generic device I/O model** — `DeviceIo`: buttons (id, down / up), axes (id, −1…1 or 0…1), lamps (id, off / on / blink), displays (id, a text grid and simple glyphs; a bitmap later) | the user's call: ONE API for every panel, real or virtual |
| SD-5 | ⭐ **adapters ("drivers")**: keyboard / mouse (through the keymap's device scope), joystick / gamepad (a NEW input backend), a physical replica (a driver in a node extension speaking one small generic protocol) | the user's call; the logic never knows which |
| SD-6 | **the logic runs on the node that operates the holder** (the player node; with a gateway seat, the server node) and writes the block there; other nodes see the replicated block | the station already runs there; one writer per block (R-4212) |
| SD-7 | **the view owns the camera while a device is active**: FOV / zoom steps, a sensor mode (eye / night / thermal — the sensor view tied to the device, Sensor View G-10), a reticle; the HUD is a **data model** (`DeviceHudModel`, the `FighterHudModel` precedent) drawn by the station — so an external client can draw it later | the station owns the camera today; a data model keeps the DPS path open |
| SD-8 | **a device sub-mode of the Embodied hat**: the active device's controls are a keymap scope above the person's own keys; one key / a device wheel switches between the devices he carries; "none" returns to the plain soldier | Seats and Hats: one hat, one camera owner |
| SD-9 | **issued devices** = the devices attached to his avatar; slice 1 pre-attaches them in the scenario; creating and attaching at run time uses the same boarding path (later) | the user's call |
| SD-10 | **per-station feature switch** (later slice): which device types a station may use — a station layer under the node features | Node Features is per node today |
| SD-11 | **the existing binoculars / zoom / sight branches** in the station become the first devices (behaviour unchanged), so there is ONE device path, not a device framework beside hard-coded branches | ruling 9 |
| SD-12 | **recording and the debrief**: the block and `DeviceInfo` are recorded; replay restores them and the view can redraw a device's display as it was | the user's call |

## 3b. ⭐ rev 3 — one generic output description, the HUD as gizmos, the device in pause (user, `2026-11-27`)

🔒 User: *"Regarding the device output, I would like this to be some kind of generic description and not specific structure per each device … the
scheme of this structure will be known by the device logic, and the device adapter"* · *"What about drawing the head-up display on the player's
station? Couldn't it be expressed by the gizmo technology, leaving the logic on the brain node on one single place?"* · *"the looking from the
device, including the zoom levels, might need to be enabled also when the simulation is paused … the brain logic of the device might need to tick
also in pause, but it needs to be very well aware that this is a paused mode and not the live. Recording system already supports recording in pause."*

✅ **Measured:** gizmo primitives already travel between nodes — the `DebugPrimitivesBatch` DDS topic, applied by
`Hrot.Network.NED/Gizmos/DebugPrimitivesIngressTranslator.cs` (latest batch per node replaces the buffer), and GizmoMap draws **screen-space**
primitives (`CoordinateSpace.Screen`, `DebugPrimitiveRenderer2D.cs:312`). ⚠ It is a DEBUG path today: per node, not per station, not recorded.
⚠ **R-3114**: *"nothing a unit's AI does takes effect on a paused frame"* — a device ticking in pause must stay inside it (SD-19).

| # | rev 3 decision (supersedes) | why |
|---|---|---|
| SD-14′ | ⭐ **ONE generic output description for every device** — `DeviceOutputs` (unmanaged, recorded): a schema id + version, then an opaque bounded payload whose LAYOUT the device logic and the adapter share (the transport and the recorder never look inside) — ⭐ its standard content is a **display list**: screen-space primitives (text, lines, rectangles, symbols, a bitmap later) in the device's own display coordinates, plus lamp values and named fields; sent on change, in a few parts | the user's call: one transport for any device; a display list is generic AND drawable by any consumer |
| SD-17 | ⭐ **the HUD on the station = that display list drawn as SCREEN-SPACE GIZMOS** by one generic `DeviceHudProjector` — no device-specific drawing code on the player node; the logic stays on the brain, in ONE place | the user's call; GizmoMap already draws screen-space primitives |
| SD-18 | **a physical replica's adapter** renders the SAME display list onto its LCD and maps the lamps to its LEDs; the keyboard adapter's on-screen panel is the gizmo HUD itself | one description, every consumer |
| SD-19 | ⭐ **local bindings**: a display-list element may name a LOCAL value the player node fills each frame — heading, pitch, the current zoom, the point under the reticle — so a compass tape or reticle follows the trainee's head without a round trip (SD-15) | a display that must move with the camera cannot wait for the brain |
| SD-20 | ⭐ **the device ticks in PAUSE, knowingly**: the device runtime runs every frame (not only when time advances), and the behaviour gets `DeviceTick.Paused`; paused, it may change only the DEVICE's own state and outputs (pages, menus, zoom step, sensor mode, reticle, a measurement shown from the frozen scene) — ⛔ never a world effect (no order, no designation, no shot: refused, or queued for the first live frame, the device's choice and shown on its display) | R-3114 holds for the world; the instructor's "observe the frozen scene" works; the recorder already records in pause |
| SD-21 | **the replay draws the HUD** from the recorded `DeviceOutputs` (behaviours do not run in a replay) — the trainee's display exactly as it was | the debrief sees what the trainee saw |
| SD-22 | **the gizmo transport becomes a production path**: per STATION addressing (the `StationId`), on change, never a debug-only buffer | ⚠ to design with ui: today's `DebugPrimitivesBatch` is per node and debug-only — reuse its primitives and renderer, give device outputs their own topic (`DeviceOutputs` above), never the debug one |

✅ SD-20 APPROVED as ruling **R-4300** (user `2026-11-27`: *"Yes, record that exception."*) — an exception to R-3114 for device-local state only.

## 3c. Local bindings — how a gizmo reads a value the player node supplies (SD-19, investigated `2026-11-27`)

🔒 User: *"the idea of providing a local value to the gizmo renderer is interesting … What would it take? How to teach the gizmo renderer to take
some external provided value, and how to encode what value, what gizmo should be reading."*

✅ **Measured — the gizmo library already has both halves:**

| precedent | where | what it proves |
|---|---|---|
| **meta-primitives** — non-visual primitives that configure the renderer (`ContextMenuBinding`, `InputCaptureBinding`, `MainMenuBinding`, `LayerControlMask`) | `GizmoMap.Contracts/Primitives/DebugPrimitiveShape.cs` (11–14) | a binding can travel as ONE more primitive in the same batch, no new channel |
| **two-pass resolve** — `SpatialAnchor` primitives are cached in pass 1 and the primitives that reference them are resolved in pass 2, in place, on the receiving client | `GizmoMap.Presentation/Rendering/DebugPrimitiveRenderer2D.cs:70-90` | the renderer already late-binds data the sender did not know |
| **interned strings** — long text and JSON travel as a `StringHash` resolved through an intern map | `GizmoTextDraw.cs:28-35`, `DebugGizmoLayer.cs:80` | a format template can travel the same way |
| the primitive is a fixed **64-byte** union (a 40-byte payload per shape) | `DebugPrimitive.cs` | a binding fits in one primitive |

⭐ **The mechanism — a `ValueBinding` meta-primitive** (a new `DebugPrimitiveShape`), evaluated in the renderer's existing two-pass resolve:

| field (in the 40-byte payload) | meaning |
|---|---|
| `ValueKey` (uint) | WHICH local value — the FNV-1a hash of a dotted name from a fixed vocabulary (below) |
| `Mode` (byte) | WHAT it does to its group: **Translate** X / Y (value × scale + offset), **Rotate**, **Scale**, **Format** (text), **ShowIf** (visible when the value is in [min, max]) |
| `GroupId` (ushort) | the primitives it moves — those of the same batch whose `SubElementId` equals it |
| `Scale`, `Offset`, `Min`, `Max` (floats) | the mapping, the clamp, the wrap (a 0–360° heading tape wraps) |
| `FormatHash` (uint) | for Format: an interned template, e.g. `"{0:000}°"` |

- **Pass 1** collects the bindings (as it collects anchors); **pass 2** asks the player node's **`ILocalValueSource`** for each key once per frame and applies
  the mode to its group — array-backed, 0 B per frame.
- **The vocabulary** is a short, documented contract the device logic picks from: `camera.heading`, `camera.pitch`, `camera.fov`, `camera.zoomStep`,
  `reticle.range` (a local ray under the reticle), `station.paused`, `time.sim`. An unknown key hides its group and logs once.
- **Who supplies values**: the possession station (camera, reticle, pause); a physical replica's adapter may add its own (an encoder on the
  device) through the same interface.
- **A compass tape** = a ruler of lines and labels in one group + a Translate-X binding on `camera.heading` with wrap 360 + a clip window —
  ⚠ a **clip rectangle** meta-primitive (scissor) is the one more piece it needs.
- **Other renderers** (the Unreal client through the gateway) implement the same small binding table with their own camera — the binding is DATA,
  not code.

**What it takes** (a ui-lane slice inside SD-S1): the new shape + payload in `GizmoMap.Contracts`; the bind step in the two-pass resolve; the clip
rectangle; `ILocalValueSource` + the vocabulary; the station as the first source; a frame rail (a fake source moves a bound group by an exact
offset; 0 B per steady frame); one sample — the rangefinder's compass tape and range readout.

## 3d. A device's laser — the beam and the spot (user, `2026-11-27`)

🔒 User: *"Sometimes we need to render the laser beam going from the device … what part will be responsible for giving or controlling the beam
renderer? And how to make sure it responds to trainees' inputs … the laser beam is a global entity which needs to be displayed by every other node
if their sensor allows for it … the hit testing … needs a raycast … a laser spot … is seen by the observation devices having a sensor capable of
seeing it … so it needs to be replicated."*

| # | decision (lean) | why |
|---|---|---|
| LS-1 | ⭐ **the emission is a recorded, replicated component on the DEVICE entity** — `LaserEmission` (unmanaged): on / off, its band, the pointing (origin on the device, direction), the **authoritative spot** (the point it hits, the entity and part it hits, or none). ⛔ not a separate beam entity | the device already carries the pose, the owner and the recording; a second entity would have to be kept in step with it every frame |
| LS-2 | ⭐ **the pointing comes from the device's own orientation**, and that comes from the trainee's input through the device I/O: the mouse look in virtual mode, an orientation sensor (an axis set) on a physical replica — the SAME path either way | one rule for every panel (SD-4); turning the real device and moving the mouse are both axes |
| LS-3 | ⛔ *superseded by LS-14 (the muscle computes it, not the brain)* ⭐ **ONE node computes the spot**: the node that runs the device's logic (the brain, rev 2) traces the beam each frame while it is on — the existing world trace (`IWorldQuery`, the sight purpose: what stops light) plus the bodies on the line — and writes the spot; sent on change (a distance and rate threshold), smoothed on the receivers | one authoritative spot that every other system reads — never N nodes disagreeing about where the light is |
| LS-4 | ⭐ **the trainee's own view is predicted locally**: on his player node the beam and spot are drawn from his LOCAL look and a local trace, so they follow the mouse with no round trip; every other node draws the replicated emission | the same split as SD-15 (look stays local); the trainee never sees his own beam lag |
| LS-5 | ⭐ **visibility is ONE shared rule** — "is this band visible to this viewer's sensor?" (eye / night vision / thermal, and later a device's own optic) — called by HROT's 3-D map, by the external IG through the gateway, and by the AI's perception | the shared look-library pattern (`Hrot.Look`, renderer-facing rules in one place) |
| LS-6 | **the spot is a replicated point other systems may READ** — an AI observer's perception (a sensor that sees the band), the after-action review; what any weapon model does with it is owned by the weapons design, not here | the device only emits; consumers read through their own designs |
| LS-7 | **recorded**: the replay draws the beam and the spot as they were | `LaserEmission` is a recorded component (SD-12) |
| LS-8 | **in pause** (R-4300): the trainee may point and see where it would fall on the frozen scene; the emission is a device-local display — ⛔ no world effect is raised from a paused frame | R-4300 |

### 3d-2. An AI vehicle's designator, and tracing in pause (user, `2026-11-27`, investigated the same day)

🔒 User: *"What if the target designator is mounted on a vehicle which is driven by the AI with no actual device, purely as a game logic? Does it
make sense to create a device entity in this case? … If it is an overkill or problematic, then the independent laser beam entity is still an
option. Also not clear if the ray-cast queries are working in pause when sim time is frozen."*

📐 Measured (graph `search_graph` `.*(Hit|Body|Spatial|Proximity)(Index|Grid|Hash|Volume).*` Class — 13; grep `laser|designat` over the toolkits —
only the squad's *designated target*, no laser code exists):

| claim | code | design basis |
|---|---|---|
| a vehicle already carries its turret, mounts and sensors as PART entities | ✅ `PartMetadata.cs:14` (parent + instance id); `PartIndex.cs:10-22` (*"a weapon mount, a turret, a sensor, a behaviour-owned sensor"*) | Q79 §0.10 (parts reuse the instance id on the wire) |
| the terrain trace is a plain synchronous query — no per-tick cache | ✅ `TerrainWorldQuery.cs:89-130` (`SightBlocked`, `Trace` read `TerrainWorld`, the doors, the clouds) | — |
| the entity ray SOLVER does not run in pause | ✅ `RaycastSolverSystem.cs:47` `[TickMode(WhenTimeAdvances)]`, request/result EVENTS | R-3114 |
| the entity grid is rebuilt only when time advances, but can be queried synchronously | ✅ `SpatialHashSystem.cs:22` `WhenTimeAdvances`; `SpatialHashGrid.QuerySegment` (`SpatialHashGrid.cs:230`), already read that way by `BallisticsSystem` | — |
| a system that must work in pause declares `EveryFrame` and stays 0 B | ✅ precedent `UnderFireSightSystem` (`UnderFireSight.cs:217`, *"a scenario load while PAUSED must still get its sight"*) | R-4300 |

| # | decision (lean) | why |
|---|---|---|
| LS-9 | ⭐ **the laser system is generic over `LaserEmission`, whoever carries it** — it does not know about devices, I/O or HUDs. A device is ONE way to set the pointing; an AI is another | one laser path (ruling 9): the spot, the replication, the visibility rule (LS-5) and the recording are the same whether a trainee or an AI lases |
| LS-10 | ⭐ **an AI vehicle's designator is a PART entity of the vehicle** (`PartMetadata`, like its sensor or weapon mount), carrying `LaserEmission` and nothing else — no `DeviceIo`, no `DeviceOutputs`, no HUD. The vehicle's AI sets its pointing (aim at the ordered target, through the part's own orientation, as a turret aims); the laser system does the rest | ✅ not overkill: parts are the existing, indexed (`PartIndex`), replicated way a vehicle carries a sub-system, so this adds ONE part per designating vehicle and no new mechanism. ⭐ The trainee's device mounted on a vehicle station is the same shape — a child entity with `LaserEmission` — so the two cases meet by construction |
| LS-11 | ⛔ **not an independent beam entity** — and ⛔ not `LaserEmission` on the vehicle entity itself | a beam entity has no pose of its own (it must copy the emitter's every frame) and a lifecycle per lase (spawn / despawn churn on the wire). On the vehicle itself it would work for one emitter, but loses the mount's own orientation and offset, and a second emitter (a sight + a designator) cannot exist — the part costs about the same and has neither problem |
| LS-12 | ⚠ *the node is the MUSCLE (LS-14)* ⭐ **tracing in pause works, with one rule**: the laser step is `[TickMode(EveryFrame)]` (it must, for LS-8); it traces the terrain with `IWorldQuery` (synchronous, ✅ works frozen) and the bodies with a SYNCHRONOUS `SpatialHashGrid.QuerySegment` + a live narrow phase — ⛔ never `RaycastRequestEvent`, whose solver does not run in pause. It writes nothing that has a world effect on a paused frame (R-3114 / R-4300): in pause it only refreshes the drawn spot | ✅ the trace reads only state that does not change while time is frozen. ⚠ The one gap: the entity grid is the last advanced tick's, so an entity MOVED or PLACED by an edit during pause is found by the beam only after the next step — said, accepted for a preview (LS-8); a rail pins the frozen-world case |
| LS-13 | an AI-driven designator does NOT lase in pause | its pointing is set by the AI, which is frozen (R-3114); the last replicated emission stays drawn. Only a trainee's device (R-4300) re-points in pause |

### 3d-3. The brain asks, the muscle traces — and the part id space (user, `2026-11-27`)

🔒 User: *"the brain might not have direct access to the terrain with raycasts … it needs to send a request to the muscle node. So do not expect
the answer coming immediately. Only the muscle node knows the current state of the terrain in details necessary for the raycasting."* · *"is
that … entity indexing working for different kind of child entities … the sensors have some index range … the devices might need different
range … the index is shared, it's not per subentity type, correct?"*

| claim | code | design basis |
|---|---|---|
| the brain node HAS the static terrain world (every ECS node loads it) | ✅ `RoleLoadRequirements.cs:33-39` (`UniversalParts`) | R-182 / R-183, `DESIGN_Terrain_World.md` §5 |
| ⭐ but hitting BODIES lives on the muscle: the ray solver and ballistics are registered by the SimHost combat module | ✅ `Hrot.SimHost/Modules/CombatModule.cs` (the only production registrar) | HL-2 (people hit by their body parts — the muscle's pose) |
| perception's contact memory is muscle-only, sent to the brain as changes | ✅ `DESIGN_Sensors_And_Doctrine.md:63, :453` | R-185 |
| the part id is ONE space per parent, shared by every kind | ✅ `PartMetadata.cs:14-17` (parent + `InstanceId`); `PartIndex` sorts a parent's parts by id, ties in entity order | Q79 §0.10 (the wire keys a part by root + descriptor + instance) |
| kinds are kept apart partly by RANGE, partly by the kind's own component | ✅ TKB sensors `1000+i` (`SensorChildFactory.cs:25`), turrets `2000+i` (`TurretParts.cs:20`); weapon mounts `0..n` (`CombatTkbTranslator.cs:122`) and behaviour EQS sensors `1..` (`EqsChildSensor.cs:129`) OVERLAP and are told apart by `WeaponMountInfo` / `EqsSensor` | — |
| ⚠ some lookups trust the range alone | ✅ `TurretParts.cs:66`, `RaycastSolverSystem.cs:371`, `TurretPoseSource.cs:45` match `InstanceId == 2000` with no kind check | ⛔ searched, none found — the ranges are convention, written nowhere |

| # | decision (lean) | why |
|---|---|---|
| LS-14 | ⭐ **the MUSCLE computes the spot; the brain only sets the pointing.** Split the component the usual intent / status way: `LaserPointing` (on / off, band, direction — written by whoever points: the trainee's device logic or the AI) and `LaserSpot` (the hit point, entity, part — written by the muscle that simulates the emitter's body, every frame it is on). ⛔ no request / reply: the muscle reads the replicated pointing and answers through the replicated spot, ONE hop late | ✅ the muscle owns the bodies and their poses (HL-2) and the combat ray path; one writer per component. ⭐ The latency is hidden where it matters: the trainee's own beam is predicted locally (LS-4); everyone else sees the spot one hop late, like any other replicated state |
| LS-15 | ⭐ **the laser step runs on the muscle `[TickMode(EveryFrame)]`**, LS-12's synchronous trace, there | the muscle is where the entity grid and the bodies are |
| LS-16 | ⭐ **devices and designators take their own part-id range: `3000+i`**, written as a named constant beside the others; and their lookups check the kind's component too (`LaserPointing` / the device component), never the id alone | ✅ ids are one space per parent; a range keeps them off the turret's `2000` that three lookups trust blindly. ⭐ Both, because the id alone is how a collision would hide |
| LS-17 | file a tracker row: **write the part-id ranges down in ONE place** (a `PartIds` table: mounts `0..`, EQS `1..` by component, sensors `1000`, turrets `2000`, devices `3000`) and give the turret lookups a kind check | ⛔ today the ranges live only in three constants and the mounts / EQS overlap is safe only because every lookup of theirs filters by component |

⚠ Not measured: whether the brain's pointing replicates to the muscle on a PAUSED frame (R-4300 needs it for a trainee re-pointing in pause). The
trainee's own view does not depend on it (LS-4, local); everyone else's does ⇒ measure in SD-S1 before building LS-8 for other viewers.

⚠ To measure first: whether the trainee's look direction already reaches the brain node in the control frame (the frame carries aim fields —
`OperatorControlFrameEvent`), and the cost of one trace per lasing device per frame on the brain node.

## 3a. ⭐ rev 2 — the device runs on the BRAIN (user, `2026-11-27`)

🔒 User: *"Could we reuse the brain blackboard components and its dynamic allocation mechanism directly, meaning the device logic will run on the
brain … make the status in the form of an allocated record in the Blackboard made available for the blueprints … majority of the logic of the
device will be for sure written in C# … if we define that the device logic runs on a brain node then only all the inputs to it and outputs from it
towards the player are replicated … I lean to events … for the output side, the status structure is probably better for late join"*.

✅ **Measured — it already exists: UNIT MEMORY** (Q87, **R-237**, `FDP/Toolkits/Fdp.Toolkits/Behavior/UnitMemory.cs`): a `[UnitMemory]` struct is
ONE slot in an entity's blackboard store, keyed by its TYPE, allocated on first touch (appended mid-tick, nothing moves), guarded by a layout
hash (a changed struct or an older recording re-initialises, never mis-reads), **recorded, not scenario-saved**, shared by every behaviour of that
entity — reached by C# (`UnitMemory.Ref<T>`), by a BTree / HSM `ref` param, and by blueprints through the **`GetShared` / `SetShared`** nodes
(R-237; present in the blueprint compiler's node set — ⚠ end-to-end on a device not yet measured).

| # | rev 2 decision (supersedes) | why |
|---|---|---|
| SD-2′ | ⭐ **the device's logic state is a `[UnitMemory]` struct per device type** (e.g. `RangefinderState`) in the DEVICE ENTITY's blackboard — ⛔ no new `DeviceStateBlock` | ruling 9: the dynamic allocation, the layout guard, the recording and the blueprint access already exist |
| SD-6′ | ⭐ **the device entity has a brain; its logic is a BEHAVIOUR** (C# first — a BTree / HSM, or a blueprint) run as its task on the brain node; it reads its inputs, writes its unit memory, raises its effects through the existing seams | the user's call; blueprints can then operate a device too |
| SD-13 | **inputs → the brain as EVENTS** (the user's lean): `DeviceButtonEvent {device, button, down}` reliable and ordered; `DeviceAxisEvent {device, axis, value}` sent on change (dead-band, ≤ a rate) with LATEST-WINS per (device, axis) | few event types cover every panel; an axis is continuous, so only its newest value matters |
| SD-14 | ⭐ **outputs → the player as REPLICATED STATE**, not events: a `DeviceOutputs` (unmanaged — lamps, the fixed text grid of each display, the HUD model's fields, the view mode: zoom step, sensor mode, reticle) written by the behaviour, sent ON CHANGE, split into a few parts (lamps · displays · view) so one change sends one part | ⭐ late join comes free (the last value is kept); ⭐ the REPLAY shows exactly what the trainee saw from the recorded outputs, without running the logic (behaviours do not run in a replay); events re-sent on join would rebuild what a kept state already is |
| SD-15 | ⭐ **look and camera motion stay LOCAL** on the player node (mouse look, smooth zoom between steps, head movement) — the brain decides the device's MODE (which zoom step, which sensor), never the per-frame view | a round trip to another node per mouse move would lag; in the editor (one process) it is all local anyway |
| SD-16 | **the brain must tick an ACTIVE device every frame** (exempt from the brain cadence, CE-2382), an idle device rarely | a button must answer within a frame or two |

⚠ What to measure first (slice 1): `GetShared` / `SetShared` on a device entity's unit memory end to end; the brain cadence exemption; the
round-trip delay player node → brain → player node on a two-process run (the editor has none).

## 3e. ⭐ The independent review (`2026-11-27`) — findings and leans

A separate session reviewed the device infrastructure (§3d's beam sections were out of its scope). Spot-verified here:
`BrainTickSystem.cs:59` is `WhenTimeAdvances` · `StatefulSlotScope.cs:18` (*"its only consumer, GetShared/SetShared, went with CE-440"*) ·
`OperatorControlFrameEvent` (`Possession.cs:83`) carries the station's axes and COUNTED presses (`ShotSeq`, `CancelSeq`, `AmmoSeq` — *"a counter,
not a level, so a click … is never lost"*) · `RecorderTickSystem` checkpointing mode: *"a paused cluster writes nothing"*.

| # | finding (severity) | lean |
|---|---|---|
| RV-1 | 🔴 nothing ticks a device in pause: the brain tick is `WhenTimeAdvances` | ⭐ a **`DeviceTickSystem`, `EveryFrame`, on the brain**: it runs ONLY device behaviours, as a per-entity phase over device entities, with a `Paused` flag in the context; on a paused frame it may write only the device's OWN unit memory and outputs (R-4300's boundary), never a request to the world. Its input ingress is every frame too. ⚠ to measure in SD-S1: that the control frame reaches the brain on a paused frame |
| RV-2 | 🔴 the UML, §5's SD-S1 and §6 describe rev 1 | ⭐ **rev 4 redraws all three diagrams** for what rev 2/3 build (the brain behaviour, the input path, `DeviceOutputs`, the projector, `ValueBinding`, `DeviceTickSystem`) and re-scopes SD-S1 — after the leans here are approved, so it is drawn once |
| RV-3 | 🟠 blueprints cannot reach unit memory today (CE-440 removed the nodes) | ⭐ slice 1's device logic is **C# / BTree** (the C# accessor exists); a unit-memory BLUEPRINT node is its own later row (restore it on the R-237 shape). R-237 gets a known-rot note |
| RV-4 | 🟠 two parent links: a carried device boards a seat (a full entity), a mounted one was a `PartMetadata` part (no network id, no lifecycle, keyed by its root) | ⭐ **a device is ALWAYS a full network entity, and is ATTACHED BY BOARDING a station** — a person's Hands / Back, or a vehicle's device station (a designator's mount is a vehicle station). ⇒ input events address it, it can be dropped / destroyed, its authority is its own. ⛔ supersedes LS-10 / LS-16; the AI vehicle's designator is a device entity boarded in the vehicle (no I/O, no HUD — LS-9 unchanged). LS-17 (write the part-id ranges down) stays as a row of its own |
| RV-5 | 🟠 `DeviceButtonEvent` / `DeviceAxisEvent` duplicate the control frame | ⭐ **extend `OperatorControlFrameEvent` with a fixed DEVICE section**: the active device's id, N axes, and per-button press COUNTERS (the frame's own `ShotSeq` pattern — so presses survive latest-wins). ⛔ no second input path. A replica panel's extra controls fill the same section through its adapter |
| RV-6 | 🟠 zoom has two owners (the station today, the device output in SD-14) | ⭐ the **device output owns the zoom STEP**; the station only smooths the camera between steps. SD-11's migration says so |
| RV-7 | 🟠 SD-17 (a local projector, no gizmo wire) vs SD-22 (gizmo DDS per station) | ⭐ **keep SD-17**; SD-22 is limited to EXTERNAL renderers (an outside IG drawing the HUD) |
| RV-8 | 🟠 several stations on one node (the editor as server) | ⭐ the projector draws only the active device of THIS view's station (keyed by `StationId`); a rail with two stations on one node |
| RV-9 | 🟠 objectives cannot read an opaque payload | ⭐ `DeviceOutputs`' FIELDS are named (a name hash → value table, unmanaged) and a checks cue reads a field by name; each slice lists its expect rows; SD-S5 gets a scenario |
| RV-10 | 🟠 paused frames are recorded only in FULL mode, and all share one sim time | ⛔ *corrected by §3f D-6 (frames are wall-clock stamped)* ⭐ say it: a debrief of a paused device change needs FULL recording; the scrubber addresses paused frames by frame, not time — ⚠ measure the replay time axis in SD-S1 before promising more |
| RV-11 | 🟠 output authority / descriptors unstated | ⭐ the outputs are **separate components** (lamps · fields · display list), each its own descriptor in the device entity's descriptor group, **owned by the node that owns the device entity** (the brain running its holder) — one writer each |
| RV-12…16 | ⚪ minors: reuse `CadenceMode` (`BehaviorComponents.cs:59-66`) for SD-16; typos (§3b "SD-19" → SD-20; Q-6 answered by R-4300; §6's one-writer line); unbuilt dependencies named per slice (a device Take / Drop verb, a gamepad input source, a clip-rectangle primitive); a 0 B rail for the projector and the adapters too; `FighterHud` routed through the display list in a later slice | ⭐ accept all |

⚠ Not verified by the review (carried into SD-S1's first measurements): the control frame and replication on a paused frame · whether a
device entity can carry its own `BehaviorState` / blackboard (RV-1 runs device behaviours ON the device entity) · the round-trip delay · the
bundled Raylib gamepad API · `DESIGN_Player_HUD_And_Debrief`, Sensor View G-10, Air Combat §4d not yet read for contradictions.

## 3f. ⭐ The user's answers to §3e, and the measurements made BEFORE building (`2026-11-27`)

🔒 User: *"device entity cannot be boarded like a passenger, it can be attached to it"* · *"the unit memory access from the blueprints should be
likely issued like a tracked item … keep the infrastructure supporting that option later"* · *"how the head-up display on the player node knows
how to render the outputs … we do not desire to hard-code the rendering logic on the player node stations … it makes no sense to use gizmos for
the external image generator and not for internal 3D map. It should be a unified path. The head-up display should be keyed like the seat, the
same way as clients of the DPS, and the gateway is using"* · *"The recording is stamping the records with wall clock time … So we can replay
also happening during the pauses exactly"* · *"No measurements should go to the first implementation step … needs to be done prior
implementation and should go to the design"* · *"As most of the device logic will be hard-coded in C#, what shape would it have?"* · the other
§3e leans APPROVED.

### Measured (code, `2026-11-27`)

| question | answer | evidence |
|---|---|---|
| is there an attachment of one network entity to another that is not a passenger? | ✅ **the `Station` seat role**: a weapon ENTITY is mounted on a host by occupying a seat of role `Station` (it stands at the seat's place and facing; it is no passenger — no crew, no pose); a host type's `StartWith` fills it at placement | `DESIGN_Weapon_Systems.md` §2 (`SeatDto` role `Station`, `Accepts`, `StartWith`; *"a weapon in a Station seat stands at the seat's place, facing"*); DPS `SeatRoleStation` (`DpsTypes.idl:105`). graph `.*(Attach\|Attached\|Mounted\|Carried\|Holder).*` Class — 60, no entity-to-entity attach component |
| do control frames reach the brain on a PAUSED frame? | ✅ **yes on the wire, ⛔ no consumer**: `NedReplicationModule` is `EveryFrame` with no pause gate, so the interaction ingress publishes `OperatorControlFrameEvent` every frame; its only consumer `PossessionSystem` is `WhenTimeAdvances` | `NedReplicationModule.cs:55, :719` (grep `paus` → 0); `InteractionTransport.cs:139`; `Possession.cs:197` |
| can a device entity run its OWN brain? | ✅ **yes**: the brain tick walks every entity that has an owned `BehaviorState`, by brain tier — not only units ⇒ the device's logic and its `[UnitMemory]` live ON the device entity (self-only, R-4212 — no cross-entity write) | `BrainTickSystem.cs:176` (`WithOwnedWhen<BehaviorState>`), `:229` (tiers) |
| does the recorder separate paused frames? | ✅ **yes**: every frame carries a WALL-CLOCK stamp beside sim time, so a replay can play a pause back exactly (FULL mode). ⚠ CHECKPOINTING mode still writes nothing while paused | `RecorderTickSystem.cs:153, :175` (`wallClockTicks`); `:64-66` |
| how does the gateway key a station's HUD? | ⭐ **one DDS partition per station**; a station's gizmos go out as `GizmoFrame` keyed by **`Producer` (the station's partition) + `Layer`**, a complete list of primitives per sample; the gateway runs HROT's own gizmo projectors ON the station node. ⚠ the DPS primitive is world-space only and meta primitives are not sent | SimNetGateway `DD-017` §3.2a, `DPS-Client-Guide.md` "Gizmos" |
| a gamepad source? | ✅ the bundled `Raylib-cs 7.0.2` exposes `IsGamepadAvailable`, `GetGamepadAxisMovement`, `IsGamepadButtonPressed` … ; HROT calls none yet | the package's exports |
| how is a hand-written C# behaviour shaped? | ⭐ a blackboard struct + a `[BehaviorResolver]` (params → block) + a `[BTreeDefinition]` whose tree is often ONE stateful action | `AdvanceToBehavior.cs` — `.StatefulAction(bb => bb.Advance, bb => bb.State, PostureNodes.AdvanceAndAttack)` |
| the round trip player → brain → player | ⚠ **derived, not timed**: the frame leaves on the player's next egress, is applied by the device tick on the brain's next frame, and the changed outputs leave on that frame's egress ⇒ two transport hops + at most one brain frame. ⏳ a timed number needs a live cluster run — done before SD-S1 starts, not in it | the three systems above |

### Decisions (leans)

| # | decision (lean) | why |
|---|---|---|
| D-1 | ⭐ **a device is ATTACHED to its holder through a `Station` seat** — the weapon-entity mechanism: a person's Hands / Back stations and a vehicle's device station are seats of role `Station` that `Accepts` the device type; `StartWith` pre-attaches it (an AI vehicle's designator). ⛔ not a passenger seat, ⛔ not a new attach component. ⛔ supersedes SD-1's "boards as a passenger" wording and LS-10 / LS-16 | ✅ one existing attachment for entities that ride on another (ruling 9); a weapon already takes exactly this path, the DPS contract already names `SeatRoleStation` |
| D-2 | ⭐ blueprint access to unit memory is **CE-4705** (its own row, later); the device's state is a `[UnitMemory]` struct, so a restored node reaches it with no device change | the option stays open by construction |
| D-3 | ⭐⭐ **the HUD is ONE gizmo layer per station, drawn by every renderer**: the internal 3-D map and the external IG (through `GizmoFrame`, `Producer` = the station) draw the same primitives. ⭐ **No device-specific code on the player node.** The device TYPE carries a HUD **template** (data, in its TKB type: gizmo primitives — text, lines, boxes, sprites — whose values are BOUND to the device's named output fields); the device logic on the brain writes only the FIELDS (small, replicated, recorded). ONE generic projector on the station node fills the template from the fields (and the local `ValueBinding`s, §3c) into the station's `DeviceHud` layer | ✅ the rendering is data, not code, so a new device adds a template, not a renderer; the wire carries fields, not a picture; replay and late join need only the fields (the template is static). ⛔ rejected: the brain emitting the primitives itself — the HUD's drawing would live in device C#, and the whole picture would cross the wire on every change |
| D-4 | ⭐ **the HUD layer is keyed by the station** — `StationId` on HROT's player node, the station's partition (`Producer`) on the DPS wire; a node with several stations produces one layer per station | the same key the gateway already uses (DD-017 §3.2a) |
| D-5 | ⚠ **the DPS gizmo contract grows a SCREEN-SPACE primitive flag** (a HUD is drawn on the screen, not the terrain) — a SimNetGateway change, filed there when SD-S1 is dispatched | the contract is world-space only today |
| D-6 | ⭐ RV-10 corrected: **paused frames replay exactly** (wall-clock stamped) in FULL recording; CHECKPOINTING records nothing while paused — a debrief of paused device use needs FULL mode | `RecorderTickSystem.cs:153` |
| D-7 | ⭐ **the device runs its OWN brain on the device entity** (a device brain tier); `DeviceTickSystem` (RV-1, `EveryFrame`) ticks that tier, and it reads the control frame's device section itself (the frame reaches it in pause; `PossessionSystem` does not run then) | measured above; self-only writes, so the parallel rules hold by construction |
| D-8 | ⭐ **the C# shape: one device = one behaviour of ONE stateful action** — a `[BTreeDefinition]` tree `.StatefulAction(bb => bb.Params, bb => bb.State, DeviceNodes.Rangefinder)`; the action reads the device's control section, steps its modes (an enum in its `[UnitMemory]` state — off · standby · ranging · menu …), writes its output fields. ⭐ a device with real mode structure may be a hand-written HSM instead; the infrastructure does not care | ✅ the library already writes behaviours this way (`AdvanceToBehavior`); the tree adds nothing for a device, the one action is the device |
| D-9 | ⭐ **measurements happen BEFORE a slice, in this design** — the timed round trip is the one left (⏳ above); every other question SD-S1 carried is answered here | 🔒 user |

⚠ Rev 4 (§3e RV-2) redraws the diagrams on D-1 … D-8 once they are approved.

## 4. Rejected — one line each

- **Typed components per device type** — the user's concern holds: dozens of types, each a component, descriptor and translator; the block keeps the recorder ignorant.
- **Device logic inside the station's code** (today's binoculars) — every new device would grow one class; no external panel could drive it.
- **A managed state object** — every change would allocate in replay (CLAUDE.md "new components").
- **A new `DeviceStateBlock` (rev 1)** — unit memory already is the generic, lazily allocated, recorded, blueprint-reachable record (R-237).
- **Outputs as events only** — late join and replay would need the state rebuilt from them; a kept replicated state is that state.
- **One protocol per physical device** — the user's call: drivers adapt, the logic sees one model.

## 5. Slices — each with its feature-test scenario (R-4277)

| slice | builds | scenario · what its objectives judge |
|---|---|---|
| **SD-S1** the framework + binoculars / laser rangefinder | SD-1…SD-9, SD-11, SD-12: the device entity, the state block, `DeviceIo`, the runtime, the view, the keyboard / mouse adapter, a LOOPBACK replica adapter for tests, the binoculars and a rangefinder device; devices pre-attached in the scenario | `dv-observe-and-range`: a trainee (driven over HTTP in the test) switches to the rangefinder and measures named targets — objectives: each target's measured range within the device's accuracy; the device's display shows it; a dropped device lies in the world and another trainee picks it up; replay shows the same display |
| **SD-S2** joystick / gamepad + the drone ground station | the joystick adapter; the drone operator device: the operator's body stays on the ground, a control link to the drone, HUD with battery / signal / link state from the drone's simulation | `dv-drone-operator`: the trainee flies the drone to a point and back; objectives: the HUD's battery and signal match the simulation; losing the link is shown |
| **SD-S3** the pilot station device + digital target hand-off | the pilot device: flight HUD, a structured target message (9-line) received digitally from another trainee's device and shown on the pilot's display and map | `dv-target-handoff`: one trainee sends a target message from his device, the pilot station shows it and the point on its map |
| **SD-S4** the weapon aiming unit | a device that controls a manned weapon system (optics, zoom, sensor modes, the operating procedure) — its weapon effects stay with the weapon systems design | `dv-aiming-unit`: the trainee runs the procedure and the weapon system reaches its ready state |
| **SD-S5** per-station features + run-time issue | SD-10; creating and attaching devices at run time | — |

## 6. Rules this respects

- **R-4212 parallel**: one writer per block (the holder's operating node); effects through commands / existing seams.
- **Recorded components unmanaged**: the block and `DeviceInfo` are fixed-size.
- **Hot path**: the runtime steps only the ACTIVE device of each operated holder; adapters fill reused buffers (0 B per steady frame, a rail).
- **R-4295**: the wire carries its own device types; translators convert.
- **R-4277**: every slice ships its scenario with expect rows.

## 9. Open questions — with leans

| # | question | ⭐ lean |
|---|---|---|
| Q-1 | ~~the block's tiers~~ | ⛔ superseded by SD-2′ (unit memory sizes itself) |
| Q-2 | the replica protocol's transport | a node extension listening on a local socket (JSON lines or a small binary frame), the same model as `DeviceIo`; DDS later if a replica must sit on another machine |
| Q-3 | the joystick backend | Raylib's gamepad API first (already linked); a wider HID layer only if a device needs it |
| Q-4 | which lane | **ui** builds SD-S1 (the station, the view, the keymap); the entity and the state block with **backend**, cross-lane allowed |
| Q-6 | ⭐ SD-20 — may a device behaviour tick on a PAUSED frame, changing only its own state and outputs (an exception to R-3114 for device-local state)? | yes — the instructor's frozen-scene observation needs it; world effects stay refused in pause |
| Q-7 | the display list's size bound | a few KB per device, split into parts, sent on change — measured in slice 1 |
| Q-5 | the device wheel's key and the HUD layout | the ui lane's mockup first (R-4143: every UI design gets a mockup before it is built) |
