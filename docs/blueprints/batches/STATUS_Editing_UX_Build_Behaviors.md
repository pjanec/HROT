<!--STATUS state: LIVE · updated: 2026-11-07 · current-answer: the table -->
# STATUS — behaviors, editing-UX remainder ([handoff](HANDOFF_Coordinator_To_Behaviors_Editing_UX_Remainder.md), scope frozen at `b76ddfb4`)

Started at `6d361486a` (merged coordinator; then backend `1dfb2d2d9`, ui `e713ec394`).

| item | state | sha | what the other lanes can now use / notes |
|---|---|---|---|
| V-1 | DONE | `66acc54ee` | `TriggerAdvertisesItsAuthoredKeys` green: the test now asserts the TYPED `do` (task 17 made it `List<ActionStep>`; only a step's `args` is free-form JSON) — the schema was right, the test was stale. SV-1 note answered: the four blueprint float instants queued as CE-2421 |
| V-2 CE-4306 | DONE | `4e24c2ef7` | ⭐ an operator's order (`EntityOrders.Order` / `Behavior`, every station path) is an `EditOp.Command`: journalled, and the brain's owner answers **Applied** or **Refused + why** (`BrainRefusalText`: *"a higher-ranked order is running"* …, or the class resolver's detail) under the order's id → `EntityEditOutcome` → your `EditNotifications`. ⚠ wire: `TacticalIntentRequest.RequestId` appended. ⏭ Release (a Clear) unanswered — its event is recorded (design §4.4) |
| V-3 CE-4307 | DONE — *Begin phase* built after P-6 merged | (this commit) | ⭐ `ActionSentence.Parts` (Hrot.Presentation/Rules): a do step as words — use it wherever a step is shown. The Do tab: buttons before the sentence; an Order step offers ORDER KINDS (`OrderMenu.Orders`); the `Order` verb accepts `Order.<kind>`; ⭐ new verb `BeginPhase {target}` (fires a phase trigger; `RuleOrigin.Rule`) — ui: offer it in a phase's menus / the Phases group |
| V-4 CE-4308 | DONE | (this commit) | ⭐ **ui:** `RulesWindow.OpenGallery(subjectNetworkId, areaNetworkId)` — call it from the mission task's *New rule…* (P-7 7c) and the timeline's *Add rule at this moment…* (A6); + Add opens the gallery, every kind under *More…*. ⚠ catalogue: `RuleMenuActions.DeclareInto` (one line added to `ActionCatalogue.Build`), ids 545/546; the menu goldens gained exactly those two rows (Edit mode, units / areas) — for the user's review with A3's |
| V-5 CE-4309 | DONE | (this commit) | CE-4272 MEASURED: an A-10C born at 300 m flies on (150 m/s, 30 s) — the crash is a ground start. `PlacementSession` in the placement gizmo: flying types airborne (Shift: on the ground), PgUp/PgDn level where floors stack, drag = heading; hint lines as the mockup. ⏭ **ui:** the mockup's "Place <type>" tool panel is P1's tool panel — not built here; `PlacementSession.Hint` gives its words |
| V-6 CE-4310 | DONE | `f5c270bd3` | the raw Components tab: nested structs and short inline arrays open by default, Expand / Collapse All reach them, typed fields (EntityRef/Link, PointLink, AreaLink, RefKey, SimInstant/Duration) drawn as leaves through the forms' own `ReferenceLeafDrawers.Handles`; tab order measured already last |

## Lifecycle fixes ([handoff](HANDOFF_Coordinator_To_Lanes_Lifecycle_Fixes.md), frozen at `f523103e`) — claimed after the editing-UX remainder

| item | state | sha | notes |
|---|---|---|---|
| L-3 CE-4322 | DONE | (this commit) | ⭐ `InstructorEnd` + `InstructorEndControl.CreateOneShot / SeatRefusal` (wired by the RulesBundle on every host): no instructor-end rule ⇒ a one-shot EndExercise "Ended by the instructor"; ⭐ `IExerciseEnder` on `ClusterTimeTransportAdapter.Ender` — ■ in Live asks "End the exercise?" (house Cancel = keep running), leaves Live once the end is in. Gates: Presentation.Tests RunnableRules + TheOneTimeFacade + MainToolbarTimeControl + RulesWindowFrameRail + MissionSummary 219/0; AiShared TheRulesBundleTests 11/0; the ■ rail red-proved. ⚠ no cluster rail run (row 8): the end itself is the existing EndExercise verb through the Game master's path |
| L-4 CE-4312 | DONE by backend | — | not ours: backend built it (`EntityNames.Next`, their status row) before this claim — claim withdrawn |
| L-5 CE-4321 | DONE | (this commit) | ⏪ Review beside ▶ in Live (`ReviewChoices()`, the mode menu's list); `RunMode.ReviewBanner` on the Player. Gates: Presentation.Tests MainToolbarTimeControl + TheOneTimeFacade + TransportIcons 174/0, ColourIconsFrameRail 6/0 under Xvfb (rendered, compared); Hrot.Player builds |
| L-6 CE-4318 (the window) | DONE | (this commit) | `ProblemsWindow` + the toolbar ⚠ count (editor). Gates: Presentation.Tests TheProblemsWindowTests 3/0 (one under Xvfb, rendered), Hrot.Editor builds; the select rail red-proved |
| RX6 (LC-7) | DONE | V-4 | + Add shows the gallery first; every kind under More… |

## Taken from ui ([handoff](HANDOFF_Coordinator_To_Behaviors_Taken_From_Ui.md), frozen at `9cce7b4a`) — merged coordinator/ui/backend first; none was started by ui

| item | state | sha | notes |
|---|---|---|---|
| W-4 Run to … (LC-10, over backend's `RunToController`) | ⏳ CLAIMED | — | first: smallest, backend half built |
| W-3 A6 (timeline + mode indicator as catalogue targets) | ⏳ CLAIMED | — | |
| W-1 P-8 CE-4304 (mission plan on the map, Phases group, ONE Tasks lane) | ⏳ CLAIMED | — | |
| W-2 B-17 (map interaction P1, P2, P4–P6) | ⏳ CLAIMED | — | |

## Gates
| item | command | result |
|---|---|---|
| V-6 | Fdp.Presentation.Tests (inspector / reflector / renderer / panels / forms / editing subsets) · Hrot.Editor.AiShared.Tests (whole) · Hrot.Blueprints.Tests (Details / ScenarioComponentsView / Editor) | 175/0 · 2264/0 · 1242/1 — the 1 red is CE-2422 (CE1161 start-time form), PRE-EXISTING: red with V-6's production files reverted; a 2nd red (CycloneDDS TypeLoad) was the stale restore, green after restore. ⚠ the full Fdp.Presentation.Tests run crashes its test host (also with the new tests excluded; not proved at base). 5 new rails + 1 guard, each red-proved |
| V-5 | Presentation.Tests EntityPlacementGizmoTests+ScenarioSpawnAdapter+Placement · the live probe (`--mode all`, A-10C at z 300) | 18/0; 3 new rails, the airborne one red-proved (Resolve ignoring the height ⇒ red); live: 300 m · 150 m/s · health 600/600 for 30 s |
| V-4 | Presentation.Tests R5c+R5b+TheMenusAreGolden+TheActionCatalogue · AiShared.Tests TheRulesBundleTests · build Hrot.Editor / Hrot.CGF / Hrot.ReplayBrowser | 45/0 · 8/0 · 0 errors ×3; goldens regenerated, diff = only rows 545/546 (12 lines, scripted check); gallery rail red-proved (no common.subject ⇒ 1/2) |
| V-3 | `quick-check.sh Hrot.Presentation.Tests RuleEditorR5bTests` · `xvfb-run … --filter RulesWindowFrameRail\|Rules` | 15/0 · 172/0/0 (display rails ran, not skipped); 3 new rails; the key one red-proved (RuleSentence back on Humanize(act) ⇒ TheListLine red, 14/1) — a visual item, one red-proof (proportionate); frames compared with rule-list-visual.svg; BeginPhase: ScenarioRulesTests 138/0, red-proved (the Control call removed ⇒ 0/1) |
| V-2 | Fdp.Toolkits.Tests Behavior+Replication · SimHost.Tests TacticalIntent/InteractionTransport/EntityOrders/Order · NED.Tests TacticalIntent/GenericMessage · SimHost.Integration (whole) | 859/0 · 129/0 · 15/0 · 49/1 (`MissionExecutionFlow`, pre-existing CE-3369); 7 new F5 rails, each red-proved by a mutation |
| V-1 | `quick-check.sh Hrot.Editor.Tests "ABehaviourAdvertisesItsRealParametersTests"` | 26 / 0 / 0 (was 25 / 1); test-only change, dll newer than the edit |
