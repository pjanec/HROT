<!--STATUS
state: LIVE — DESIGN, leans RV-1 … RV-9 await the user
updated: 2026-11-02 (behaviors' V-1/V-2/V-4 folded in)
build-state: DESIGN
current-answer: §3 (decisions) · §4 (phases)
stale-below: nothing
known-rot: none
related-designs:
  - docs/DESIGN_Scenario_Rules.md — OWNS the decisions this builds: SR-56 (objectives view), SR-103 (rules lanes), SR-105 / SR-109 / SR-110 (the rules view), SR-111 (mission summary), SR-112 (debrief / HUD — later here)
  - docs/designs/rules-view/README.md — the nine mockups (Main, MissionSummary, …); the SR rows win where they differ
  - docs/DESIGN_Director_Timeline_Editor.md — OWNS the ONE timeline panel (TE-21 / R-4075) and its lanes (§9c); TL-X1…X5 here extend it for every host
  - docs/DESIGN_Entity_Hierarchy_Views.md — the entity tree; the rules view's row tree is NOT that tree (rows are objectives / triggers, not entities in a relation)
  - docs/DESIGN_Macro_Authoring_Editor.md — the parameter bindings a trigger's Do tab will show (⛓ chips, MA-1)
-->

# DESIGN — **the rules view and the mission summary** (ui half of [HANDOFF_Coordinator_To_Ui_Rules_View_And_Summary](blueprints/batches/HANDOFF_Coordinator_To_Ui_Rules_View_And_Summary.md))

The handoff's items ① CE-4167 (the timeline extensions), ② the rules view, ③ connectors + expected bars, ⑤ runtime control and ⑥ the first half of the mission summary are designed here: their data exists. ⭐ **Updated `2026-11-02`:** behaviors delivered V-1 / V-2 / V-4 on `behaviors` (`59a8e5d23`, `DESIGN_Scenario_Rules.md` §00.10), which moved ③ and ⑤ from waiting to buildable.
Items ④ (live leaf values, V-3), ⑦ and ⑧ wait for behaviors' data (V-3 as built, B3–B5) and are only placed here, not designed.
⭐ **Updated again `2026-11-02`:** behaviors' S4 landed (`behaviors@02c50f84c`, [NOTE_Behaviors_To_Ui_Rules_S4_Group_And_Measures](blueprints/batches/NOTE_Behaviors_To_Ui_Rules_S4_Group_And_Measures.md)): `ObjectiveStatus.Group` (the section a standard is shown under), the measure kinds (`HitRatio` · `Count` · `ReportLatency` · `TimeTo` · `Sample`; `Score` already folds *lower is better*) and `MemberEvaluation` (one inserted root per member, `Subject` = the man). ⇒ the rules view groups measures by `Group`; the summary's *scores by level* (⑦) and the trainee debrief (⑧) become designable on this data — a later section, after R1–R4.

## 1. INVENTORY *(`2026-11-02`)*

**Queries:**
- graph: `.*(Objective|Rules|Trigger|Mission).*` Class → 80. **None is a view of objectives or triggers**: they are mission plans, BDC messages and various `*Rules` helpers.
- graph: `.*Timeline\w*(Window|Panel|Track|Host)$` → hosts found by grep instead: `EventTimelineWindow`, `ReplayTimelineWindow`, and `DirectorTracks` (3 `ITimelineTrack`s).
- grep: `ObjectiveStatus` / `ObjectiveLinks` / `ExerciseResult` · `IndexedLaneDefinition` providers (3: combat, behaviour, replication) · `GetObjectives` (debug API).
- ⚠ Coverage was not checked: `check_index_coverage` is not in the CLI.

| measured | where | so |
|---|---|---|
| the ONE timeline panel draws read-only LANES from an index and editable TRACKS above them; a track item is a `Key` or a `From…To` `Segment` with a colour; the row label is a flat `Label` drawn at a fixed `LabelWidth` | `Fdp.Presentation/ImGui/Panels/Timeline/TimelinePanel.cs:415-487` (`DrawTrack`); `ITimelineTrack.cs` | the rules view = one track per objective / trigger (SR-110). The panel lacks: styles beyond Key / Segment (TL-X2), a tree with a status in the label column (TL-X3), links between items on different rows (TL-X1), a connect gesture (TL-X4), a *Coming up* strip (TL-X5) |
| ⚠ the panel draws NOTHING without an index extent: `if (to <= from) { "No recording." ; return; }` | `TimelinePanel.cs:142-148` | in edit mode before a run there are no frames ⇒ the rules view would be blank ⇒ RV-2 (the extent also comes from the tracks) |
| a LIVE index exists: `LiveTimelineSource` evaluates the registered indexed lanes on the live world each frame | `Fdp.Toolkits/ReplayBrowser/Timeline/LiveTimelineSource.cs` | the rules view runs live and on a recording through the SAME source seam (`ITimelineSource`) |
| hit positions of every drawn track item are already collected each frame (`_trackHits`: track id, item id, x, y) | `TimelinePanel.cs:97, :451` | TL-X1's connectors are drawn from these after the tracks, with no second layout |
| `ObjectiveStatus` (replicated): role (Objective · Measure · Trigger), state, trigger state (Disarmed … Done), owner side, progress cur / need, value / target, score, points, `ChangedAt`, `LastFired`, `FireCount`, reason, title, kind, required, scored, weight, visible sides · `ObjectiveLinks {Subject, Parent}` · `ExerciseResult` | `Fdp.Toolkits/ScenarioRules/ObjectiveStatus.cs`, `ObjectiveLinks.cs`, `ExerciseResult.cs` | every row, its tree, its state words and its CURRENT span come from these. ⛔ They hold only the CURRENT state + when it changed, not history |
| ⛔ the SR-103 provider `ScenarioRulesTimelineLanes` (`rules.fired`, `objective.state`, …) is designed, NOT built; neither the ui nor the behaviors handoff lists it | grep `IndexedLaneDefinition` → 3 providers, none for rules; `DESIGN_Scenario_Rules.md` SR-103 | the PAST at actual times (item ②, "✅ now") needs it ⇒ RV-4 |
| ✅ **delivered by behaviors (`59a8e5d23`)**: `ObjectiveStatus` (on topic 171, every node) gained `DependsOn` / `ActsOn` (≤ 8 net ids each: the entities a trigger's `when` names / its `do` targets), `HoldFor` · `Delay` · `Cooldown` · `Deadline` and `ExpectedAt` (sim s of the next thing the owner knows will happen; 0 = unknown). Runtime control = `RuleControl` (430, brain-local) written through `EntityWriteRouter` as three attributes `Rule.Request` (40) · `Rule.Seq` (41) · `Rule.Origin` (42) in ONE write; the owner honours a new `Seq` once and journals it | `DESIGN_Scenario_Rules.md` §00.10 V-1, V-2, V-4 (behaviors) | ⭐ connectors (X1) = `DependsOn` → this row and this row → `ActsOn`; the dashed expected bar (X2) = now → `ExpectedAt`; Fire / Disarm / Re-arm / Reset / Activate are ONE router write each — ⛔ the ui never re-derives timing or links (V-2) |
| `MacroOrigin` / `MacroInstance` on macro entities; the Scenario view groups by them (W-M1) | `Fdp.Toolkits/Scenario/Macros/MacroTypes.cs`; `ScenarioHierarchyAdapter` | the rules view's *group by macro* uses the same link |
| selecting an entity + *Show on map* already have one path (the selection singleton, `CenterOnEntitySystem` / `MapCamera.FocusOn` through the broker) | CE-4049; CE-4206 R3 | a row click goes the SAME way (SR-56: *"the ORBAT's own jump"*) |

## 2. The shape

```mermaid
classDiagram
    direction LR
    class TimelinePanel { <<existing, the ONE panel>> lanes · tracks · + header tree (X3) · links (X1) · connect drag (X4) · Coming up (X5) · extent from tracks }
    class ITimelineTrack { <<existing>> Items · Select · Move · MenuItems · + Header (X3) · + Links (X1) · + Connect (X4) }
    class TimelineTrackItem { <<existing record>> From · To · Kind · Colour · + Style (X2) }
    class TrackHeader { <<NEW record>> Depth · Text · Status · StatusColour · Icon · Collapsible }
    class TrackLink { <<NEW record>> FromItem · ToTrack · ToItem }
    class ComingUp { <<NEW, pure>> Of(tracks, now, n) — Expected / Pinned items after now }
    class RulesModel { <<NEW, pure, no ImGui>> rows from ObjectiveStatus + ObjectiveLinks + MacroOrigin · group by macro / subject / side · state words }
    class RuleTrack { <<NEW : ITimelineTrack>> one per objective / trigger row }
    class RulesWindow { <<NEW>> toolbar (group by · side · follow) · the panel in a Rules host · Overview details }
    class ScenarioRulesTimelineLanes { <<NEW provider, SR-103>> rules.fired · objective.state }
    class MissionSummaryModel { <<NEW, pure>> per side: required n/N + states · forces · result }
    class MissionSummaryWindow { <<NEW, read-only>> }
    class ObjectiveStatus { <<existing, replicated>> }
    class ObjectiveLinks { <<existing>> Subject · Parent }
    TimelinePanel ..> ITimelineTrack
    ITimelineTrack ..> TimelineTrackItem
    ITimelineTrack ..> TrackHeader
    ITimelineTrack ..> TrackLink
    TimelinePanel ..> ComingUp
    RuleTrack ..|> ITimelineTrack
    RulesModel ..> ObjectiveStatus
    RulesModel ..> ObjectiveLinks
    RulesModel ..> ScenarioRulesTimelineLanes : the past
    RulesWindow ..> RulesModel
    RulesWindow ..> TimelinePanel
    RulesWindow ..> RuleTrack
    MissionSummaryModel ..> ObjectiveStatus
    MissionSummaryWindow ..> MissionSummaryModel
```

*What the picture shows that prose hid:* the rules view adds NO drawing of its own. It is a model (no ImGui, railed as data) feeding tracks into
the ONE panel. Every visual it needs is a general panel extension (X1–X5), which the director's tracks get too. The past comes from a
lane provider, so the AAR, the director and the rules view read the same index (SR-103).

```mermaid
sequenceDiagram
    participant W as RulesWindow (each frame)
    participant M as RulesModel
    participant S as ITimelineSource (live or recording)
    participant P as TimelinePanel
    participant U as instructor
    W->>M: Build(world, groupBy, sideFilter, now)
    M->>M: rows = every entity with ObjectiveStatus (tree by Parent, grouped by macro / subject / side)
    M->>S: the rules lanes' events for each row (its past spans and fired keys)
    M-->>W: rows (header, state words, items: past spans · the current span ChangedAt→now · fired keys)
    W->>P: Draw(source, cursor, lanes) with Tracks = RuleTrack per row
    U->>P: click a row
    P->>W: track.Select
    W->>W: select the row's SUBJECT (the selection singleton) and show it on the map (the broker), Overview shows the row
```

```mermaid
graph TD
    EDITOR["Editor composition (EditorSubsystem)"] -->|registers| RW["RulesWindow · MissionSummaryWindow"]
    RB["Replay Browser (a recording)"] -->|registers| RW
    CAT["TimelineLaneCatalog (each host's)"] -->|"ScenarioRulesTimelineLanes.Register"| LANES["rules.fired · objective.state"]
    LIVE["LiveTimelineSource (live run)"] --> LANES
    REC["RecordingTimelineSource (recording)"] --> LANES
    RW -->|"reads the status of"| NED["ObjectiveStatus (replicated from the Brain)"]
    PLAYER["Player host"] -.->|"not registered: debrief / HUD are item ⑧, later"| RW
    style PLAYER stroke:#c00,stroke-dasharray: 4 3
```

*What the picture shows that prose hid:* the windows read only REPLICATED data and the shared lanes. So any node that receives `ObjectiveStatus`
(the editor, a Replay Browser on a recording) can host them, with no Brain-side code. The Player is deliberately not a host yet.

## 3. Decisions

| # | decision | ⭐ lean |
|---|---|---|
| **RV-1** | the panel extensions (CE-4167), general for every host | ⭐ **X2**: `TimelineTrackItem.Style` (`Solid` default · `Expected` dashed outline · `Hatched` · `Pinned` diamond). **X3**: an optional `ITimelineTrack.Header` (`TrackHeader`: depth, text, status text + colour, icon, collapsible). The panel draws the label column as an indented tree with the status right-aligned. A collapsed node hides its descendants (tracks are given in tree order, so depth decides). **X1**: optional `ITimelineTrack.Links` (from my item → another track's item), drawn after all tracks as an elbow line with an arrowhead, from the `_trackHits` positions. A link whose end is not drawn (scrolled away, collapsed) is not drawn. **X4**: optional `CanConnect` / `Connect(fromItem, toTrack)`. A drag from a small handle at an item's right end onto another row calls it; nothing happens on a track that does not opt in. **X5**: `ComingUp.Of(tracks, now, n)`, a pure list of the next `Expected` / `Pinned` items after now, drawn as a strip below the lanes when the host turns it on. ⭐ All default-off members ⇒ the director and the trace lanes are unchanged until they opt in |
| **RV-2** | a blank panel before a run | ⭐ the extent also covers the TRACKS' items and `[0, now]`, so tracks draw in edit mode with no recording (today: "No recording."). The lanes keep their own rule |
| **RV-3** | the rows | ⭐ one `RuleTrack` per entity with `ObjectiveStatus` (objectives, measures, triggers). The tree comes from `ObjectiveLinks.Parent`. **Group by** macro (`MacroOrigin.Instance` → its `MacroInstance.Macro`, else *Scenario*) · subject (`ObjectiveLinks.Subject`'s name) · side (`Owner`). **Side** filter = `Owner`, which also respects `VisibleSides`. The state in words follows the mockup: *Achieved* · *Active 6:10* (since `ChangedAt`) · *Armed* · *Cooldown · 1/2* (`FireCount` of max when known) · *Acting* (the step count waits for A1). The status colour follows the legend |
| **RV-4** | the PAST at actual times | ⭐ build the SR-103 provider `ScenarioRulesTimelineLanes` (two indexed lanes: `objective.state` = a value change of `ObjectiveStatus.State` on objectives and measures, and `rules.fired` = `TriggerState` → `Fired`), registered like `CombatTimelineLanes`. The row's past spans and fired diamonds are read from those events, plus the current span `ChangedAt → now` from the status. ⚠ Cross-lane: `Fdp.Toolkits/ScenarioRules` is behaviors' folder. No handoff lists this item, and the view's past depends on it. Said in the commit and a note to behaviors |
| **RV-5** | acting on a row | ⭐ click = select the row (the Overview) AND its SUBJECT through the selection singleton; *Show on map* = the broker's existing focus. The right-click menu and the details' buttons: *Fire now* · *Disarm* · *Re-arm* · *Reset* (triggers) · *Activate* / *Deactivate* (objectives) — each ONE `EntityWriteRouter.For(world).Write(entity, [Rule.Request, Rule.Seq, Rule.Origin = Instructor])`, `Seq` = the last seen + 1 (behaviors' `DebugApiService.ControlObjective` is the precedent: reuse its rule, not a copy). An item not valid in the row's state is disabled with the reason (a final trigger: *"needs Reset"*). ⭐ A live edit is FOR THIS RUN ONLY (SR-109) — the view never writes the scenario file. ⭐ The result shows as the row's new state and the owner's `Reason`, not as a ui-side guess |
| **RV-6** | the window | ⭐ `RulesWindow` (*Window ▸ Rules*), PerspectiveBound like the timeline windows. Toolbar: *Group by*, *Side*, *Follow now*, search; *Timeline* only (Graph / List later). Centre: the panel in a Rules host (its own lanes hidden except the rules lanes). Right: details of the selected row with the **Overview / Status** tab: title, kind, state, owner, subject (link), progress, value / target, score, required / scored, reason, fired n× / last. ⏳ When / Do / Rules / History / Bindings tabs need A1–A4. ⭐ Opens on a recording too, through the Replay Browser's source |
| **RV-7** | *"objectives about this unit"* (SR-56) | ⭐ one section in the entity inspector (the CE-1134 inspector): every row whose `ObjectiveLinks.Subject` is this entity, each with its state words and a link that selects it in the rules view. Empty ⇒ not drawn |
| **RV-8** | the mission summary, first half (⑥) | ⭐ `MissionSummaryModel` (pure) + `MissionSummaryWindow` (read-only, *Window ▸ Mission summary*), one card per side: **required objectives n / N** (`Required`, role Objective, `Owner` = side) with each state · **forces** = strength (alive units with `EntityInfo` of that side), casualties (`CombatLife.IsDestroyed`), macro-inserted (with `MacroOrigin`) · **result** from `ExerciseResult` (*running* until it ends). A list of the mission objectives with their states. ⛔ NO score, trend or pass mark: that is behaviors' B3 aggregation (⑦), never computed in the ui. ⭐ The same window opens on a recording |
| **RV-9** | where they live | ⭐ models in `Hrot.Presentation` (`Rules/`), pure and railed as data. The panel extensions in `Fdp.Presentation` (the panel's own assembly). The lanes provider in `Fdp.Toolkits/ScenarioRules`. Frame rails under `xvfb-run -a` for the panel extensions (one per X) and one for each window |

**Rejected — one line each:**
- *A rules-specific Gantt control* — R-4075: one panel; two timelines scrub and seek twice.
- *Reading history from a new store on the status* — SR-103 already rules the past is indexed lanes, which the AAR and the director share.
- *The rules view's tree as the entity tree's fourth relation* — its rows are objectives and triggers ordered by dependency and time, not entities in a parent relation; the entity tree stays entities (HV).
- *Computing scores or a pass mark in the summary* — the summary is a view; aggregation is behaviors' B3, never a second copy (the handoff's ⛔).
- *Hiding the not-yet-wired actions* — a menu whose shape changes when data arrives teaches the user twice; disabled with the reason is honest.

## 4. Phases

| phase | items | gate |
|---|---|---|
| **R1** | RV-1 X2 + X3 + RV-2 (styles, tree headers, extent from tracks) · RV-4 the lanes provider | rails: each style drawn (frame rail); the tree hides collapsed descendants; an empty index still draws tracks; the lanes index a state change and a fire (live + recording) |
| **R2** | RV-3 · RV-5 · RV-6 · RV-7 (the rules view, now-data) | rails on `RulesModel` (tree, grouping, side filter, state words, past spans from the lanes); a frame rail of the window; live check on a scenario with objectives and triggers |
| **R3** | RV-8 the summary's first half | rails on `MissionSummaryModel` (counts per side, casualties, inserted, result); a frame rail |
| **R4** | RV-1 X1 + X5 (connectors, Coming up) used by the rules view from `DependsOn` / `ActsOn` / `ExpectedAt` (③) · X4 (connect) on synthetic tracks only — the *drag to connect* that WRITES a `when` waits for the trigger editor (④) | rails + frame rail |

## 5. Claim table

| claim | code | design basis |
|---|---|---|
| the panel blanks with no index extent | ✅ `TimelinePanel.cs:142-148` | — (a gap, not a decision) |
| a live index source exists | ✅ `LiveTimelineSource.cs` | ✅ CE-3330 TE-17 |
| the status holds no history | ✅ `ObjectiveStatus.cs` fields | ✅ SR-103 (the past is lanes) |
| no rules lanes provider exists | ✅ grep, 3 providers, none for rules | ✅ SR-103 lean names it; ⛔ no handoff owns it |
| drawing the view needs no Brain-side change for ② ③ ⑤ ⑥ | ✅ all fields replicated, control through the router (`59a8e5d23`) | ✅ the handoff's "✅ now" column + §00.10 |
| who builds the lanes provider | ⛔ **not decided** by any handoff | RV-4 proposes ui; behaviors may prefer to own it |

## 6. AS BUILT — R1 *(ui, `2026-11-03`, CE-4167 part 1: RV-1 X2 + X3, RV-2 — general panel extensions, no rules view yet)*

⭐ Built as designed in §3 RV-1 / RV-2; the rest of this design stays `DESIGN`. All in `FDP/Engine/Fdp.Presentation/ImGui/Panels/Timeline/` (ui-only, no cross-lane edit). Every new member defaults OFF, so the director's tracks are unchanged (rail: no `Header`, every item `Solid`, flat label column).

| | as built | where |
|---|---|---|
| **X2** item styles | `TimelineTrackItemStyle { Solid, Expected, Hatched, Pinned }` = one more optional last field on `TimelineTrackItem`. Decisions are pure (`TrackLayout.DrawOf` → `ItemDraw`): *Solid* = today (fill + edge + grips ≥ 6 px; a key a filled diamond) · *Expected* = dashed outline, no fill, no grips (a key: a hollow diamond) · *Hatched* = outline + diagonal lines (`HatchLines`, clipped to the box), no fill · *Pinned* = a diamond at the start with a vertical tick (a Pinned **segment** is drawn solid with the diamond and tick at its start). Dashes: `Dashes` | `ITimelineTrack.cs`, `TrackLayout.cs`, `TimelinePanel.DrawTrack` |
| **X3** tree rows | `record TrackHeader(Depth, Text, Status?, StatusColour?, Icon?, Collapsible)` and `ITimelineTrack.Header` (default null). With a header the label column draws indent by depth, the expand arrow (`v` / `>`, a click toggles), the icon, the text and the status right-aligned in its colour (`PlanHeader`; the status is dropped, never overdrawn, when there is no room after the text). `TrackLayout.Visible` hides the tracks after a collapsed node that are DEEPER, up to the next at its depth or shallower (siblings stay; a header-less track ends a collapse). The panel keeps the collapsed set by track id (`SetCollapsed` / `IsCollapsed`, so a host can restore it). ⚠ The label column widens from 84 to 240 px ONLY while a drawn track has a header (`LabelWidthFor`) | same + `DrawHeader` |
| **RV-2** extent | no recording (index extent `to <= from`) but tracks with items ⇒ the extent is `[min(0, every From), max(now, every To / From)]` in sim seconds, at least 10 s wide (`ExtentOfTracks`), and the tracks and the ruler draw instead of *"No recording."*. `now` = the cursor's playhead on a sim axis, else 0. ⚠ **Deviation / finding:** a host with no recording has NO wall time map, so on its wall axis a sim-second item cannot be placed — the panel then puts the tracks on a plain sim axis of its own (`TimelineAxis.Sim` over an identity map, ticks = seconds) for that frame, and the playhead is its sim zero. A recording keeps its own extent; the lanes keep their own rule (no index ⇒ none). `TimelineView.MinSpan` needed no change (default 1/6 s; the extent is never narrower than 10 s) | `TimelinePanel.Draw`, `TrackLayout.ExtentOfTracks` |

Rails: `TimelineTrackStylesAndTreeTests` (pure: each style's geometry, dashes and hatch bounds, header indent / arrow / icon / status, collapse hides deeper not siblings, label width, extent from tracks) · `DirectorTracksTests.CE4167_…` (director tracks at the defaults) · `TimelinePanelFrameRail.CE4167_R1_…` (a real frame, NO recording: every style, a two-level tree with statuses, collapse; screenshots viewed, not committed). Red-proofs (each compiles): X2 — `Expected` given a fill · X3 — `Visible` hiding every following track (siblings too) · RV-2 — the extent starting at the first item instead of `min(0, …)`.
