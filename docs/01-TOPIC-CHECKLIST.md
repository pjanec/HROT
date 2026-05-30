# Documentation Update - Design Topics Checklist

**Purpose:** Track which design topics from `.dev/` sub-folders have been reflected
in the project documentation under `docs/projects/`.

**Status Legend:**
- `[ ]` = Not yet processed
- `[~]` = In Progress
- `[x]` = Completed

---

## Design Topics

### Animation

- [ ] **anim-ctrl** — Animation Control (Brain-Muscle MuscleCharacter animation pipeline)
  - Source: `.dev/anim-ctrl/`
  - Key docs: `DD-1_MuscleCharacterRuntime_v1_2.md`, `DD-2_AnimationReplication_v1_1.md`,
    `DD-3_EventCatalog_AnimationNotify_v1_3.md`, `DD-4_TKB_AnimationDescriptor_v1_2.md`,
    `DD-5_BlueprintPrimitives_v1_1.md`, `DD-Fake_FakeAnimationBackend_v1_0.md`
  - Affected docs: `docs/projects/Hrot/Subsystems/Hrot.MuscleCharacter.Animation.md` (new),
    related existing docs

### Blueprints Subsystem

- [x] **blueprints-1** — Blueprint Subsystem initial implementation (core, compiler, editor, generators, hot reload)
  - Source: `.dev/blueprints-1/`
  - Key design docs: `Blueprint_Subsystem_Architecture_v1.2.md`,
    `Blueprint_Subsystem_Runtime_Detailed_Design.md`,
    `Blueprint_Subsystem_Compiler_Detailed_Design.md`,
    `Blueprint_Subsystem_Editor_Detailed_Design.md`,
    `Blueprint_Subsystem_Hot_Reload_Detailed_Design.md`,
    `Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md`,
    `Blueprint_Subsystem_Test_Harness_Detailed_Design.md`
  - Affected docs: `docs/projects/Hrot/Blueprints/Hrot.Blueprints.Core.md`,
    `docs/projects/Hrot/Blueprints/Hrot.Blueprints.Compiler.md`,
    `docs/projects/Hrot/Blueprints/Hrot.Blueprints.Editor.md`,
    `docs/projects/Hrot/Blueprints/Hrot.Blueprints.Generators.md`

- [x] **blueprints-2** — AI Editor (HSM/BTree visual editors, NodeEditor extensions, shared infrastructure)
  - Source: `.dev/blueprints-2/`
  - Key design docs: `AI_Editor_Shared_Infrastructure.md`,
    `BTree_Editor_NodeEditor_Host_Design.md`,
    `HSM_Editor_NodeEditor_Host_Design.md`,
    `NodeEditor_Extension_NodeAttachments.md`,
    `NodeEditor_Extension_ContainerNodes.md`,
    `NodeEditor_Extension_CustomCanvasRenderer.md`
  - Affected docs: `docs/projects/Hrot/AI/Hrot.BTree.Editor.md`,
    `docs/projects/Hrot/AI/Hrot.Hsm.Editor.md`,
    `docs/projects/Hrot/Editor/` (shared infra)

- [x] **blueprints-3-when-node** — When-Node Reactivity Iteration (WhenNode, ReadEqsResultNode, SpawnEqsSensorNode lowering)
  - Source: `.dev/blueprints-3-when-node/`
  - Key design docs: `When_Reactivity_Iteration_Design_v2_2.md`
  - Affected docs: `docs/projects/Hrot/Blueprints/Hrot.Blueprints.Core.md`,
    `docs/projects/Hrot/Blueprints/Hrot.Blueprints.Compiler.md`

### AI Behaviors

- [ ] **ai-btree-deactivator-1** — EQS Sensor Lifecycle / BTree Hybrid Lifecycle Hook
  - Source: `.dev/ai-btree-deactivator-1/`
  - Key docs: `DESIGN.md`, `Btree-deactivator-DESIGN.md`, `TASK-DETAIL.md`
  - Affected docs: `docs/projects/Hrot/Subsystems/Hrot.AI.Behaviors.md`

- [ ] **ai-hsm-btree-vis-edit** — Blackboard Authoring (HSM/BTree with managed blackboard state)
  - Source: `.dev/ai-hsm-btree-vis-edit/`
  - Key docs: `Blackboard_Authoring_Detailed_Design.md`, `TASK-DETAIL.md`
  - Affected docs: `docs/projects/Hrot/AI/Hrot.BTree.Editor.md`,
    `docs/projects/Hrot/AI/Hrot.Hsm.Editor.md`,
    `docs/projects/Hrot/Editor/Hrot.Editor.AiShared.md`

