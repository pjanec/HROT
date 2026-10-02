using System.Collections.Generic;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// <c>CE-202</c> — the simulation's pseudo-random source is REPRODUCIBLE, and still looks random.
    /// </summary>
    /// <remarks>
    /// <para>🔒 User: <i>"maybe we could replace it with something repeatable which just looks random to
    /// a first time observer but is actually the same for every scenario run."</i> ⇒ the two halves of
    /// that sentence are two different rails, and BOTH are needed: a generator that always returns 0 is
    /// perfectly reproducible and useless.</para>
    /// </remarks>
    public sealed class SimRngRails
    {
        // ── Half one: the same inputs give the same answer ────────────────────────────

        [Fact]
        public void SameSimInputs_GiveTheIdenticalSequence()
        {
            var a = SimRng.FromSim(entityIndex: 7, salt: 2, simTime: 51.9f);
            var b = SimRng.FromSim(entityIndex: 7, salt: 2, simTime: 51.9f);

            for (int i = 0; i < 32; i++)
                Assert.Equal(a.NextUInt(), b.NextUInt());
        }

        [Fact]
        public void DifferentEntities_OnTheSameTick_Diverge()
        {
            // ⛔ Otherwise every tank in a wave picks the same slot and the behaviour collapses —
            //    reproducibility would have been bought by destroying the simulation.
            var a = SimRng.FromSim(entityIndex: 7, salt: 2, simTime: 51.9f);
            var b = SimRng.FromSim(entityIndex: 8, salt: 2, simTime: 51.9f);

            Assert.NotEqual(a.NextUInt(), b.NextUInt());
        }

        [Fact]
        public void DifferentCallSites_OnTheSameEntityAndTick_Diverge()
        {
            // The salt is what separates the firing-slot pick from the wander on one entity and tick.
            var slots  = SimRng.FromSim(entityIndex: 7, salt: 0, simTime: 51.9f);
            var wander = SimRng.FromSim(entityIndex: 7, salt: 1, simTime: 51.9f);

            Assert.NotEqual(slots.NextUInt(), wander.NextUInt());
        }

        [Fact]
        public void SuccessiveDraws_FromOneGenerator_Differ()
        {
            // ⭐ THE WANDER'S RAIL. A stateless seed-per-call hands x == y and sends every wanderer
            //   down the 45° diagonal — reproducible, and visibly broken.
            var rng = SimRng.FromSim(entityIndex: 3, salt: 1, simTime: 10f);
            Assert.NotEqual(rng.NextUInt(), rng.NextUInt());
        }

        // ── Half two: it still LOOKS random ───────────────────────────────────────────

        [Fact]
        public void AcrossEntities_TheDrawsSpreadOverTheWholeRange()
        {
            // ⭐⭐ The half that a "reproducible" rail alone cannot check. Adjacent seeds must NOT give
            //    adjacent results, or an observer sees tanks marching into slots 0,1,2,3 in order.
            var picked = new HashSet<int>();
            for (int entity = 0; entity < 64; entity++)
            {
                var rng = SimRng.FromSim(entity, salt: 0, simTime: 12f);
                picked.Add(rng.NextInt(0, 8));
            }

            // 64 draws over 8 buckets: a degenerate generator lands in one or two.
            Assert.True(picked.Count >= 6,
                $"Only {picked.Count} of 8 slots were ever chosen across 64 entities — the scatter is gone.");
        }

        [Fact]
        public void NextSingle_StaysInRange()
        {
            var rng = SimRng.FromSim(1, 0, 0f);
            for (int i = 0; i < 256; i++)
            {
                float v = rng.NextSingle();
                Assert.InRange(v, 0f, 1f);
            }
        }

        [Fact]
        public void NextInt_StaysInRange_AndADegenerateRangeIsNotAnException()
        {
            var rng = SimRng.FromSim(1, 0, 0f);
            for (int i = 0; i < 256; i++)
                Assert.InRange(rng.NextInt(3, 9), 3, 8);

            // An empty range returns the bound rather than throwing — the callers guard availCount == 0
            // before calling, and a throw here would turn a guarded edge into a crash.
            Assert.Equal(4, rng.NextInt(4, 4));
        }

        // ── The escape hatch is real ──────────────────────────────────────────────────

        [Fact]
        public void TurningDeterminismOff_RestoresGenuineRandomness()
        {
            // ⚠ A flag that is accepted and ignored is the failure mode this rail exists for: the whole
            //   point of an opt-out is that somebody can actually get variety back.
            SimRng.Deterministic = false;
            try
            {
                var distinct = new HashSet<int>();
                for (int i = 0; i < 200; i++)
                {
                    var rng = SimRng.FromSim(entityIndex: 7, salt: 2, simTime: 51.9f); // identical inputs
                    distinct.Add(rng.NextInt(0, 1000));
                }

                Assert.True(distinct.Count > 1,
                    "With Deterministic=false, identical inputs still produced one value — the flag is ignored.");
            }
            finally
            {
                SimRng.Deterministic = true;
            }
        }

        [Fact]
        public void TheDefaultIsDeterministic()
        {
            // 🔒 The user asked for repeatable runs to be the NORMAL case. A determinism flag nobody
            //    remembers to switch on delivers nothing.
            Assert.True(SimRng.Deterministic);
        }
    }
}
