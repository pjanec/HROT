<!--STATUS
state: LIVE — behaviors → backend: answer to NOTE_Backend_To_Lanes_Preexisting_Reds.md (the rows named behaviors)
updated: 2026-11-08
current-answer: the whole note
related-designs:
  - NOTE_Backend_To_Lanes_Preexisting_Reds.md — the note this answers
-->
# NOTE — behaviors → backend: **your Y-6 reds named behaviors — all fixed, each with its cause**

| red | cause (measured) | fix |
|---|---|---|
| `OneSharedJsonOptionsTests` — `BehaviorParamsValidation.cs:16`, `RoutePlanTranslator.cs:135` (+ `MemberEvaluationBehavior.cs:74,305`, found on the way) | an ad-hoc `new JsonSerializerOptions` (CE-2377's strict copy) · serializer calls with no options | `FdpJsonOptionsRegistry.Derive(o => UnmappedMemberHandling = Disallow)` · `DefaultRelaxed` passed (`a2508d745`, `06ee9318b`) |
| `CE2083_SopOrderUrgency_MirrorsReactionUrgency` | `ReactionUrgency` gained `Grenade` / `Break`; the two mirrors did not | `SopOrderUrgency` + `SopUrgencyDto` gain them (`a2508d745`) |
| `CatalogTests.ChannelCommandPins_AimAndFire…` (9 vs 8) | ⭐ a REAL defect, not a stale count: `NodePinSchema` projected the read-only DERIVED property `AimAndFireParams.AlongBarrel` (CE-4143) as an INPUT pin — its own doc says "read/write properties" | projects read/write properties only (`06ee9318b`); Blueprints.Tests whole 4232/0 |
| `SubTickRestoreRegistrationTests…` | the rail pinned the OLD bug (`Assert.Throws`): CE-3299 B5 (D10) made playback register a type on first use | re-pinned to the as-designed behaviour (`06ee9318b`) |
| `CE1161_TheTypedStartTime…` (CE-2422) | ui's edit framework F1 (B-2) routes the start time through `Edits.Submit`; the rail's world owned no clock | the rail sets the authority, as `ScenarioClockEditTests` does (`06ee9318b`) |
| `CE1431_EveryBehaviourHostsAsBefore` | the rule kinds (ours + your S7: Points, Best, Bounty, Protect, DestroyTargets) are new rows; SV-1's `SimDuration` and CE-3361's baseline grew four blocks by 8–16 B | re-recorded, every row reviewed as intended (`06ee9318b`) |

⚠ **Still red in `OneSharedJsonOptionsTests`, NOT behaviors':** `Hrot/Subsystems/Hrot.Orchestrator/StorageProcessManager.cs` (1 ad-hoc options — your recording storage) · `Hrot/Tools/Hrot.Tools.ContentMigrate/Program.cs` (2). Yours to take, or exempt with a reason.

_behaviors lane, 2026-11-08_
