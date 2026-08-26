using Fdp.Toolkit.Replication.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using Hrot.Map.Common.Replication.Utils;
using Hrot.CGF.Systems;
using Hrot.Core.Network;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.SimHost.Tests
{
    // â”€â”€â”€ Shared geoTransform stub â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Trivial geoTransform stub: latâ†’Y, lonâ†’X, altâ†’Z (mirrors existing
    /// <c>StubGeoTransform</c> inside CreateEntityRequestSystemTests).
    /// </summary>
    internal sealed class FactoryTestGeoTransform : IGeographicTransform
    {
        public void SetOrigin(double lat, double lon, double alt) { }

        public Vector3 ToCartesian(double lat, double lon, double alt)
            => new Vector3((float)lon, (float)lat, (float)alt);

        public (double lat, double lon, double alt) ToGeodetic(Vector3 p)
            => (p.Y, p.X, p.Z);
    }

    // â”€â”€â”€ ATTR-S5T1 / ATTR-S5T4 â€” AttributeCompilerFactory tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public class AttributeCompilerFactoryTests
    {
        private static EntityRepository CreateRepo()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<Fdp.Core.EntityInfo>();
            return repo;
        }

        // â”€â”€ ListPatchContext tests (no live ECS) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void SimHostAttributeCompiler_Name_Registered()
        {
            var compiler = AttributeCompilerFactory.Build(new FactoryTestGeoTransform());
            var ctx = new ListPatchContext(null);

            compiler.Compile("{\"Name\":\"Test\"}", ctx);

            var result = ctx.FlushComponents();
            var data   = result.OfType<Fdp.Core.EntityInfo>().Single();
            Assert.Equal("Test", data.Name);
        }

        [Fact]
        public void SimHostAttributeCompiler_Affiliation_Registered()
        {
            var compiler = AttributeCompilerFactory.Build(new FactoryTestGeoTransform());
            var ctx = new ListPatchContext(null);

            compiler.Compile("{\"Affiliation\":\"FORCE_FRIENDLY\"}", ctx);

            var result = ctx.FlushComponents();
            var data   = result.OfType<Fdp.Core.EntityInfo>().Single();
            Assert.Equal(ForceId.Friend, data.ForceId);
        }

        [Fact]
        public void SimHostAttributeCompiler_Affiliation_PreservesExistingName()
        {
            var compiler = AttributeCompilerFactory.Build(new FactoryTestGeoTransform());
            var seed     = new Fdp.Core.EntityInfo { Name = "Alpha", ForceId = ForceId.Neutral };
            var ctx      = new ListPatchContext(new List<object> { seed });

            compiler.Compile("{\"Affiliation\":\"FORCE_OPPOSING\"}", ctx);

            var result = ctx.FlushComponents();
            var data   = result.OfType<Fdp.Core.EntityInfo>().Single();
            Assert.Equal("Alpha",         data.Name);  // unchanged
            Assert.Equal(ForceId.Hostile, data.ForceId);
        }

        // â”€â”€ EcsPatchContext integration tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void AttributeCompiler_NamePatch_TriggersEntityInfoDirtyOnEcsPatchContext()
        {
            var compiler = AttributeCompilerFactory.Build(new FactoryTestGeoTransform());

            var repo   = CreateRepo();
            var entity = repo.CreateEntity();
            repo.SetComponent( entity, new Fdp.Core.EntityInfo { Name = "original" });
            // Grant authority so the invoker dispatches the setter.
            repo.SetAuthority<Fdp.Core.EntityInfo>( entity, true);

            var context = compiler.CreatePatchContext(repo, entity);
            compiler.Compile("{\"Name\":\"X\"}", context);
            context.FlushDirtyMarks();

            // SmartEgressUtil.MarkDirty stores the ordinal in EgressPublicationState.
            var state = ((ISimulationView)repo).GetManagedComponentRO<EgressPublicationState>(entity);
            Assert.NotNull(state);
            Assert.Contains((long)EDescriptorType.dtEntityInfo, state.DirtyDescriptors);
        }

        [Fact]
        public void AttributeCompiler_GeoPatch_TriggersGeoSpatialDirty()
        {
            var geo      = new FactoryTestGeoTransform();
            var compiler = AttributeCompilerFactory.Build(geo);

            var repo   = CreateRepo();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform());
            // Grant authority so the invoker dispatches the setter.
            repo.SetAuthority<SimTransform>(entity, true);

            var context = compiler.CreatePatchContext(repo, entity);
            compiler.Compile(
                "{\"GeoPosition\":{\"Latitude\":32.1,\"Longitude\":34.5,\"Altitude\":100.0}}",
                context);
            context.FlushDirtyMarks();

            var state = ((ISimulationView)repo).GetManagedComponentRO<EgressPublicationState>(entity);
            Assert.NotNull(state);
            Assert.Contains((long)EDescriptorType.dtWorldPos, state.DirtyDescriptors);
        }
    }

    // â”€â”€â”€ ATTR-S5T2 â€” CreateEntityRequestSystem JSON compiler tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public class CreateEntityRequestSystemJsonTests
    {
        private const long  ValidTkbType = 42L;
        private const ulong ValidDisType = 0x0100_0000_0000_0001UL;
        private const int   LocalNodeId  = 7;

        private static TkbDatabase CreateTkb()
        {
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("TestVehicle", ValidTkbType));
            return db;
        }

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<Fdp.Core.EntityInfo>();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkOwnership>();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterEvent<Fdp.Toolkit.Lifecycle.Events.ConstructionOrder>();
            repo.RegisterEvent<Fdp.Toolkit.Lifecycle.Events.DestructionOrder>();
            return repo;
        }

        private static EntityCreationRequest MakeRequestWithJson(string? json)
        {
            return new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = LocalNodeId,
                TkbType            = ValidTkbType,
                DisType            = ValidDisType,
                InitialAttributesJson = json,
            };
        }

        private static CreateEntityRequestSystem BuildSystemWithCompiler(
            StubRequestSource requestSource,
            out StubAckSink ackSink)
        {
            var compiler = AttributeCompilerFactory.Build(geoTransform: null);
            var tkb      = CreateTkb();
            var idAlloc  = new StubIdAllocator(startId: 100);
            ackSink      = new StubAckSink();
            return new CreateEntityRequestSystem(
                requestSource, ackSink, tkb, idAlloc, LocalNodeId,
                jsonAttributeCompiler: compiler);
        }

        [Fact]
        public void CreateEntityRequestSystem_InitialAttributesJson_PatchesName()
        {
            // Arrange
            var repo   = CreateWorld();
            var source = new StubRequestSource();
            var request = MakeRequestWithJson("{\"Name\":\"Delta-7\"}");
            source.Enqueue(request);

            var system = BuildSystemWithCompiler(source, out _);

            // Act
            system.Execute(repo, 0f);

            // Assert: SpawnEntityCommand carries IgEntityData with patched name.
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();

            Assert.Single(commands);
            var cmd      = commands[0];
            var igData   = Assert.Single(cmd.InitialComponents!.OfType<Fdp.Core.EntityInfo>());

            Assert.Equal("Delta-7", igData.Name.ToString());
        }

        [Fact]
        public void CreateEntityRequestSystem_NullJson_NoPatch()
        {
            // Arrange: null InitialAttributesJson should not throw and should process normally.
            var repo    = CreateWorld();
            var source  = new StubRequestSource();
            source.Enqueue(MakeRequestWithJson(json: null));

            var system = BuildSystemWithCompiler(source, out var ackSink);

            // Act â€” must not throw
            var ex = Record.Exception(() => system.Execute(repo, 0f));

            // Assert
            Assert.Null(ex);
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.Single(commands);
            // Phase-1 InProgress ACK is sent immediately; Phase-2 Success is dispatched
            // by NedRequestFinalizationSystem once the entity reaches Active.
            Assert.Equal((int)EntityOperationStatus.InProgress, ackSink.WrittenAcks[0].StatusCode);
        }
    }

    // â”€â”€â”€ ATTR-S5T1/S5T4/S6T1/S6T2 â€” DescriptorMapper compiler overload tests â”€

    public class DescriptorMapperCompilerTests
    {
        private static readonly FactoryTestGeoTransform GeoTransform = new();

        // Decorator that builds a compiler using the factory (same paths as production).
        private static JsonAttributeCompiler BuildCompiler()
            => AttributeCompilerFactory.Build(GeoTransform);

        [Fact]
        public void DescriptorMapper_WithCompiler_DtEntityInfoProducesIgEntityData()
        {
            var compiler = BuildCompiler();
            var descriptors = new List<EntityDescriptorUnion>
			{
                new EntityDescriptorUnion
				{
                    _d = EDescriptorType.dtEntityInfo,
                    EntityInfo = new Hrot.NED.Descriptors.EntityInfo
                    {
                        EntityId        = 0,
                        Name            = "TestUnit",
                        ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
                        CommanderId     = 42,
                    },
                },
            };

            var components = DescriptorMapper.MapToComponents(descriptors, GeoTransform, compiler);

            var igData = Assert.Single( components.OfType<Fdp.Core.EntityInfo>());
            Assert.Equal("TestUnit",     igData.Name);
            Assert.Equal(ForceId.Friend, igData.ForceId);
        }

        [Fact]
        public void DescriptorMapper_WithCompiler_NoDuplicateIgEntityData()
        {
            var compiler = BuildCompiler();
            var descriptors = new List<EntityDescriptorUnion>
			{
                new EntityDescriptorUnion
				{
                    _d = EDescriptorType.dtEntityInfo,
                    EntityInfo = new Hrot.NED.Descriptors.EntityInfo { EntityId = 0, Name = "Alpha" },
                },
            };

            var components = DescriptorMapper.MapToComponents(descriptors, GeoTransform, compiler);

            int count = components.OfType<Fdp.Core.EntityInfo>().Count();
            Assert.Equal(1, count);
        }

        [Fact]
        public void DescriptorMapper_GeoSpatial_SharedDelegate_ProducesSameResultAsDirectPath()
        {
            // Arrange: lat=10, lon=20, alt=30, heading=0
            // IdentityGeoTransform: ToCartesian(lat, lon, alt) â†’ (lon, lat, alt)
            //   â†’ Position = (20, 10, 30)
            // Both the descriptor path and the JSON path should produce the same position.
            // ⭐⭐⭐ Q59-C1 — UPDATED, exactly as ApplyGeoSpatialDescriptor's ATTR-BATCH-03 TODO demanded:
            //    "If new JSON path delegates are added for dtWorldPos (e.g. \"Heading\"), this method MUST be
            //     updated to maintain convergence … Enforced currently by [this test]."
            // 🔴 AX-018 added that delegate, so the helper now sets Rotation too — and this test began
            //    comparing UNLIKE payloads: the descriptor ALWAYS carries a heading (Ori.Heading = 0 means
            //    North, a real value), while the JSON patch omitted it and left Rotation at default(0,0,0,0),
            //    which is not even a valid quaternion.
            // ⇒ ⭐ convergence is only a meaningful claim when both routes carry the SAME information, so the
            //    JSON payload now includes the heading, and a NON-ZERO one so the assert cannot pass by
            //    both sides happening to be identity.
            const double Lat = 10.0, Lon = 20.0, Alt = 30.0, HeadingDeg = 45.0;

            var compiler = BuildCompiler();

            // â”€â”€ Descriptor path â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var descriptors = new List<EntityDescriptorUnion>
            {
                new EntityDescriptorUnion
                {
                    _d = EDescriptorType.dtWorldPos,
                    WorldPos = new WorldPos
                    {
                        EntityId = 0,
                        Pos      = new GeoPoint { Latitude = Lat, Longitude = Lon, Altitude = Alt },
                        Ori      = new EulerOri { Heading = (float)HeadingDeg },
                    },
                },
            };

            var descriptorComponents = DescriptorMapper.MapToComponents(descriptors, GeoTransform, compiler);
            var descriptorTransform  = descriptorComponents.OfType<SimTransform>().Single();

            // â”€â”€ JSON path â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var jsonCtx = new ListPatchContext(null);
            compiler.Compile(
                $"{{\"GeoPosition\":{{\"Latitude\":{Lat},\"Longitude\":{Lon},\"Altitude\":{Alt}}}," +
                $"\"Heading\":{HeadingDeg}}}",
                jsonCtx);
            var jsonComponents = jsonCtx.FlushComponents();
            var jsonTransform  = jsonComponents.OfType<SimTransform>().Single();

            // â”€â”€ Assert: same Position (both paths use the same geoTransform)
            Assert.Equal(descriptorTransform.Position.X, jsonTransform.Position.X, precision: 3);
            Assert.Equal(descriptorTransform.Position.Y, jsonTransform.Position.Y, precision: 3);
            Assert.Equal(descriptorTransform.Position.Z, jsonTransform.Position.Z, precision: 3);

            // ⭐⭐⭐ And the same Rotation — both routes now go through the ONE shared conversion
            //    (SimTransformBridgeSystem.HeadingDegToRotation), which is Q59-C1's whole point.
            // ⚠ Asserted against the BRIDGE as well as against each other: agreeing with one another
            //    would still hold if both adopted the same WRONG formula, which is how F3 survived.
            var expected = Fdp.Modules.Geographic.Systems.SimTransformBridgeSystem
                .HeadingDegToRotation((float)HeadingDeg);

            Assert.Equal(expected.Z, descriptorTransform.Rotation.Z, precision: 4);
            Assert.Equal(expected.W, descriptorTransform.Rotation.W, precision: 4);
            Assert.Equal(descriptorTransform.Rotation.Z, jsonTransform.Rotation.Z, precision: 4);
            Assert.Equal(descriptorTransform.Rotation.W, jsonTransform.Rotation.W, precision: 4);
        }
    }
}
