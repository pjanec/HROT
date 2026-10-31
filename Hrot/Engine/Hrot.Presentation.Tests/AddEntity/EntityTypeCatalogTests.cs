using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Mission;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Hrot.UI.Common.AddEntity;
using Hrot.UI.Common.Facades;
using Xunit;

namespace Hrot.Presentation.Tests.AddEntity;

/// <summary>
/// ⭐ <c>CE-1017</c> S1 — the Add Entity catalog is BUILT FROM THE TKB and grouped by DIS name, and every pick arms
/// the type's tool. 📄 docs/DESIGN_Add_Entity_Picker.md D2, D4, D6, D7.
/// </summary>
public sealed class EntityTypeCatalogTests
{
    private static TkbDatabase BuiltIn()
    {
        var db = new TkbDatabase();
        NedTkbCatalog.RegisterAll(db);
        UrbanCombatTkbCatalog.RegisterAll(db);
        CivilianTkbCatalog.RegisterAll(db);   // ⭐ CE-3220 — part of the built-in set (HrotEnvironment.RegisterBuiltInTkb)
        return db;
    }

    private static EntityTypeEntry Of(IReadOnlyList<EntityTypeEntry> c, long tkbType) => Assert.Single(c, e => e.TkbType == tkbType);

    [Fact]
    public void CE1017_APhysicalType_IsGroupedByItsNamedDisPath_WithItsIcon()
    {
        var c = EntityTypeCatalog.Build(BuiltIn());

        var t72 = Of(c, TkbEntityTypes.Tank_T72);
        Assert.Equal("T-72", t72.Name);
        Assert.Equal("Platform/Land/Russia/Tank", t72.Category);
        Assert.Equal("t72", t72.IconName);
        Assert.Equal(PlacementToolKind.Point, t72.Tool);
        Assert.True(t72.HasSide);
        Assert.StartsWith("1.1.222.1.", t72.DisText);
    }

    [Fact]
    public void CE1017_ACompositeUnit_IsListedUnderUnits()
    {
        var c = EntityTypeCatalog.Build(BuiltIn());

        Assert.Equal("Units/Land/United States/Armor", Of(c, TkbEntityTypes.Unit_TankPlatoon).Category);   // ⭐ CE-2139: the units' DIS path
        Assert.StartsWith(EntityTypeCatalog.UnitsFolder + "/", Of(c, TkbEntityTypes.Unit_InfantrySquad).Category);   // ExCon's ORBAT keys on it
        Assert.Equal(EntityTypeCatalog.UnitsFolder, DisNameTable.Default.KindName(UnitDis.Kind));
    }

    [Fact]
    public void CE1017_MapGraphics_ComeFromTheRegistry_OnceEach_SideLess_FireLineDisabled()
    {
        var c = EntityTypeCatalog.Build(BuiltIn());

        // Area and Route ARE registered as templates (without a master) — still listed exactly once.
        foreach (var g in PlacementToolRegistry.Graphics)
        {
            var e = Of(c, g.TkbType);
            Assert.Equal(EntityTypeCatalog.MapGraphicsFolder, e.Category);
            Assert.False(e.HasSide);
            Assert.Equal(g.Tool, e.Tool);
        }
        Assert.False(Of(c, TkbEntityTypes.TacGraphic_FireLine).IsEnabled);
        Assert.True(Of(c, TkbEntityTypes.TacGraphic_Area).IsEnabled);
    }

