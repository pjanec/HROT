<!--STATUS
state: LIVE
updated: 2026-11-21
current-answer: the whole file
related-designs:
  - ../../designs/minefields/DESIGN_Minefields.md §9b, §9b.1 — the design these names implement (K1–K13)
  - HANDOFF_Behaviors_To_Backend_UnderFire.md — §1, the contract this note fixes as built
  - STATUS_Backend_UnderFire.md — the build's progress per task
-->

# NOTE — backend → behaviors: the `UnderFire` contract, ids allocated (CE-2468, R-2140)

`backend`, `2026-11-21`, the first commit after the start marker (`979d55e6a`). The names are the handoff's §1, unchanged. The
signatures below are what will be built; ⭐ where §1 left a choice open ("BatchContext/buffer"), there is one overload per form, so
behaviors can bind early to any of them.

## 1. The ids — measured free on every branch (`2026-11-21`)

Branches checked: backend, behaviors, coordinator, ui, terrain, convoy, dynamic-terrain, exp/agg-backend, exp/agg-behaviors,
exp/agg-ui, showcase-brains, showcase-world, unreal-ig.

| what | value | how it was measured |
|---|---|---|
| component `UnderFireKnowledge` | **171** (`GlobalComponentIds.UnderFireKnowledge`) | the catalogue rail's FREE list (`ComponentIdCatalogueTests`) on HEAD + a `[ComponentId(171)]` / `= 171` grep on every branch. ⚠ backend's own block 434–449 is FULL; 320–322 are test structs (`ParallelSyncTests`), 327–329 agg-ui's, 398 burned |
| TKB `UnderFireKnowledgeRecord` | **7211** | every `TkbType = 72xx` on every branch: 7202–7210 taken (7207 `MinefieldStrikes`, 7209 `GroundHeatMarks`, 7210 `GroundFire`) |
| DDS descriptor `dtUnderFireKnowledge` | ordinal **210** | `AllDescriptors.cs` on every branch + every translator's `DescriptorOrdinal`: 196 ram order, 197 held, 198–200 / 207–209 convoy, 201–206 time, 216–219 air, 220–239 the aggregate lanes ⇒ 210–215 free |
| event `UnderFireLearned` | **5115** (`TerrainEventIds.UnderFireLearned`) | the terrain event range 5100–5199: 5100–5114 taken |
| `PlaceKnowledgeKind.UnderFire` | **2** | (`Minefield` = 1) |
| `SubjectKind.Place` | **3** | (`Entity` 1, `TerrainObject` 2) |
| `KnowledgeSource.Contact` / `.Burst` | **6** / **7** | (`Report` = 5) |
| `DangerAreaKind.CoveredByFire` | **9**, bit `DangerAreaKinds.CoveredByFire = 1 << 9` | (`Minefield` = 8; the mask is a `ushort` since CE-4624) |

## 2. The signatures — all in `Fdp.Toolkit.Terrain` (`FDP/Toolkits/Fdp.Toolkits/Terrain/UnderFire/`) unless named

```csharp
public enum UnderFireCause : byte { Indirect = 1, Direct = 2 }

/// a side's fresh knowledge of one place under fire, as a reader sees it (the union of every node's records)
public readonly struct UnderFireRecord
{
    public UnderFireCause Cause { get; }
    public Vector3 Point { get; }          // frozen at the first learn (K7)
    public float RangeM { get; }           // Indirect: the shelled radius · Direct: the enemy's covering range
    public float EnemyStrength { get; }    // Direct: the enemy danger seen at the break (0 for Indirect)
    public SimInstant LearnedAt { get; }   // the last evidence (newest wins)
}

public static class UnderFireEvents
{
    // ⭐ callable from the parallel brain phase (R-4212): an event, applied by the serial writer next tick
    public static void LearnDirect(BatchContext ctx, int side, Vector3 point, float rangeM, float enemyStrength, SimInstant at);
    public static void LearnDirect(EntityCommandBuffer buffer, int side, Vector3 point, float rangeM, float enemyStrength, SimInstant at);
    public static void LearnDirect(EntityRepository world, int side, Vector3 point, float rangeM, float enemyStrength, SimInstant at);  // serial; inside a phase the bus defers it into the batch by itself
    public static void LearnIndirect(EntityRepository world, int side, Vector3 point, float radiusM, SimInstant at);                     // ShelledAreas' (UF-7)
}

public sealed class UnderFireTable
{
    public static UnderFireTable Of(ISimulationView view);   // ⭐ rebuilt only when a record changes (K4, K11), else the same table — 0 B a steady frame
    public bool NearestFresh(int side, Vector3 point, float withinM, out UnderFireRecord record);   // allocation-free; a not-Cooled record of that side whose frozen point is within withinM (2-D distance), the nearest
}
// Fdp.Toolkit.Squad.DangerArea
public enum DangerAreaKind : byte { …, CoveredByFire = 9 }
public enum DangerAreaKinds : ushort { …, CoveredByFire = 1 << 9 }
```

⭐ `side` is the `ForceId` as an `int` (the same as `PlaceKnowledgeHeader.Side`); `at` is the sim instant of the evidence (behaviors:
the break's time). The per-side route cost needs nothing from behaviors: every path query of a mover with a side is priced (UF-5).

## 3. What behaviors should know before binding

| | |
|---|---|
| ⚠ a learn lands NEXT tick | the writer is serial (it applies events after the phase), so `NearestFresh` does not see a learn in the tick it was made |
| ⚠ `Indirect` is never priced | K8: shelling stays cross-or-hold (AQ101); only `Direct` costs a route |
| ⚠ a record ages | `Indirect` is ignored 120 s after its last evidence, `Direct` 600 s; destroyed at 2 × that. A reader never refreshes it |
| ⚠ an existing `UnderFire` component already exists | `Fdp.Toolkit.Combat.Components.UnderFire` (the suppression stamp, CE-1223) is a different thing: per MAN, not per place |

---
_backend lane, 2026-11-21_
