# BATCH-03: Phase 2 — TKB Animation Descriptor (Design-time → Runtime)

**Batch Number:** BATCH-03  
**Tasks:** ANC-P2-01, ANC-P2-02, ANC-P2-03, ANC-P2-04, ANC-P2-05, ANC-P2-06, ANC-P2-07, ANC-P2-08  
**Phase:** Phase 2 — TKB animation descriptor  
**Estimated Effort:** 3–4 days  
**Priority:** HIGH (unblocks Phase 3–7, but depends on Phases 0–1)  
**Dependencies:** BATCH-01 (Phase 0), BATCH-02 (Phase 1)

---

## 📋 Batch Goal

Implement the TKB animation descriptor system — the bridge from design-time JSON (`.tkb` files) to runtime ECS components. This batch creates:
- **Data transfer objects (DTOs)** for design-time animation definitions
- **Stable ID hashing** for deterministic montage/marker identification
- **TKB entity translator** for component injection during entity promotion
- **Per-class baked cache** with hot-reload support
- **Baking pipeline** converting DTOs to runtime `CharacterAnimationDefRuntime` data
- **Editor query API** for picker attributes and UI support
- **Validation rules** (ANIM001–ANIM007) for design-time integrity checks

**Once complete,** the full Muscle ECS systems pipeline (Phase 3) can inject and use animation definitions, and integration tests (Phase 7) can validate end-to-end behavior.

---

## 🚀 Developer Onboarding

### Required Reading (IN ORDER)
1. **Batch Instructions:** This file — goals and architecture.
2. **Phase 0 & 1 Reports:** `.dev/anim-ctrl/reports/BATCH-01-REPORT.md` and `.dev/anim-ctrl/reports/BATCH-02-REPORT.md` — Verify P0/P1 contracts.
3. **Mini Design:** `.dev/anim-ctrl/AnimationControl_BrainMuscle_MiniDesign_v0_3.md` — Channel shapes, action flow.
4. **Task Details:** `.dev/anim-ctrl/TASK-DETAIL.md` (Phase 2 section) — Exact task specs.
5. **Design Document:** `.dev/anim-ctrl/DD-4_TKB_AnimationDescriptor_v1_2.md` — Full descriptor spec, baking algorithm, query API.
6. **Reference Design File:** `.dev/anim-ctrl/DD-4_TKB_AnimationDescriptor_v1_2.md` §8 (Sniper example JSON).
7. **Test Spec:** `.dev/anim-ctrl/DD-Tests_AnimationControl_v1_1.md` (§11.2) — TKB translator/query test expectations.

### Source Code Location
**Primary implementation:**
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Descriptors/` — DTO definitions
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Translators/` — `AnimationTkbTranslator`
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Baking/` — Baking logic
- `Hrot.Editor.AiShared/Catalog/` — Editor query API (`IAnimationTkbQueries`, `AnimationTkbQueries`)

**Test projects:**
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/` (xUnit) — Translator, cache, query tests
- `Hrot.Editor.AiShared.Tests/` — Editor query API tests (if not already present)

**Integration points:**
- Uses DTOs from Phase 2 (this batch)
- Uses components from Phase 0 (already available)
- Uses fake backend from Phase 1 (already available)
- Registers translator with `TkbDescriptorRegistry` (existing infrastructure)
- Subscribes to `ITkbHotReloadEvents` for hot reload (existing event system)

### Debt-Tracker Items to Address
- **D-03:** Verify `SimHostSubsystem` implements `IWindowRegistrar` (addressed in P1-09, confirm still valid)
- **D-04:** Phase 2+ systems must verify correct field names in channel access (`Params`/`State`)
- **D-05/D-06:** Deferred items from Phase 1; note for later phases

### Report Submission
When complete, write findings to `.dev/anim-ctrl/reports/BATCH-03-REPORT.md` including:
- **Completed tasks:** Status of all 8 tasks
- **Test results:** Translator, cache, query API test count + pass rate + runtime
- **Code metrics:** LOC per task
- **DTOs + Baking:** Validation against DD-4 §8.1 Sniper example
- **Editor integration:** Query API tested with sample DTO
- **Validators:** All 7 rules implemented + test coverage (positive/negative per rule)
- **Blockers:** Any issues preventing Phase 3 compilation or integration
- **Debt items:** Resolution status of D-03, D-04, observations on D-05/D-06

