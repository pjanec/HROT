using System.Numerics;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Replication.Egress;
using Hrot.NED.Descriptors;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-147</c> — the egress translator ATTACHES the shadow it needs, rather than requiring
    /// somebody upstream to have provided it.</b>
    ///
    /// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §13.7 (which supersedes §13.3's placement
    /// ruling). ⭐ These rails are the fast counterpart to <c>TheEgressShadowExistsAtBirthTests</c> in
    /// <c>Hrot.ClusterRunner.Integration.Tests</c>: those assert the INVARIANT on a real cluster spawn;
    /// these assert the MECHANISM that now makes it true, in ~1 s instead of a booted cluster.</para>
    ///
    /// <para>🔴 <b>THE DEFECT SHAPE, and why the starting state matters.</b> Every rail here begins with an
    /// owned entity that has <c>SimTransform</c> + <c>NetworkIdentity</c> and deliberately <b>NO</b>
    /// <c>NetworkTransform</c> — the exact state the production TKB catalog produced. Before <c>CE-147</c>
    /// the translator's scan query REQUIRED the shadow, so such an entity was never visited at all: no
    /// exception, no log line, simply <b>zero</b> <c>WorldPos</c> ever published (<c>AX-011</c>). ⇒ these
    /// rails are only meaningful because they start from the broken state, not from a fixed-up one.</para>
    ///
    /// <para>⚠ <b>What these rails deliberately do NOT cover.</b> They exercise one translator against a
    /// hand-built world. ⛔ They cannot see a spawn path that fails to produce <c>SimTransform</c> in the
    /// first place — that is the cluster suite's job, and the two are complementary rather than
    /// redundant.</para>
    /// </summary>
    [Collection("SimHostDds")]
    public class TheEgressAttachesItsOwnShadowTests
    {
        private readonly ITestOutputHelper _out;
        public TheEgressAttachesItsOwnShadowTests(ITestOutputHelper output) => _out = output;

        /// <summary>Trivial lon/lat passthrough — these rails are about component lifecycle, not geodesy.</summary>
        private sealed class IdentityGeoTransform : IGeographicTransform
        {
            public void SetOrigin(double lat, double lon, double alt) { }
            public Vector3 ToCartesian(double lat, double lon, double alt)
                => new Vector3((float)lon, (float)lat, (float)alt);
            public (double lat, double lon, double alt) ToGeodetic(Vector3 pos)
                => (pos.Y, pos.X, pos.Z);
        }

        /// <summary>
        /// ⭐⭐ A world that CAN hold the shadow. ⚠ <c>NetworkTransform</c> is registered but never added —
        /// the whole point is that the translator adds it.
        /// </summary>
        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<NetworkTransform>();
            return repo;
        }

        /// <summary>Owned when <c>primaryOwnerId == localNodeId</c>; a replica otherwise.</summary>
        private static Entity MakeEntity(EntityRepository repo, int primaryOwnerId, Vector3 position)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new NetworkIdentity(42));
            repo.AddComponent(e, new NetworkAuthority(primaryOwnerId: primaryOwnerId, localNodeId: 1));
            repo.AddComponent(e, new SimTransform { Position = position, Rotation = Quaternion.Identity });
            return e;
        }

        // ── the core rail ────────────────────────────────────────────────────

        /// <summary>
        /// ⭐⭐⭐ <b>THE RAIL THIS CHANGE EXISTS FOR.</b> An owned entity with no shadow gets one from the
        /// translator itself.
        ///
        /// <para>📐 <b>Red before the fix</b>, and for the informative reason: the scan query required
        /// <c>NetworkTransform</c>, so this entity matched nothing and the loop body never ran.</para>
        /// </summary>
        [Fact]
        [Trait("Category", "Integration")]
        public void AnOwnedEntityWithNoShadowGetsOneFromTheTranslator()
        {
            const uint domainId = 215u;
            using var participant = new DdsParticipant(domainId);
            var translator = new GeoSpatialEgressTranslator(
                participant, new NetworkEntityMap(), new IdentityGeoTransform(), localNodeId: 1);

            var repo = CreateWorld();
            var e = MakeEntity(repo, primaryOwnerId: 1, position: new Vector3(13.4f, 52.5f, 0f));

            Assert.False(repo.HasComponent<NetworkTransform>(e),
                "precondition: the entity must START without the shadow — that is the state the production " +
                "TKB catalog produced, and the state AX-011 was about.");

            translator.ScanAndPublish(repo);

            Assert.True(repo.HasComponent<NetworkTransform>(e),
                "CE-147: the egress translator must attach the shadow it needs. Without it the scan query " +
                "silently skips the entity and NO WorldPos is ever published for it.");
        }

        /// <summary>
        /// ⭐⭐ <b>Authority over the shadow, not merely its presence.</b>
        ///
        /// <para>⚠ <c>AddComponent</c> does not touch the <c>AuthorityMask</c>, and the spawn path snapshots
        /// that mask at an instant which has already passed by the time egress runs ⇒ a shadow attached here
        /// would be <b>present but unowned</b> unless the grant is explicit. ⭐ This rail is what lets
        /// <c>SimHostNodeBootstrapper</c>'s hook be retired without dropping the property its own cluster
        /// rail (<c>TheOwnerHasAuthorityOverItsOwnShadow</c>) asserts.</para>
        /// </summary>
        [Fact]
        [Trait("Category", "Integration")]
        public void TheOwnerHoldsAuthorityOverTheShadowItAttached()
        {
            const uint domainId = 216u;
            using var participant = new DdsParticipant(domainId);
            var translator = new GeoSpatialEgressTranslator(
                participant, new NetworkEntityMap(), new IdentityGeoTransform(), localNodeId: 1);

            var repo = CreateWorld();
            var e = MakeEntity(repo, primaryOwnerId: 1, position: new Vector3(13.4f, 52.5f, 0f));

            translator.ScanAndPublish(repo);

            Assert.True(repo.HasAuthority<NetworkTransform>(e),
                "CE-147: the owner must hold authority over the shadow it writes every tick.");
        }

        /// <summary>
        /// ⭐⭐⭐ <b>A REPLICA must NOT be given a shadow by this path.</b>
        ///
        /// <para>⭐ This rail pins the ATTACH BELOW THE AUTHORITY GATE. ⛔ Moving it above the gate would
        /// still make the core rail pass while spending 28 bytes on every ghost duplicating what
        /// <c>GeoSpatialIngressTranslator</c> already provisions on first receipt — a regression no other
        /// rail here would catch.</para>
        /// </summary>
        [Fact]
        [Trait("Category", "Integration")]
        public void AReplicaGetsNoShadowFromTheEgressPath()
        {
            const uint domainId = 217u;
            using var participant = new DdsParticipant(domainId);
            var translator = new GeoSpatialEgressTranslator(
                participant, new NetworkEntityMap(), new IdentityGeoTransform(), localNodeId: 1);

            var repo = CreateWorld();
            var e = MakeEntity(repo, primaryOwnerId: 2, position: new Vector3(13.4f, 52.5f, 0f));

            translator.ScanAndPublish(repo);

            Assert.False(repo.HasComponent<NetworkTransform>(e),
                "CE-147: the attach belongs BELOW the authority gate. A replica's shadow is written by " +
                "GeoSpatialIngressTranslator on first receipt; attaching one here duplicates it on every ghost.");
        }

        /// <summary>
        /// ⭐⭐⭐ <b>The OUTCOME: a stationary owned entity reaches the wire on the FIRST scan.</b>
        ///
        /// <para>⭐⭐ This is the §13.4 zero-seeding requirement observed rather than asserted structurally.
        /// The translator publishes only when the live pose differs from the shadow, or when the salted
        /// heartbeat fires at <c>% 600</c> ticks. ⛔ Seeding the fresh shadow from the entity's CURRENT
        /// <c>SimTransform</c> would make this very first comparison say <i>"has not moved"</i> and the
        /// entity would be invisible to the cluster for up to 600 ticks — <b>10 s at 60 Hz</b>. ⭐ Seeding to
        /// zeros forces this publish, and this rail is what would catch a well-meaning "seed it from the
        /// live pose" change.</para>
        /// </summary>
        [Fact]
        [Trait("Category", "Integration")]
        public void AStationaryOwnedEntityReachesTheWireOnTheFirstScan()
        {
            const uint domainId = 218u;
            using var participant = new DdsParticipant(domainId);
            using var reader = new DdsReader<WorldPos>(participant, "WorldPos");
            var translator = new GeoSpatialEgressTranslator(
                participant, new NetworkEntityMap(), new IdentityGeoTransform(), localNodeId: 1);

            var repo = CreateWorld();
            MakeEntity(repo, primaryOwnerId: 1, position: new Vector3(13.4f, 52.5f, 0f));

            Thread.Sleep(200);
            translator.ScanAndPublish(repo);   // ⚠ ONE scan — no heartbeat can have fired yet
            Thread.Sleep(200);

            int samples = 0;
            using (var loan = reader.Take())
                foreach (var s in loan) if (s.IsValid && s.Data.EntityId == 42) samples++;

            _out.WriteLine($"WorldPos samples after a single scan: {samples}");
            Assert.True(samples > 0,
                "CE-147/§13.4: the first scan must publish. 0 here means either the entity was skipped " +
                "(the AX-011 shape) or the fresh shadow was seeded from the live pose, which hides a " +
                "stationary entity behind the 600-tick heartbeat.");
        }

        /// <summary>
        /// ⭐⭐ <b>After publishing, the shadow holds what was SENT</b> — so the next tick compares against
        /// the just-sent pose rather than re-publishing forever.
        ///
        /// <para>⚠ Without this the zero seeding would be a permanent leak, not a one-shot kick: every
        /// subsequent scan would see <c>live != zeros</c> and publish again at 60 Hz, which is the opposite
        /// failure from <c>AX-011</c> and just as invisible.</para>
        /// </summary>
        [Fact]
        [Trait("Category", "Integration")]
        public void TheShadowHoldsTheJustPublishedPoseSoTheNextScanIsQuiet()
        {
            const uint domainId = 219u;
            using var participant = new DdsParticipant(domainId);
            var translator = new GeoSpatialEgressTranslator(
                participant, new NetworkEntityMap(), new IdentityGeoTransform(), localNodeId: 1);

            var repo = CreateWorld();
            var pos = new Vector3(13.4f, 52.5f, 7f);
            var e = MakeEntity(repo, primaryOwnerId: 1, position: pos);

            translator.ScanAndPublish(repo);

            var shadow = repo.GetComponent<NetworkTransform>(e);
            _out.WriteLine($"shadow after first scan: ({shadow.LastPosition.X}, {shadow.LastPosition.Y}, {shadow.LastPosition.Z})");
            Assert.Equal(pos, shadow.LastPosition);
        }

        /// <summary>
        /// ⭐⭐ <b>A world that never registered the component must not THROW.</b>
        ///
        /// <para>⚠ A bare <c>AddComponent</c> throws <i>"Component NetworkTransform is not registered"</i> —
        /// 📌 that measurement is what §13.3 used to reject the engine-level placement outright. ⭐ The guard
        /// mirrors <c>SpatialCoreTkbTranslator</c>'s <c>IsComponentTypeRegistered</c>: a world that cannot
        /// hold the shadow cannot egress either, so skipping is correct and silence is correct. ⛔ Without
        /// this rail the guard is an untested branch that turns a missing registration into a crash.</para>
        /// </summary>
        [Fact]
        [Trait("Category", "Integration")]
        public void AWorldWithoutTheComponentRegisteredIsSkippedNotCrashed()
        {
            const uint domainId = 220u;
            using var participant = new DdsParticipant(domainId);
            var translator = new GeoSpatialEgressTranslator(
                participant, new NetworkEntityMap(), new IdentityGeoTransform(), localNodeId: 1);

            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<SimTransform>();
            // ⛔ NetworkTransform deliberately NOT registered.

            var e = repo.CreateEntity();
            repo.AddComponent(e, new NetworkIdentity(42));
            repo.AddComponent(e, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            repo.AddComponent(e, new SimTransform { Position = new Vector3(1, 2, 3), Rotation = Quaternion.Identity });

            var ex = Record.Exception(() => translator.ScanAndPublish(repo));

            Assert.Null(ex);
            Assert.False(repo.HasComponent<NetworkTransform>(e));
        }
    }
}