    [Fact]
    public void CE1017_AHiddenType_AndAMasterlessTemplate_AreNotListed()
    {
        var db = new TkbDatabase();
        var hidden = new TkbTemplate("Sensor Part", 90001);
        hidden.AddDescriptor(new TkbMasterDto { CustomName = "Sensor Part", HideFromPalette = true });
        db.Register(hidden);
        db.Register(new TkbTemplate("No Master", 90002));
        var shown = new TkbTemplate("Thing", 90003, categoryPath: "Props\\Crates");
        shown.AddDescriptor(new TkbMasterDto { CustomName = "Crate" });
        db.Register(shown);

        var c = EntityTypeCatalog.Build(db);

        Assert.DoesNotContain(c, e => e.TkbType is 90001 or 90002);
        var crate = Of(c, 90003);
        Assert.Equal("Crate", crate.Name);
        Assert.Equal("Props/Crates", crate.Category);   // no DIS ⇒ the template's own category path
        Assert.Equal("entity/_point", EntityTypeCatalog.IconKeyOf(crate));
    }

    [Fact]
    public void CE1017_PickerEntries_SplitSideBearingFromMapGraphics()
    {
        var c = EntityTypeCatalog.Build(BuiltIn());

        var sided = EntityTypeCatalog.ToPickerEntries(c, sideBearing: true).ToList();
        var graphics = EntityTypeCatalog.ToPickerEntries(c, sideBearing: false).ToList();

        Assert.Contains(sided, p => p.Name == "T-72" && p.IconKey == "entity/t72" && p.Category == "Platform/Land/Russia/Tank");
        Assert.DoesNotContain(sided, p => p.Category == EntityTypeCatalog.MapGraphicsFolder);
        Assert.Equal(PlacementToolRegistry.Graphics.Count, graphics.Count);
        Assert.All(graphics, p => Assert.IsType<EntityTypeEntry>(p.Tag));
        Assert.Contains(graphics, p => p.Name == "Fire Line" && !p.IsEnabled && p.Description is { Length: > 0 });
    }

    // ── PlacementToolRegistry.Arm ────────────────────────────────────────────

    private sealed class RecordingSpawn : ISpawnController
    {
        public readonly List<string> Calls = new();
        public void StartPlacementMode(long tkbType, string? initialPropertiesJson = null) => Calls.Add($"point {tkbType} {initialPropertiesJson}");
        public void StartAreaAuthoringMode(string styleOverrideJson = "") => Calls.Add("area");
        public void StartAreaAuthoringMode(long tkbType, string styleOverrideJson = "") => Calls.Add($"area:{tkbType}");
        public void StartRouteAuthoringMode() => Calls.Add("route");
        public void StartZoneAuthoringMode(string styleOverrideJson = "") => Calls.Add("zone");
    }

    [Fact]
    public void CE1017_Arm_APhysicalType_ArmsThePointToolWithTheSide()
    {
        var spawn = new RecordingSpawn();

        Assert.True(PlacementToolRegistry.Arm(spawn, TkbEntityTypes.Tank_T72, eForceIdentifier.FORCE_OPPOSING));

        Assert.Equal($"point {TkbEntityTypes.Tank_T72} {{\"Affiliation\":\"FORCE_OPPOSING\"}}", Assert.Single(spawn.Calls));
    }

    [Theory]
    [InlineData(TkbEntityTypes.TacGraphic_Area, "area")]
    [InlineData(TkbEntityTypes.TacGraphic_Route, "route")]
    [InlineData(TkbEntityTypes.TerrainZone, "zone")]
    [InlineData(TkbEntityTypes.WeatherRegion, "area:8811")]   // ⭐ CE-1161 — an area born as a Weather Region
    [InlineData(TkbEntityTypes.StoneSource, "area:8812")]     // ⭐ CE-3235 — an area born as a Stone Source
    public void CE1017_Arm_AGraphic_ArmsItsOwnTool_NoSideAsked(long tkbType, string expected)
    {
        var spawn = new RecordingSpawn();

        Assert.True(PlacementToolRegistry.Arm(spawn, tkbType, eForceIdentifier.FORCE_FRIENDLY));

        Assert.Equal(expected, Assert.Single(spawn.Calls));
    }

