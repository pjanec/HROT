using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Hrot.Presentation.Map;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>UXI-23</c> <c>S1</c> rails — the map's presentation INPUTS reach the muscle.</b>
    ///
    /// <para>📐 <b>The defect these pin, measured live on <c>--mode all</c> 2026-08-28.</b> The
    /// <c>SimHost</c> perspective drew <b>3</b> non-<c>Line</c> gizmo primitives against the
    /// <c>Scenario</c> perspective's <b>69</b>, over the <i>same 8 entities</i>. The gizmos, their
    /// registration and the execution gate were all healthy — what was missing were the two components
    /// every entity gizmo projects from: <see cref="MapDisplayComponent"/> and <c>VisualData</c>. Both
    /// were produced by <b>IG-private</b> code, so a host that cannot reference <c>Hrot.IG</c> could
    /// never obtain them.</para>
    ///
    /// <para>🔒 <b>These rails assert the COMPONENT IS PRESENT, never that a translator is "in the
    /// list".</b> ⚠ <see cref="PresentationTkbTranslator"/> early-returns when its component type is
    /// unregistered — no throw, no log — so a list-membership assertion stays green through exactly the
    /// failure being fixed.</para>
    /// </summary>
    public class MapPresentationParityRails
    {
        private const long TestTkbType = 9101L;

        // ── ① the component registration reaches SimHost ──────────────────────────

        /// <summary>
        /// 🔴 The direct regression: SimHost had NO <see cref="MapDisplayComponent"/> registration
        /// anywhere in the project. Red-proof: drop <c>MapPresentationRegistry.RegisterAll</c> from
        /// <c>SimHostComponentRegistry</c>.
        /// </summary>
        [Fact]
        public void SimHostComponentRegistry_RegistersMapDisplayComponent()
        {
            using var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            Assert.True(world.IsComponentTypeRegistered<MapDisplayComponent>(),
                "SimHost must register MapDisplayComponent — the shared entity gizmos layer-cull on it, "
              + "and without it MapLayerAssignmentSystem cannot stamp a mask.");
        }

        /// <summary>
        /// ⭐ The shared list is the ONLY thing that needs calling — a host gets the whole map component
        /// set from one call, so it cannot half-adopt it.
        /// </summary>
        [Fact]
        public void MapPresentationRegistry_RegistersTheMapComponentSet()
        {
            using var world = new EntityRepository();
            MapPresentationRegistry.RegisterAll(world);

            Assert.True(world.IsComponentTypeRegistered<MapDisplayComponent>());
        }

        /// <summary>
        /// ⭐ Idempotent: the editor registers into both its world and its pre-tick snapshot, and
        /// SimHost reaches the shared list through its own registry as well.
        /// </summary>
        [Fact]
        public void MapPresentationRegistry_RegisterAll_IsIdempotent()
        {
            using var world = new EntityRepository();
            MapPresentationRegistry.RegisterAll(world);

            Assert.Null(Record.Exception(() => MapPresentationRegistry.RegisterAll(world)));
            Assert.True(world.IsComponentTypeRegistered<MapDisplayComponent>());
        }

        // ── ② the translator actually writes VisualData on a TKB-built entity ─────

        /// <summary>
        /// 🔒 <b>The rail the design demands.</b> Asserts the component is PRESENT after injection —
        /// the one assertion that fails when the translator is present but the registration is not.
        /// Red-proof: remove <c>PresentationTkbTranslator</c> from <c>SimHostNodeBootstrapper</c>'s
        /// translator list (or drop the registration and watch the early-return swallow it).
        /// </summary>
        [Fact]
        public void PresentationTranslator_OnSimHostWorld_WritesVisualData()
        {
            using var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            var template = new TkbTemplate("TestVisualUnit", TestTkbType);
            template.AddDescriptor(new VisualDefinitionDto
            {
                SymbolCode   = "SFGPU----------",   // 'F' at index 1 => Friend
                ModelPath    = "models/test.mdl",
                ColorHex     = "#FF00FF",
                MapShapeName = "test-shape"
            });

            var entity = world.CreateEntity();
            new PresentationTkbTranslator().Inject(world, entity, template);

            Assert.True(world.HasComponent<VisualData>(entity),
                "A TKB-built entity must carry VisualData — the entity gizmos project from it.");
            Assert.Equal("SFGPU----------", world.GetComponentRO<VisualData>(entity).SymbolCode.ToString());
        }

        /// <summary>
        /// ⚠ Pins the SILENT half explicitly, so nobody 'fixes' the early-return into a throw and
        /// nobody mistakes its silence for success. An unregistered component yields no component and
        /// no exception — which is precisely why this bug survived.
        /// </summary>
        [Fact]
        public void PresentationTranslator_WithoutRegistration_SilentlyWritesNothing()
        {
            using var world = new EntityRepository();   // deliberately bare — no registry call

            var template = new TkbTemplate("TestVisualUnit", TestTkbType);
            template.AddDescriptor(new VisualDefinitionDto
            {
                SymbolCode = "SHGPU----------",
                ModelPath  = "",
                ColorHex   = ""
            });

            var entity = world.CreateEntity();
            var translator = new PresentationTkbTranslator();
            var ex = Record.Exception(
                (System.Action)(() => translator.Inject(world, entity, template)));

            Assert.Null(ex);   // it does NOT throw …
            Assert.False(world.IsComponentTypeRegistered<VisualData>());   // … and writes nothing.
        }

        // ── ③ the layer system stamps the mask ────────────────────────────────────

        /// <summary>
        /// ⭐ The shared <see cref="MapLayerAssignmentSystem"/> is reachable from SimHost's assembly at
        /// all — it used to live in <c>Hrot.IG</c>, which SimHost cannot reference — and it stamps a
        /// mask on an entity that matches no layer rather than leaving it invisible.
        /// </summary>
        [Fact]
        public void MapLayerAssignmentSystem_StampsMapDisplayComponent()
        {
            using var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            var entity = world.CreateEntity();
            world.AddComponent(entity, new SimTransform());

            new MapLayerAssignmentSystem().Execute(world, 0.016f);

            Assert.True(world.HasComponent<MapDisplayComponent>(entity),
                "MapLayerAssignmentSystem must stamp MapDisplayComponent on map entities.");
            Assert.Equal(0xFFFF_FFFFu, world.GetComponentRO<MapDisplayComponent>(entity).LayerMask);
        }

        /// <summary>
        /// ⭐⭐ The <c>S4</c> seam stays open: the layer definition set is INJECTABLE, which is how one
        /// set becomes shareable across subsystems without cloning the system per host.
        /// </summary>
        [Fact]
        public void MapLayerAssignmentSystem_AcceptsAnInjectedLayerSet()
        {
            using var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            const uint customBit = 1u << 7;
            var custom = new[]
            {
                new MapLayerDefinition("everything", customBit, (_, _, _) => true)
            };

            var entity = world.CreateEntity();
            world.AddComponent(entity, new SimTransform());

            new MapLayerAssignmentSystem(custom).Execute(world, 0.016f);

            Assert.Equal(customBit, world.GetComponentRO<MapDisplayComponent>(entity).LayerMask);
        }

        // ── ④ the bit constants are one declaration, not two hand-synced copies ───

        /// <summary>
        /// 📌 <c>MapLayerBits</c> used to be a hand-maintained copy of these five values, kept in step
        /// by a comment reading "must match … exactly". This rail makes the agreement mechanical.
        /// </summary>
        [Fact]
        public void MapLayerBits_AndRegistryBits_AreOneDeclaration()
        {
            Assert.Equal(Hrot.Map.Common.Config.MapLayerBits.GroundUnitsBit,      MapLayerRegistry.GroundUnitsBit);
            Assert.Equal(Hrot.Map.Common.Config.MapLayerBits.AirUnitsBit,         MapLayerRegistry.AirUnitsBit);
            Assert.Equal(Hrot.Map.Common.Config.MapLayerBits.VehiclesBit,         MapLayerRegistry.VehiclesBit);
            Assert.Equal(Hrot.Map.Common.Config.MapLayerBits.TacticalGraphicsBit, MapLayerRegistry.TacticalGraphicsBit);
            Assert.Equal(Hrot.Map.Common.Config.MapLayerBits.RoadGraphsBit,       MapLayerRegistry.RoadGraphsBit);
        }
    }
}
