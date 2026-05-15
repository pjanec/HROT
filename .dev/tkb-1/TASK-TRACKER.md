# TASK-TRACKER: Transient Knowledge Base (TKB) — tkb-1

| ID | Title | Status |
|---|---|---|
| **Phase 1: Domain Schema** | | |
| TKB-001 | `[TkbDescriptor]` and field attributes | [ ] |
| TKB-002 | Concrete DTOs (TkbMasterDto, VehicleParametersDto, WeaponCapabilitiesDto, AmmoWeaponBallisticsDto) | [ ] |
| **Phase 2: VFS and Transport Tier** | | |
| TKB-003 | `TkbEntityFile`, `ITkbStorageStrategy`, `RawDirectoryTkbProvider` | [ ] |
| TKB-004 | `ZipTkbProvider` (read-only; `NotSupportedException` on write) | [ ] |
| TKB-005 | `TkbUnifiedLoader` | [ ] |
| **Phase 3: In-Memory Registry Refactoring** | | |
| TKB-006 | Refactor `TkbTemplate` to pure descriptor bag (remove `ApplyTo`) | [ ] |
| TKB-007 | Extend `ITkbDatabase` with `Clear()`, `GetEntitiesByCategory()`, `ActiveTkbName` | [ ] |
| TKB-008 | Implement new members in `TkbDatabase` (directory boundary semantics) | [ ] |
| **Phase 4: Streaming Deserialization** | | |
| TKB-009 | `TkbDeserializer` and `TkbFormatException` (zero-alloc span parsing; LOH test) | [ ] |
| **Phase 5: Source Generator** | | |
| TKB-010 | `TkbDescriptorRegistry` with `AlternateLookup` | [ ] |
| TKB-011 | `Tkb.SourceGen` project and `TkbDescriptorGenerator` | [ ] |
| **Phase 6: ECS Projection & Translator Wiring** | | |
| TKB-012 | `ITkbEntityTranslator` interface | [ ] |
| TKB-013 | `VehicleKinematicsTkbTranslator` (reference implementation) | [ ] |
| TKB-014 | Migrate `ApplyTo` in `GhostPromotionSystem`, `NetworkSpawningSystem`, `BlueprintApplicationSystem` | [ ] |
| TKB-015 | Register `ITkbDatabase` as ECS singleton in SimHost/CGF bootstrappers | [ ] |
| TKB-022 | Instantiate and wire `ITkbEntityTranslator` list in composition root | [ ] |
| **Phase 7: Node-Side Handler** | | |
| TKB-019 | `TkbLoadClusterStateHandler` (local scenario peek; `ActiveTkbName` on success) | [ ] |
| **Phase 8: Scenario Header, Save Pipeline & Bootstrapper** | | |
| TKB-016 | Extend `ScenarioHeaderDto` with `TkbName` | [ ] |
| TKB-018 | Orchestrator `TkbName` consensus check (sanity gate; no wire injection) | [ ] |
| TKB-020 | Wire `TkbLoadClusterStateHandler` in `NodeBootstrapper.BuildOrchestration()` | [ ] |
| TKB-021 | Wire `ActiveTkbName` into scenario save pipeline (`ScenarioFileService`) | [ ] |
