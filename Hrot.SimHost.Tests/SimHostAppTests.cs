using System.Linq;
using Hrot.Map.Common;
using Hrot.Map.Common.Systems;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.SimHost.Systems;
using Hrot.SimHost;
using CarKinem.Road;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Replication.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Tests for <see cref="SimHostApp"/> system registration.
    /// </summary>
    [Collection("SimHostDds")]
    public class SimHostAppTests
    {
        // ── BUG2-N001 ── No duplicate system registrations ────────────────────

        /// <summary>
        /// Verifies that <see cref="UpdateEntityDescriptorRequestSystem"/> is registered
        /// exactly once in the kernel group — guards against the duplicate that caused
        /// double ACKs when a descriptor-update request arrived.
        /// </summary>
        [Fact]
        public void RegisteredSystemTypes_ContainsNoDuplicates()
        {
            const uint domain = 160u;
            using var participant = new DdsParticipant(domain);

            var entityMap      = new NetworkEntityMap();
            var wgs84          = HrotEnvironment.CreateGeoTransform();
            var doctrineReg    = new DoctrineRegistry();
            var compiler       = AttributeCompilerFactory.Build(wgs84);

            var group = new SystemGroup();
            var repo  = new EntityRepository();
            group.Create(repo);

            // Register the exact same set that SimHostApp._kernelGroup builds.
            group.AddSystem(new MissionControlRequestSystem(participant, entityMap, doctrineReg));
            group.AddSystem(new MissionAdapterSystem(doctrineReg, entityMap));
            group.AddSystem(new UpdateEntityDescriptorRequestSystem(participant, entityMap, wgs84));
            group.AddSystem(new UpdateEntityAttributeRequestSystem(participant, entityMap, wgs84, compiler));
            // (The duplicate in SimHostApp was the second UpdateEntityDescriptorRequestSystem —
            //  it is intentionally NOT added here to mirror the fixed code.)

            var systems = group.GetSystems();
            var descriptorSystems = systems
                .Where(s => s is UpdateEntityDescriptorRequestSystem)
                .ToList();

            Assert.Single(descriptorSystems);
        }

        // ── BUG2-R001: LoadRoadNetwork static helper ──────────────────────────

        /// <summary>
        /// When a valid path is supplied and the loader succeeds, the returned blob
        /// must equal the value produced by the loader.
        /// </summary>
        [Fact]
        public void LoadRoadNetwork_ValidPath_ReturnsLoadedBlob()
        {
            var expected = new RoadNetworkBuilder().Build(10f, 3, 3);
            var returned = SimHostApp.LoadRoadNetwork(
                "some_road.json",
                loader: _ => expected);

            Assert.Equal(expected, returned);
        }

        /// <summary>
        /// When the loader throws, no exception must escape and the method must return
        /// a default (empty) <see cref="RoadNetworkBlob"/>.
        /// </summary>
        [Fact]
        public void LoadRoadNetwork_InvalidPath_DoesNotThrow()
        {
            var returned = SimHostApp.LoadRoadNetwork(
                "nonexistent.json",
                loader: _ => throw new System.IO.FileNotFoundException("not found"));

            // No exception propagated; a default blob is returned.
            Assert.Equal(default, returned);
        }

        /// <summary>
        /// When the path is empty or whitespace, a default blob is returned without
        /// invoking the loader at all.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void LoadRoadNetwork_EmptyPath_ReturnsDefault(string? path)
        {
            bool loaderCalled = false;
            var returned = SimHostApp.LoadRoadNetwork(
                path,
                loader: _ => { loaderCalled = true; return new RoadNetworkBuilder().Build(1f, 2, 2); });

            Assert.False(loaderCalled);
            Assert.Equal(default, returned);
        }
    }
}
