# ONBOARDING: Transient Knowledge Base (TKB) — tkb-1

**Workstream:** tkb-1  
**Design reference:** `.dev/tkb-1/DESIGN.md`  
**Task reference:** `.dev/tkb-1/TASK-DETAIL.md`

---

## What Is the TKB?

The **Transient Knowledge Base** is the cluster-wide, engine-agnostic blueprint registry. It
defines every entity type that can be instantiated in a simulation: tanks, infantry, helicopters,
weapons, ammunition, IG models, etc.

- Each entity is described by a set of **descriptors** — pure C# POCOs (no ECS, no MessagePack).
- Each entity has a **64-bit `TkbId`** (called `TkbType` in the codebase) used as a primary key.
- Each node loads the TKB **before** scenario content is materialized. Scenario-driven entity
  creation always finds blueprints in memory.
- The TKB **survives cluster state transitions** (it is cached across `Idle`) and is only reloaded
  when the TKB name or ZIP file timestamp changes.

**What TKB is NOT:**
- It is not the ECS entity registry — `EntityRepository` tracks live instances.
- It is not a DDS topic — TKB is static asset data loaded from disk.
- It is not tied to a specific engine — the same ZIP file feeds SimHost, IG, CGF, etc.,
  each loading only the descriptors it knows about.

---

## Where Is the Code?

| What | Where |
|---|---|
| `ITkbDatabase` interface | `FDP/Engine/Fdp.Core/Abstractions/ITkbDatabase.cs` |
| `TkbTemplate` class | `FDP/Engine/Fdp.Core/Abstractions/TkbTemplate.cs` |
| `TkbDatabase` implementation | `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDatabase.cs` |
| `TkbIdentity` ECS component | `FDP/Toolkits/Fdp.Toolkits/Replication/Components/TkbIdentity.cs` |
| Hardcoded fallback catalog | `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/NedTkbCatalog.cs` (or `BdcTkbCatalog.cs`) |
| `[TkbDescriptor]` attribute | `FDP/Toolkits/Fdp.Toolkits/Tkb/Attributes/TkbDescriptorAttribute.cs` |
| VFS tier (providers, loader) | `FDP/Toolkits/Fdp.Toolkits/Tkb/Vfs/` |
| Deserializer | `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDeserializer.cs` |
| Descriptor registry | `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDescriptorRegistry.cs` |
| Source generator | `FDP/Toolkits/Fdp.Toolkit.Tkb.SourceGen/TkbDescriptorGenerator.cs` |
| Translator interface | `FDP/Engine/Fdp.Core/Abstractions/ITkbEntityTranslator.cs` |
| Load handler (SimHost) | `Hrot/Subsystems/Hrot.SimHost/Orchestration/Handlers/TkbLoadClusterStateHandler.cs` |
| IG bootstrapper (already wires TKB) | `Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs` |
| SimHost bootstrapper | `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs` |

---

## Core Data Flow (Runtime)

```
Disk (ZIP)
    |
    v
TkbUnifiedLoader -> ZipTkbProvider -> yields TkbEntityFile (stream per entity)
    |
    v
TkbDeserializer.ParseAndRegister(file, db)
    |
    +-- reads $guid -> TkbType
    +-- for each root JSON property:
    |       split on '#' -> (HierarchicalName, PartId)
    |       TkbDescriptorRegistry.TryGetValue(key) -> thunk
    |       thunk(template, partId, jsonElement) -> template.AddDescriptor<T>(dto, partId)
    |
    v
ITkbDatabase.Register(template)
    |
    v
Runtime query: template = db.GetByType(entity.TkbType)
    |
    v
ITkbEntityTranslator.Inject(repo, entity, template)
    |
    +-- template.GetDescriptor<VehicleParametersDto>()
    +-- repo.IsComponentTypeRegistered<VehicleParams>() -- ALWAYS CHECK FIRST
    +-- repo.AddComponent<VehicleParams>(entity, ...)
```

---

## How to Add a New Descriptor

**Step 1: Define the DTO.**

Create a pure POCO in `Fdp.Toolkit.Tkb.Domain` (or a HROT-layer namespace for HROT-specific
descriptors):

```csharp
[TkbDescriptor("Gen.SensorCapabilities")]
public record SensorCapabilitiesDto
{
    [EditUnit("m")]
    public float DetectionRange { get; init; }

    public bool IsActive { get; init; }
}
```

Rules:
- Must carry `[TkbDescriptor]` with a domain-prefixed name (`Gen.`, `CGFX.`, `BIG.`, etc.).
- `TkbMaster` is the only descriptor without a prefix.
- No ECS types. No `[MessagePackObject]`. No inheritance from engine base classes.
- Use `record`, `struct`, or `class` — records are preferred.

**Step 2: The source generator does the rest.**

If your project references `Tkb.SourceGen` as an analyzer (see `DESIGN.md` Phase 5), the
generator emits a `[ModuleInitializer]` that registers the parser at app startup. No manual
`TkbDescriptorRegistry.RegisterParser(...)` call is needed.

**Step 3: Add sample JSON data.**

In your TKB JSON files, add the new descriptor block under its `HierarchicalName`:

```json
{
  "$guid": 4001,
  "TkbMaster": { "CustomName": "FLIR Sensor", "DisType": "7.1.225.0.0.0.0" },
  "Gen.SensorCapabilities": {
    "DetectionRange": 5000.0,
    "IsActive": true
  }
}
```

---

