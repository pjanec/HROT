<!--STATUS state: LIVE · updated: 2026-11-07 · current-answer: the table -->
# STATUS — behaviors, editing-UX remainder ([handoff](HANDOFF_Coordinator_To_Behaviors_Editing_UX_Remainder.md), scope frozen at `b76ddfb4`)

Started at `6d361486a` (merged coordinator; then backend `1dfb2d2d9`, ui `e713ec394`).

| item | state | sha | what the other lanes can now use / notes |
|---|---|---|---|
| V-1 | DONE | `66acc54ee` | `TriggerAdvertisesItsAuthoredKeys` green: the test now asserts the TYPED `do` (task 17 made it `List<ActionStep>`; only a step's `args` is free-form JSON) — the schema was right, the test was stale. SV-1 note answered: the four blueprint float instants queued as CE-2421 |
| V-2 CE-4306 | DONE | `4e24c2ef7` | ⭐ an operator's order (`EntityOrders.Order` / `Behavior`, every station path) is an `EditOp.Command`: journalled, and the brain's owner answers **Applied** or **Refused + why** (`BrainRefusalText`: *"a higher-ranked order is running"* …, or the class resolver's detail) under the order's id → `EntityEditOutcome` → your `EditNotifications`. ⚠ wire: `TacticalIntentRequest.RequestId` appended. ⏭ Release (a Clear) unanswered — its event is recorded (design §4.4) |
| V-3 CE-4307 | DONE (part) — *Begin phase* SKIPPED, blocked on backend P-6 (CE-4302) | (this commit) | ⭐ `ActionSentence.Parts` (Hrot.Presentation/Rules): a do step as words — use it wherever a step is shown. The Do tab: buttons before the sentence; an Order step offers ORDER KINDS (`OrderMenu.Orders`); the `Order` verb accepts `Order.<kind>` |
| V-4 CE-4308 | — | — | |
| V-5 CE-4309 | — | — | |
| V-6 CE-4310 | — | — | |

## Gates
| item | command | result |
|---|---|---|
| V-3 | `quick-check.sh Hrot.Presentation.Tests RuleEditorR5bTests` · `xvfb-run … --filter RulesWindowFrameRail\|Rules` | 15/0 · 172/0/0 (display rails ran, not skipped); 3 new rails red-proved by mutation (Message arm, RuleSentence on Humanize, WithOrderKind bare kind); frames compared with rule-list-visual.svg |
| V-2 | Fdp.Toolkits.Tests Behavior+Replication · SimHost.Tests TacticalIntent/InteractionTransport/EntityOrders/Order · NED.Tests TacticalIntent/GenericMessage · SimHost.Integration (whole) | 859/0 · 129/0 · 15/0 · 49/1 (`MissionExecutionFlow`, pre-existing CE-3369); 7 new F5 rails, each red-proved by a mutation |
| V-1 | `quick-check.sh Hrot.Editor.Tests "ABehaviourAdvertisesItsRealParametersTests"` | 26 / 0 / 0 (was 25 / 1); test-only change, dll newer than the edit |