- [ ] **ai-hsm-fixes-1** — HSM fixes and deferred enhancements (orthogonal region arbitration, replay)
  - Source: `.dev/ai-hsm-fixes-1/`
  - Key docs: `design-talk.md`
  - Affected docs: `docs/projects/Hrot/Subsystems/Hrot.AI.Behaviors.md`

- [x] **group-maneuvers** — Squad Coordination / Group Maneuvers (7 phases: primitives, perception, maneuver selection, authority, catalog, authoring shells, debug overlays)
  - Source: `.dev/group-maneuvers/`
  - Key design docs: `Squad_Coordination_Design_v1_1.md`,
    `Step_1_5_TargetMemory_3D_Reconciliation.md`
  - Affected docs: `docs/projects/Hrot/Subsystems/Hrot.SquadCoordination.md`

### Environment Query System

- [x] **eqs-2** — EQS v1.3 (Environment Query System, sensor lifecycle, query primitives)
  - Source: `.dev/eqs-2/`
  - Key docs: `EQS_Design_v1.3_final.md`, `IMPLEM_DETAILS.md`, `TASK-DETAIL.md`
  - Affected docs: `docs/projects/FDP/Toolkits/Fdp.Toolkits.Spatial.Eqs.md` (created), `docs/projects/FDP/Toolkits/Fdp.Toolkits.md` (updated)

### Utility AI

- [x] **utility-ai** — Utility AI (scoring runtime, source generator, curve widget, overlays, visual editor, tuning console)
  - Source: `.dev/utility-ai/`
  - Key design docs: `Utility_AI_Design_v1_1.md`,
    `Utility_AI_SourceGenerator_Design_v1_1.md`,
    `Utility_AI_Editor_Design_v1_2.md`,
    `Runtime_Tuning_Console_and_AI_Overlays_Design_v1_0.md`,
    `Curve_Editor_in_StructEdit_Guide_v1_1.md`
  - Affected docs: `docs/projects/Hrot/Subsystems/Hrot.AI.Behaviors.md` and/or new UtilityAI doc

### Navigation

- [ ] **navig-2** — Navigation Subsystem v2 (engine-backed nav, fake nav, test infrastructure)
  - Source: `.dev/navig-2/`
  - Key docs: `Navigation_Design_v2_0.md`, `DD-EngineBacked-Nav.md`, `DD-Fake-Nav.md`, `DD-Tests-Nav.md`
  - Affected docs: `docs/projects/Hrot/Subsystems/` (new Navigation doc may be needed)

### 3D Spatial Awareness

- [ ] **promote-to-3d** — 3D Cognitive Spatial Awareness Promotion (3D perception / TargetMemory)
  - Source: `.dev/promote-to-3d/`
  - Key docs: `3D_Cognitive_Spatial_Awareness_Promotion_Design_v1_1.md`, `TASK-DETAIL.md`
  - Affected docs: `docs/projects/Hrot/Subsystems/Hrot.AI.Behaviors.md` or new doc

### Debug / Breakpoints

- [ ] **breakpoints-1** — Universal Breakpoints (debug pause infrastructure)
  - Source: `.dev/breakpoints-1/`
  - Key docs: `DESIGN.md`, `universal-breakpoints-DESIGN.md`, `TASK-DETAIL.md`
  - Affected docs: `docs/projects/Hrot/Engine/`, related existing docs

### Data / Serialization

- [ ] **json-migration** — JSON Migration System (schema versioning, migration adapters)
  - Source: `.dev/json-migration/`
  - Key docs: `Migration-system.md`, `TASK-DETAILS.md`
  - Affected docs: `docs/projects/FDP/Core/`, or new doc

### Replay / Recording

- [ ] **replay-browser-frankenstein** — Replay Browser Frankenstein (enhanced replay browser)
  - Source: `.dev/replay-browser-frankenstein/`
  - Key docs: `DESIGN.md`, `TASK-DETAILS.md`
  - Affected docs: `docs/projects/Hrot/Subsystems/Hrot.ReplayBrowser.md`

### Visual Tooling

- [ ] **visual-asset-comparison** — Visual Asset Comparison tool
  - Source: `.dev/visual-asset-comparison/`
  - Key docs: `Visual_Asset_Comparison_Detailed_Design.md`, `TASK-DETAILS.md`
  - Affected docs: new doc under `docs/projects/Hrot/` or `docs/projects/Hrot/Editor/`

---

## Notes

- `ai-aaa-1`: Discussion/onboarding notes only, no implementation tasks — skip.
- `batches`: Batch instruction files (BATCH-27 through BATCH-34), workflow only — skip.
- `ai-hsm-fixes-1`: Design discussion only (no TASK-TRACKER / formal tasks) — process as best-effort.