If you need clarification, create `.dev/anim-ctrl/questions/BATCH-03-QUESTIONS.md`.

---

## 📝 Task Breakdown & Success Criteria

### ANC-P2-01 — `CharacterAnimationDefDto` + nested DTOs
**File:** `Hrot.MuscleCharacter.Animation/Descriptors/CharacterAnimationDefDto.cs`  
**Reference:** DD-4 §2

**Define the DTO hierarchy:**

```csharp
[TkbDescriptor("Anim.CharacterDef")]
public class CharacterAnimationDefDto
{
    public SlotDefDto[] Slots;           // Animation layers (e.g., upper body, lower body)
    public MontageDefDto[] Montages;     // Playable animation montages
    public StanceTransitionDto[] Transitions;  // Stance-change animations
    public AimConfigDto AimConfig;       // Optional aim system config (null if no aim)
}

public class SlotDefDto
{
    public int Priority;
    public SlotCompositingMode CompositingMode;
    // ... slot-specific config (blend in/out times, etc.)
}

public class MontageDefDto
{
    public string Name;
    public int DurationFrames;
    public bool IsStanceTransition;
    public MontageNotifyRefDto[] Notifies;
    // ... blending, playback config, etc.
}

public class MontageNotifyRefDto
{
    public string Name;
    public int FrameNumber;
    public AnimNotifyCategory Kind;
    // ... notify-specific fields
}

public class StanceTransitionDto
{
    public string SourceStance;
    public string TargetStance;
    public string MontageRef;  // Montage that performs the transition
    // ... transition config
}

public class AimConfigDto
{
    public float BlendInTime;
    public float BlendOutTime;
    // ... aim-specific fields
}

public class NotifyMarkerDefDto
{
    public string Name;
    public AnimNotifyCategory Kind;
    // ... marker-specific config
}

public enum SlotCompositingMode
{
    Replace = 0,
    Blend = 1,
}
```

**Requirements:**
- `[TkbDescriptor("Anim.CharacterDef")]` attribute on root DTO
- All nested classes are `public` and JSON-serializable
- References to `AnimNotifyCategory` enum (from Phase 0)
- Name fields are unique within their collection

**Success criteria:**
- All classes compile without errors
- Deserialization test from DD-4 §8.1 Sniper JSON passes
- `[TkbDescriptor]` registers with `TkbDescriptorRegistry` (mirror existing `WeaponCapabilitiesDto` test)

---

### ANC-P2-02 — Stable ID hashing
**File:** `Hrot.MuscleCharacter.Animation/Hashing/StableIdHasher.cs`  
**Reference:** DD-4 §3.1, §3.4

**Implement deterministic hashing:**

```csharp
public static class StableIdHasher
{
    /// <summary>
    /// Compute a stable montage ID from a name using FNV1a64.
    /// Deterministic: same name → same ID across runs / machines.
    /// </summary>
    public static int ComputeMontageAssetId(string montageName)
    {
        // MontageAssetId = (int)(FNV1a64(name) & 0x7FFFFFFF)
        // Result: signed 32-bit positive int [0, 0x7FFFFFFF)
    }

    /// <summary>
    /// Compute a marker hash from a name using FNV1a32.
    /// Used by AnimNotifyEvent.MarkerHash and picker resolution.
    /// </summary>
    public static uint ComputeMarkerHash(string markerName)
    {
        // MarkerHash = FNV1a32(name)
        // Result: unsigned 32-bit
    }
}
```

**Behavior:**
- **Determinism:** Same input → same output, every run, every machine
- **Collision tolerance:** Rare collisions acceptable (documented risk); not a blocker
- **Reuse:** Call from editor, tests, and `BakeForTest`

**Success criteria:**
- Determinism test: `ComputeMontageAssetId("Reload_Rifle")` produces same ID across 10 runs
- Known-vector test: hard-code expected ID for at least one known name (e.g., "Reload_Rifle" → specific value)
- Zero collisions in a test set of ≥50 unique names

---

