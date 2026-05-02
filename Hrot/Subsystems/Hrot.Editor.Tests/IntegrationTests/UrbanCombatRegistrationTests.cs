using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Tkb;
using Fdp.Examples.Scenarios.Integrated;
using Xunit;

namespace Hrot.Editor.Tests.IntegrationTests;

/// <summary>
/// Integration tests verifying that the urban-combat TKB blueprints and behaviors
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

    // ── Behavior IDs ─────────────────────────────────────────────────────────

    private const int BehaviorWanderCivil    = 1001;
    private const int BehaviorConvoyEscort   = 2001;
    private const int BehaviorInfantryCombat = 2002;
    private const int BehaviorAmbush         = 2003;

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
    // Behavior registration
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// All four UrbanCombat behavior IDs must be findable in the registry after
    /// <see cref="UrbanCombatNewScenario.RegisterUrbanCombatBehaviors"/> is called.
    /// </summary>
    [Theory]
    [InlineData(BehaviorWanderCivil,    "WanderCivil")]
    [InlineData(BehaviorConvoyEscort,   "ConvoyEscort")]
    [InlineData(BehaviorInfantryCombat, "InfantryCombat")]
    [InlineData(BehaviorAmbush,         "Ambush")]
    public void RegisterUrbanCombatBehaviors_RegistersAllFourBehaviors(int id, string expectedName)
    {
        var registry = new BehaviorRegistry();

        UrbanCombatNewScenario.RegisterUrbanCombatBehaviors(registry);

        Assert.True(registry.TryGetDefinition(id, out _),
            $"Behavior id={id} ({expectedName}) was not registered.");
    }

    /// <summary>
    /// Each registered behavior must resolve its name to the expected integer ID.
    /// </summary>
    [Theory]
    [InlineData("WanderCivil",    BehaviorWanderCivil)]
    [InlineData("ConvoyEscort",   BehaviorConvoyEscort)]
    [InlineData("InfantryCombat", BehaviorInfantryCombat)]
    [InlineData("Ambush",         BehaviorAmbush)]
    public void RegisterUrbanCombatBehaviors_NameResolvesToCorrectId(string name, int expectedId)
    {
        var registry = new BehaviorRegistry();

        UrbanCombatNewScenario.RegisterUrbanCombatBehaviors(registry);

        Assert.True(registry.TryGetId(name, out int id),
            $"Behavior '{name}' could not be resolved to an ID.");
        Assert.Equal(expectedId, id);
    }
}
