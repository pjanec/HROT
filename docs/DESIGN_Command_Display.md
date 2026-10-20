<!--STATUS
state: LIVE
updated: 2026-10-20
build-state: BUILT for D1–D3 (CE-1192, CE-1191); D4 (the picture and the log on a node with no brain) ✅ approved 2026-10-19 (R-2058), behaviors (the log, CE-2301) and backend (the picture's replication) build it; ui's one part — absence shown — BUILT 2026-10-19 (§7, D4 row) · §8 the Sectors layer BUILT 2026-10-20 (CE-1225, §8a)
current-answer: §1 what · §2 INVENTORY · §3 classes · §4 sequence · §5 modules · §6 decisions (leans) · §7 as built · §8 the Sectors layer (CE-1225)
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md — OWNS CommandPicture, the reports, SquadReportLog and the staff (§6, §7c); this design only DRAWS them
  - docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md — OWNS sectors and NoFire areas (§13, §13a DA-24) and EngagementArea
  - docs/designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md — §13 the lane split (UI-5, UI-6), §12.4 PC-11 the report log
  - docs/DESIGN_Terrain_Combat_Tuning.md — §5b the squad gizmo this sits beside (SquadGizmo, family SquadAssignment)
  - docs/DESIGN_Map_3D_Mode.md — the map's 2-D primitives are drawn in 3-D too (§7)
-->

# Command display — the sectors, the command picture and the Reports window *(UI-5, UI-6)*

🔒 User, `2026-10-18` (R-2046, the platoon programme): the ui lane's part is *"displays that let a person watch a command chain work"* —
[hand-off](blueprints/batches/HANDOFF_Behaviors_To_Ui_Platoon_Demos.md) **UI-5** (the map shows the command picture) and **UI-6** (a
Reports window).

## 1. What

| | shows | from |
|---|---|---|
| **UI-5a** sectors | every unit's sector AREA outlined in that unit's colour, with its name; every NoFire area hatched red | `EngagementArea.AllowArea` / `.NoFire` (area network ids) — ✅ **replicated, on every node** |
| **UI-5b** the picture | for the SELECTED / pinned commander: its reported elements as symbols (size class, MG / sniper / AT flags, fading with age), its subordinates' last progress, open requests; a man's sector ARC | `CommandPicture` — ⚠ **brain-local** (registered on SimHost only, never replicated) |
| **UI-6** Reports | the report log: time, kind + subtype, from → to (names), SPOT fields, age, `Synthetic` marked; filters by unit and kind | `SquadReportLog` of the node — ⚠ filled only where the staff's inbox runs (brain nodes) |

## 2. INVENTORY *(graph CLI re-indexed `2026-10-19`, then grep; `check_index_coverage` is not available through the CLI)*

| query | result | ⇒ |
|---|---|---|
| `search_graph name_pattern=".*Gizmo$" label=Class` | 80 classes — the map overlays among them: `TacticalAreaGizmo`, `MapOverlayGizmo`, `TerrainZoneGizmo` (area outlines), `DangerAreaGizmo`, `SquadGizmo` (squad commander: members, phase, contacts), `DoorLeafGizmo` (a GLOBAL gizmo, once per frame) | ⭐ no gizmo reads `EngagementArea` or `CommandPicture` — new ones; the outline helper `EntityPresentationGizmoShared.DrawClosedPolylineOutline` is reused |
| `search_graph name_pattern=".*Window$" label=Class` | 90 — log-like windows: `MessageLogWindow`, `HsmEventsWindow` (rows + a frame snapshot model, U-obs-5) | ⭐ the Reports window follows `HsmEventsWindow`'s shape (a pure model + a thin `ManagedWindow`) |
| grep `CommandPicture` outside `Squad/Command` | registered by `Hrot.SimHost/CognitiveComponentRegistry.cs:34` only; read by `DebugApiService.Reports.cs` (`GET /entities/{id}/reports`), the commander nodes | ⚠ no translator, no descriptor — **not on the wire** (the design: *"read on the brain node"*, Command Chain CC-12 / CC-16) |
| grep `SquadReportLog` | filled by `StaffInboxSystem` (`StaffSystems.cs:228`) **after** `if (!IsComponentTypeRegistered<CommandPicture>()) return;` | ⚠ a node without the staff keeps no log, though `SquadReportNotification` reaches every node's bus (Command Chain §5: *"ingress on every node"*) |
| grep `EngagementArea` reads | `EngagementAreas` (the one reader), `DebugApiService.Sector.cs` (*"the replicated EngagementArea reads it back here"*) | ✅ replicated |
| area polygon | `WeatherField.TryOutline(repo, area, out outline)` — points relative to the area's `SimTransform` (the `EngagementAreas.InArea` reader) | ⭐ reused, not re-read |

## 3. Classes

```mermaid
classDiagram
    direction LR
    class IGlobalStatelessGizmo { <<existing>> Draw(view, draw) once per frame }
    class IStatelessGizmo { <<existing>> Draw(view, entity, draw) }
    class SectorAreasGizmo { <<NEW, UI-5a>> each unit's AllowArea outlined in its colour + name · each NoFire area hatched red, each area ONCE }
    class CommandPictureGizmo { <<NEW, UI-5b>> family SquadAssignment · elements as symbols, age fading · subordinate rows · requests }
    class SectorArcGizmo { <<NEW, UI-5b, as built>> family SquadAssignment · a man's arc }
    class ReportsWindowModel { <<NEW, UI-6>> Rows(log, now, unitFilter, kindFilter) · pure, no ImGui }
    class ReportsWindow { <<NEW, UI-6>> ManagedWindow · table + two filters }
    class EngagementArea { <<existing, replicated>> AllowArea · NoFire[4] · Arc }
    class CommandPicture { <<existing, brain-local>> Elements[8] · Subordinates[6] · Requests[4] }
    class SquadReportLog { <<existing, per node>> Recent(max) · entries with Verdict }
    class WeatherField { <<existing>> TryOutline(area) }
    class EntityPresentationGizmoShared { <<existing>> DrawClosedPolylineOutline }
    class SquadGizmo { <<existing>> members, phase, contacts — unchanged }
    IGlobalStatelessGizmo <|.. SectorAreasGizmo
    IStatelessGizmo <|.. CommandPictureGizmo
    SectorAreasGizmo ..> EngagementArea : reads (every unit)
    SectorAreasGizmo ..> WeatherField : outline
    SectorAreasGizmo ..> EntityPresentationGizmoShared : outline
    CommandPictureGizmo ..> CommandPicture : reads (where present)
    IStatelessGizmo <|.. SectorArcGizmo
    SectorArcGizmo ..> EngagementArea : reads the arc
    ReportsWindow --> ReportsWindowModel : renders
    ReportsWindowModel ..> SquadReportLog : Peek(world)
```
*What the picture shows that prose hid:* the sectors are drawn by ONE global gizmo, not a per-unit one — many men list the same NoFire
area (it is looked up through the chain), so a per-unit projector would draw it once per man. And nothing here writes: all three only
read components the behaviours lane owns.

## 4. Sequence — a frame on any map host

```mermaid
sequenceDiagram
    participant R as GizmoRenderer (every map host)
    participant S as SectorAreasGizmo
    participant V as the node's world
    participant P as CommandPictureGizmo
    R->>S: Draw(view, draw) once
    S->>V: query EngagementArea (every unit)
    S->>V: resolve AllowArea and NoFire net ids, TryOutline each ONCE
    S-->>R: outlines (unit colour + name), NoFire hatch (red)
    R->>P: Draw(view, commander, draw) for the selected / pinned
    P->>V: CommandPicture present on this node?
    alt brain host (SimHost)
        P-->>R: element symbols, age-faded, rows, requests
    else no brain here (editor, CGF, ExCon)
        P-->>R: nothing (D4)
    end
```

## 5. Modules — who registers it, who calls it each frame, where the data is

```mermaid
graph TD
    REG[GizmoReflectionRegistrar - every map host] --> SA[SectorAreasGizmo]
    REG --> CP[CommandPictureGizmo]
    WR[window registrar - editor, ExCon] --> RW[ReportsWindow]
    EA[EngagementArea - replicated] --> SA
    PIC[CommandPicture - SimHost only] --> CP
    LOG[SquadReportLog - filled by StaffInboxSystem] --> RW
    PIC -.->|no translator| UI[editor / CGF / ExCon world]
    LOG -.->|inbox not registered there| UI
    style PIC fill:#fde,stroke:#c33
    style LOG fill:#fde,stroke:#c33
    linkStyle 6 stroke:#c33,stroke-dasharray:4
    linkStyle 7 stroke:#c33,stroke-dasharray:4
```
*Caption — the dead edges (red, dashed):* the picture and the log never reach a node without a brain. The gizmos and the window are
registered there and draw nothing until D4 lands; on SimHost's map (and `--mode all`'s SimHost world) they work today.

