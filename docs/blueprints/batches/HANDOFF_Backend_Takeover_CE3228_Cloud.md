# HANDOFF backend → backend (new session) — finish CE-3228 (creation request on the FDP bus) and ship CE-3219 part 3 (the gas cloud)

<!--STATUS
state: LIVE
updated: 2026-10-17
current-answer: whole note — read "State", then "Do next"
related-designs:
  - docs/DESIGN_Entity_Creation_Unification.md — §3.4d (NEW, this work: one request on the FDP bus), §3.4a/§3.4b (why the forwarder wraps only the LOCAL intake)
  - docs/DESIGN_Civilians_And_Crowd_Control.md — §7.3b/§7.3c the cloud, §7.3d gas as a danger area
  - docs/DESIGN_Entity_Authoring_Surface.md — G1 / acceptance ⑧ (all authoring affordances end in a REQUEST)
-->

⚠ **WIP branch.** Pushed to a temporary branch because of a rate limit, NOT to `backend`. **The tree does NOT compile** (step 2 below is
half-done). Take this branch, finish, gate, then merge into `backend` and push `backend`. Delete the temp branch afterwards.

## Decisions the user made (verbatim) — all APPROVED, do not re-ask

- Cloud: *"Claud being a special area entity approved"* (R-3014) · *"the ground wind could move the cloud"* · *"Not every tick that would kill
  performance"* ⇒ **1 Hz drift** · **people only** (gas) · *"avoidance via brain yes … taking cover farther from its current or future expected
  location"* · *"Behaviors accepted, handoff to behavior lane should refer to the cloud design and expected behaviors. Danger area kind for gas
  seems okay."*
- Creation: *"whoever wants to create an entity simply puts a request in the form of FDP bus event, and some system will create the entity"* ·
  *"I am not aware of any scenario loading thread and network thread and UI thread … Do we really need it?"* (measured: NO — every producer
  runs on the frame thread; table in §3.4d) · *"Isn't the creation request coming from the network exactly the same one … Do we need some new
  FDP request? I do not think so."* ⇒ **reuse `EntityCreationRequest`, no new type** · *"No valid node ID should be 0"* ⇒ the editor moves
  off 0; at the request level **owner 0 = "the system picks"** (default processor → role-group grants to the least-loaded node). Both approved.
- ⛔ Rejected along the way (do NOT reintroduce): a toolkit `AreaEffectCloudRequest` event + Hrot spawn system (two steps); the cloud
  publishing `SpawnEntityCommand` directly (ORDER level — skips routing + load-balanced grants, Authoring Surface G1); a `ThisNode = -1`
  owner sentinel (reverted); the queue singleton.

## State

