using System;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// The simulation's pseudo-random source: reproducible by default, and derived from sim state
    /// rather than from a global generator.
    /// </summary>
    /// <remarks>
    /// <para>🔒 <b>User, <c>2026-09-04</c> and again <c>2026-09-05</c>:</b> <i>"the randomization in the
    /// behavior still exists — which is not good and should be changed to optionally seeded
    /// randomization to get really same results"</i> · <i>"maybe we could replace it with something
    /// repeatable which just looks random to a first time observer but is actually the same for every
    /// scenario run."</i></para>
    ///
    /// <para>⭐⭐⭐ <b>The algorithm is NOT new — it is <c>SlotOps.PickRandomFreeSlot</c>'s, lifted so
    /// there is ONE of it.</b> That kernel already carried a deterministic sim-derived xorshift and its
    /// own doc calls it <i>"architect Q#8-C, mandated for replay/rollback/headless-proof
    /// determinism"</i>. ⛔ The mandate existed; the two call sites that actually run in production
    /// never adopted it and still called <c>Random.Shared</c>. Writing a second generator here would
    /// have been the duplication this programme exists to remove.</para>
    ///
    /// <para>⭐⭐ <b>Why it still LOOKS random.</b> A xorshift over
    /// <c>entityIndex ^ salt ^ (int)simTime</c> decorrelates adjacent seeds, so neighbouring entities
    /// on the same tick get well-spread values. An observer sees scatter; a second run of the same
    /// scenario sees the identical scatter.</para>
    ///
    /// <para>⚠ <b>What determinism here does NOT buy.</b> It makes the BEHAVIOUR reproducible, not the
    /// whole cluster: DDS delivery order, wall-clock scheduling and floating-point across hosts are
    /// untouched. ⇒ two runs of one process match; two different nodes still need the usual care.</para>
    /// </remarks>
    public struct SimRng
    {
        /// <summary>
        /// When true (the default) the source is reproducible. Set false to restore the previous
        /// <see cref="Random.Shared"/> behaviour.
        /// </summary>
        /// <remarks>
        /// ⭐ An explicit escape hatch, because "the same every run" is exactly wrong for anything that
        /// wants genuine variety — a soak test looking for rare orderings, say. ⛔ It is opt-OUT rather
        /// than opt-in on purpose: the user asked for repeatable runs to be the normal case, and a
        /// determinism flag nobody remembers to switch on delivers nothing.
        ///
        /// <para>⚠ It is a process-wide static, matching <c>FdpConfig.EnforceExplicitEventRegistration</c>
        /// — the established shape for a boot-time switch in this codebase. It is NOT per-world, so a
        /// host running two worlds cannot make one random and one not.</para>
        /// </remarks>
        public static bool Deterministic = true;

        private uint _state;

        /// <summary>
        /// Builds a generator from simulation state. The same inputs always produce the same sequence.
        /// </summary>
        /// <param name="entityIndex">Usually the acting entity's index — separates concurrent actors.</param>
        /// <param name="salt">A per-call-site discriminator (a wave number, a channel id, a constant).</param>
        /// <param name="simTime">Simulation time; separates the same actor across ticks.</param>
        /// <remarks>
        /// ⛔ <b>Give different call sites different salts.</b> Two sites seeded identically on the same
        /// tick return the same number, which reads as a correlation bug in the behaviour rather than as
        /// a seeding mistake.
        /// </remarks>
        public static SimRng FromSim(int entityIndex, int salt, float simTime)
        {
            uint seed = (uint)(entityIndex ^ salt ^ (int)simTime);
            // xorshift so even tiny/adjacent seeds spread — SlotOps' own comment, and its own constants.
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            if (seed == 0) seed = 0x9E3779B9u;
            return new SimRng { _state = seed };
        }

        /// <summary>Advances and returns the next raw value.</summary>
        public uint NextUInt()
        {
            // Advancing per draw is what lets ONE seed serve several values — the wander needs an x and
            // a y, and a stateless seed-per-call would hand it x == y.
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            if (_state == 0) _state = 0x9E3779B9u;
            return _state;
        }

        /// <summary>A value in <c>[0, 1)</c>.</summary>
        public float NextSingle()
            => Deterministic ? (NextUInt() >> 8) * (1.0f / 16777216.0f) : Random.Shared.NextSingle();

        /// <summary>A value in <c>[minInclusive, maxExclusive)</c>.</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            if (!Deterministic) return Random.Shared.Next(minInclusive, maxExclusive);

            uint span = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % span);
        }
    }
}