## 6. Decisions *(leans — reply "approved" or name the one to change)*

| id | decision | ⭐ lean | rejected — one line each |
|---|---|---|---|
| **D1** | where sectors come from | ⭐ `EngagementArea` of every unit (replicated): `AllowArea` = the sector, outlined in the unit's colour (a palette by network id) with the unit's name at the polygon's centre; `NoFire` areas hatched red (diagonal lines every 3 m, clipped to the polygon) and outlined; each area drawn ONCE per frame however many units list it | style by the area's TKB kind — sectors and NoFire areas are ordinary area entities; only the UNIT says what an area is to it |
| **D2** | the picture | ⭐ `CommandPictureGizmo`, family `SquadAssignment` (selected + pinned, as `SquadGizmo`): each element a diamond at `At`, side ∝ `Size` (team / squad / platoon / company), a letter for the equipment flags (M machine gun · S sniper · A anti-tank), alpha `1 − age / 120 s` floored at 0.25, its `Count` and age as text; each subordinate row as a line to it labelled with its last `Progress`; each open request as a `?` at its point; a man's own arc as a wedge | draw on every unit — eight elements × every commander is noise; the hand-off says the SELECTED commander |
| **D3** | the Reports window | ⭐ a pure `ReportsWindowModel.Rows(log, now, unit, kind)` + a thin `ManagedWindow` (the `HsmEventsWindow` shape): newest first, columns time · kind/subtype · from → to (names) · SPOT fields · age (now − `ObservedAt`) · `SYN` mark · verdict; filters: a unit (from or to) and a kind | a second log of the ui's own — two logs of one stream drift (R-132) |
| **D4** | the picture and the log on a node with no brain | ✅ **APPROVED 2026-10-19 as leaned — behaviors builds it (`CE-2301`, [Command Chain §8b](designs/group-maneuvers/DESIGN_Command_Chain_Example.md))**; was ⭐ **behaviors' call** ([NOTE](blueprints/batches/NOTE_Ui_To_Behaviors_Command_Display_Data.md)) — ui's lean: ① the LOG: `StaffInboxSystem` appends to `SquadReportLog` BEFORE its `CommandPicture` guard, and is registered wherever `SquadReportNotification` is (verdict `not-owned-here` there) — one writer, every node; ② the PICTURE: replicate `CommandPicture` as a read-only Brain-group component (it is fixed-size and NoScenario already) | ⓑ fold a display copy on every node (the inbox ungated) — the brain-only resets (`OrderGiven`) would make the copies disagree · ⓒ the window asks the brain node over HTTP — the map is not an HTTP client |