    [Fact]
    public void CE1017_Arm_ATypeWithNoTool_ArmsNothing()
    {
        var spawn = new RecordingSpawn();

        Assert.False(PlacementToolRegistry.Arm(spawn, TkbEntityTypes.TacGraphic_FireLine, eForceIdentifier.FORCE_FRIENDLY));

        Assert.Empty(spawn.Calls);
    }
    // ── CE-2138 C2 — the class table must not move a built-in type (handoff Track C §3: a golden taken BEFORE C2) ─────────

    /// <summary>Every built-in type's 3-D family, picker folder and fallback icon, one line each, ordered by type.</summary>
    internal static string BuiltInSnapshot(ITkbDatabase db)
    {
        var lines = new List<string>();
        foreach (var t in db.GetAll().OrderBy(t => t.TkbType))
            lines.Add($"{t.TkbType} {t.Name} | dis {(t.DisType.Value != 0 ? t.DisType.ToString() : "-")} | " +
                      $"family {Hrot.UI.Common.Map3D.VisualFamilies.Of(t)} | folder {EntityTypeCatalog.CategoryOf(t, DisNameTable.Default)} | " +
                      $"icon {EntityTypeCatalog.FallbackIconName(t) ?? "-"}");
        return string.Join("\n", lines);
    }

    /// <summary>Every DIS level the names table names (a number it does not name reads "Category 7" and is left out).</summary>
    internal static string DisNamesSnapshot(DisNameTable names)
    {
        var lines = new List<string>();
        for (int k = 1; k <= 255; k++)
        {
            string kn = names.KindName((byte)k);
            if (kn != $"Kind {k}") lines.Add($"{k} {kn}");
            for (int d = 1; d <= 255; d++)
            {
                string dn = names.DomainName((byte)k, (byte)d);
                if (dn != $"Domain {d}") lines.Add($"{k}.{d} {dn}");
                for (int c = 1; c <= 255; c++)
                {
                    string cn = names.CategoryName((byte)k, (byte)d, (byte)c);
                    if (cn != $"Category {c}") lines.Add($"{k}.{d}.*.{c} {cn}");
                }
            }
        }
        for (int c = 1; c <= ushort.MaxValue; c++)
        {
            string n = names.CountryName((ushort)c);
            if (n != $"Country {c}") lines.Add($"country {c} {n}");
        }
        return string.Join("\n", lines);
    }