## How to Add a New ECS Translator

A **translator** converts one or more TKB descriptors (N) into one or more ECS components (M).
This is the M:N mapping described in `DESIGN.md` Phase 6.

**Step 1: Implement `ITkbEntityTranslator`.**

```csharp
public sealed class SensorTkbTranslator : ITkbEntityTranslator
{
    public IEnumerable<Type> GetConsumedDescriptors()
    {
        yield return typeof(SensorCapabilitiesDto);
    }

    public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
    {
        var dto = template.GetDescriptor<SensorCapabilitiesDto>();
        if (dto == null) return;

        // Always guard: not every engine has every component type registered.
        if (repo.IsComponentTypeRegistered<SensorParams>())
        {
            repo.AddComponent(entity, new SensorParams
            {
                DetectionRange = dto.DetectionRange,
                IsActive = dto.IsActive
            });
        }
    }
}
```

**The `IsComponentTypeRegistered<T>()` guard is mandatory.** Without it, the translator
would crash on nodes that do not have the component type registered (e.g., an IG node running
`SensorTkbTranslator` that targets a SimHost-only component).

**Step 2: Register the translator.**

Translators are passed to `GhostPromotionSystem` via constructor injection in the bootstrapper:

```csharp
// In NodeBootstrapper.BuildOrchestration() or similar composition root:
var translators = new List<ITkbEntityTranslator>
{
    new VehicleKinematicsTkbTranslator(),
    new SensorTkbTranslator(),
    // ...
};

var ghostPromotion = new GhostPromotionSystem(world, tkbDb, translators);
```

---

## How TKB Is Loaded at Runtime

The `TkbLoadClusterStateHandler` intercepts `PrepareLive` and `PrepareEdit` transitions:

1. Reads `TkbName` from the `EditLoadHandlerPayload` (placed there by the orchestrator after
   reading `ScenarioHeaderDto.TkbName` with a forward-only `Utf8JsonReader`).
2. Checks the **differential cache**: if `TkbName` and the ZIP file's modification timestamp
   are unchanged since the last load, the existing in-memory TKB is reused (no I/O).
3. On cache miss: calls `_tkbDb.Clear()`, then opens the ZIP via `TkbUnifiedLoader`, and calls
   `TkbDeserializer.ParseAndRegister()` for each entity file.
4. If `TkbName` is null (no opinion): uses `NedTkbCatalog.RegisterAll()` as the fallback.

**The TKB is NOT distributed by the orchestrator.** File distribution is out-of-band (the
`StorageGatewayModule` SMB pull pipeline or equivalent). The node assumes the ZIP is already
in its local staging area.

**The handler must be registered before `HrotScenarioLoadHandler`** in
`NodeBootstrapper.BuildOrchestration()`. Handlers run in registration order. TKB must be fully
populated before the scenario parser looks up blueprints.

---

## How the Fallback Catalog Works

If `TkbName` is null or empty (no TKB specified), `TkbLoadClusterStateHandler` falls back to
calling `NedTkbCatalog.RegisterAll(tkbDb)`. This registers the hardcoded development catalog.

`NedTkbCatalog.RegisterAll()` is already called during `HrotEnvironment.CreateTkb()` at node
startup. The handler's fallback branch only fires if the database is empty AND there is no TKB
name in the payload — it does NOT double-register.

---

## Selective Ingestion: Why Some Descriptors Are Silently Skipped

Each engine assembly registers only the `[TkbDescriptor]` types it was compiled with. An IG node
has `BIG.WeaponAmmoFireVisuals` registered; a SimHost node does not. When a SimHost node ingests
a TKB file that contains a `BIG.WeaponAmmoFireVisuals` block:

```json
"BIG.WeaponAmmoFireVisuals": { ... }
```

`TkbDeserializer` looks up `"BIG.WeaponAmmoFireVisuals"` in `TkbDescriptorRegistry` and finds no
registered parser. It silently skips the block (walks a pointer; does not parse the JSON sub-tree).
The `TkbTemplate` is registered without that descriptor.

This is intentional and correct. The same TKB ZIP file feeds all nodes asymmetrically.

---

## TkbId Layout

```
F PP KK NNNNNNNNNNNN
|  |  |  |
|  |  |  +-- Ordinal (12 digits): sequential, random, or content hash
|  |  +----- Entity Kind (2 digits): mirrors DIS Entity Type kind (extended)
|  +-------- Project number (2 digits): 00 = common Base TKB
+----------- Offline-allocation flag: 0 = standard, 9 = offline-allocated
```

Example: `0 00 01 000000000100` = standard, base TKB, Platform kind, ordinal 100.

All runtime lookups use the 64-bit value directly via `ITkbDatabase.GetByType(long tkbType)`.
The `TkbIdentity` ECS component on every live entity stores this value.

---

## Key Files to Read First

If you are new to this subsystem, read these files in order:

1. `.dev/tkb-1/DESIGN.md` — architectural overview and phase-by-phase design
2. `FDP/Engine/Fdp.Core/Abstractions/ITkbDatabase.cs` — the main service contract
3. `FDP/Engine/Fdp.Core/Abstractions/TkbTemplate.cs` — the blueprint container
4. `FDP/Toolkits/Fdp.Toolkits/Tkb/TkbDeserializer.cs` — how JSON becomes a template
5. `Hrot/Subsystems/Hrot.SimHost/Orchestration/Handlers/TkbLoadClusterStateHandler.cs` — integration point