## 7. As built

| | as built *(`CE-1192`, ui, `2026-10-19`)* — and the deviations, argued |
|---|---|
| **D1** `SectorAreasGizmo` | `Hrot.Presentation/ScenarioEditor/Gizmos/SectorAreasGizmo.cs`, a global gizmo (auto-registered): one query over `EngagementArea`, sectors keyed by area id (the first unit naming it gives the colour and the name), NoFire areas in a set ⇒ each drawn once; outline via `EntityPresentationGizmoShared.DrawClosedPolylineOutline`, the hatch `SectorAreasGizmo.Hatch` (45°, 3 m, even-odd pairs per line, so a concave area hatches only inside) |
| **D2** `CommandPictureGizmo` + ⚠ `SectorArcGizmo` | the picture as §6 D2 (diamonds by size class, flag letters, count + age + `SYN`, alpha `max(0.25, 1 − age / 120 s)`, dashed lines to subordinates with their last progress, `? kind` at requests). ⚠ **Deviation:** the man's arc is its OWN projector (`SectorArcGizmo`, on `EngagementArea` + `SimTransform`, family `SquadAssignment`) — a man has an arc and no picture, so it cannot ride the picture's projector |
| rails | `CommandDisplayGizmoTests` (`Hrot.Presentation.Tests`): two sectors in their units' colours with names and the NoFire room hatched ONCE though eleven units name it (the hand-off's fixture: two sectors, one NoFire room, three elements) · the hatch lies inside a concave L · the picture's three elements sized by class and fading with age · a man's arc along his bearings. Red-proved: dedup off, hatch pairing shifted, no fading, bearings swapped |

