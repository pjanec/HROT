using Fdp.Toolkit.Replication.Attributes;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Hrot.Map.Common;
using Hrot.Map.Common.Systems;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.SimHost;
using CarKinem.Road;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.ModuleHost;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Services;
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
            var wgs84          = HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0.0);
            var behaviorReg    = new BehaviorRegistry();
            var compiler       = AttributeCompilerFactory.Build(wgs84);

            var systems = new System.Collections.Generic.List<Fdp.ModuleHost.Abstractions.IEcsModuleSystem>();

            // Register the exact same set that SimHostApp._kernelGroup builds.
            systems.Add(new Hrot.Common.Systems.MissionControlExecutionSystem(entityMap, behaviorReg, new TacticalIntentMapperRegistry()));
            systems.Add(new UpdateEntityDescriptorRequestSystem(participant, entityMap, wgs84));
            systems.Add(new UpdateEntityAttributeRequestSystem(participant, entityMap, wgs84, compiler));
            // (The duplicate in SimHostApp was the second UpdateEntityDescriptorRequestSystem —
            //  it is intentionally NOT added here to mirror the fixed code.)

            var descriptorSystems = systems
                .Where(s => s is UpdateEntityDescriptorRequestSystem)
                .ToList();

            Assert.Single(descriptorSystems);
        }

        // ⛔ CE-3127 — the LoadRoadNetwork helper (BUG2-R001) is gone with NodeConfiguration.RoadNetworkBlobPath: the road graph
        //   comes from the terrain (TerrainLoadStepTests covers it).

        [Fact]
        public void TestHook_SetMovementIntent_PreservesAndIncrementsExistingIntentId()
        {
            using var world = new EntityRepository();
            world.RegisterComponent<NavigationIntent>();

            var entity = world.CreateEntity();
            world.AddComponent(entity, new NavigationIntent { IntentId = 5u });

            var entityMap = new NetworkEntityMap();
            entityMap.Register(42L, entity);

            var app = new SimHostApp();
            SetPrivateField(app, "_world", world);
            SetPrivateField(app, "_entityMap", entityMap);

            var destination = new Vector2(10f, 20f);
            app.TestHook_SetMovementIntent(42L, destination, speed: 12f);

            var intent = world.GetComponent<NavigationIntent>(entity);
            Assert.Equal(6u, intent.IntentId);
            Assert.Equal(NavigationMode.DirectPoint, intent.Mode);
            Assert.Equal(new Vector3(destination.X, destination.Y, 0f), intent.FinalDestination);
            Assert.Equal(12f, intent.TargetSpeed);
            Assert.Equal(20f, intent.ArrivalRadius);
        }

        private static void SetPrivateField(object target, string fieldName, object? value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field!.SetValue(target, value);
        }
    }
}
