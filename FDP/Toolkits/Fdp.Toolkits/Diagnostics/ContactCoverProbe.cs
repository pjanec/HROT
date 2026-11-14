using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Diagnostics
{
    /// <summary>
    /// ⭐ <b>CE-4456 N-2 — same-tick cover and contact</b> (📄 docs/blueprints/batches/STATUS_Memory_And_Contact.md). Opt-in
    /// <c>HROT_CONTACT_PROBE=1</c>: at every node frame's start (<c>ModuleHostKernel.FrameStartProbe</c>) it compares the live world with the
    /// previous frame and logs ONE line per sim tick to <c>HROT_CONTACT_PROBE_OUT</c> (default the temp folder) + <c>.&lt;pid&gt;.tsv</c>:
    /// <list type="bullet">
    /// <item><b>commits</b> — units whose <see cref="CoverClaim"/> became live this tick, or whose hide point moved &gt; 0.5 m (a re-commit);</item>
    /// <item><b>samePair</b> — pairs among this tick's commits whose hide points are within 1.5 m (the claim radius, <c>PeekAndFireNodes.ClaimRadius</c>);</item>
    /// <item><b>onLive</b> — this tick's commits within 1.5 m of ANOTHER unit's claim that was already live;</item>
    /// <item><b>unitsNewContact</b> / <b>newContacts</b> — units (a sensor part's parent) whose <see cref="SensorContactList"/> gained an entity
    /// it did not hold last tick, and how many entries.</item>
    /// </list>
    /// Cover claims live on the brain node (CGF), contact lists on the muscle node (SimHost) — each node writes its own file; join them by tick.
    /// ⛔ An instrument: it allocates while it runs; off, it is never registered.
    /// </summary>
    public static class ContactCoverProbe
    {
        public static readonly bool On = EnvVars.Read(EnvVars.HrotContactProbe) == "1";
        /// <summary><c>HROT_CONTACT_PROBE_DETAIL=1</c> — a last column naming each commit: <c>entity:x,z</c> then <c>+</c> (newly live) or
        /// <c>~d</c> (moved d m), then <c>@</c> the claim's remaining life (s).</summary>
        public static readonly bool Detail = EnvVars.Read(EnvVars.HrotContactProbeDetail) == "1";
        private static readonly System.Text.StringBuilder s_detail = new();

        private const float SameRadius = 1.5f, MovedM = 0.5f;
        private static readonly Dictionary<int, (Vector3 Hide, bool Live)> s_lastClaim = new();
        private static readonly Dictionary<int, long[]> s_lastContacts = new();
        private static readonly List<Vector3> s_commits = new(), s_liveOthers = new();
        private static readonly List<int> s_commitOwner = new(), s_liveOwner = new();
        private static readonly HashSet<int> s_unitsNew = new();
        private static StreamWriter? s_out;
        private static uint s_lastTick = uint.MaxValue;

#pragma warning disable CA2255   // a module initializer is the point: the probe registers itself when the toolkit loads, opt-in only
        [ModuleInitializer]
#pragma warning restore CA2255
        internal static void Register()
        {
            if (!On) return;
            Fdp.ModuleHost.ModuleHostKernel.FrameStartProbe += BeforeFrame;
        }

        private static void BeforeFrame(EntityRepository world)
        {
            uint tick = ((ISimulationView)world).Tick;
            if (tick == s_lastTick) return;   // paused / no new tick
            s_lastTick = tick;
            try { Sample(world, tick); } catch { /* an instrument never breaks the frame */ }
        }

        private static unsafe void Sample(EntityRepository world, uint tick)
        {
            double now = world.SimulationTime;
            int commits = 0, samePair = 0, onLive = 0, newContacts = 0;
            s_detail.Clear(); s_commits.Clear(); s_commitOwner.Clear(); s_liveOthers.Clear(); s_liveOwner.Clear(); s_unitsNew.Clear();

            if (world.IsComponentTypeRegistered<CoverClaim>())
            {
                foreach (var e in world.Query().With<CoverClaim>().Build())
                {
                    var c = world.GetComponentRO<CoverClaim>(e);
                    bool live = c.Until.Seconds > now;
                    s_lastClaim.TryGetValue(e.Index, out var prev);
                    bool committed = live && (!prev.Live || Vector3.Distance(prev.Hide, c.Hide) > MovedM);
                    if (committed)
                    {
                        commits++; s_commits.Add(c.Hide); s_commitOwner.Add(e.Index);
                        if (Detail) s_detail.Append($" {e.Index}:{c.Hide.X:F1},{c.Hide.Z:F1}{(prev.Live ? "~" + Vector3.Distance(prev.Hide, c.Hide).ToString("F1") : "+")}@{c.Until.Seconds - now:F2}");
                    }
                    else if (live) { s_liveOthers.Add(c.Hide); s_liveOwner.Add(e.Index); }
                    s_lastClaim[e.Index] = (c.Hide, live);
                }
                for (int i = 0; i < s_commits.Count; i++)
                {
                    for (int j = i + 1; j < s_commits.Count; j++) if (Vector3.Distance(s_commits[i], s_commits[j]) < SameRadius) samePair++;
                    for (int k = 0; k < s_liveOthers.Count; k++)
                        if (s_liveOwner[k] != s_commitOwner[i] && Vector3.Distance(s_commits[i], s_liveOthers[k]) < SameRadius) { onLive++; break; }
                }
            }

            if (world.IsComponentTypeRegistered<SensorContactList>() && world.IsComponentTypeRegistered<PartMetadata>())
            {
                foreach (var e in world.Query().With<SensorContactList>().Build())
                {
                    var list = world.GetComponentRO<SensorContactList>(e);
                    if (!s_lastContacts.TryGetValue(e.Index, out var prev)) { s_lastContacts[e.Index] = prev = new long[17]; prev[0] = -1; }   // [0] = count; -1 = first sample (records only)
                    int added = 0;
                    for (int i = 0; i < list.Count; i++)
                    {
                        long id = list.EntityIds[i]; bool had = false;
                        for (int j = 0; j < prev[0]; j++) if (prev[1 + j] == id) { had = true; break; }
                        if (!had) added++;
                    }
                    if (added > 0 && prev[0] >= 0)
                    {
                        newContacts += added;
                        int unit = world.HasComponent<PartMetadata>(e) ? world.GetComponentRO<PartMetadata>(e).ParentEntity.Index : e.Index;
                        s_unitsNew.Add(unit);
                    }
                    prev[0] = Math.Min(list.Count, 16);
                    for (int i = 0; i < prev[0]; i++) prev[1 + i] = list.EntityIds[i];
                }
            }

            if (s_out == null)
            {
                var path = (EnvVars.Read(EnvVars.HrotContactProbeOut) is { Length: > 0 } o ? o : Path.Combine(Path.GetTempPath(), "hrot-contact-probe"))
                           + "." + Environment.ProcessId + ".tsv";
                s_out = new StreamWriter(path, append: false) { AutoFlush = false };
                s_out.WriteLine("tick\tsimTime\tcommits\tsamePair\tonLive\tunitsNewContact\tnewContacts");
            }
            s_out.WriteLine($"{tick}\t{now:F2}\t{commits}\t{samePair}\t{onLive}\t{s_unitsNew.Count}\t{newContacts}" + (Detail ? "\t" + s_detail : ""));
            if ((tick & 63) == 0) s_out.Flush();
        }
    }
}
