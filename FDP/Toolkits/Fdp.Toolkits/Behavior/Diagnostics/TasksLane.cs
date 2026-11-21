using System;
using System.Collections.Generic;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.ReplayBrowser.Timeline;

namespace Fdp.Toolkit.Behavior.Diagnostics
{
    /// <summary>What happened to a unit's task at a frame of the Tasks lane.</summary>
    public enum TaskMarkerKind : byte { Started = 0, Done = 1, Failed = 2 }

    /// <summary>One marker of the ONE Tasks lane: at <paramref name="Frame"/>, unit <paramref name="Unit"/>'s task <paramref name="Task"/> (1-based) started / ended.</summary>
    public readonly record struct TaskMarker(int Frame, int Unit, int Task, TaskMarkerKind Kind);

    /// <summary>One task of the SELECTED unit as a bar: from its start to its end (or to the last frame while it runs), how it ended (null = running).</summary>
    public readonly record struct TaskBar(int Task, int FromFrame, int ToFrame, TaskMarkerKind? Ended);

    /// <summary>
    /// ⭐ W-1 / P-8 8c (📄 docs/UX/DESIGN_Mission_Plan_Editor.md §5.8 E4, §5.12; mockup <c>mission-plan-detail-visual.svg</c> C) — reads the ONE Tasks
    /// lane (<see cref="BehaviourTimelineLanes.Tasks"/>): each entry is a unit's plan moving (Value = its current task, X = how the latest task ended,
    /// Y = its task count), read here as markers — a task STARTED, ended DONE, ended FAILED — each naming the unit and the task. PURE: no world,
    /// the index only; ⭐ a thousand units stay ONE lane (the markers carry the unit), the timeline's entity filter is <paramref name="unitFilter"/>.
    /// </summary>
    public static class TasksLane
    {
        /// <summary>The markers of every unit (or of <paramref name="unitFilter"/> only, when non-zero), in frame order, into <paramref name="into"/> (cleared).</summary>
        public static void Markers(TimelineIndex index, List<TaskMarker> into, int unitFilter = 0)
        {
            into.Clear();
            if (!index.Has(BehaviourTimelineLanes.Tasks.Id)) return;
            var lane = index.Lane(BehaviourTimelineLanes.Tasks.Id);
            var last = new Dictionary<int, (int Current, int Count, bool HaltShown)>();
            for (int i = 0; i < lane.Count; i++)
            {
                var e = lane[i];
                if (unitFilter != 0 && e.Subject != unitFilter) continue;
                int v = (int)e.Value, count = (int)e.Y;
                var outcome = (MissionPhaseOutcome)(int)e.X;
                if (!last.TryGetValue(e.Subject, out var prev))
                {
                    if (v < count) into.Add(new TaskMarker(e.Frame, e.Subject, v + 1, TaskMarkerKind.Started));   // first sight: the task it runs
                    last[e.Subject] = (v, count, false);
                    continue;
                }
                if (v > prev.Current)
                {
                    for (int k = prev.Current; k < v; k++)   // every task passed since the last entry ended; the latest says how, the others were Done
                        into.Add(new TaskMarker(e.Frame, e.Subject, k + 1, k == v - 1 && outcome == MissionPhaseOutcome.Failed ? TaskMarkerKind.Failed : TaskMarkerKind.Done));
                    if (v < count) into.Add(new TaskMarker(e.Frame, e.Subject, v + 1, TaskMarkerKind.Started));
                    last[e.Subject] = (v, count, false);
                }
                else if (v < prev.Current || count != prev.Count)   // a new plan or a jump back: the task it runs now starts
                {
                    if (v < count) into.Add(new TaskMarker(e.Frame, e.Subject, v + 1, TaskMarkerKind.Started));
                    last[e.Subject] = (v, count, false);
                }
                else if (outcome == MissionPhaseOutcome.Failed && !prev.HaltShown && v < count)   // halted on its current task (a fault)
                {
                    into.Add(new TaskMarker(e.Frame, e.Subject, v + 1, TaskMarkerKind.Failed));
                    last[e.Subject] = (v, count, true);
                }
            }
        }

        /// <summary>The bars of ONE unit (the selection) from its markers: each task from its start to its end; a running one to <paramref name="lastFrame"/>.</summary>
        public static void Bars(IReadOnlyList<TaskMarker> markers, int unit, int lastFrame, List<TaskBar> into)
        {
            into.Clear();
            int openTask = 0, openFrom = 0;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m.Unit != unit) continue;
                if (m.Kind == TaskMarkerKind.Started)
                {
                    if (openTask != 0) into.Add(new TaskBar(openTask, openFrom, m.Frame, null));   // replaced before it ended
                    openTask = m.Task; openFrom = m.Frame;
                }
                else if (m.Task == openTask)
                {
                    into.Add(new TaskBar(openTask, openFrom, m.Frame, m.Kind));
                    openTask = 0;
                }
            }
            if (openTask != 0) into.Add(new TaskBar(openTask, openFrom, Math.Max(openFrom, lastFrame), null));
        }
    }
}
