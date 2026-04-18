using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Tkb;
using Fdp.Examples.Scenarios.Integrated;
using Xunit;

namespace Hrot.Editor.Tests.IntegrationTests;

/// <summary>
/// Integration tests verifying that the urban-combat TKB blueprints and doctrines
/// are correctly registered by <see cref="UrbanCombatNewScenario"/> helper methods.
///
/// These tests exercise the static registration path used by <c>EditorSubsystem</c>
/// (tasks DEM2-1.1 and DEM2-1.2) without standing up the full editor kernel.
/// </summary>
public sealed class UrbanCombatRegistrationTests
{
    // ── TKB IDs ───────────────────────────────────────────────────────────────

    private const long TkbCivilianPedestrian = 1001L;
    private const long TkbCivilianCar        = 1002L;
    private const long TkbMilitaryApc        = 2001L;
    private const long TkbInfantrySoldier    = 2002L;
    private const long TkbInsurgent          = 2003L;

    // ── Doctrine IDs ─────────────────────────────────────────────────────────

    private const int DoctrineWanderCivil    = 1001;
    private const int DoctrineConvoyEscort   = 2001;
    private const int DoctrineInfantryCombat = 2002;
    private const int DoctrineAmbush         = 2003;

    // ═════════════════════════════════════════════════════════════════════════
    // TKB registration
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// All five UrbanCombat TKB type IDs must be findable in the database after
    /// <see cref="UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates"/> is called.
    /// </summary>
    [Theory]
    [InlineData(TkbCivilianPedestrian)]
    [InlineData(TkbCivilianCar)]
    [InlineData(TkbMilitaryApc)]
    [InlineData(TkbInfantrySoldier)]
    [InlineData(TkbInsurgent)]
    public void RegisterUrbanCombatTkbTemplates_RegistersAllFiveTypes(long tkbType)
    {
        ITkbDatabase db = new TkbDatabase();

        UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(db);

        Assert.True(db.TryGetByType(tkbType, out _),
            $"TKB type {tkbType} was not registered.");
    }

    /// <summary>
    /// Each registered TKB template must have the expected human-readable name.
    /// </summary>
    [Theory]
    [InlineData(TkbCivilianPedestrian, "CivilianPedestrian")]
    [InlineData(TkbCivilianCar,        "CivilianCar")]
    [InlineData(TkbMilitaryApc,        "MilitaryAPC")]
    [InlineData(TkbInfantrySoldier,    "InfantrySoldier")]
    [InlineData(TkbInsurgent,          "Insurgent")]
    public void RegisterUrbanCombatTkbTemplates_TemplateNamesMatch(long tkbType, string expectedName)
    {
        ITkbDatabase db = new TkbDatabase();

        UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(db);

        db.TryGetByType(tkbType, out var template);
        Assert.Equal(expectedName, template.Name);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Doctrine registration
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// All four UrbanCombat doctrine IDs must be findable in the registry after
    /// <see cref="UrbanCombatNewScenario.RegisterUrbanCombatDoctrines"/> is called.
    /// </summary>
    [Theory]
    [InlineData(DoctrineWanderCivil,    "WanderCivil")]
    [InlineData(DoctrineConvoyEscort,   "ConvoyEscort")]
    [InlineData(DoctrineInfantryCombat, "InfantryCombat")]
    [InlineData(DoctrineAmbush,         "Ambush")]
    public void RegisterUrbanCombatDoctrines_RegistersAllFourDoctrines(int id, string expectedName)
    {
        var registry = new DoctrineRegistry();

        UrbanCombatNewScenario.RegisterUrbanCombatDoctrines(registry);

        Assert.True(registry.TryGetDefinition(id, out _),
            $"Doctrine id={id} ({expectedName}) was not registered.");
    }

    /// <summary>
    /// Each registered doctrine must resolve its name to the expected integer ID.
    /// </summary>
    [Theory]
    [InlineData("WanderCivil",    DoctrineWanderCivil)]
    [InlineData("ConvoyEscort",   DoctrineConvoyEscort)]
    [InlineData("InfantryCombat", DoctrineInfantryCombat)]
    [InlineData("Ambush",         DoctrineAmbush)]
    public void RegisterUrbanCombatDoctrines_NameResolvesToCorrectId(string name, int expectedId)
    {
        var registry = new DoctrineRegistry();

        UrbanCombatNewScenario.RegisterUrbanCombatDoctrines(registry);

        Assert.True(registry.TryGetId(name, out int id),
            $"Doctrine '{name}' could not be resolved to an ID.");
        Assert.Equal(expectedId, id);
    }
}