| | done | where |
|---|---|---|
| ✅ | the cloud: `AreaEffectCloud` (component 399), `CloudTable`/`AreaEffectClouds.Of` sight overlay at the world-query seam, `AreaEffectCloudSystem` (owner-only, 1 Hz step: drift by `WeatherField.WindAt` × elapsed, Irritant to `PhysicsColliderReaders.IsPerson` inside, destroy at end), `WarheadDto.Cloud*` + `WarheadKind.Smoke`, munitions 6039/6040/6041, launcher man carries 2 gas grenades, TKB 7202, `dtAreaEffectCloud` 144 + translators in the shared pack | `FDP/Toolkits/Fdp.Toolkits/Combat/*`, `Hrot.Core/Tkb/*`, NED |
| ✅ | the cloud asks for its entity with `AreaEffectClouds.Request` → `EntityCreationRequest` on the bus, owner 0 | `AreaEffectClouds.cs`, `AreaEffectSystem.RequestCloud` |
| ✅ | rails written: `AreaEffectCloudTests` (10), `NonLethalMunitionTests.CE3219_…`, `BodyPartReplicationTests.CE3219_…`, `EntityOrdersAcrossNodesTests.CE3219_…` (expects the DEFAULT PROCESSOR = CGF to own it — SimHost is not the arbiter, `CgfSubsystem.cs:931`) | ⚠ last green run was BEFORE the bus change (71/71 toolkit) — re-run |
| ✅ | design §3.4d (3 UML diagrams, decisions, claim table) | `DESIGN_Entity_Creation_Unification.md` |
| ✅ | `EntityCreationRequest` MOVED to `Fdp.Toolkit.NetworkSpawning.Events` (`FDP/Toolkits/Fdp.Toolkits/NetworkSpawning/Events/EntityCreationRequest.cs`), unchanged + `[DataPolicy(NoReplay)]` | |
| ✅ | `BusEntityCreationRequestSource` (local intake = `ReadManaged<EntityCreationRequest>`) replaces the queue in `EntityCreationPack`; `EntityCreation.LocalRequests` removed, `EntityCreation.Publish(request)` added, `RequestEntityCreation` publishes on the bus; event registered in the pack + `HrotSharedComponentRegistry` | `Hrot.Core/Network`, `Hrot.Common/EntityCreation` |
| ✅ | `ScenarioEntityCreationRequestSource` DELETED | |
| ⛔ | **NOT DONE — the tree does not compile until this is done:** migrate every user of the deleted queue and of the moved type | step 2 |

## Do next (in order)

