using Hrot.Map.Definitions.Tkb;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-118</c> / <c>UXI-23 S1</c> — <c>NedTkbBuilder.WithVisual</c> must actually STORE
    /// what it is given.</b>
    ///
    /// <para>🔴 It used to resolve the template, <b>never invoke the configure delegate</b>, and return —
    /// under the comment <i>"VisualData ECS component will be applied by IG-side translator in
    /// Phase 6."</i> Phase 6 never happened, so nine catalog call sites authored symbol codes, colours
    /// and models into a delegate nobody called, <c>VisualDefinitionDto</c> was produced by nothing in
    /// the repository, and its only consumer — <c>PresentationTkbTranslator</c> — was inert on every
    /// host. ⚠ Identical in shape to <c>WithPhysics</c>'s dropped fields (<c>CE-113</c>).</para>
    ///
    /// <para>🔒 These rails assert the DESCRIPTOR IS PRESENT and CARRIES THE VALUES — an absent
    /// descriptor is indistinguishable from an unauthored one, which is exactly why this was silent.</para>
    /// </summary>
    public class NedTkbBuilderVisualTests
    {
        private const long TestTkbId = 9902L;

        private static TkbDatabase BuildDatabase(
            string symbolCode  = "SFGPUCIZ-------",
            string modelPath   = "models/test_tank.obj",
            string colorHex    = "#2E4057",
            float  scale       = 1.2f,
            bool   showLabel   = true,
            string? mapShape   = null)
        {
            var db = new TkbDatabase();
            new NedTkbBuilder(db)
                .DefineVehicle(TestTkbId, "TestVisualVehicle")
                .WithVisual(TestTkbId, v =>
                {
                    v.SymbolCode   = symbolCode;
                    v.ModelPath    = modelPath;
                    v.ColorHex     = colorHex;
                    v.Scale        = scale;
                    v.ShowLabel    = showLabel;
                    v.MapShapeName = mapShape;
                });
            return db;
        }

        /// <summary>
        /// 🔴 The regression itself: no descriptor was ever attached. Red-proof: drop the
        /// <c>AddDescriptor</c> call in <c>WithVisual</c>.
        /// </summary>
        [Fact]
        public void WithVisual_StoresVisualDefinitionDescriptor()
        {
            var template = BuildDatabase().GetByType(TestTkbId)!;
            Assert.True(template.HasDescriptor<VisualDefinitionDto>(),
                "WithVisual must attach a VisualDefinitionDto — PresentationTkbTranslator consumes it, "
              + "and without it no TKB-built entity ever carries VisualData.");
        }

        /// <summary>
        /// ⚠ Pins that the configure delegate is INVOKED. A descriptor built from a fresh
        /// <c>IgVisualDef</c> would still satisfy the presence rail above while silently serving
        /// defaults — the subtler half of the same bug.
        /// </summary>
        [Fact]
        public void WithVisual_InvokesTheConfigureDelegate()
        {
            var called = false;
            var db = new TkbDatabase();
            new NedTkbBuilder(db)
                .DefineVehicle(TestTkbId, "TestVisualVehicle")
                .WithVisual(TestTkbId, _ => called = true);

            Assert.True(called, "WithVisual must invoke configure — it used to ignore it entirely.");
        }

        [Fact]
        public void WithVisual_CarriesSymbolCode()
        {
            var dto = BuildDatabase(symbolCode: "SHGPUCIZ-------")
                .GetByType(TestTkbId)!.GetDescriptor<VisualDefinitionDto>()!;
            Assert.Equal("SHGPUCIZ-------", dto.SymbolCode);
        }

        [Fact]
        public void WithVisual_CarriesModelPathAndColour()
        {
            var dto = BuildDatabase(modelPath: "models/m1_abrams.obj", colorHex: "#123456")
                .GetByType(TestTkbId)!.GetDescriptor<VisualDefinitionDto>()!;
            Assert.Equal("models/m1_abrams.obj", dto.ModelPath);
            Assert.Equal("#123456", dto.ColorHex);
        }

        [Fact]
        public void WithVisual_CarriesScaleAndLabelFlag()
        {
            var dto = BuildDatabase(scale: 2.5f, showLabel: false)
                .GetByType(TestTkbId)!.GetDescriptor<VisualDefinitionDto>()!;
            Assert.Equal(2.5f, dto.Scale);
            Assert.False(dto.ShowLabel);
        }

        /// <summary>
        /// ⭐ The optional 2-D shape override survives, including its null (auto-select) case.
        /// </summary>
        [Fact]
        public void WithVisual_CarriesMapShapeName_IncludingNull()
        {
            var named = BuildDatabase(mapShape: "tank-2d")
                .GetByType(TestTkbId)!.GetDescriptor<VisualDefinitionDto>()!;
            Assert.Equal("tank-2d", named.MapShapeName);

            var auto = BuildDatabase(mapShape: null)
                .GetByType(TestTkbId)!.GetDescriptor<VisualDefinitionDto>()!;
            Assert.Null(auto.MapShapeName);
        }

        /// <summary>
        /// ⭐⭐ The real catalog is the thing that matters: every entry that authors visuals must now
        /// produce a descriptor. This is the rail that would have caught the original defect, because
        /// it exercises the production data rather than a test fixture.
        /// </summary>
        [Fact]
        public void NedTkbCatalog_AuthoredEntriesCarryVisualDescriptors()
        {
            var db = new TkbDatabase();
            NedTkbCatalog.RegisterAll(db);

            long[] authored =
            {
                TkbEntityTypes.Tank_M1Abrams,
                TkbEntityTypes.IFV_Bradley,
                TkbEntityTypes.Truck_HMMWV,
                TkbEntityTypes.Tank_T72,
                TkbEntityTypes.Infantry_Rifleman,
            };

            foreach (var tkbType in authored)
            {
                var template = db.GetByType(tkbType);
                Assert.NotNull(template);
                Assert.True(template!.HasDescriptor<VisualDefinitionDto>(),
                    $"TKB type {tkbType} authors visuals via WithVisual but carries no descriptor.");

                var dto = template.GetDescriptor<VisualDefinitionDto>()!;
                Assert.False(string.IsNullOrWhiteSpace(dto.SymbolCode),
                    $"TKB type {tkbType} must carry a non-empty MIL-STD-2525 symbol code.");
            }
        }
    }
}