### ANC-P2-03 — `AnimationTkbTranslator.Inject`
**File:** `Hrot.MuscleCharacter.Animation/Translators/AnimationTkbTranslator.cs`  
**Reference:** DD-4 §4, §5.3 (DD-1), §4.2

**Implement the TKB entity translator:**

```csharp
public class AnimationTkbTranslator : ITkbEntityTranslator
{
    public IEnumerable<string> GetConsumedDescriptors()
    {
        yield return "Anim.CharacterDef";  // Register interest in CharacterAnimationDefDto
    }

    public void Inject(
        TkbEntity tkbEntity,
        in TkbPromotionContext context)
    {
        // 1. Get the DTO from context
        var dto = context.GetDescriptor<CharacterAnimationDefDto>("Anim.CharacterDef");

        // 2. Bake the DTO into CharacterAnimationDefRuntime
        var baked = BakeAnimationDef(dto);

        // 3. Inject all replicated + internal components (guarded by IsComponentTypeRegistered)
        context.SetComponent(tkbEntity, new AnimationChannel { /* ... */ });
        if (IsComponentTypeRegistered<LookAtChannel>())
        {
            context.SetComponent(tkbEntity, new LookAtChannel { /* ... */ });
        }
        context.SetComponent(tkbEntity, new CharacterAnimationDefRuntime { /* baked */ });
        context.SetComponent(tkbEntity, new AnimationExecutorState { /* ... */ });

        // 4. Conditionally inject aim components (only if AimConfig present)
        if (dto.AimConfig != null && IsComponentTypeRegistered<LookAtChannel>())
        {
            context.SetComponent(tkbEntity, new LookAtExecutorState { /* ... */ });
        }
    }
}
```

**Key behaviors:**
- **Guarded injection:** Each component check `IsComponentTypeRegistered<T>()` before inject
- **Conditional aim:** `LookAtChannel` + `LookAtExecutorState` only if `AimConfig != null`
- **Baking:** Call internal bake logic to produce runtime data from DTO
- **No ordering deps:** Translator can run in any order

**Success criteria:**
- Unit test: promoting a Sniper template attaches exactly the component set from DD-4 §8.3
- Conditional test: DTO without `AimConfig` → no aim components injected
- Component-count test: expected number of components (≈9 replicated + internal)

---

### ANC-P2-04 — Per-class baked cache + hot reload
**File:** `Hrot.MuscleCharacter.Animation/Baking/BakedAnimationCache.cs`  
**Reference:** DD-4 §4.1, §7, §9.1

**Implement the baking cache:**

```csharp
public class BakedAnimationCache : IDisposable
{
    private ConcurrentDictionary<long, CharacterAnimationBakedData> _cache =
        new ConcurrentDictionary<long, CharacterAnimationBakedData>();

    private ITkbHotReloadEvents _hotReloadEvents;
    private IDisposable _subscription;

    public BakedAnimationCache(ITkbHotReloadEvents hotReloadEvents)
    {
        _hotReloadEvents = hotReloadEvents;
        _subscription = hotReloadEvents.Subscribe(OnDescriptorChanged);
    }

    public CharacterAnimationBakedData GetOrBake(long classId, CharacterAnimationDefDto dto)
    {
        return _cache.GetOrAdd(classId, _ => BakeDef(dto));
    }

    private void OnDescriptorChanged(TkbDescriptorChangedEvent evt)
    {
        // If the descriptor ID matches one in the cache, evict it
        if (evt.DescriptorName == "Anim.CharacterDef")
        {
            _cache.TryRemove(evt.ClassId, out _);
        }
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _cache.Clear();
    }
}
```

**Behavior:**
- **Per-class caching:** One entry per `classId` (entity template)
- **Hot reload:** On `DescriptorChanged` for `Anim.CharacterDef`, evict the affected class's cache entry
- **Lazy baking:** Call `GetOrBake` only when needed; cache hit on repeated promotes of same template
- **Lifecycle:** Unsubscribe from hot-reload events on `Dispose`

**Success criteria:**
- Unit test: first `GetOrBake` calls `BakeDef`; second call returns cached result
- Unit test: `OnDescriptorChanged` for the class evicts; next `GetOrBake` re-bakes
- Unit test: `OnDescriptorChanged` for unrelated descriptor is ignored (cache untouched)
- Lifecycle test: `Dispose` unsubscribes; no errors on subsequent events

