using System;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ <c>R-4297</c> (<c>R-3039</c> S4; 📄 docs/DESIGN_System_State_Inventory.md §9) — what a field a system, module or network translator carries between frames IS,
    /// for a checkpoint, a recording, a replay seek and the dry run (they copy the WORLD and nothing else). The letters are the inventory's.
    /// </summary>
    public enum StateClass : byte
    {
        /// <summary>K — not state: a dependency or configuration fixed at start-up.</summary>
        NotState,
        /// <summary>A — derived: rebuilt from the world (a cache, an index, per-frame scratch, a diagnostic); a restore may clear it.</summary>
        Derived,
        /// <summary>B — protocol: re-derived at a world replacement — its owner must react to <c>WorldEpoch.Moved</c>.</summary>
        Protocol,
        /// <summary>S — session: cluster, transport or process state that is not the world; a restore leaves it, by design.</summary>
        Session,
        /// <summary>C — simulation memory held outside the ECS: a listed move into a component (S1 / S2). ⛔ A new one fails the rail.</summary>
        SimulationMemory,
        /// <summary>X — state in an engine outside the ECS (SUMO): a listed item; no field move fixes it.</summary>
        External,
    }

    /// <summary>
    /// ⭐⭐ <c>R-4297</c> — the classification of a field WHERE IT IS WRITTEN (the central hand ledger retired): put one of the derived attributes on the field, or on a
    /// class held in fields (it classifies every field holding it — "once per type"). <see cref="Why"/> is required: it is the row the ledger used to carry. The
    /// guard rails read it (<c>SystemStateProbe</c>, <c>WorldBoundaryProbe</c>; <c>Hrot.ClusterRunner.Tests</c>).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public abstract class SystemStateAttribute : Attribute
    {
        protected SystemStateAttribute(StateClass @class, string why)
        {
            Class = @class;
            Why = why ?? "";
        }

        /// <summary>The class (the inventory's letter).</summary>
        public StateClass Class { get; }

        /// <summary>Why — what the field holds and why that is its class.</summary>
        public string Why { get; }
    }

    /// <summary>A — rebuilt BEFORE USE in every run (scratch: cleared at the top, filled from this frame's world). ⭐ It also needs no reset at the world
    /// boundary (the world-boundary rail exempts it).</summary>
    public sealed class FrameScratchAttribute : SystemStateAttribute { public FrameScratchAttribute(string why) : base(StateClass.Derived, why) { } }

    /// <summary>A — a cache or index derived from the world (rebuilt on demand); a restore may clear it. ⚠ One that holds entities must still forget them at
    /// the world boundary (<c>WorldEpoch.Moved</c>).</summary>
    public sealed class WorldDerivedAttribute : SystemStateAttribute { public WorldDerivedAttribute(string why) : base(StateClass.Derived, why) { } }

    /// <summary>B — protocol: what was sent, received or asked over the network; its owner must forget or re-derive it when <c>WorldEpoch.Moved</c> fires
    /// (directly, through <c>PublishedInstances.OnFrame</c>, <c>OnWorldBoundary</c> / <c>ForgetLastWorld</c>, or the ELM's <c>OnWorldReplaced</c>).</summary>
    public sealed class ProtocolCacheAttribute : SystemStateAttribute { public ProtocolCacheAttribute(string why) : base(StateClass.Protocol, why) { } }

    /// <summary>K — not state: a dependency or configuration fixed at start-up.</summary>
    public sealed class NotStateAttribute : SystemStateAttribute { public NotStateAttribute(string why) : base(StateClass.NotState, why) { } }

    /// <summary>S — session state a restore leaves by design (the cluster, the transport, the process).</summary>
    public sealed class SessionStateAttribute : SystemStateAttribute { public SessionStateAttribute(string why) : base(StateClass.Session, why) { } }

    /// <summary>C — simulation memory still outside the ECS: a LISTED move (the rail counts them and may only shrink).</summary>
    public sealed class SimulationMemoryAttribute : SystemStateAttribute { public SimulationMemoryAttribute(string why) : base(StateClass.SimulationMemory, why) { } }

    /// <summary>X — state in an engine outside the ECS: a LISTED item.</summary>
    public sealed class ExternalStateAttribute : SystemStateAttribute { public ExternalStateAttribute(string why) : base(StateClass.External, why) { } }
}
