using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Fdp.Toolkit.Spatial.Eqs.Tests
{
    /// <summary>Rails for <see cref="EqsChildSensor"/>'s epoch stamp (CE-3049).</summary>
    public class EqsChildSensorTests
    {
        /// <summary>
        /// ⭐ CE-3049 — a token of another slot (a doctrine run: high bit set) must not stamp like the behaviour run with
        /// the same low bits. 🔴 The stamp kept only the owner's low 16 bits, so 0x80000005 and 5 stamped alike.
        /// </summary>
        [Fact]
        public void StampOwner_ADoctrineToken_DiffersFromTheBehaviourTokenWithTheSameLowBits_CE3049()
        {
            Assert.NotEqual(EqsChildSensor.StampOwner(1u, 5u), EqsChildSensor.StampOwner(1u, 0x8000_0005u));
            // behaviour tokens below 65 536 stamp exactly as before (no change for existing sensors)
            Assert.Equal((5u << 16) | 1u, EqsChildSensor.StampOwner(1u, 5u));
        }

        /// <summary>⭐ CE-3049 — a refresh that overflows the count wraps it and leaves the owner stamp alone. 🔴 <c>EqsLifecycleNodes</c>
        /// did <c>Epoch++</c>, carrying the overflow into the owner bits. ⭐ CE-2158: the count is the low 8 bits now (bits 8–15 are the
        /// lifetime) — ⛔ SUPERSEDED: "wraps the low half".</summary>
        [Fact]
        public void NextEpoch_WrapsTheRefreshCount_NeverTouchesTheOwnerStampOrTheLifetime_CE3049()
        {
            uint stamped = EqsChildSensor.StampLifetime(EqsChildSensor.StampOwner(0xFFu, 7u), 3u);
            uint next = EqsChildSensor.NextEpoch(stamped);
            Assert.Equal(stamped & 0xFFFFFF00u, next & 0xFFFFFF00u);
            Assert.Equal(0u, next & 0xFFu);
            Assert.Equal(EqsChildSensor.StampOwner(3u, 7u), EqsChildSensor.NextEpoch(EqsChildSensor.StampOwner(2u, 7u)));
        }

        /// <summary>⭐⭐ <c>CE-2158</c> (EQS §17.6.1) — a sensor ended and re-made on the SAME part id gets a different epoch, even in one
        /// frame and in the same run; a part's first sensor stamps as before; another part's first sensor is unaffected; a refresh keeps
        /// the lifetime. 🔴 Red before: every lifetime was <c>StampOwner(1, run)</c>.</summary>
        [Fact]
        public void Ensure_ASensorRemadeOnTheSamePart_GetsANewEpoch_TheFirstStampsAsBefore_CE2158()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<PartMetadata>();
            repo.RegisterComponent<EqsSensor>();
            repo.RegisterComponent<EqsCognitiveBuffer>();
            repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorOwnedPart>();
            repo.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();
            var parent = repo.CreateEntity();
            var config = new EqsSensor { BlueprintId = 1u, Epoch = 1u, SearchRadius = 10f };

            var first = EqsChildSensor.Ensure(repo, parent, siteId: 1, in config);
            uint e1 = repo.GetComponentRO<EqsSensor>(first).Epoch;
            Assert.Equal(1, repo.GetComponentRO<PartMetadata>(first).InstanceId);
            Assert.Equal(EqsChildSensor.StampOwner(1u, 0u), e1);                    // a part's first lifetime: as before

            repo.DestroyEntity(first);                                              // ended …
            var second = EqsChildSensor.Ensure(repo, parent, siteId: 1, in config); // … and re-made in the same frame
            Assert.Equal(1, repo.GetComponentRO<PartMetadata>(second).InstanceId); // on the same (reused) part id
            uint e2 = repo.GetComponentRO<EqsSensor>(second).Epoch;
            Assert.NotEqual(e1, e2);
            Assert.Equal(e1 & 0xFFFF00FFu, e2 & 0xFFFF00FFu);                       // same owner, same refresh count

            var other = EqsChildSensor.Ensure(repo, parent, siteId: 2, in config);  // part 2's first lifetime
            Assert.Equal(EqsChildSensor.StampOwner(1u, 0u), repo.GetComponentRO<EqsSensor>(other).Epoch);

            Assert.True(EqsChildSensor.Refresh(repo, second));
            Assert.Equal(e2 & 0xFFFFFF00u, repo.GetComponentRO<EqsSensor>(second).Epoch & 0xFFFFFF00u);   // a refresh keeps the lifetime
        }
    }
}