---

### ANC-P2-05 — `CharacterAnimationDefRuntime` baking + `BakeForTest`
**File:** `Hrot.MuscleCharacter.Animation/Baking/BakedAnimationDef.cs`  
**Reference:** DD-4 §4 (`BakeDef`), §8.3; DD-Tests §8, §11.2

**Implement the baking algorithm:**

```csharp
public class CharacterAnimationBakedData
{
    // Built from DTO during baking:
    public Dictionary<int, MontageInfo> MontageDict;        // MontageAssetId → info
    public HashSet<byte> SupportedStances;                   // Stance IDs available in DTO
    public Dictionary<(byte, byte), string> TransitionMap;   // (from, to) → transition montage name
    public List<SlotInfo> Slots;                             // Priority-sorted slot list
    public AimSnapshot AimSnapshot;                          // Aim config snapshot (nullable)
}

public static class BakingUtils
{
    /// <summary>
    /// Bake a DTO into runtime baked data.
    /// Builds montage dict, stance set, transition table, slot table (sorted by priority), aim config.
    /// </summary>
    public static CharacterAnimationBakedData BakeDef(CharacterAnimationDefDto dto)
    {
        var montageDict = new Dictionary<int, MontageInfo>();
        var supportedStances = new HashSet<byte>();
        var transitionMap = new Dictionary<(byte, byte), string>();
        var slots = new List<SlotInfo>();

        // 1. Populate montage dict with stable IDs
        foreach (var montageDef in dto.Montages)
        {
            int assetId = StableIdHasher.ComputeMontageAssetId(montageDef.Name);
            montageDict[assetId] = new MontageInfo
            {
                Name = montageDef.Name,
                Duration = montageDef.DurationFrames,
                IsStanceTransition = montageDef.IsStanceTransition,
                Notifies = montageDef.Notifies.Select(n => new NotifyInfo
                {
                    Name = n.Name,
                    MarkerHash = StableIdHasher.ComputeMarkerHash(n.Name),
                    Kind = n.Kind,
                    Frame = n.FrameNumber,
                }).ToList(),
            };
        }

        // 2. Build stance set from transitions
        foreach (var trans in dto.Transitions)
        {
            byte fromId = ParseStanceId(trans.SourceStance);  // Convert name → ID
            byte toId = ParseStanceId(trans.TargetStance);
            supportedStances.Add(fromId);
            supportedStances.Add(toId);
            transitionMap[(fromId, toId)] = trans.MontageRef;
        }

        // 3. Sort slots by priority (ascending)
        slots = dto.Slots.OrderBy(s => s.Priority)
            .Select(s => new SlotInfo { Priority = s.Priority, CompositingMode = s.CompositingMode })
            .ToList();

        // 4. Snapshot aim config (if present)
        var aimSnapshot = dto.AimConfig != null ? new AimSnapshot
        {
            BlendInTime = dto.AimConfig.BlendInTime,
            BlendOutTime = dto.AimConfig.BlendOutTime,
        } : null;

        return new CharacterAnimationBakedData
        {
            MontageDict = montageDict,
            SupportedStances = supportedStances,
            TransitionMap = transitionMap,
            Slots = slots,
            AimSnapshot = aimSnapshot,
        };
    }

    /// <summary>
    /// Public test API: Bake a DTO directly without caching or registration.
    /// Exposed via [InternalsVisibleTo("Hrot.Animation.Integration.Tests")].
    /// </summary>
    internal static CharacterAnimationBakedData BakeForTest(CharacterAnimationDefDto dto)
    {
        return BakeDef(dto);
    }
}
```

**Behavior:**
- **Deterministic:** Same DTO → same baked data
- **Stable IDs:** Use hashing from P2-02 for montage IDs
- **Priority sort:** Slots ordered by priority (ready for composition in systems)
- **Aim optional:** If `AimConfig` null, `AimSnapshot` is null
- **Component integration:** `CharacterAnimationDefRuntime` will hold a reference to baked data

