using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Contracts;

namespace Fdp.Toolkit.Combat
{
    /// <summary>One burst a node saw (<see cref="RecentDetonations"/>).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RecentBurst
    {
        public Vector3 At;
        public double Time;
        public Entity Shooter;
        public long Ammo;
        /// <summary>Running count of bursts recorded on this node — a stable id for this one (1-based).</summary>
        public long Serial;
    }

    [InlineArray(RecentDetonations.Capacity)]
    public struct RecentBurstSlots
    {
        private RecentBurst _element;
    }

    /// <summary>
    /// ⭐ <c>CE-2176</c> IF-3 (📄 docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §3.5, §3.8) — the last <see cref="Capacity"/> bursts this
    /// node saw (where, when, whose round), so a brain that ticks on its own period can ask "where did my battery's round land?" without
    /// reading the frame's bus. A fire-mission observer is the reader; it still decides for itself whether its eye can SEE the burst.
    /// <para>📐 On a brain-only node the bursts arrive through <c>MunitionDetonationIngressTranslator</c> (registered there for this); on a
    /// one-world host they are the local <see cref="DetonationNotification"/>s — the same event either way.</para>
    /// <para>A world singleton like <see cref="DetonationTraces"/> (CE-3117, R-226): recorded and snapshotted, so a replay seek restores
    /// what the observer had seen; never saved into a scenario; no translator. ⛔ Not <see cref="DetonationTraces"/> itself: that one is
    /// filled only where damage is assessed (from <c>DetonationLog</c>), keeps 8 bursts and no shooter.</para>
    /// </summary>
    [ComponentId(GlobalComponentIds.RecentDetonations)]
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    public struct RecentDetonations
    {
        /// <summary>How many bursts are kept (oldest first out).</summary>
        public const int Capacity = 64;

        /// <summary>How many bursts were recorded since the node started (the newest one's <see cref="RecentBurst.Serial"/>).</summary>
        public long Count;
        public RecentBurstSlots Slots;

        /// <summary>Records one burst into the world's memory (made on first use).</summary>
        public static void Add(EntityRepository world, Vector3 at, double time, Entity shooter, long ammo)
        {
            var m = world.HasSingletonUnmanaged<RecentDetonations>() ? world.GetSingletonUnmanaged<RecentDetonations>() : default;
            m.Count++;
            m.Slots[(int)((m.Count - 1) % Capacity)] = new RecentBurst { At = at, Time = time, Shooter = shooter, Ammo = ammo, Serial = m.Count };
            world.SetSingletonUnmanaged(in m);
        }

        /// <summary>The kept bursts after <paramref name="since"/> (sim s), oldest first, into <paramref name="into"/>; returns how many.</summary>
        public static int Since(EntityRepository world, double since, Span<RecentBurst> into)
        {
            if (!world.HasSingletonUnmanaged<RecentDetonations>()) return 0;
            var m = world.GetSingletonUnmanaged<RecentDetonations>();
            int n = 0;
            for (long s = Math.Max(1, m.Count - Capacity + 1); s <= m.Count && n < into.Length; s++)
            {
                var b = m.Slots[(int)((s - 1) % Capacity)];
                if (b.Time > since) into[n++] = b;
            }
            return n;
        }

        /// <summary>How many bursts the world has recorded (0 when none).</summary>
        public static long CountOf(EntityRepository world)
            => world.HasSingletonUnmanaged<RecentDetonations>() ? world.GetSingletonUnmanaged<RecentDetonations>().Count : 0;
    }

    /// <summary>⭐ <c>CE-2176</c> IF-3 — records each <see cref="DetonationNotification"/> into <see cref="RecentDetonations"/> (on a brain node).</summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class DetonationMemorySystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.Bus.IsRegistered<DetonationNotification>()) return;
            double now = Fdp.Toolkit.Time.SimClock.Of(repo).TotalTime;
            foreach (ref readonly var d in view.ReadEvents<DetonationNotification>())
                RecentDetonations.Add(repo, new Vector3(d.HitX, d.HitY, d.HitZ), now, d.Shooter, d.Ammo);
        }
    }
}