    /// <summary>⭐ The golden, taken on the pre-C2 code (<c>VisualFamilies</c>' switches + <c>DisNames.json</c>). C2 (CE-2138)
    /// must keep every line; C3 (CE-2139) moves only the three units, on purpose: DIS 1.1.225 → 11.1.225.&lt;arm&gt;.&lt;echelon&gt;.&lt;variant&gt;,
    /// folder Units/United States → Units/Land/United States/&lt;arm&gt; (family and icon unchanged).</summary>
    private const string BuiltInGolden = """
        100 M1 Abrams | dis 1.1.225.1.0.0.0 | family Tank | folder Platform/Land/United States/Tank | icon _tank
        101 M2 Bradley IFV | dis 1.1.225.2.0.0.0 | family Afv | folder Platform/Land/United States/Armored Fighting Vehicle | icon _afv
        102 HMMWV (M2, crewed) | dis 1.1.225.6.0.0.0 | family WheeledUtility | folder Platform/Land/United States/Small Wheeled Utility Vehicle | icon _wheeled
        103 T-72 | dis 1.1.222.1.0.0.0 | family Tank | folder Platform/Land/Russia/Tank | icon _tank
        104 Technical (HMG) | dis 1.1.0.6.0.0.0 | family WheeledUtility | folder Platform/Land/Small Wheeled Utility Vehicle | icon _wheeled
        105 Technical (ATGM) | dis 1.1.0.6.0.0.0 | family WheeledUtility | folder Platform/Land/Small Wheeled Utility Vehicle | icon _wheeled
        106 Pickup (Toyota) | dis 1.1.0.6.0.0.0 | family WheeledUtility | folder Platform/Land/Small Wheeled Utility Vehicle | icon _wheeled
        107 M109 155 mm self-propelled howitzer | dis 1.1.225.4.1.0.0 | family SpArtillery | folder Platform/Land/United States/Self-Propelled Artillery | icon _tank
        108 Technical (DShK) | dis 1.1.0.6.0.0.0 | family WheeledUtility | folder Platform/Land/Small Wheeled Utility Vehicle | icon _wheeled
        109 Technical (minigun) | dis 1.1.0.6.0.0.0 | family WheeledUtility | folder Platform/Land/Small Wheeled Utility Vehicle | icon _wheeled
        110 Technical (ZU-23-2) | dis 1.1.0.6.0.0.0 | family WheeledUtility | folder Platform/Land/Small Wheeled Utility Vehicle | icon _wheeled
        111 HMMWV (M2, no crew) | dis 1.1.225.6.0.0.0 | family WheeledUtility | folder Platform/Land/United States/Small Wheeled Utility Vehicle | icon _wheeled
        200 Rifleman | dis 3.1.225.1.0.0.0 | family Person | folder Life Form/Land/United States/Infantry (HROT) | icon _person
        201 Officer | dis 3.1.225.1.0.0.0 | family Person | folder Life Form/Land/United States/Infantry (HROT) | icon _person
        202 Rifleman (Stinger) | dis 3.1.225.1.0.0.0 | family Person | folder Life Form/Land/United States/Infantry (HROT) | icon _person
        203 Rifleman (Igla) | dis 3.1.222.1.0.0.0 | family Person | folder Life Form/Land/Russia/Infantry (HROT) | icon _person
        204 Paratrooper | dis 3.1.225.1.0.0.0 | family Person | folder Life Form/Land/United States/Infantry (HROT) | icon _person
        205 SF operator | dis 3.1.225.1.0.0.0 | family Person | folder Life Form/Land/United States/Infantry (HROT) | icon _person
        206 Pilot | dis 3.1.225.1.0.0.0 | family Person | folder Life Form/Land/United States/Infantry (HROT) | icon _person
        207 Automatic rifleman (M249) | dis 3.1.225.1.0.0.0 | family Person | folder Life Form/Land/United States/Infantry (HROT) | icon _person
        208 Machine gunner (PKM) | dis 3.1.222.1.0.0.0 | family Person | folder Life Form/Land/Russia/Infantry (HROT) | icon _person
        209 Sniper (M24) | dis 3.1.225.1.0.0.0 | family Person | folder Life Form/Land/United States/Infantry (HROT) | icon _person
        210 Marksman (M110) | dis 3.1.225.1.0.0.0 | family Person | folder Life Form/Land/United States/Infantry (HROT) | icon _person
        211 Marksman (SVD) | dis 3.1.222.1.0.0.0 | family Person | folder Life Form/Land/Russia/Infantry (HROT) | icon _person
        301 Tank Platoon | dis 11.1.225.2.4.1.0 | family Unit | folder Units/Land/United States/Armor | icon _unit
        302 Infantry Squad | dis 11.1.225.1.2.1.0 | family Unit | folder Units/Land/United States/Infantry | icon _unit
        303 Tank Platoon (Auto Spawn) | dis 11.1.225.2.4.2.0 | family Unit | folder Units/Land/United States/Armor | icon _unit
        304 Artillery Battery | dis 11.1.225.4.5.1.0 | family Unit | folder Units/Land/United States/Artillery | icon _unit
        305 Infantry Platoon | dis 11.1.225.1.4.1.0 | family Unit | folder Units/Land/United States/Infantry | icon _unit
        400 UH-60A Black Hawk | dis 1.2.225.21.0.0.0 | family Helicopter | folder Platform/Air/United States/Utility Helicopter | icon -
        401 F-16C Fighting Falcon | dis 1.2.225.1.0.0.0 | family Jet | folder Platform/Air/United States/Fighter/Air Defense | icon -
        402 C-130H Hercules | dis 1.2.225.4.0.0.0 | family CargoPlane | folder Platform/Air/United States/Cargo/Tanker | icon -
        403 FPV drone | dis 1.2.0.50.0.0.0 | family Drone | folder Platform/Air/Unmanned Air Vehicle | icon -
        404 UH-60A Black Hawk (doors off) | dis 1.2.225.21.0.0.0 | family Helicopter | folder Platform/Air/United States/Utility Helicopter | icon -
        405 A-10C Thunderbolt II | dis 1.2.225.2.0.0.0 | family AttackJet | folder Platform/Air/United States/Attack/Strike | icon -
        406 AH-64 Apache (attack helicopter) | dis 1.2.225.20.0.0.0 | family AttackHelicopter | folder Platform/Air/United States/Attack Helicopter | icon -
        600 M2 HMG | dis 1.1.0.50.1.0.0 | family WeaponSystem | folder Platform/Land/Weapon System (HROT) | icon -
        601 DShK HMG | dis 1.1.0.50.1.0.0 | family WeaponSystem | folder Platform/Land/Weapon System (HROT) | icon -
        602 M134 minigun | dis 1.1.0.50.2.0.0 | family WeaponSystem | folder Platform/Land/Weapon System (HROT) | icon -
        603 Javelin | dis 1.1.0.50.3.0.0 | family WeaponSystem | folder Platform/Land/Weapon System (HROT) | icon -
        604 NLAW | dis 1.1.0.50.4.0.0 | family WeaponSystem | folder Platform/Land/Weapon System (HROT) | icon -
        605 M72 LAW | dis 1.1.0.50.4.0.0 | family WeaponSystem | folder Platform/Land/Weapon System (HROT) | icon -
        606 Kornet (tripod) | dis 1.1.0.50.3.0.0 | family WeaponSystem | folder Platform/Land/Weapon System (HROT) | icon -
        607 TOW (tripod) | dis 1.1.0.50.3.0.0 | family WeaponSystem | folder Platform/Land/Weapon System (HROT) | icon -
        608 Sentry gun | dis 1.1.0.50.5.0.0 | family WeaponSystem | folder Platform/Land/Weapon System (HROT) | icon -
        609 60 mm mortar | dis 1.1.0.10.1.0.0 | family WeaponSystem | folder Platform/Land/Mortar | icon -
        610 81 mm mortar | dis 1.1.0.10.2.0.0 | family WeaponSystem | folder Platform/Land/Mortar | icon -
        611 120 mm mortar | dis 1.1.0.10.3.0.0 | family WeaponSystem | folder Platform/Land/Mortar | icon -
        612 105 mm towed howitzer | dis 1.1.0.5.1.0.0 | family WeaponSystem | folder Platform/Land/Towed Artillery | icon -
        613 Stinger (MANPADS) | dis 1.1.0.50.6.0.0 | family WeaponSystem | folder Platform/Land/Weapon System (HROT) | icon -
        614 Igla (MANPADS) | dis 1.1.0.50.6.0.0 | family WeaponSystem | folder Platform/Land/Weapon System (HROT) | icon -
        615 ZU-23-2 | dis 1.1.0.50.7.0.0 | family WeaponSystem | folder Platform/Land/Weapon System (HROT) | icon -
        616 Rifle (pick-up) | dis 1.1.0.50.8.0.0 | family WeaponSystem | folder Platform/Land/Weapon System (HROT) | icon -
        617 Pistol (pick-up) | dis 1.1.0.50.9.0.0 | family WeaponSystem | folder Platform/Land/Weapon System (HROT) | icon -
        618 Explosive charge | dis 1.1.0.50.10.0.0 | family WeaponSystem | folder Platform/Land/Weapon System (HROT) | icon -
        1002 CivilianCar | dis 1.1.0.81.0.0.0 | family WheeledCar | folder Platform/Land/Car | icon _wheeled
        1050 Hatchback | dis 1.1.0.81.0.0.0 | family WheeledCar | folder Platform/Land/Car | icon _wheeled
        1051 DeliveryVan | dis 1.1.0.81.0.0.0 | family WheeledCar | folder Platform/Land/Car | icon _wheeled
        1052 CityBus | dis 1.1.0.81.0.0.0 | family WheeledCar | folder Platform/Land/Car | icon _wheeled
        1053 BoxTruck | dis 1.1.0.81.0.0.0 | family WheeledCar | folder Platform/Land/Car | icon _wheeled
        1054 Sedan | dis 1.1.0.81.0.0.0 | family WheeledCar | folder Platform/Land/Car | icon _wheeled
        1055 Estate | dis 1.1.0.81.0.0.0 | family WheeledCar | folder Platform/Land/Car | icon _wheeled
        1056 Suv | dis 1.1.0.81.0.0.0 | family WheeledCar | folder Platform/Land/Car | icon _wheeled
        1101 CivilianAdultMale | dis 3.1.0.2.0.0.0 | family Person | folder Life Form/Land/Civilian (HROT) | icon _person
        1102 CivilianAdultFemale | dis 3.1.0.2.0.0.0 | family Person | folder Life Form/Land/Civilian (HROT) | icon _person
        1103 CivilianMinor | dis 3.1.0.2.0.0.0 | family Person | folder Life Form/Land/Civilian (HROT) | icon _person
        1104 CivilianElderly | dis 3.1.0.2.0.0.0 | family Person | folder Life Form/Land/Civilian (HROT) | icon _person
        1201 SecurityShield | dis 3.1.0.3.0.0.0 | family Person | folder Life Form/Land/Security Force (HROT) | icon _person
        1202 SecurityBaton | dis 3.1.0.3.0.0.0 | family Person | folder Life Form/Land/Security Force (HROT) | icon _person
        1203 SecurityLauncher | dis 3.1.0.3.0.0.0 | family Person | folder Life Form/Land/Security Force (HROT) | icon _person
        2001 MilitaryAPC | dis 1.1.0.2.0.0.0 | family Afv | folder Platform/Land/Armored Fighting Vehicle | icon _afv
        2002 InfantrySoldier | dis 3.1.0.1.0.0.0 | family Person | folder Life Form/Land/Infantry (HROT) | icon _person
        2003 Insurgent | dis 3.1.0.1.0.0.0 | family Person | folder Life Form/Land/Infantry (HROT) | icon _person
        2004 Grenadier | dis 3.1.0.1.0.0.0 | family Person | folder Life Form/Land/Infantry (HROT) | icon _person
        2005 MortarTeam | dis 3.1.0.1.0.0.0 | family Person | folder Life Form/Land/Infantry (HROT) | icon _person
        2006 JavelinTeam | dis 3.1.0.1.0.0.0 | family Person | folder Life Form/Land/Infantry (HROT) | icon _person
        2007 NlawGunner | dis 3.1.0.1.0.0.0 | family Person | folder Life Form/Land/Infantry (HROT) | icon _person
        2008 UnarmedFighter | dis 3.1.0.1.0.0.0 | family Person | folder Life Form/Land/Infantry (HROT) | icon _person
        2009 AssaultRifleman | dis 3.1.0.1.0.0.0 | family Person | folder Life Form/Land/Infantry (HROT) | icon _person
        8801 TacGraphic_FireLine | dis - | family Unknown | folder Other | icon -
        8802 TacGraphic_Route | dis - | family Unknown | folder Other | icon -
        8803 TacGraphic_Area | dis - | family Unknown | folder Other | icon -
        8805 Door | dis - | family Unknown | folder Other | icon -
        8806 Car | dis 1.1.0.81.0.0.0 | family WheeledCar | folder Platform/Land/Car | icon _wheeled
        8807 Sandbag wall | dis 5.0.0.0.0.0.0 | family Unknown | folder Cultural Feature | icon -
        8808 Concrete block | dis 5.0.0.0.0.0.0 | family Unknown | folder Cultural Feature | icon -
        8809 Crate | dis 5.0.0.0.0.0.0 | family Unknown | folder Cultural Feature | icon -
        8810 World Environment | dis - | family Unknown | folder Other | icon -
        8811 Weather Region | dis - | family Unknown | folder Other | icon -
        8812 Stone Source | dis - | family Unknown | folder Other | icon -
        8850 Traffic | dis - | family Unknown | folder Other | icon -
        8851 SUMO car | dis 1.1.0.81.0.0.0 | family WheeledCar | folder Platform/Land/Car | icon _wheeled
        """;

