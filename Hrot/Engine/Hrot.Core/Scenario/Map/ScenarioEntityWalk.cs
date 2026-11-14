using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Common.Components;

namespace Hrot.Map.Common.Scenario;

/// <summary>
/// ⭐⭐ CE-4210 W-M6 — the ONE walk of an entity's components with a <see cref="LeafApplier"/>: every typed leaf (<see cref="EntityRefRemap.WalkObject"/>), every
/// brain-slot / mission-task PARAMS BLOB (through the behaviour's contract, <see cref="ScenarioBehaviorRemapper.ApplyJson"/> — the one blob parser), and every route's
/// waypoints (an immutable record: a NEW plan goes back in the list). Used by the scenario LOAD (<c>StagingEntityExtractor</c>: resolve / relocate) and by Save selection as macro
/// (<c>MacroClosure</c>: collect the outside references). ⛔ No second copy — a blob reference is treated exactly like a typed leaf (SR-159: bound as its Param form).
/// 📄 docs/DESIGN_Macro_Authoring_Editor.md §9.
/// </summary>
public static class ScenarioEntityWalk
{
    /// <summary>Applies <paramref name="pass"/> to every component in <paramref name="comps"/> (in place; a boxed struct in its box; a record or a route replaced by its new value).
    /// <paramref name="behaviorRemapper"/> null ⇒ the blobs are left alone (a host with no behaviour registry). The blob holders: <see cref="ActiveMissionPlan"/> tasks,
    /// <see cref="InitialBrainIntent"/> (staging's, and the live world's through <c>BrainIntentReader.Read</c> — the very slots the save writes).</summary>
    public static void Apply(List<object> comps, ScenarioBehaviorRemapper? behaviorRemapper, LeafApplier pass)
    {
        for (int ci = 0; ci < comps.Count; ci++)
        {
            switch (comps[ci])
            {
                case ActiveMissionPlan plan when behaviorRemapper != null:
                    int n = 0;
                    foreach (var task in plan.Plan.Tasks)
                    {
                        pass.Entering($"task {++n} ({task.BehaviorName}) params");
                        // ⭐ N-9 (CE-4471) — written only when the pass CHANGED it: a reading pass (the editor's Collector) leaves a live plan untouched
                        if (behaviorRemapper.ApplyJson(task.BehaviorName, task.BehaviorParams, pass) is { } p1 && p1 != task.BehaviorParams) task.BehaviorParams = p1;
                    }
                    break;
                case InitialBrainIntent brain when behaviorRemapper != null:
                    if (brain.Behavior is { } b) { pass.Entering("Behavior.Params"); if (behaviorRemapper.ApplyJson(b.Name, b.Params, pass) is { } p2 && p2 != b.Params) b.Params = p2; }
                    if (brain.Sop is { } sop) { pass.Entering("Sop.Params"); if (behaviorRemapper.ApplyJson(sop.Name, sop.Params, pass) is { } p3 && p3 != sop.Params) sop.Params = p3; }
                    break;
                case RoutePlan route:
                    comps[ci] = route.WithWaypoints(list =>
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            var w = list[i];
                            w.Position = (System.Numerics.Vector3)pass.Point(new WorldPoint(w.Position));
                            list[i] = w;
                        }
                    });
                    continue;
            }
            EntityRefRemap.WalkObject(comps[ci], pass);
        }
    }
}