| id | built (CE-1191 for D3) | classes — where | registered | rails | deviation from §3 / §6 |
|---|---|---|---|---|---|
| **D3** Reports window | ✅ the log of the node as a table: time · kind / subtype · from → to · SPOT fields · age · SYN · verdict, newest first; unit combo (from OR to) and kind combo | `ReportsWindowModel` (pure: `Rows`, `Units`, `NameOf`, the two empty/no-match texts) + `ReportRow` / `ReportSpot` + `ReportsWindow` (`ManagedWindow`, with `BuildViewModel` → `ReportsWindowViewModel`, dumped through `PanelSnapshot`) — `Hrot/Engine/Hrot.Presentation/Windows/`. Shared words: `CommandReportText` (`SubtypeName`, `CarriesElementFields`, `UnitName`) — `FDP/Toolkits/Fdp.Toolkits/Squad/Command/`, **moved out of the HTTP route**: `DebugApiService.Reports.cs` now calls it too (subtype text, the SPOT-field kinds, the subordinate's name) — one copy | **Editor** (`editor_reports`, perspective Scenario, `EditorSubsystem.RegisterWindows`, non-headless) · **ExCon** (`excon_reports`, in the `_mock` block of `ExConSubsystem.RegisterWindows`) · **SimHost** (`simhost_reports`, `SimHostSubsystem.RegisterWindows`) — ids in `ScenarioPanelWindowIds`. `() => world` per host: editor `_world`, SimHost `vis.GetRepo()`, ExCon `() => null` | `Hrot.Presentation.Tests/Windows/ReportsWindowTests.cs` (the feature suite: 20-report fixture, order, unit/kind/both filters, SYN, age, names, SPOT only for SPOT/CONTACT, null/empty log, the empty text, the window's dump) | ① `Rows` takes the **world** as well as the log (`Rows(world, log, now, unit, kind)`): a name is an `EntityInfo` read on the world, the log holds entity handles only. ② also registered on **SimHost** — the one host where the log fills today (§5); the brief named editor and ExCon. ③ **ExCon keeps no world at all** (the map is remote), so its window shows the empty line permanently — it can only change when D4 puts a log where ExCon can read it, not by `StaffInboxSystem` writing it on every node. ④ a unit is named by `EntityInfo.Name`, else `#index` (no entity map in the model, so no network id) |
| **D4** — ui's part *(behaviors' [answer](blueprints/batches/NOTE_Behaviors_To_Ui_Command_Display_D4_Answer.md), R-2058, `2026-10-19`)* | ✅ absence is visible: a SELECTED commander (a unit some `UnitSubordinate` names as commander) with no `CommandPicture` on this node gets the line *"no picture on this node yet"* over his head; the Reports window's empty line is now *"No report on this node yet."* (the log fills on every node once behaviors' inbox lands) | `CommandPictureAbsentGizmo` (family `SquadAssignment`, beside `CommandPictureGizmo`) · `ReportsWindowModel.EmptyText` | `CommandDisplayGizmoTests.D4_ACommanderWithNoPictureOnThisNode_SaysSo` (red-proved) | — |

## 8. The Sectors layer — one checkbox for every sector *(UI-5c, `CE-1225`, ui, `2026-10-20`; build-state: BUILT — §8a)*

🔒 **User, `2026-10-20`:** *"The behavior lane has developed attack and defend behavior for platoons and squads. they are using sectors
assigned to them. Do you support rendering the sectors behind a switch/checkbox in the map tactical layers?"* · on the lean: *"Yes, lets do
sector checkbox first."*

**INVENTORY** *(graph CLI `search_graph .*Sector.*` (Class) → 3: `SectorArcGizmo`, `SectorAreasGizmo`, `SectorSplit` (behaviors' plan
helper, not drawn); `query_graph` IMPLEMENTS `IStatelessGizmo` 38 / `IGlobalStatelessGizmo` 15; grep `layer:` per projector)* —