1. `git fetch origin backend behaviors ui` and merge `origin/backend` into this branch (nothing should conflict; the WIP was branched from it).
2. **Migrate the ~60 files** that still name `ScenarioEntityCreationRequestSource`, `LocalRequests`, `LocalEntityCreationRequests`,
   `ScenarioSource`, or `Hrot.Core.Network.EntityCreationRequest` (list: `grep -rln "ScenarioEntityCreationRequestSource\|LocalRequests\b\|LocalEntityCreationRequests\|ScenarioSource\b" --include=*.cs Hrot Stride FDP`):
   - moved type: add `using Fdp.Toolkit.NetworkSpawning.Events;` / replace the qualifier. ⛔ never rename an identifier (CLAUDE.md ②).
   - queue holders (CGF `CgfLogicPack`/`CgfSubsystem`/`CgfApplication`/`CgfEpisodeLoadHandler`, `ScenarioLoadStep`, IG `IgNodeBootstrapper`/`IgApplication`/`MapCommandController`/`MiniExConPanelState`/`IgEntityCreationRequests`, `SimHostApp`, `NodeBootstrapper`, Editor `EditorSubsystem`/`DebugApiService`, `EntityAuthoring`, Stride `TestHarnessContext` + harness cases): hold an `FdpEventBus` (or the world) and `.Enqueue(r)` → `.PublishManaged(r)`.
   - `EjectionSystem`, `MapCameraLayer`: publish on `repo.Bus` instead of reading the singleton (drop the singleton guard; a replay has no request system — say so in the comment). This also closes `CE-3226`.
   - `ScenarioSpawnAdapter`: ALL THREE affordances (placement, area, route) publish an `EntityCreationRequest` via ONE mapping helper — zero `PublishManaged(cmd)` of a `SpawnEntityCommand` left (Authoring Surface G1/⑧). ⇒ nothing depends on the editor being node 0 any more.
   - `GlobalComponentIds.EntityCreationRequestSource`: keep the id, comment it RETIRED (never reuse).
   - tests: `EntityCreationRequestSourceTests` (drop the deleted class's tests, port Composite/Forwarding, add `BusSource_DrainsWhatWasPublished_LastFrame`), pack/forwarding/ejection/load/CGF/IG/Editor/Presentation/integration/Stride tests: publish on the bus and make sure a `SwapBuffers()` (or a kernel tick) happens before the request system runs. Never weaken an assertion.
   - ⚠ measured facts you can rely on: `PublishManaged` is lock-safe and visible after the next swap (`FdpEventBus.cs:62`); the kernel swaps once per update after `Input` (`ModuleHostKernel.cs:513/555`) and `CreateEntityRequestSystem` is `[UpdateInPhase(Input)]` ⇒ every request is read exactly once, nothing is dropped.
   - ⚠ check under strict mode (`FdpConfig.EnforceExplicitEventRegistration`, set in production `Program.cs`): `RegisterManagedEvent<EntityCreationRequest>()` calls `FdpAutoSerializer.EnsureCompiled<T>()` — the type has an `IReadOnlyDictionary<int,(long, IReadOnlyList<object>)>` property; if that throws, say so (NoReplay should exempt it from recording, but registration may still compile a serializer).
3. **Editor off node 0:** `EditorSubsystem.cs:176` `EditorNodeId = 0` → a non-zero id (lean `1`; the editor is an offline node). Check what it feeds: the staging root path (`GetNodeStagingRoot`), the recording node id, `EditorApplication.cs:38` `DerRepo(localNodeId: 0)`, tests asserting either.
4. **Build per project, never the solution** (CLAUDE.md T-tiers; build the TEST project before `--no-build`), all in the background. Projects: Hrot.Core, Hrot.Common, Hrot.Presentation, Hrot.CGF, Hrot.IG, Hrot.SimHost, Hrot.Editor, Hrot.Network.NED, their test projects, Hrot.SimHost.Integration.Tests, Hrot.ClusterRunner(.Integration).Tests, Fdp.Toolkits.Tests, Stride/*.
5. **Gates** (report per the gate contract): feature suites — `AreaEffectCloudTests`, `AreaEffectSystemTests`, `SpawnSystemTests`, `NonLethalMunitionTests`, `EffectTests.LR1_*`, `BodyPartReplicationTests`, `TheDescriptorMapIsWired`, `EntityCreationPackRails`, `EntityCreationForwardingRails`, `CreateEntityRequestSystemTests`, `EntityGenesisHazardRails`, `EjectionSystemTests`, `ScenarioLoadStepTests`, `CgfEpisodeLoadHandlerTests`, IG `MapCommandController|MiniExCon`, Editor `EditorCapabilities|OfflineKernelBoot`, Presentation `EntityAuthoringPack`; integration `EntityOrdersAcrossNodesTests` (incl. `CE3219_…`, `CE3224_…`, `CE1148_…`, `CE3167_…`) and `SplitAuthoritySpawnTests`. Red-proofs for the cloud: clouds ignored by sight ⇒ sight rails red; no drift ⇒ drift rail red; no authority gate ⇒ ownership rail red. Then the full touched unit suites once. `python3 scripts/tracker-counts.py --check`, `python3 scripts/design-digest.py --check`, mermaid check on both designs.
6. **Fold the as-built** into §3.4d (mark BUILT, note anything that deviated) and §7.3c (creation row: "the entity-creation request on the bus, owner 0 — the default processor creates it"; drop the stale "SpawnEntityCommand/ThisNode" wording there and in §7.3b's diagrams/decision table — they were written for the reverted step); update `NOTE_Backend_To_Ui_AreaEffectCloud.md` (`AreaEffectClouds.Request`, not `Spawn`); Authoring Surface ⑧ closed; tracker: CE-3219 part 3 BUILT, CE-3223 gas rounds, **CE-3226 closed by this**, new row **CE-3228** (this change) from the backend block (next free CE-3229), CE-3227 (gas danger-area kind) stays open.
7. Commit (gated work only), merge into `backend`, push `origin backend`. Send the behaviors lane the frame `HANDOFF_Backend_To_Behaviors_Gas_Reactions.md` (already written) and the ui lane the note.

## Still open after this (not for this handoff)
`CE-3227` gas as a `DangerAreaKind` (design §7.3d) · `CE-3220` part 2 (retire civilian 1001/1003) · re-run the live `ua-civ-neutral-gate`
check to confirm `CE-2242` + `CE-3225` end to end.
