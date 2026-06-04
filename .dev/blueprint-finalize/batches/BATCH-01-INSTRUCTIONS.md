# BATCH-01 — DEBT-MVE-003: multi-blueprint quick-reload safety (P1)

> **Coder contract:** read `.dev/.guides/DEV-GUIDE_claude.md` first. Verify-first, cite `file:line`,
> never fake a pass, run the full implement→build→test→fix loop to green before reporting.
> **Codebase Memory MCP first** for any extra lookups (`list_projects` → `get_architecture` →
> `search_graph`/`get_code_snippet`). Project: `D-Work-IOS-IG-SimHost-FDP-2`. Do **not** use `search_code`/grep the whole tree.

## Mission (one batch, isolated)

Fix the P1 production blocker **DEBT-MVE-003**: editor blueprint quick-reload wipes sibling
blueprints from the registry and dangles their ALC delegates. Land the fix **plus a multi-blueprint
proof test** and update the debt tracker. **Scope is DEBT-MVE-003 ONLY** — do not touch codegen
(`StateFields`/DEBT-MVE-002), node pins, FunctionCall, or anything else.

## Confirmed root cause (already verified by lead — re-verify, don't re-derive from scratch)

The editor wires the **FDP** coordinator `Fdp.Toolkit.Behavior.AiHotReloadCoordinator` for blueprint
quick-reload (`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs:2089`), handed to `QuickReloadService`.

Two defects in that path:
1. **Registry wipe** — `BlueprintRegistry.CommitStaging`
   (`FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs:118-138`) builds a brand-new snapshot
   from **only** the staging buffer. Quick-reloading one blueprint (1-entry staging) erases all others.
2. **ALC dangle** — `AiHotReloadCoordinator.ApplyQuickReload`
   (`FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs:174-202`) tracks a single
   `_currentAlc` (field at line 81) and unloads it on the next reload — killing sibling blueprints'
   `Tick`/`InitDefault` delegates → access violation on their next tick.

**Out of scope (do NOT modify):** the *other* coordinator `Hrot.Editor.AiHotReloadCoordinator`
(`Hrot/Subsystems/Hrot.Editor/AiHotReloadCoordinator.cs`). It is the file-watcher full-rebuild path
for the AI-behaviors DLL, where full-replace is correct.

## Inviolable constraints

- **Do NOT change `CommitStaging` semantics.** It must remain full-replace. `SC2_CommitStaging_Replaces_PreviousContent`
  and `SC6_TwoCommitStagingCalls_SecondWins` (`Hrot.Blueprints.Tests/Runtime/BlueprintRegistryTests.cs:85,170`)
  depend on replace, and the file-watcher path depends on it. Add a **new** method instead.
- **Projection-only invariant** stays intact (loaded `.bp.json` store `"Pins": []`). This batch does not
  touch assets/codegen, so it should be unaffected — but do not introduce any pin persistence.
- Branch `blueprint-integ-1`. No `editor_stride`. GizmoMap.Contracts stays 0.2.2. Don't touch `Hrot.IG`/DDS/`Stride/`.

---

## Change 1 — `BlueprintRegistry.CommitStagingMerge` (new, upsert)

File: `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs`

Add a new public method next to `CommitStaging`. It merges (upserts) the staging buffer into a **copy**
of the current snapshot — preserving every definition NOT present in staging (siblings + code-defined
defs registered via `RegisterDirect`/`RegisterLibrary`/etc.). Atomic via `Interlocked.Exchange`.

```csharp
/// <summary>
/// Atomically merges <paramref name="staging"/> into the current snapshot by UPSERTING each
/// staged definition by id, preserving every definition not present in staging. Used by the
/// Quick-Reload path, where staging contains only the recompiled blueprint(s); siblings and
/// code-defined definitions must survive. Contrast with <see cref="CommitStaging"/>, which
/// fully replaces the snapshot (file-watcher full-rebuild path). Fires OnRegistryChanged.
/// </summary>
public void CommitStagingMerge(BlueprintRegistryStaging staging)
{
    var prev = _current; // single atomic read

    var byId            = new Dictionary<int, BlueprintDefinition>(prev.ById);
    var byName          = new Dictionary<string, int>(prev.ByName, StringComparer.Ordinal);
    var worldSingletons = new Dictionary<int, BlackboardTier>(prev.WorldSingletons);

    foreach (var kv in staging.Definitions)
    {
        // Upsert by id. If the id already maps to a different name, drop the stale name entry
        // so ByName never points at a replaced definition.
        if (byId.TryGetValue(kv.Key, out var old) &&
            !string.Equals(old.Name, kv.Value.Name, StringComparison.Ordinal))
        {
            byName.Remove(old.Name);
        }
        byId[kv.Key]          = kv.Value;
        byName[kv.Value.Name] = kv.Key;
    }

    foreach (var kv in staging.WorldSingletons)
        worldSingletons[kv.Key] = kv.Value;

    var next = new Snapshot
    {
        ById               = byId,
        ByName             = byName,
        WorldSingletons    = worldSingletons,
        WorldSingletonList = BuildWorldSingletonList(worldSingletons),
    };

    Interlocked.Exchange(ref _current, next);
    OnRegistryChanged?.Invoke();
}
```