| claim | code — how it IS | design — how it was MEANT |
|---|---|---|
| both sector drawings sit on layer 0, so only "Entities off" (which hides the units too) hides them | `IDebugDrawBuilder.DrawLine(… byte layer = 0 …)`; neither gizmo passes a layer; `EntityPresentationGizmoShared.DrawClosedPolylineOutline` hard-codes `layer: 0` | §6 D1/D2 named no layer — searched, none |
| a squad's arc draws only for the selected / pinned unit | `SectorArcGizmo`, family `SquadAssignment` | §6 D2 (*"a man's own arc"*) |
| behaviors writes THREE tiers onto the one replicated `EngagementArea` | the platoon's slice on each line squad (`PlatoonDefendBehavior` DA-52), the support squad's arc (`PlatoonAttackBehavior`), each man's arc from his post (`SquadManeuverRunner.Sector`, DA-37) — behaviors branch, `2026-10-19` | [Defend & Attack](designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md) DA-37, DA-52; R-3020 / R-2054 |
| a COMMANDER is a unit some replicated `UnitSubordinate.Commander` names | `CommandPictureAbsentGizmo` (§7 D4) | §7 D4 |
| a z = 0 line is draped on the ground in 3-D; any other z floats | `GizmoRenderer3D` (`drape = !Anchored && z == 0`) | [Map 3-D](DESIGN_Map_3D_Mode.md) M18, M22 |
| the layer mask: toggles 0…13, everything from `FirstUntoggledLayer` (14) up always on; the selection ring at 14 | `LayerControlDto.ToMask`, `DebugTraceLayers.Selection` | [Tuning §5d](DESIGN_Terrain_Combat_Tuning.md) C; CE-1160 |

```mermaid
classDiagram
  class LayerControlDto {
    <<exists, changed>>
    Sectors : new, false
    FirstUntoggledLayer = 15
  }
  class DebugTraceLayers {
    <<exists, changed>>
    Sectors = 14
    Selection = 255 (was 14)
  }
  class SectorAreasGizmo {
    <<exists, changed>>
    Draw() areas, NoFire AND every commander's arc, on Sectors
    DrawArc(draw, area, colour) static, shared
  }
  class SectorArcGizmo {
    <<exists, changed>>
    family SquadAssignment, any selected unit's arc, on Sectors
  }
  class EntityPresentationGizmoShared {
    <<exists, changed>>
    DrawClosedPolylineOutline(…, layer)
  }
  class EngagementArea { <<FDP Toolkits, replicated>> AllowArea · NoFire · ArcSet, ArcOrigin, bearings, ranges }
  class UnitSubordinate { <<FDP Core, replicated>> Commander }
  LayerControlDto --> DebugTraceLayers : bit 14
  SectorAreasGizmo ..> EngagementArea : every unit
  SectorAreasGizmo ..> UnitSubordinate : which units command
  SectorArcGizmo ..> SectorAreasGizmo : DrawArc
  SectorAreasGizmo ..> EntityPresentationGizmoShared : outline on Sectors
```
*What the picture shows that prose hid:* nothing new is drawn by a new class — the global gizmo, which already walks every
`EngagementArea`, also draws the commanders' arcs, and the per-unit arc gizmo keeps the men behind selection. The checkbox is one bit;
the only structural change is moving the selection ring out of the way to the top bit, so the toggles stay one contiguous range.

```mermaid
sequenceDiagram
  participant U as Operator
  participant L as LayerControlGizmo
  participant G as SectorAreasGizmo (global)
  participant A as SectorArcGizmo (per unit, family SquadAssignment)
  participant R as 2-D / 3-D renderer
  U->>L: Tactical Map Layers, ticks "Sectors"
  L->>R: LayerControlMask, bit 14 on
  loop each frame
    G->>G: commanders = every UnitSubordinate.Commander
    G->>R: each AllowArea outlined + name, each NoFire hatched (layer 14, z = 0)
    G->>R: each commander's arc (layer 14, z = 0)
    A->>R: the selected / pinned unit's arc (layer 14, z = 0)
  end
  R->>R: draws layer 14 only while the bit is on, z = 0 draped in 3-D
```