    /// <summary>⭐ The DIS names as <c>DisNames.json</c> held them — the class table must name exactly these levels; ⭐ C3 adds the
    /// unit Kind (11) and its arms, nothing else. ⭐ Weapon systems WS-1 adds the land class 1.1.*.50 (DESIGN_Weapon_Systems §5),
    /// deliberately.</summary>
    private const string DisNamesGolden = """
        1 Platform
        1.1 Land
        1.1.*.1 Tank
        1.1.*.2 Armored Fighting Vehicle
        1.1.*.3 Armored Utility Vehicle
        1.1.*.4 Self-Propelled Artillery
        1.1.*.5 Towed Artillery
        1.1.*.6 Small Wheeled Utility Vehicle
        1.1.*.7 Large Wheeled Utility Vehicle
        1.1.*.8 Small Tracked Utility Vehicle
        1.1.*.9 Large Tracked Utility Vehicle
        1.1.*.10 Mortar
        1.1.*.50 Weapon System (HROT)
        1.1.*.81 Car
        1.2 Air
        1.2.*.1 Fighter/Air Defense
        1.2.*.2 Attack/Strike
        1.2.*.3 Bomber
        1.2.*.4 Cargo/Tanker
        1.2.*.5 ASW/Patrol/Observation
        1.2.*.6 Electronic Warfare
        1.2.*.7 Reconnaissance
        1.2.*.8 Surveillance/C2 (AEW)
        1.2.*.20 Attack Helicopter
        1.2.*.21 Utility Helicopter
        1.2.*.22 ASW/Patrol Helicopter
        1.2.*.23 Cargo Helicopter
        1.2.*.24 Observation Helicopter
        1.2.*.25 Special Operations Helicopter
        1.2.*.40 Trainer
        1.2.*.47 Light/Patrol Craft
        1.2.*.50 Unmanned Air Vehicle
        1.2.*.57 Non-Combatant Commercial Aircraft
        1.3 Surface
        1.4 Subsurface
        1.5 Space
        2 Munition
        3 Life Form
        3.1 Land
        3.1.*.1 Infantry (HROT)
        3.1.*.2 Civilian (HROT)
        3.1.*.3 Security Force (HROT)
        3.2 Air
        3.3 Surface
        3.4 Subsurface
        4 Environmental
        5 Cultural Feature
        6 Supply
        7 Radio
        8 Expendable
        9 Sensor/Emitter
        11 Units
        11.1 Land
        11.1.*.1 Infantry
        11.1.*.2 Armor
        11.1.*.3 Mechanized Infantry
        11.1.*.4 Artillery
        11.1.*.5 Reconnaissance
        country 71 France
        country 78 Germany
        country 222 Russia
        country 224 United Kingdom
        country 225 United States
        """;

    [Fact]
    public void CE2138_EveryBuiltInType_KeepsIts3dFamily_PickerFolder_AndIcon()
        => Assert.Equal(BuiltInGolden, BuiltInSnapshot(BuiltIn()));

    [Fact]
    public void CE2138_TheDisNames_AreUnchanged_ByTheMoveIntoCode()
        => Assert.Equal(DisNamesGolden, DisNamesSnapshot(DisNameTable.Default));
}
