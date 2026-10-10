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

        /// <summary>
        /// ⭐ <c>CE-1017</c> S0 — every built-in placeable type carries a DIS type WITH its country, the master descriptor's
        /// text equals the template's DIS type, and every authored visual names its icon. 📄 docs/DESIGN_Add_Entity_Picker.md §5 S0.
        /// </summary>
        [Fact]
        public void CE1017_BuiltInTypes_CarryDisWithCountry_MasterTextAgrees_AndAnIconName()
        {
            var db = new TkbDatabase();
            NedTkbCatalog.RegisterAll(db);
            Hrot.Core.Tkb.UrbanCombatTkbCatalog.RegisterAll(db);

            (long Type, ushort Country)[] ned =
            {
                (TkbEntityTypes.Tank_M1Abrams, NedTkbCatalog.UnitedStates), (TkbEntityTypes.IFV_Bradley, NedTkbCatalog.UnitedStates),
                (TkbEntityTypes.Truck_HMMWV, NedTkbCatalog.UnitedStates),   (TkbEntityTypes.Tank_T72, NedTkbCatalog.Russia),
                (TkbEntityTypes.Infantry_Rifleman, NedTkbCatalog.UnitedStates), (TkbEntityTypes.Unit_TankPlatoon, NedTkbCatalog.UnitedStates),
                (TkbEntityTypes.Unit_InfantrySquad, NedTkbCatalog.UnitedStates), (TkbEntityTypes.Unit_TankPlatoon_Auto, NedTkbCatalog.UnitedStates),
            };
            foreach (var (type, country) in ned)
            {
                var t = db.GetByType(type)!;
                Assert.Equal(country, t.DisType.Country);
                Assert.NotEqual((byte)0, t.DisType.Kind);
                Assert.Equal(t.DisType.ToString(), t.GetDescriptor<TkbMasterDto>()!.DisType);
                Assert.False(string.IsNullOrEmpty(t.GetDescriptor<VisualDefinitionDto>()!.IconName), $"TKB type {type} names no icon");
                Assert.False(t.GetDescriptor<TkbMasterDto>()!.HideFromPalette);
            }

            foreach (var type in new long[] { 1001, 1002, 2001, 2002, 2003 })   // the UrbanCombat five
            {
                var t = db.GetByType(type)!;
                Assert.NotEqual((byte)0, t.DisType.Kind);
                Assert.Equal((byte)1, t.DisType.Domain);
                Assert.Equal(t.DisType.ToString(), t.GetDescriptor<TkbMasterDto>()!.DisType);
            }

            // the HMMWV's category is SISO "small wheeled utility vehicle", and the names table says so
            var names = Hrot.Core.Tkb.DisNameTable.Default;
            Assert.Equal(new[] { "Platform", "Land", "United States", "Small Wheeled Utility Vehicle" },
                         names.Path(db.GetByType(TkbEntityTypes.Truck_HMMWV)!.DisType));
            Assert.Equal(new[] { "Platform", "Land", "Russia", "Tank" }, names.Path(db.GetByType(TkbEntityTypes.Tank_T72)!.DisType));
        }

        /// <summary>
        /// ⭐ <c>CE-3112</c> / 5d-2 — every built-in HUMAN template is a pedestrian that can interact: the Pedestrian class maps
        /// it to the Infantry navmesh layer (R-222), and <c>CanInteract</c> lets the interaction channel run its door actions.
        /// 🔴 Both were missing on the UrbanCombat humans: soldiers planned on the vehicle mesh (no doorway fits) and a door
        /// action failed at once (the bt-doors live run, 2026-10-08).
        /// </summary>
        [Fact]
        public void CE3112_BuiltInHumans_ArePedestrians_ThatCanInteract()
        {
            var db = new TkbDatabase();
            NedTkbCatalog.RegisterAll(db);
            Hrot.Core.Tkb.UrbanCombatTkbCatalog.RegisterAll(db);
            foreach (var type in new long[] { 1001, 2002, 2003, TkbEntityTypes.Infantry_Rifleman })
            {
                var t = db.GetByType(type)!;
                Assert.Equal(CarKinem.Core.VehicleClass.Pedestrian, t.GetDescriptor<VehicleParametersDto>()?.VehicleClass);
                Assert.True(t.GetDescriptor<BehaviorProfileDto>()?.CanInteract, $"TKB type {type} ({t.Name}) cannot interact");
            }
        }

        /// <summary>⭐ <c>CE-1017</c> S0 — the names table: unknown numbers read "Category 7", a 0 level is skipped.</summary>
        [Fact]
        public void CE1017_DisNameTable_NamesKnownLevels_SkipsZero_AndNumbersTheUnknown()
        {
            var names = Hrot.Core.Tkb.DisNameTable.Default;
            Assert.Equal(new[] { "Life Form", "Land" }, names.Path(new Fdp.Core.DISEntityType { Kind = 3, Domain = 1 }));
            Assert.Equal(new[] { "Platform", "Land", "Country 999", "Category 77" },
                         names.Path(new Fdp.Core.DISEntityType { Kind = 1, Domain = 1, Country = 999, Category = 77 }));
            Assert.Empty(names.Path(default));
        }
    }
}
