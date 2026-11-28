using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core;
using Hrot.Core.Tkb;
using Hrot.Map.Common;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4693</c> (bridge laying BL-S3 / BL-S4, ① ②; 📄 docs/designs/engineering/DESIGN_Bridge_Laying.md §12d) — the BRIDGE ENGINEERS are in the real catalogue:
    /// the assault bridge layer (TKB 118, tracked, class <c>AssaultBridgeLayer</c> 1.1.*.9.3) and the floating-bridge engineer (TKB 119, wheeled, class
    /// <c>FloatingBridgeEngineer</c> 1.1.*.7.1), unarmed, each with its <see cref="BridgeLayerDto"/> naming a walkable-deck span and a SOURCE; the assault span is the
    /// sourced Wolverine's (26 m deck over a 24 m gap, ≤ 5 min / ≤ 10 min), longer than its gap. 🔴 Red-proof: drop <c>DefineBridgeLayers</c> ⇒ the types are absent.
    /// </summary>
    public class BridgeLayerTkbTests
    {
        private static readonly TkbDatabase Db = HrotEnvironment.CreateTkb();

        [Theory]
        [InlineData(TkbEntityTypes.Engineer_AssaultBridgeLayer, 118L, "AssaultBridgeLayer", LocomotionClass.Tracked, TkbEntityTypes.Obstacle_AssaultBridge)]
        [InlineData(TkbEntityTypes.Engineer_FloatingBridge, 119L, "FloatingBridgeEngineer", LocomotionClass.Wheeled, TkbEntityTypes.Obstacle_FloatingBridge)]
        public void TheCatalogue_HasTheBridgeEngineer_ItsClass_ItsSpan_AndItsSource(long type, long wire, string cls, LocomotionClass mobility, long bridge)
        {
            var t = Db.GetByType(type);
            Assert.NotNull(t);
            Assert.Equal(wire, type);   // a permanent wire value
            Assert.Equal(mobility, t!.GetDescriptor<LocomotionDto>()!.Class);
            var suite = t.GetDescriptor<WeaponSuiteDto>();
            Assert.True(suite == null || suite.Mounts.Count == 0, "an engineering vehicle carries no weapon");
            Assert.Equal(cls, EntityClassTable.Default.MostSpecific(t.DisType)!.Name);
            var layer = t.GetDescriptor<BridgeLayerDto>();
            Assert.NotNull(layer);
            Assert.Equal(bridge, layer!.BridgeType);
            Assert.True(layer.Carried >= 1 && layer.SpanMaxM > 0f && layer.BuildSeconds > 0f && layer.RecoverSeconds > 0f);
            Assert.False(string.IsNullOrWhiteSpace(layer.Source), "Q-6: the figures name their source");
            var deck = Db.GetByType(bridge)!.GetDescriptor<StaticObstacleDto>();
            Assert.Equal(Fdp.Toolkit.Terrain.ObstacleKind.WalkableDeck, deck!.Kind);
            Assert.True(deck.Length >= layer.SpanMaxM, $"the deck ({deck.Length} m) reaches over the longest gap ({layer.SpanMaxM} m)");
        }

        [Fact]
        public void TheAssaultSpan_IsTheSourcedWolverine()
        {
            var layer = Db.GetByType(TkbEntityTypes.Engineer_AssaultBridgeLayer)!.GetDescriptor<BridgeLayerDto>()!;
            var deck = Db.GetByType(TkbEntityTypes.Obstacle_AssaultBridge)!.GetDescriptor<StaticObstacleDto>()!;
            Assert.Equal(26f, deck.Length);
            Assert.Equal(4f, deck.Width);
            Assert.Equal(24f, layer.SpanMaxM);
            Assert.Equal(300f, layer.BuildSeconds);
            Assert.Equal(600f, layer.RecoverSeconds);
            Assert.Contains("wolverine", layer.Source, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
