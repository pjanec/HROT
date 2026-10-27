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
using Hrot.Common.Systems;   // Q65 obstacle 1: the request tier moved here
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

            // ⭐⭐⭐ Q59-E — descriptor ordinals now come from the world's DescriptorOwnershipMap, fed by the
            //    NETWORK layer's translators, not from a constant in the JSON routing table. ⇒ a test that
            //    asserts a dirty DESCRIPTOR must supply that declaration; without it the map is legitimately
            //    empty (a networkless host) and nothing is marked.
            FakeDescriptorTranslator.ContributeProductionPairings(repo);
            return repo;
        }

        // â”€â”€ ListPatchContext tests (no live ECS) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void SimHostAttributeCompiler_Name_Registered()
        {
            var compiler = AttributeCompilerFactory.Build();
            var ctx = new ListPatchContext(null);

            compiler.Compile("{\"Name\":\"Test\"}", ctx);

            var result = ctx.FlushComponents();
            var data   = result.OfType<Fdp.Core.EntityInfo>().Single();
            Assert.Equal("Test", data.Name);
        }

        [Fact]
        public void SimHostAttributeCompiler_Affiliation_Registered()
        {
            var compiler = AttributeCompilerFactory.Build();
            var ctx = new ListPatchContext(null);

            compiler.Compile("{\"Affiliation\":\"FORCE_FRIENDLY\"}", ctx);

            var result = ctx.FlushComponents();
            var data   = result.OfType<Fdp.Core.EntityInfo>().Single();
            Assert.Equal(ForceId.Friend, data.ForceId);
        }

        [Fact]
        public void SimHostAttributeCompiler_Affiliation_PreservesExistingName()
        {
            var compiler = AttributeCompilerFactory.Build();
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
            var compiler = AttributeCompilerFactory.Build();

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
        public void AttributeCompiler_PositionPatch_TriggersGeoSpatialDirty()
        {
            // ⭐ CE-4073 — the position route is local ENU metres; it dirties the same dtWorldPos descriptor the geodetic one did.
            var compiler = AttributeCompilerFactory.Build();

            var repo   = CreateRepo();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform());
            // Grant authority so the invoker dispatches the setter.
            repo.SetAuthority<SimTransform>(entity, true);

            var context = compiler.CreatePatchContext(repo, entity);
            compiler.Compile(
                "{\"Position\":{\"X\":120.5,\"Y\":-40.25,\"Z\":3.0}}",
                context);
            context.FlushDirtyMarks();

            var state = ((ISimulationView)repo).GetManagedComponentRO<EgressPublicationState>(entity);
            Assert.NotNull(state);
            Assert.Contains((long)EDescriptorType.dtWorldPos, state.DirtyDescriptors);
        }

        // ── ⭐⭐⭐ CE-4073 rail (a) — a Position patch moves SimTransform.Position to EXACTLY those metres, on a host WITH and a host
        //    WITHOUT a geographic transform. The two shapes of the patch (nested and flat-dotted) and the two kinds of host are one
        //    theory each: the old route registered the geodetic paths only `if (geoTransform != null)`, so the "without" row was the
        //    silent-drop defect. ⛔ Build() takes no transform now, so "a host with / without one" differ only in whether the world
        //    carries an IGeographicTransform singleton — which must change NOTHING.
        public static IEnumerable<object[]> PositionPatchShapes() => new[]
        {
            new object[] { "{\"Position\":{\"X\":120.5,\"Y\":-40.25,\"Z\":3.0}}", false },
            new object[] { "{\"Position.X\":120.5,\"Position.Y\":-40.25,\"Position.Z\":3.0}", false },
            new object[] { "{\"Position\":{\"X\":120.5,\"Y\":-40.25,\"Z\":3.0}}", true },
            new object[] { "{\"Position.X\":120.5,\"Position.Y\":-40.25,\"Position.Z\":3.0}", true },
        };

        [Theory]
        [MemberData(nameof(PositionPatchShapes))]
        public void AttributeCompiler_PositionPatch_MovesSimTransformToExactlyThoseMetres(string patch, bool hostHasGeoTransform)
        {
            var repo   = CreateRepo();
            if (hostHasGeoTransform)
                repo.SetSingletonManaged<IGeographicTransform>(new FactoryTestGeoTransform());   // the transform's presence must change nothing
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = new Vector3(9f, 9f, 9f) });
            repo.SetAuthority<SimTransform>(entity, true);

            // The world's own compiler — the one every host builds (no geographic argument exists to vary).
            var compiler = Fdp.Toolkit.Replication.Attributes.AttributeInterpreterProvider.GetOrCreateJson(repo);
            compiler.Compile(patch, compiler.CreatePatchContext(repo, entity));

            var pos = repo.GetComponentRO<SimTransform>(entity).Position;
            Assert.Equal(new Vector3(120.5f, -40.25f, 3.0f), pos);
        }

        [Fact]
        public void AttributeCompiler_PositionPatch_OfOneAxis_KeepsTheOtherTwo()
        {
            // ⭐ The axes are independent plain copies — a patch carrying only X leaves Y and Z alone (no pre-fill, nothing to convert).
            var repo   = CreateRepo();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform { Position = new Vector3(1f, 2f, 3f) });
            repo.SetAuthority<SimTransform>(entity, true);

            var compiler = AttributeCompilerFactory.Build();
            compiler.Compile("{\"Position\":{\"X\":77.0}}", compiler.CreatePatchContext(repo, entity));

            Assert.Equal(new Vector3(77f, 2f, 3f), repo.GetComponentRO<SimTransform>(entity).Position);
        }

        [Fact]
        public void AttributeCompiler_TheGeodeticPositionPathsAreGone()
        {
            // ⭐ CE-4073 — the retired GeoPosition.* paths are not registered on any host.
            var paths = AttributeCompilerFactory.Build().RegisteredPaths;
            Assert.Contains("Position.X", paths);
            Assert.Contains("Position.Y", paths);
            Assert.Contains("Position.Z", paths);
            Assert.DoesNotContain(paths, p => p.StartsWith("GeoPosition", StringComparison.Ordinal));
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
            repo.RegisterComponent<NetworkAuthority>();
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
            var compiler = AttributeCompilerFactory.Build();
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
            => AttributeCompilerFactory.Build();

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

            var components = DescriptorMapper.MapToComponents(descriptors, compiler);

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

            var components = DescriptorMapper.MapToComponents(descriptors, compiler);

            int count = components.OfType<Fdp.Core.EntityInfo>().Count();
            Assert.Equal(1, count);
        }

        [Fact]
        public void DescriptorMapper_GeoSpatial_SharedDelegate_ProducesSameResultAsDirectPath()
        {
            // Arrange: local ENU position (20, 10, 30) m, heading 45.
            // ⭐ CE-4073 — both routes now carry LOCAL ENU metres (the descriptor always did; the JSON route used to be lat/lon/alt).
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
            const double X = 20.0, Y = 10.0, Z = 30.0, HeadingDeg = 45.0;

            var compiler = BuildCompiler();

            // â”€â”€ Descriptor path â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var descriptors = new List<EntityDescriptorUnion>
            {
                new EntityDescriptorUnion
                {
                    _d = EDescriptorType.dtWorldPos,
                    // ⭐ AQ107 B / D — the descriptor is a local-ENU pose; the JSON route now speaks the same frame (CE-4073), so
                    //   the same place and heading are encoded here with the SAME shared heading conversion.
                    WorldPos = Hrot.Map.Common.Replication.Utils.WorldPosCodec.Encode(
                        0, 0.0,
                        new System.Numerics.Vector3((float)X, (float)Y, (float)Z),
                        Fdp.Modules.Geographic.Systems.SimTransformBridgeSystem.HeadingDegToRotation((float)HeadingDeg),
                        System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero),
                },
            };

            var descriptorComponents = DescriptorMapper.MapToComponents(descriptors, compiler);
            var descriptorTransform  = descriptorComponents.OfType<SimTransform>().Single();

            // â”€â”€ JSON path â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var jsonCtx = new ListPatchContext(null);
            compiler.Compile(
                $"{{\"Position\":{{\"X\":{X},\"Y\":{Y},\"Z\":{Z}}}," +
                $"\"Heading\":{HeadingDeg}}}",
                jsonCtx);
            var jsonComponents = jsonCtx.FlushComponents();
            var jsonTransform  = jsonComponents.OfType<SimTransform>().Single();

            // â”€â”€ Assert: same Position (a plain copy on both paths)
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
