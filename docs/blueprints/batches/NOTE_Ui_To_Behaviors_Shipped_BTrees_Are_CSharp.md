<!--STATUS
state: LIVE — ui → behaviors: the shipped .btree.json trees are C# now (CE-1410, R-1042); how to add or change one
updated: 2026-10-27
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../DESIGN_Behavior_Self_Registration.md — §10d owns the curated registration as extended here (SOP orders, channels, the bake, labels)
  - ../../DESIGN_Decision_Layer.md — owns what an SOP IS (§4: the slot, DoWhenIdle / React, the gate)
-->
# NOTE — ui → behaviors: the shipped BTrees are C# (`BTreeBuilder`), not `.btree.json`

**Dispatched at** the commit that adds this file (`ui`). ⚠ **Cross-lane:** the trees are the behaviors lane's; the ui lane converted them on
the user's ruling below.

## 1. The ruling

🔒 **User, `2026-10-27`, verbatim:** *"The json btrees - rather conver them to csharp hardcoded ones, easier on ai agent dev. Then no need to
solve their layout."* — [R-1042](../RULINGS.md), [CE-1410](../Blueprint_Issues_Tracker.md).

## 2. What moved where

| tree (same name, AssetId and node ids) | was | is now |
|---|---|---|
| `BasicInfantrySop` · `BasicSquadSop` · `BasicPlatoonSop` · `BasicVehicleSop` | `Assets/BTrees/Library/Sop/*.btree.json` | `Brains/BasicInfantrySop.cs` · `BasicSquadSop.cs` · `BasicPlatoonSop.cs` · `BasicVehicleSop.cs` |
| `CivilianSop` · `InertSop` · `PilotSop` | `Assets/BTrees/Library/Sop/*.btree.json` | `Brains/CivilianInertPilotSops.cs` |
| `TakeCover` · `FallBack` · `Flank` · `FiringPosition` · `Sentry` | `Assets/BTrees/Library/Tactics/*.btree.json` | `Brains/LibraryTactics.cs` |
| `DangerCrossing` | `Assets/BTrees/Library/Tactics/DangerCrossing.btree.json` | `Brains/DangerCrossing.cs` (its resolver stays `DangerAreaNodes.ResolveDangerCrossing`) |
| `PlatoonHillAttack` | `Assets/BTrees/Demos/HillAttack/PlatoonHillAttack.btree.json` | its EXISTING C# twin, now the registered tree: `HillAttackCommanderNodes.BuildPlatoonHillAttackTree` (CE-430's catalog-only copy; one implementation, not a second) |
| `HullDownAttackRun` | `Assets/BTrees/Demos/HillAttack/HullDownAttackRun.btree.json` | `Brains/HillAttackTrees.cs` (resolver unchanged in `HillAttackTankNodes`) |

⭐ **Kept as JSON:** `Samples/` (3 — human showcases; `SampleScoutDiscoveryTests`, `BTreeShowcaseAndStarterTests`, the persistence round
trips and the hosting route `T07`/showcases → `SampleScout` read them) and `Tests/Authoring/` (21 + the new `T42`, CE-1413 — fixtures of the JSON pipeline:
`BTreeGoldenCorpusTests`, `BTreeCallShapeTests`, the `T*_ProofTests`, `Ce1368OpenAndResetLayoutTests`). No scenario runs either.

⭐ **Behaviour is unchanged, and that is pinned:** `Hrot.SimHost.Tests/ShippedTreesAreCSharpTests` prints each PRODUCTION definition (its
blob, its parse of four probe payloads, every bound param's type and value, every node's id / title / comment) and compares it to
`Snapshots/ShippedTrees/<name>.tree.txt`, recorded from the JSON registration while both were registered side by side (all 15 equal).
⚠ The one deliberate storage difference: a stateful node's working memory is a field of the block (`StatefulAction(bb => bb.P,
bb => bb.State, …)`) instead of a per-node occurrence slot — the CombatPosture port's form (R-266).

## 3. How to add (or change) a shipped SOP now

```csharp
public static class MySop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Blackboard                          // the tree's tuning: its constructor IS the defaults (R-3002)
    {
        public SopSenseParams Hit;
        public Blackboard() => Hit = new SopSenseParams { Kind = SensorChange.Hit, Seconds = 3f };
    }

    [BehaviorResolver("MySop")]                      // an SOP takes no params: a payload with fields is refused (CE-2132)
    public static unsafe void Resolve(string json, byte* memory, int capacity, EntityRepository world, Entity self)
        => BehaviorParams.RequireNoStoredFields(json, "MySop", typeof(NoParams).FullName!);

    [Behavior("MySop", Params = typeof(NoParams))]
    [BTreeDefinition("MySop", AssetId = "<a new guid>", Curated = true, Group = "Library/Sop", ParamsType = typeof(Blackboard))]
    public static BTreeBuilder<Blackboard, BTreeContext> Build()
        => new BTreeBuilder<Blackboard, BTreeContext>()
        .Selector(rows => rows
            .Sequence(row => row
                .Condition(bb => bb.Hit, SopConditions.SensedFresh).Label("Hit (fresh, 3 s)")
                .React(BehaviorNames.FightFromCover, ReactionUrgency.Hit).Label("React: FightFromCover"))
                .Label("Hit → fight from cover")
            .DoWhenIdle(BehaviorNames.Idle).Label("Idle: Idle"))
            .Label("Rows — first true wins");
}
```

| ⭐ | |
|---|---|
| the verbs | `.React(name, urgency)` / `.DoWhenIdle(name)` (SOP orders, `SharedNodeBuilderExtensions`); conditions/actions bind shared nodes as every curated tree does; `.Label(…)` / `.Comment(…)` title the node added last (what the editor shows) |
| what the registration reads off the tree | "is an SOP" (it carries orders — CE-1019) and its channel set, so the SOP picker offers it; nothing to declare |
| ids | omit `visualId` for a new node (a deterministic id is minted, CE-1345); the converted trees keep their JSON ids |
| a tactic with a params contract | its DTO first in the block, `[Behavior(name, Params = typeof(Dto))]`, resolver `BehaviorParams.OverlayFlatAt<Dto>(json, memory, capacity)` (`LibraryTactics.cs`) |
| layout | none — the editor lays a C#-built tree out (`BTreeAutoLayout`, CE-1367) and opens it read-only |
| a designer's own SOP | still a `.btree.json` saved by the editor; its `SopOrder` node binds the same key (`HsmActionKey.SopOrderKeyName`) |

## 4. Open, filed

- [CE-1411](../Blueprint_Issues_Tracker.md) — a curated tree's `[WritesChannel]` node does not release its channel on Failure (the JSON
  thunk does). None of the 15 binds one; the gap is the curated path's.
- [CE-1412](../Blueprint_Issues_Tracker.md) — a curated tree publishes no `ManagedBlackboardVariables`, so the editor's live Variables
  values are empty for it (the inspector falls back to the layout type).
- [CE-1413](../Blueprint_Issues_Tracker.md) ✅ — the JSON generator's corpus had lost every asset with an `SopOrder` node, a managed SOP
  blackboard or a deactivator-paired node; `Tests/Authoring/T42_SopOrdersAndDeactivator.btree.json` (+ goldens) restores them.
