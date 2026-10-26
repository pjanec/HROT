using System;
using System.Text.Json.Nodes;
using Fdp.Core;

namespace Fdp.Toolkit.ReplayBrowser.Diff
{
    /// <summary>
    /// ⭐ RB-2 (<c>docs/DESIGN_Replay_Browser_Debug_Http.md</c>) — <b>the next (or previous) frame where an entity changed</b>, found in a
    /// reader of its OWN over the recording file (replay-browser-2's isolation invariant: never the GUI's sandbox). ONE algorithm for
    /// the Replay Browser's Diff panel ("seek to next change") and the debug API's <c>POST /replay/seek {next: {entity}}</c>.
    /// </summary>
    /// <remarks>
    /// <para>⭐ Forward: from <c>startFrame</c>, the first later frame whose dump differs from the frame before. Backward: the last frame
    /// before <c>startFrame</c> that differs from the one before it (a walk from frame 0 — the recording only steps forward cheaply).</para>
    /// <para>⚠ Blocking and whole-file: run it off the main thread.</para>
    /// </remarks>
    public static class EntityChangeFinder
    {
        /// <param name="fdpPath">The recording to scan.</param>
        /// <param name="startFrame">The frame the search starts from (the one shown now).</param>
        /// <param name="target">The entity in the reader's world at a frame — a held handle, or a network id resolved there.</param>
        /// <param name="direction">&gt; 0 forward, &lt; 0 backward.</param>
        /// <param name="dump">The entity's dump (a serializer's JSON), or null when it does not exist at that frame.</param>
        /// <param name="counts">Whether a changed top-level node (a component) counts — the Diff panel's excluded types, or one component.</param>
        /// <param name="epsilon">The diff's float tolerance.</param>
        /// <returns>The frame, or <see langword="null"/> when nothing changes in that direction.</returns>
        public static int? Find(
            string fdpPath, int startFrame, Func<EntityRepository, Entity> target, int direction,
            Func<EntityRepository, Entity, JsonNode?> dump, Func<string, bool> counts, double epsilon,
            IComponentDiffService? diff = null)
        {
            var diffService = diff ?? new ComponentDiffService();
            using var ctx = new ReplayBrowserContext();
            ctx.LoadRecording(fdpPath);
            var playback = ctx.Playback;
            if (playback == null) return null;
            var repo = ctx.SandboxRepo;

            JsonNode? Dump()
            {
                var e = target(repo);
                return !e.IsNull && repo.IsAlive(e) ? dump(repo, e) : null;
            }

            bool Changed(JsonNode? before, JsonNode? after)
            {
                foreach (var node in diffService.ComputeTreeDiff(before, after, epsilon))
                    if (node.IsModified && counts(node.Name)) return true;
                return false;
            }

            if (direction > 0)
            {
                ctx.SeekToFrame(startFrame, suppressHistory: true);
                var baseline = Dump();
                while (ctx.StepForward(suppressHistory: true))
                {
                    var current = Dump();
                    if (Changed(baseline, current)) return playback.CurrentFrame;
                    baseline = current;
                }
                return null;
            }

            if (startFrame <= 0) return null;
            ctx.SeekToFrame(0, suppressHistory: true);
            int? last = null;
            var before = Dump();
            if (Changed(null, before)) last = 0;
            while (playback.CurrentFrame < startFrame - 1)
            {
                if (!ctx.StepForward(suppressHistory: true)) break;
                var current = Dump();
                if (Changed(before, current)) last = playback.CurrentFrame;
                before = current;
            }
            return last;
        }
    }
}
