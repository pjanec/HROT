using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.DDS.DM;
using Bagira.IG.Components;
using Bagira.Map.Common.Replication.Utils;
using FDP.Toolkit.Replication.Patching;
using Bagira.SimHost.Systems;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;

namespace Bagira.SimHost.Tests
{
    // ─── Shared geoTransform stub ────────────────────────────────────────────

    /// <summary>
    /// Trivial geoTransform stub: lat→Y, lon→X, alt→Z (mirrors existing
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

    // ─── ATTR-S5T1 / ATTR-S5T4 — AttributeCompilerFactory tests ─────────────

    public class AttributeCompilerFactoryTests
    {
        private static EntityRepository CreateRepo()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            repo.RegisterComponent<IG.Components.EntityInfo>();
            return repo;
        }

        // ── ListPatchContext tests (no live ECS) ─────────────────────────────

        [Fact]
        public void SimHostAttributeCompiler_Name_Registered()
        {
            var compiler = AttributeCompilerFactory.Build(new FactoryTestGeoTransform());
            var ctx = new ListPatchContext(null);

            compiler.Compile("{\"Name\":\"Test\"}", ctx);

            var result = ctx.FlushComponents();
            var data   = result.OfType<IG.Components.EntityInfo>().Single();
            Assert.Equal("Test", data.Name);
        }

        [Fact]
        public void SimHostAttributeCompiler_Affiliation_Registered()
        {
            var compiler = AttributeCompilerFactory.Build(new FactoryTestGeoTransform());
            var ctx = new ListPatchContext(null);

            compiler.Compile("{\"Affiliation\":\"FORCE_FRIENDLY\"}", ctx);

            var result = ctx.FlushComponents();
            var data   = result.OfType<IG.Components.EntityInfo>().Single();
            Assert.Equal(ForceId.Friend, data.ForceId);
        }

        [Fact]
        public void SimHostAttributeCompiler_Affiliation_PreservesExistingName()
        {
            var compiler = AttributeCompilerFactory.Build(new FactoryTestGeoTransform());
            var seed     = new IG.Components.EntityInfo { Name = "Alpha", ForceId = ForceId.Unknown };
            var ctx      = new ListPatchContext(new List<object> { seed });

            compiler.Compile("{\"Affiliation\":\"FORCE_OPPOSING\"}", ctx);

            var result = ctx.FlushComponents();
            var data   = result.OfType<IG.Components.EntityInfo>().Single();
            Assert.Equal("Alpha",         data.Name);  // unchanged
            Assert.Equal(ForceId.Hostile, data.ForceId);
        }

        // ── EcsPatchContext integration tests ────────────────────────────────