```mermaid
graph TD
  REG["GizmoReflectionRegistrar — every map host"] --> G[SectorAreasGizmo]
  REG --> A[SectorArcGizmo]
  POL["GizmoFamilyVisibilityPolicy (SquadAssignment scope)"] --> A
  G --> BUF["DebugPrimitive buffer, layer 14"]
  A --> BUF
  LCG["LayerControlGizmo — View, Tactical Map Layers..."] -->|"mask bit 14"| R2[DebugPrimitiveRenderer2D]
  LCG -->|"mask bit 14"| R3[GizmoRenderer3D]
  BUF --> R2
  BUF --> R3
  NET["EngagementArea + UnitSubordinate replicated (EntityEngagementAreaTranslators)"] --> G
```
*Who calls it each frame:* the stateless gizmo system of EVERY map host (editor, SimHost, CGF, IG) — the data is replicated, so a
node with no brain draws the same sectors.

| id | decision | ⭐ lean | rejected — one line each |
|---|---|---|---|
| **S-1** | the switch | ⭐ a **Sectors** checkbox, OFF by default (§5d A: a quiet map), bit 14; areas, NoFire hatching and arcs all on it | reuse "Squad: selected only" — unticking it also draws every man's arc and the leader–member lines · leave sectors on Entities — hiding them hides the units |
| **S-2** | whose sectors | ⭐ every COMMANDER's arc for every unit while the box is ticked (the platoon's slices, the support squad's arc); a man's own arc only when he is selected or pinned (the family scope, as before) | every man's arc — a platoon is ~30 arcs over the map |
| **S-3** | height | ⭐ arcs drawn at z = 0 ⇒ draped on the ground in 3-D, like the areas | at the arc's origin height — the far edge floats over or sinks under a slope |
| **S-4** | the selection ring | ⭐ moves to bit 255 (still above every toggle, still always on) so the toggles stay one range 0…14 and later toggles (`CE-1203`) can grow it | a hole list of toggled bits above 14 — two rules for one mask |
| **S-5** | a selected commander | ⭐ his arc is drawn by both gizmos — the same line twice, invisible; no per-frame selection lookup in the global gizmo | skip selected commanders in one of them — needs the selection on a stateless global gizmo for no visible gain |

⚠ **Not drawn: the platoon's whole area** when it came in the order — it lives only in the play's params, never on an entity; its squad
slices together cover it. Drawing it needs behaviors to put it on the platoon leader's `EngagementArea` (not asked).

### 8a. As built *(`CE-1225`, ui, `2026-10-20`)* — matches §8's three diagrams; no deviation

| piece | where |
|---|---|
| S-1 the bit and the checkbox | `DebugTraceLayers.Sectors = 14` (`Fdp.Toolkits`); `LayerControlDto.Sectors` (default false), `ToMask`, `FirstUntoggledLayer = 15` (`Hrot.Common/Diagnostics/Gizmos/LayerControlGizmo.cs`) |
| S-2 commanders for everyone | `SectorAreasGizmo` — a cached `UnitSubordinate` query → `Commanders(…)` set each frame; a commander's arc through `SectorArcGizmo.DrawArc`; areas, names and the NoFire hatch pass `layer: Sectors` (`EntityPresentationGizmoShared.DrawClosedPolylineOutline` gained a `layer` argument); both gizmos return at once when the layer is hidden (`IsLayerActive`), so a hidden layer costs no work |
| S-3 on the ground | `SectorArcGizmo.DrawArc` draws at z = 0 (the arc's X/Y origin) |
| S-4 the selection ring | `DebugTraceLayers.Selection = 255`. ⚠ Measured on the way: the 2-D renderer SORTS by layer and the pick test prefers the higher layer (`DebugGizmoLayer`), so the ring is still drawn on top and still out-ranks everything below it — as at 14; sector lines (layer 14) now draw above the unit symbols (0) and cards (11), and are lines, so they never take a pick (only boxes and spheres are hit-tested) |
| S-5 | as leaned — a selected commander's arc is emitted twice, identically |
| rails | `CommandDisplayGizmoTests` (`Hrot.Presentation.Tests`): `CE1225_EveryCommandersArc_IsDrawnForEveryone_OnTheGround_OnTheSectorsLayer_AndAMansOwnArcIsNot` · `CE1225_TheSectorsLayerOff_NoAreaNoFireOrArc_IsDrawn` · the CE-1192 area rail now also asserts its layer. `LayerControlGizmoTests.CE3117_…` (`Hrot.SimHost.Tests`): Sectors defaults off, toggles its bit, lies below the always-on range. Frame: `Map3DFrameRail` ⑨ → `docs/assets/map-3d/map3d-9-sectors.png` (a platoon's three squad slices draped in 3-D, the rifleman's own arc absent) |