Also add a public read accessor on `BlueprintRegistryStaging` (so the coordinator can learn which ids
were recompiled without touching the `internal Definitions` field):

```csharp
/// <summary>The blueprint ids staged in this buffer (the recompiled set for a Quick-Reload).</summary>
public IReadOnlyCollection<int> StagedBlueprintIds => Definitions.Keys;
```

Note (document, do not over-engineer): merge upserts world-singleton markings; it does not *remove* a
singleton marking for a blueprint that stops being a singleton. Acceptable for the quick-reload path.

## Change 2 — per-asset ALC retention in the FDP coordinator

File: `FDP/Toolkits/Fdp.Toolkits/Behavior/AiHotReloadCoordinator.cs`

Replace the single-ALC field with a per-blueprint-id map, and rewrite `ApplyQuickReload` to (a) use
`CommitStagingMerge` and (b) unload only the ALC(s) previously backing the recompiled id(s).

1. Replace the field (line 81):
   ```csharp
   // ---- ALC state (main-thread-only). One collectible ALC retained per blueprint id so that
   //      quick-reloading one blueprint never unloads a sibling's still-live Tick/InitDefault. ----
   private readonly Dictionary<int, AssemblyLoadContext> _alcByBlueprintId = new();
   ```

2. Rewrite `ApplyQuickReload`:
   ```csharp
   public void ApplyQuickReload(
       AssemblyLoadContext newAlc,
       BehaviorRegistry behaviorStaging,
       BlueprintRegistryStaging blueprintStaging)
   {
       try
       {
           // Step 1: MERGE-commit so sibling + code-defined definitions survive (DEBT-MVE-003).
           _blueprintRegistry.CommitStagingMerge(blueprintStaging);

           // Step 2: apply staging behavior registry -> live registry.
           _behaviorRegistry.MergeFrom(behaviorStaging);

           // Step 3: retain newAlc per recompiled id; unload only ALCs no longer referenced.
           var supersededAlcs = new List<AssemblyLoadContext>();
           foreach (var id in blueprintStaging.StagedBlueprintIds)
           {
               if (_alcByBlueprintId.TryGetValue(id, out var prevAlc) &&
                   !ReferenceEquals(prevAlc, newAlc))
               {
                   supersededAlcs.Add(prevAlc);
               }
               _alcByBlueprintId[id] = newAlc;
           }
           foreach (var old in supersededAlcs.Distinct())
           {
               // Only unload if no retained id still references this ALC.
               bool stillReferenced = false;
               foreach (var a in _alcByBlueprintId.Values)
                   if (ReferenceEquals(a, old)) { stillReferenced = true; break; }
               if (!stillReferenced)
                   old.Unload();
           }

           // Step 4: fire completion.
           OnReloadCompleted?.Invoke();
       }
       catch (Exception ex)
       {
           // newAlc was never retained on the failure paths above commit; unload it.
           try { newAlc.Unload(); } catch { /* best-effort */ }
           OnReloadFailed?.Invoke(ex);
           throw;
       }
   }
   ```
   Add `using System.Linq;` if not already present (it is — confirm).

3. Update `Dispose` (currently lines 239-248) to unload all retained ALCs instead of the single field:
   ```csharp
   [MethodImpl(MethodImplOptions.NoInlining)]
   public void Dispose()
   {
       StopWatching();
       _behaviorRegistry.Clear();
       foreach (var alc in _alcByBlueprintId.Values.Distinct())
       {
           try { alc.Unload(); } catch { /* best-effort */ }
       }
       _alcByBlueprintId.Clear();
   }
   ```

4. `GetCurrentAlc()` test seam (line 207) references the removed field. Replace it with seams the
   multi-blueprint test needs (these are `internal`; `Hrot.Blueprints.Tests` already has
   `InternalsVisibleTo` on `Fdp.Toolkits`):
   ```csharp
   /// <summary>Test seam: number of distinct ALCs currently retained.</summary>
   internal int RetainedAlcCountForTest => _alcByBlueprintId.Values.Distinct().Count();

   /// <summary>Test seam: the ALC currently retained for a blueprint id, or null.</summary>
   internal AssemblyLoadContext? GetRetainedAlcForTest(int blueprintId)
       => _alcByBlueprintId.TryGetValue(blueprintId, out var alc) ? alc : null;
   ```
   **First** find every caller of `GetCurrentAlc()` (use the memory graph / a scoped search within
   `FDP/Toolkits` and `Fdp.Toolkits.Tests`). If any test calls it, update that test to the new seams.
   Do not leave a dangling reference.