        [Fact]
        public void AttributeCompiler_NamePatch_TriggersEntityInfoDirtyOnEcsPatchContext()
        {
            var compiler = AttributeCompilerFactory.Build(new FactoryTestGeoTransform());

            var repo   = CreateRepo();
            var entity = repo.CreateEntity();
            repo.SetComponent( entity, new IG.Components.EntityInfo { Name = "original" });
            // Grant authority so the invoker dispatches the setter.
            repo.SetAuthority<IG.Components.EntityInfo>( entity, true);

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
            Assert.Contains((long)EDescriptorType.dtGeoSpatial, state.DirtyDescriptors);
        }
    }

    // ─── ATTR-S5T2 — CreateEntityRequestSystem JSON compiler tests ───────────

    public class CreateEntityRequestSystemJsonTests
    {
        private const long  ValidTkbType = 42L;
        private const ulong ValidDisType = 0x0100_0000_0000_0001UL;
        private static readonly DisTypeStruct ValidDisTypeStruct = new DisTypeStruct { Kind = 1, Extra = 1 };
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
            repo.RegisterComponent<IG.Components.EntityInfo>();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkOwnership>();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterEvent<FDP.Toolkit.Lifecycle.Events.ConstructionOrder>();
            repo.RegisterEvent<FDP.Toolkit.Lifecycle.Events.DestructionOrder>();
            return repo;
        }

        private static CreateEntityRequest MakeRequestWithJson(
            string? json,
            List<EntityDescriptorUnion>? extraDescriptors = null)
        {
            var descriptors = new List<EntityDescriptorUnion>
            {
                new EntityDescriptorUnion
                {
                    _d = EDescriptorType.dtEntityMaster,
                    EntityMaster = new EntityMaster
                    {
                        EntityId = 0,
                        TkbType  = ValidTkbType,
                        DisType  = ValidDisTypeStruct,
                    },
                },
            };

            if (extraDescriptors != null)
                descriptors.AddRange(extraDescriptors);

            return new CreateEntityRequest
            {
                RequestId          = Guid.NewGuid(),
                Owner              = new NodeId { AppDomainId = 1, AppInstanceId = 2 },
                Flags              = 0,
                InitialDescriptors = descriptors,
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
                geoTransform: null,
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
            var commands = ((ISimulationView)repo).ConsumeManagedEvents<SpawnEntityCommand>();

            Assert.Single(commands);
            var cmd      = commands[0];
            var igData   = Assert.Single(cmd.InitialComponents!.OfType<IG.Components.EntityInfo>());

            Assert.Equal("Delta-7", igData.Name.ToString());
        }

        [Fact]
        public void CreateEntityRequestSystem_InitialAttributesJson_DoesNotOverwriteAffiliation()
        {
            // Arrange: descriptor carries ForceId = FORCE_FRIENDLY; JSON patches only Name.
            var repo    = CreateWorld();
            var source  = new StubRequestSource();
            var entityInfoDescriptor = new EntityDescriptorUnion
			{
                _d = EDescriptorType.dtEntityInfo,
                EntityInfo = new BDC.SSTD.EntityInfo
                {
                    EntityId        = 0,
                    Name            = "Original",
                    ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
                },
            };
            var request = MakeRequestWithJson(
                "{\"Name\":\"Echo-1\"}",
                extraDescriptors: new List<EntityDescriptorUnion> { entityInfoDescriptor });
            source.Enqueue(request);

            var system = BuildSystemWithCompiler(source, out _);

            // Act
            system.Execute(repo, 0f);

            // Assert
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ConsumeManagedEvents<SpawnEntityCommand>();

            Assert.Single(commands);
            var igData2  = Assert.Single(commands[0].InitialComponents!.OfType<IG.Components.EntityInfo>());

            Assert.Equal("Echo-1",       igData2.Name.ToString());
            Assert.Equal(ForceId.Friend, igData2.ForceId);
        }

        [Fact]
        public void CreateEntityRequestSystem_NullJson_NoPatch()
        {
            // Arrange: null InitialAttributesJson should not throw and should process normally.
            var repo    = CreateWorld();
            var source  = new StubRequestSource();
            source.Enqueue(MakeRequestWithJson(json: null));

            var system = BuildSystemWithCompiler(source, out var ackSink);

            // Act — must not throw
            var ex = Record.Exception(() => system.Execute(repo, 0f));

            // Assert
            Assert.Null(ex);
            repo.Bus.SwapBuffers();
            var commands = ((ISimulationView)repo).ConsumeManagedEvents<SpawnEntityCommand>();
            Assert.Single(commands);
            Assert.Equal(0, ackSink.WrittenAcks[0].ErrorCode);
        }
    }

    // ─── ATTR-S5T1/S5T4/S6T1/S6T2 — DescriptorMapper compiler overload tests ─

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
                    EntityInfo = new BDC.SSTD.EntityInfo
                    {
                        EntityId        = 0,
                        Name            = "TestUnit",
                        ForceIdentifier = eForceIdentifier.FORCE_FRIENDLY,
                        CommanderId     = 42,
                    },
                },
            };

            var components = DescriptorMapper.MapToComponents(descriptors, GeoTransform, compiler);

            var igData = Assert.Single( components.OfType<IG.Components.EntityInfo>());
            Assert.Equal("TestUnit",     igData.Name);
            Assert.Equal(ForceId.Friend, igData.ForceId);
            Assert.Equal(42,             igData.CommanderId);
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
                    EntityInfo = new BDC.SSTD.EntityInfo { EntityId = 0, Name = "Alpha" },
                },
            };

            var components = DescriptorMapper.MapToComponents(descriptors, GeoTransform, compiler);

            int count = components.OfType<IG.Components.EntityInfo>().Count();
            Assert.Equal(1, count);
        }

        [Fact]
        public void DescriptorMapper_GeoSpatial_SharedDelegate_ProducesSameResultAsDirectPath()
        {
            // Arrange: lat=10, lon=20, alt=30, heading=0
            // IdentityGeoTransform: ToCartesian(lat, lon, alt) → (lon, lat, alt)
            //   → Position = (20, 10, 30)
            // Both the descriptor path and the JSON path should produce the same position.
            const double Lat = 10.0, Lon = 20.0, Alt = 30.0;

            var compiler = BuildCompiler();

            // ── Descriptor path ───────────────────────────────────────────────
            var descriptors = new List<EntityDescriptorUnion>
            {
                new EntityDescriptorUnion
                {
                    _d = EDescriptorType.dtGeoSpatial,
                    GeoSpatial = new GeoSpatial
                    {
                        EntityId = 0,
                        Pos      = new GeoPosition { Latitude = Lat, Longitude = Lon, Altitude = Alt },
                        Rot      = new OrientationHPR { Heading = 0f },
                    },
                },
            };

            var descriptorComponents = DescriptorMapper.MapToComponents(descriptors, GeoTransform, compiler);
            var descriptorTransform  = descriptorComponents.OfType<SimTransform>().Single();

            // ── JSON path ─────────────────────────────────────────────────────
            var jsonCtx = new ListPatchContext(null);
            compiler.Compile(
                $"{{\"GeoPosition\":{{\"Latitude\":{Lat},\"Longitude\":{Lon},\"Altitude\":{Alt}}}}}",
                jsonCtx);
            var jsonComponents = jsonCtx.FlushComponents();
            var jsonTransform  = jsonComponents.OfType<SimTransform>().Single();

            // ── Assert: same Position (both paths use the same geoTransform)
            Assert.Equal(descriptorTransform.Position.X, jsonTransform.Position.X, precision: 3);
            Assert.Equal(descriptorTransform.Position.Y, jsonTransform.Position.Y, precision: 3);
            Assert.Equal(descriptorTransform.Position.Z, jsonTransform.Position.Z, precision: 3);

            // Both paths leave Rotation at default for the shared delegate
            // (heading/rotation is outside the scope of the shared coordinate delegate).
            Assert.Equal(descriptorTransform.Rotation, jsonTransform.Rotation);
        }
    }
}
