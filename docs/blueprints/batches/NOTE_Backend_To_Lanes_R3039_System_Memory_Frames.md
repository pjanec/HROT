<!--STATUS
state: LIVE — backend → behaviors, ui, coordinator (traffic): FYI — R-3039 S1 edits backend made in your code, at the user's word
updated: 2026-10-22
current-answer: the whole note
related-designs:
  - docs/DESIGN_System_State_Inventory.md — §6 S1 as built (where each memory went, the rail shape); §2 the work list that is left (S2, X)
  - docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md §7g — R-3039 (the rule), the A/B/C test
-->

# NOTE — backend → behaviors · ui · coordinator (traffic): R-3039 S1 touched your code (FYI, nothing to do)

**Why.** `R-3039` (user, `2026-10-22`): *a system's working memory is entities and components; a checkpoint is a plain ECS snapshot.*
A checkpoint, a recording and the dry run copy the world and nothing else, so state a system keeps in its own fields is LOST at a
restore — 📐 measured on `EjectionSystem`: a checkpoint restored into a fresh process ejected a SECOND pilot. The S4 rail
(`EverySystemStateFieldIsClassifiedTests`) classifies every field a system, module or translator carries between frames
([the inventory](../../DESIGN_System_State_Inventory.md)); its C / X rows are the work list and may only shrink.

🔒 **User, `2026-10-22`:** *"you are free to handle yourself the squad sense memory (behaviors), the traffic pair (traffic owner), and
animation registration (ui)"* — so backend built them, instead of the frames this note first carried. Each has a *restore into a fresh
system* rail. ⭐ **New code in these systems keeps its memory on the entity** — the S4 rail fails on a new C field.

| lane | what changed in your code | ⭐ what to know | rail |
|---|---|---|---|
| **behaviors** | `SquadSenseChanges` — the per-man / per-squad memory is `SquadMemberSense` (445, on the man) and `SquadUnitSense` (446, on the commander), registered in `HrotSharedComponentRegistry` | a hand-built test world that runs the squad driver must register both (it throws otherwise — `UtilityTestWorld` and `SquadCoordinationSystemTests` do) | `SquadManeuverTests.R3039_TheUntenableWindow_SurvivesACheckpointRestoredIntoAFreshSense` |
| **behaviors** | `DangerAreaSensorSystem` — the edge memory is `EdgeFeature` / `EdgeThreatened` / `EdgeRated` on `DangerAreaCognitiveBuffer` (+8 B; a route change clears only the answer) | the `CE3072_B0_Sizes` contract counts the 8 B | `DangerAreaSensorSystemTests.R3039_TheEdges_SurviveARestoreIntoAFreshSystem` |
| **behaviors** + **ui** | the five dispatchers — `_previousAction` and `ForgetLastWorld` are gone; the action last entered is `DispatchedAction` beside `DispatchedInstanceId` (the three Brain channels: in the pad, size unchanged; `AnimationChannelStatus` / `LookAtChannelStatus`: appended, Muscle-local, not on the wire) | an index reused by the next world now starts from a fresh channel by construction (CE-3076's case) | `LocomotionDispatcherTests.R3039_TheRunningAction_IsExitedAfterACheckpointRestoredIntoAFreshDispatcher` |
| **ui** | `AnimationRuntimeBridgeSystem` — `_entityClassIds` is a derived `_registered` set; `ClassOf` takes the class from the raw `BackendHandle`, or past a handle another process encoded, from the entity's `TkbIdentity` | `CharacterAnimationDefRuntime` is unchanged (a field there broke its 16 B layout contract) | `Phase3SystemTests.R3039_AfterACheckpointRestoredIntoAFreshProcess_TheCharacterRegistersUnderItsClass` |
| **coordinator / traffic** | `TrafficShadowFollowSystem` — the follow is `TrafficShadowFollow` (447, backend's block — not traffic's 450–459, so no collision) on the body, registered by `TrafficPack`; `TrafficPanicSystem._held` classified **B** (in flight with SUMO, `R-3041`) · ⚠ also fixed in `TrafficPack`: an unbraced `if` registered `NavigationApplied` unconditionally (backend's own slip from #5) | ⏳ **the X rows (SUMO) — backend's LEAN, not ruled: a restore restarts traffic** (§2c). Re-seeding SUMO from the restored replicas is the alternative — say if a restored run must keep its traffic | `AHandedOverVehicleFollowsItsShadowTests.R3039_TheFollow_SurvivesACheckpointRestoredIntoAFreshSystem` |


## ⭐ Addendum — S3: every class-B cache forgets the world when it is replaced (`2026-10-22`)

A restore raises ONE signal (`AfterRestore.Run` → `WorldEpoch.Restored`); every owner of protocol state (class B: "last published",
"pending until the entity exists", requests in flight) now clears it on its own next tick through `WorldEpoch.Moved` — so after a restore
every owner republishes (`R-3041`). Rail: `EverySystemStateFieldIsClassifiedTests.EveryProtocolField_IsReDerivedAtAWorldReplacement`
⇒ ⭐ **a new B field needs that hook, or is class S** (session state a restore leaves — the cluster, the transport). [Inventory §8](../../DESIGN_System_State_Inventory.md)

| lane | touched | |
|---|---|---|
| **ui** | the eight `Hrot.Animation.Replication` egress translators (channel / look-at intent + status, stance intent + status, montage queue + state) | a `_worldEpoch` and one `Moved` line each |
| **coordinator / traffic** | `TrafficPanicSystem._held` cleared on `Moved` | |
| **all** | `LifecycleSystem` re-derives the ELM's handshakes after a RESTORE (not a load) — the replay design's §2.1m re-derive, until now reached only from the editor preview | rail `APreviewLeavesNoTraceTests.R3039_AfterRestoreRun_ReachesTheElm_AndALoadDoesNot` |

---
_backend lane, 2026-10-22_
