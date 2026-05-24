# TASK-TRACKER: Transient Knowledge Base (TKB) — tkb-1

| ID | Title | Status |
|---|---|---|
| **Phase 1: Domain Schema** | | |
| TKB-001 | `[TkbDescriptor]` and field attributes | [x] |
| TKB-002 | Concrete DTOs (TkbMasterDto, VehicleParametersDto, WeaponCapabilitiesDto, AmmoWeaponBallisticsDto) | [x] |
| **Phase 2: VFS and Transport Tier** | | |
| TKB-003 | `TkbEntityFile`, `ITkbStorageStrategy`, `RawDirectoryTkbProvider` | [x] |
| TKB-004 | `ZipTkbProvider` (read-only; `NotSupportedException` on write) | [x] |
| TKB-005 | `TkbUnifiedLoader` | [x] |
| **Phase 3: In-Memory Registry Refactoring** | | |
| TKB-006 | Refactor `TkbTemplate` to pure descriptor bag (remove `ApplyTo`) | [x] |
| TKB-007 | Extend `ITkbDatabase` with `Clear()`, `GetEntitiesByCategory()`, `ActiveTkbName` | [x] |
| TKB-008 | Implement new members in `TkbDatabase` (directory boundary semantics) | [x] |
| **Phase 4: Streaming Deserialization** | | |
| TKB-009 | `TkbDeserializer` and `TkbFormatException` (zero-alloc span parsing; LOH test) | [x] |
| **Phase 5: Source Generator** | | |
| TKB-010 | `TkbDescriptorRegistry` with `AlternateLookup` | [x] |
| TKB-011 | `Tkb.SourceGen` project and `TkbDescriptorGenerator` | [x] |
| **Phase 6: ECS Projection & Translator Wiring** | | |
| TKB-012 | `ITkbEntityTranslator` interface | [x] |
| TKB-013 | `VehicleKinematicsTkbTranslator` (reference implementation) | [x] |
| TKB-014 | Migrate `ApplyTo` in `GhostPromotionSystem`, `NetworkSpawningSystem`, `BlueprintApplicationSystem` | [x] |
| TKB-015 | Register `ITkbDatabase` as ECS singleton in SimHost/CGF bootstrappers | [x] |
| TKB-022 | Instantiate and wire `ITkbEntityTranslator` list in composition root | [x] |
| **Phase 7: Node-Side Handler** | | |
| TKB-019 | `TkbLoadClusterStateHandler` (local scenario peek; `ActiveTkbName` on success) | [x] |
| **Phase 8: Scenario Header, Save Pipeline & Bootstrapper** | | |
| TKB-016 | Extend `ScenarioHeaderDto` with `TkbName` | [x] |
| TKB-018 | Orchestrator `TkbName` consensus check (sanity gate; no wire injection) | [x] |
| TKB-020 | Wire `TkbLoadClusterStateHandler` in `NodeBootstrapper.BuildOrchestration()` | [x] |
| TKB-021 | Wire `ActiveTkbName` into scenario save pipeline (`ScenarioFileService`) | [x] |