**Success criteria:**
- Baking test: `BakeDef(dto)` produces non-null `MontageDict` with expected montage count
- Baking test: `TryGetMontageInfo(assetId, out info)` resolves a baked montage's slot/duration/notifies
- Parity test: `BakeForTest(dto)` produces equivalent data to production baking path
- Slot-sort test: slots returned in priority order (ascending)

---

### ANC-P2-06 — `IAnimationTkbQueries` editor query API
**File:** `Hrot.Editor.AiShared/Catalog/IAnimationTkbQueries.cs` + `AnimationTkbQueries.cs`  
**Reference:** DD-4 §5, §9.6

**Define the query interface:**

```csharp
public interface IAnimationTkbQueries
{
    /// <summary>
    /// Get all playable montages for the current target class, excluding stance-transition montages.
    /// </summary>
    IEnumerable<string> GetPlayableMontages();

    /// <summary>
    /// Get a specific montage by name.
    /// </summary>
    bool TryGetMontage(string name, out MontageInfo info);

    /// <summary>
    /// Get all supported stances for the current target class.
    /// </summary>
    IEnumerable<byte> GetSupportedStances();

    /// <summary>
    /// Check if the animation definition supports aim.
    /// </summary>
    bool SupportsAim();

    /// <summary>
    /// Get all available marker names (union of all markers across all montages).
    /// </summary>
    IEnumerable<string> GetAvailableMarkers();

    /// <summary>
    /// Get marker name from hash (reverse lookup).
    /// </summary>
    bool TryGetMarkerName(uint markerHash, out string name);

    /// <summary>
    /// Resolve a montage name to its stable ID.
    /// </summary>
    int ResolveMontageId(string montageName);
}
```

**Implement in `Hrot.Editor.AiShared.Catalog`:**

```csharp
public class AnimationTkbQueries : IAnimationTkbQueries
{
    private CharacterAnimationBakedData _bakedData;

    public AnimationTkbQueries(CharacterAnimationBakedData bakedData)
    {
        _bakedData = bakedData;
    }

    public IEnumerable<string> GetPlayableMontages()
    {
        // Exclude IsStanceTransition == true
        return _bakedData.MontageDict.Values
            .Where(m => !m.IsStanceTransition)
            .Select(m => m.Name);
    }

    public IEnumerable<string> GetAvailableMarkers()
    {
        // Union of all marker names across all montages
        return _bakedData.MontageDict.Values
            .SelectMany(m => m.Notifies)
            .Select(n => n.Name)
            .Distinct();
    }

    // ... implement remaining methods
}
```

**Behavior:**
- **Filtering:** Playable list excludes `IsStanceTransition` montages
- **Marker union:** Collects all markers from all montages
- **Reverse lookup:** `TryGetMarkerName` searches all montages for matching hash
- **Target class context:** Use current target class to fetch baked data

**Success criteria:**
- Query test: playable list excludes `Trans_*` montages from Sniper DTO
- Query test: `GetAvailableMarkers()` returns union of all marker names
- Query test: `ResolveMontageId("Reload_Rifle")` matches hash from P2-02
- Stance test: `GetSupportedStances()` returns all stances from transitions

---

### ANC-P2-07 — Validators ANIM001–ANIM007
**File:** `Hrot.MuscleCharacter.Animation/Validators/AnimationValidators.cs`  
**Reference:** DD-4 §6

**Implement 7 validator rules:**

| ID | Rule | Trigger | Severity | Scope |
|----|------|---------|----------|-------|
| ANIM001 | Montage exists | PlayMontage/etc node picks unknown montage | ERROR | Blueprint |
| ANIM002 | Stance supported | SetStanceNode picks unsupported stance | ERROR | Blueprint |
| ANIM003 | Aim config present | LookAtPoint/Entity used but AimConfig null | ERROR | Blueprint |
| ANIM004 | Marker exists | AnimNotifyEvent picks unknown marker | WARNING | Blueprint |
| ANIM005 | Chain same-slot | PlayMontageChain entries in different slots | ERROR | Blueprint |
| ANIM006 | DTO transition montage exists | CharacterAnimationDefDto.Transitions refs missing montage | ERROR | TKB load |
| ANIM007 | DTO notify marker exists | MontageNotifyRefDto refs missing marker | ERROR | TKB load |

