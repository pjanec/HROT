using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Time;

namespace Fdp.Toolkit.Ambient
{
    /// <summary>
    /// ⭐ <c>CE-11034</c> (R-11014; 📄 docs/designs/audio/DESIGN_Ambience.md §1) — ONE AMBIENCE LAYER: the war around the scenario, an air-raid siren at a town, an alarm
    /// in a base. A layer is an entity carrying this component: <b>positionless</b> (no <c>SimTransform</c>) ⇒ heard everywhere; with a <c>SimTransform</c> ⇒ heard within
    /// <see cref="Radius"/> of it. <see cref="Kind"/> is a short dotted NAME from the one list (<c>AmbienceKinds</c>, Hrot.Core) — never a sample: a renderer maps the name to
    /// whatever sound it has, and an unknown name falls back to its parent (<c>alarm.airraid.preliminary</c> → <c>alarm.airraid</c> → <c>alarm</c>).
    /// <para>⭐ UNMANAGED and fixed-size (CLAUDE.md "NEW COMPONENTS"): it is recorded, saved and replicated (<c>dtAmbience</c>), so a late joiner hears what is sounding and a
    /// replay restores it. Written ONLY by the layer's owner (the creation path, <c>SetAmbience</c>'s update, the expiry system).</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.Ambience)]
    [DataPolicy(DataPolicy.Default)]   // recorded, saved (an authored layer is a scenario entity), snapshotted
    public struct Ambience
    {
        /// <summary>The kind: a dotted name from the one list (<c>alarm.airraid</c>, <c>battle.distant</c>), never a sample.</summary>
        public FixedString32 Kind;
        /// <summary>How strong, 0…1 (a renderer's gain; 0 is silence — <c>SetAmbience</c> with 0 removes the layer).</summary>
        public float Intensity;
        /// <summary>Metres within which a POSITIONED layer is heard (ignored when the layer has no position).</summary>
        public float Radius;
        /// <summary>The sim time it ends (<c>ISimClock.TotalTime</c>); 0 = until removed. The owner's expiry system removes the layer at this time, never before.</summary>
        public SimInstant Until;

        /// <summary>True once its time is up (<see cref="Until"/> 0 never is).</summary>
        public readonly bool IsOver(double now) => Until.Seconds > 0.0 && now >= Until.Seconds;
    }

    /// <summary>
    /// ⭐ <c>CE-11034</c> — the owner's side of an ambience layer: it ENDS it at its <see cref="Ambience.Until"/> (sim time only — never the wall clock; 0 = never). Scheduled on every node and
    /// gated on what this node OWNS, so whoever owns a layer ends it. A REPLICATED layer ends through the one network destroy
    /// (<see cref="Fdp.Toolkit.NetworkSpawning.NetworkEntityDestroy"/>: the DDS dispose reaches every replica, the recorder sees it gone) and is marked <c>TearDown</c> at once, so this
    /// query does not ask again; a layer with no network identity (an offline world) is destroyed here.
    /// <para>⛔ HOT PATH: runs every frame — no LINQ, no allocation in the steady state (a rail pins 0 B).</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public sealed class AmbienceExpirySystem : IEcsModuleSystem
    {
        /// <summary>How many layers this node ended (a rail and a diagnostics counter read it).</summary>
        [field: Fdp.Core.WorldDerived("diagnostic counter")]
        public long Expired { get; private set; }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<Ambience>()) return;
            double now = SimClock.TotalTimeOf(repo);   // ⛔ not SimClock.Of(repo).TotalTime: that boxes a 24 B clock every frame (CE-4635)
            foreach (var e in repo.Query().With<Ambience>().Build())
            {
                ref readonly var layer = ref repo.GetComponentRO<Ambience>(e);
                if (!layer.IsOver(now) || !repo.HasAuthority(e)) continue;
                // serial, and the query is an index walk: changing the entity under it is safe (the RecordWriterCore precedent)
                if (Fdp.Toolkit.NetworkSpawning.NetworkEntityDestroy.Request(repo, e, "Ambience ended")) repo.SetLifecycleState(e, EntityLifecycle.TearDown);
                else repo.DestroyEntity(e);
                Expired++;
            }
        }
    }
}
