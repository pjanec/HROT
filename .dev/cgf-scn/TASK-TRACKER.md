# Task Tracker

**Reference:** See [TASK-DETAIL.md](./TASK-DETAIL.md) for complete task descriptions.

---

## Phase 1 — Entity Creation Source Infrastructure

**Goal:** Extend the entity creation ingestion pathway in CGF to accept
both live DDS requests and in-memory scenario-sourced requests.

- [x] **TASK-C001** ScenarioEntityCreationRequestSource [details](./TASK-DETAIL.md#task-c001--scenarioentitycreationrequestsource)
- [x] **TASK-C002** CompositeEntityCreationRequestSource [details](./TASK-DETAIL.md#task-c002--compositeentitycreationrequestsource)
- [x] **TASK-C003** Wire composite source into CgfLogicPack [details](./TASK-DETAIL.md#task-c003--wire-composite-source-into-cgflogicpack)

---

## Phase 2 — Staging Entity Extractor

**Goal:** Implement reusable staging-repo-based extraction of `EntityCreationRequest`
objects from a scenario/episode JSON, including network ID remapping and
component filtering.

- [ ] **TASK-C013** EntityCreationRequest DTO extension + CreateEntityRequestSystem genesis gateway [details](./TASK-DETAIL.md#task-c013--entitycreationrequest-extension-and-createentityrequestsystem-genesis-gateway)
- [ ] **TASK-C004** StagingEntityExtractor [details](./TASK-DETAIL.md#task-c004--stagingentityextractor)
- [ ] **TASK-C005** Behavior param remapping infrastructure [details](./TASK-DETAIL.md#task-c005--behavior-param-remapping-infrastructure)

---

## Phase 3 — CGF Scenario Load Handler

**Goal:** Replace the CGF's header-peek-only scenario handler with one that
injects entities through the genesis pipeline.

- [ ] **TASK-C006** CgfScenarioLoadHandler [details](./TASK-DETAIL.md#task-c006--cgfscenarioloadhandler)

---

## Phase 4 — CGF Episode Load Handler

**Goal:** Fix the architectural defects in episode injection by replacing the
CGF's `ReferenceEpisodeLoadHandler` with a staging-pipeline-based handler.

- [ ] **TASK-C007** CgfEpisodeLoadHandler [details](./TASK-DETAIL.md#task-c007--cgfepisodeloadhandler)

---

## Phase 5 — Generic Mission Editor UI

**Goal:** Replace hardcoded doctrine param UI in `MissionPanel` with a
generic, DTO-attribute-driven rendering mechanism.

- [ ] **TASK-C008** Presentation attributes (MapPickable*) [details](./TASK-DETAIL.md#task-c008--presentation-attributes)
- [ ] **TASK-C009** BehaviorUiCompiler [details](./TASK-DETAIL.md#task-c009--behavioruicompiler)
- [ ] **TASK-C010** MissionPanel integration [details](./TASK-DETAIL.md#task-c010--missionpanel-integration)
- [ ] **TASK-C011** Composition root registration [details](./TASK-DETAIL.md#task-c011--composition-root-registration)

---

## Phase 6 - SimHost Episode Handler Passive Demotion

**Goal:** Prevent split-brain entity duplication when CGF episode genesis is
active.  Must ship in the same release as TASK-C007.

- [ ] **TASK-C012** Demote SimHost episode handler to world:null [details](./TASK-DETAIL.md#task-c012--simhost-episode-handler-passive-demotion)