**Behavior:**
- **ANIM001–005:** Run at Blueprint/node compile time (Stage-2 validator)
- **ANIM006–007:** Run at TKB load (DTO validation)
- **Severities:** 001/002/003/005/006/007 are errors (blocking); 004 is warning (non-blocking)

**Example (ANIM001 — montage exists):**
```csharp
public static void ValidateMontagePick(int montageId, IAnimationTkbQueries queries)
{
    if (!queries.TryGetMontage(montageId, out _))
    {
        LogError("ANIM001: Montage not found in character definition");
    }
}
```

**Success criteria:**
- Per-rule test: positive case (valid input) passes
- Per-rule test: negative case (invalid input) produces expected error/warning
- Severity verification: ANIM004 produces warning, others produce errors
- DTO-level test (ANIM006/007): validation runs at TKB load, catches missing transitions/markers

---

### ANC-P2-08 — TKB translator/query test suite
**File:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/TkbTranslatorTests.cs`  
**Reference:** DD-Tests §11.2

**Create dedicated translator + cache + query tests:**

```csharp
[Collection("AnimationTkb")]
public class AnimationTkbTranslatorTests
{
    // Test translator injection, cache behavior, query API
    [Fact]
    public void Translator_InjectsAllComponents_WhenPromoting()
    {
        // Arrange: Sniper DTO from DD-4 §8.1
        var dto = TestData.SniperCharacterAnimationDef();

        // Act: Promote an entity with the translator
        var entity = PromoteWithTranslator(dto);

        // Assert: All expected components attached
        Assert.True(entity.HasComponent<AnimationChannel>());
        Assert.True(entity.HasComponent<CharacterAnimationDefRuntime>());
        Assert.True(entity.HasComponent<AnimationExecutorState>());
        // ... 7+ replicated components
    }

    [Fact]
    public void Cache_InvalidatesOnDescriptorChange()
    {
        // Test: bake once → cached; DescriptorChanged → evict; next → re-bake
    }

    [Fact]
    public void Queries_ExcludesStanceTransitionMontages()
    {
        // Test: GetPlayableMontages() excludes Trans_* entries
    }

    [Fact]
    public void Queries_UnionAllMarkers()
    {
        // Test: GetAvailableMarkers() contains all marker names
    }

    // ... 5+ additional tests covering validators, edge cases
}
```

**Coverage:**
- Translator injection (all components, conditional aim)
- Cache eviction on descriptor change
- Query API (playables, stances, markers, aim support)
- Validators (positive + negative per rule)
- Edge cases (empty DTO, null aim config, etc.)

**Success criteria:**
- Suite green (10+ tests)
- <1 s total runtime
- All coverage areas addressed per DD-Tests §11.2

---

## ✅ Acceptance Criteria for Batch-03

1. **All 8 tasks completed:**
   - DTOs defined and deserializable
   - Stable hashing deterministic
   - Translator injects components correctly
   - Cache with hot-reload working
   - Baking produces correct runtime data
   - Editor query API functional
   - All 7 validators implemented
   - Test suite comprehensive

2. **DTO validation:**
   - Deserialization from DD-4 §8.1 Sniper JSON passes
   - Baking produces expected montage/stance/transition data

3. **Translator integration:**
   - Registers with `TkbDescriptorRegistry`
   - Injects all Phase 0 components
   - Conditional aim components per `AimConfig`
   - No blocking errors

4. **Test coverage:**
   - 15+ tests covering translator, cache, queries, validators
   - <1 s total runtime
   - Positive + negative cases per validator

5. **Editor integration:**
   - Query API used by picker attributes (P4-02, P5-01+)
   - Ready for custom drawers

6. **No blockers for Phase 3:**
   - Systems can now query animation definitions
   - Baked data available for dispatcher/executor systems
   - Ready for Layer-2 system tests

7. **Batch report submitted:**
   - `.dev/anim-ctrl/reports/BATCH-03-REPORT.md`
   - Task-by-task summary
   - Test results + metrics
   - DTO validation against Sniper example
   - Validator coverage matrix

---

## 🔗 Next Batch

Once BATCH-03 is approved, the next batch will cover **Phase 3 — Muscle ECS Systems** (ANC-P3-01 through ANC-P3-11), implementing seven animation systems, capability-change reactors, and Layer-2 system tests.