## Change 3 — multi-blueprint proof test

File: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Runtime/BlueprintHotReloadMveTests.cs`
(add a new `[Fact]`; reuse the existing `MakeCoordinator`, `HotReload`, def factories, harness).

The existing tests only ever reload **one** id, so they never exercise the bug. Add a test that
registers two distinct blueprints and proves the sibling survives the quick-reload of the other.

Requirements (must genuinely fail against the OLD code and pass against the fix):
- Two distinct identities (distinct Guid → distinct `BlueprintId`, distinct `Name`), each an
  Instance-dispatch def with a `"Count"` `StateFields` entry — mirror `MakeDefV1`/`MakeAsset` but
  parameterize id/name/delta. (Add small private helpers, e.g. `MakeAsset(Guid, name)` and
  `MakeCountingDef(name, hash, delta)`.)
- Flow:
  1. `HotReload(coordinator, idA, defA)`; `SpawnAndAttach(assetA)`; `Pump(3)` → A's Count == 3.
  2. `HotReload(coordinator, idB, defB)` — **this is the operation that wipes A under the bug**
     (1-entry staging full-replace).
  3. Assert **A still in the registry**: `fixture.Registry.TryGetById(idA, out _)` is `true`.
  4. `SpawnAndAttach(assetB)`; `Pump(2)`.
  5. Assert A keeps ticking with no reset/crash: A's Count continues to climb (== 5 after 2 more pumps).
  6. Assert B ticks: B's Count == 2.
  7. **ALC retention:** after reloading A again (`HotReload(coordinator, idA, defAv2)`), assert
     `coordinator.RetainedAlcCountForTest == 2` (A's latest + B's), and that B's retained ALC is the
     same instance that was registered for B (capture it via `GetRetainedAlcForTest(idB)` before and
     after — it must be unchanged), while A's retained ALC for `idA` changed to the new one.
- Add a class-level XML-doc paragraph noting this is the DEBT-MVE-003 regression proof and what each
  assertion would read under the bug (A wiped → `TryGetById(idA)` false / `ReadIntField` throws).

> Note: the hand-built defs' delegates live in the test assembly, not in the throwaway ALC, so the
> *registry-wipe* half is what the tick assertions prove directly; the *ALC-dangle* half is proven
> structurally via the retention seams (sibling ALC not unloaded). State this honestly in the report.

## Change 4 — debt tracker

File: `.dev/blueprint-integ-1/DEBT-TRACKER.md`
Flip the DEBT-MVE-003 row `Status` from `OPEN` to `RESOLVED (BF01)` and append a one-line resolution
note citing `BlueprintRegistry.CommitStagingMerge` + the per-asset ALC map in
`Fdp.Toolkit.Behavior.AiHotReloadCoordinator.ApplyQuickReload` + the new proof test name. Do NOT delete
the row or edit other rows.

---

## Verification (reach green before reporting — paste real output in the report)

Run from repo root (PowerShell). Build first, then the targeted suites:

1. `dotnet build IOS-IG-SimHost.sln` — 0 errors; **0 new warnings** in touched projects
   (`Fdp.Toolkits`, `Hrot.Blueprints.Tests`). A full `--no-incremental` rebuild surfaces ~26
   pre-existing warnings in unrelated test projects (DEBT-BCP-004) — leave them.
2. `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests` filtered to:
   - `FullyQualifiedName~BlueprintRegistryTests` → all green (existing replace tests must still pass).
   - `FullyQualifiedName~BlueprintHotReloadMveTests` → existing 3 + your new multi-blueprint test green.
3. Full `Hrot.Blueprints.Tests`: only the **10 pre-existing DEBT-006** golden/snapshot failures may be
   red (0 new). The flaky sub-80ns perf test (DEBT-014) passes in isolation.
4. If `Fdp.Toolkits.Tests` references `CommitStaging`/the coordinator, run it too and keep it green.
5. `Hrot.ClusterRunner.Integration.Tests --filter FullyQualifiedName~EditorSubsystemBoot` → 10/10
   (composition still boots — `EditorSubsystem.cs:2089` wiring unchanged at the call site).

If unsure whether a failure is pre-existing, baseline against `git stash` per the onboarding's
"Pre-existing failures" list. **Never re-baseline goldens in this batch** (none should change).

## Report

Write `.dev/blueprint-finalize/reports/BATCH-01-REPORT.md`:
- What changed (file:line for each edit), the design as built, and any deviation from these instructions.
- The verification command outputs (real, pasted).
- Explicit statement of which half of DEBT-MVE-003 each test assertion proves (per the honesty note above).
- Any new debt surfaced (append to the tracker with a new DEBT row if so).
- **Do not commit.** The lead reviews, then commits per batch.
