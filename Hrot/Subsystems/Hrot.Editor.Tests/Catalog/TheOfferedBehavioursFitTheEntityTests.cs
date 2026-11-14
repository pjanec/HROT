using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.Common.Events;
using Hrot.Map.Common.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.CGF.Configuration;
using Hrot.Core;
using Hrot.Core.Mission;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Hrot.UI.Common.AddEntity;
using Hrot.UI.Common.Adapters;
using Hrot.NED.Common;
using Xunit;

namespace Hrot.Editor.Tests.Catalog;

/// <summary>
/// ⭐⭐ <c>CE-4350</c> (docs/DESIGN_Orders_Params_Entity_Classes.md §14) — the behaviours the mission editor's "+ Add task" OFFERS fit the entity's
/// class. 🔒 User report: <i>the entity mission plan editor offers tasks / behaviours that do not fit the entity's type.</i>
/// <para>THE GOLDEN (<c>OfferedBehaviours.golden.txt</c>): for every built-in TKB type the picker lists (plus the two body-less rules types), the list the
/// service offered BEFORE the filter (recorded at HEAD, kept verbatim) and the list it offers NOW. The test compares NOW with the file;
/// <c>HROT_GOLDEN_UPDATE=1</c> rewrites the NOW sections (never BEFORE). Every difference between the two is named in the design's §14 AS-BUILT.</para>
/// </summary>
public sealed class TheOfferedBehavioursFitTheEntityTests : IDisposable
{
    private static string GoldenPath([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "OfferedBehaviours.golden.txt");

    private readonly EntityRepository _repo = new();
    private readonly BehaviorRegistry _registry = new();
    private readonly TkbDatabase _tkb = HrotEnvironment.CreateTkb();

    public TheOfferedBehavioursFitTheEntityTests()
    {
        _repo.RegisterComponent<TkbIdentity>();
        _repo.RegisterComponent<NetworkIdentity>();
        _repo.RegisterManagedComponent<ActiveMissionPlan>();
        _repo.RegisterEvent<MissionControlAckEvent>();
        CgfBehaviorSetup.LoadFromAiAssembly(_registry);   // the production loader: every curated behaviour of every technology
    }

    public void Dispose() => _repo.Dispose();

    private int _next = 1000;

    private int Spawn(long tkbType, DISEntityType dis)
    {
        var e = _repo.CreateEntity();
        int net = _next++;
        _repo.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)tkbType });
        _repo.AddComponent(e, new NetworkIdentity { Value = net });
        _repo.SetDisType(e, dis);
        return net;
    }

    private IReadOnlyList<string> Offered(long tkbType, DISEntityType dis)
        => new ScenarioMissionService(_repo.Bus, _repo, _registry).GetAvailableBehaviorChoices(Spawn(tkbType, dis))
            .Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    private IReadOnlyList<string> OfferedByTkb(long tkbType)
    {
        Assert.True(_tkb.TryGetByType((TkbTypeId)tkbType, out var t), $"TKB type {tkbType} is not built in");
        return Offered(tkbType, t.DisType);
    }

    /// <summary>The golden's rows: every picker type, then the rules types. (id, name, dis text)</summary>
    private IEnumerable<(long Id, string Name, DISEntityType Dis)> Types()
    {
        var seen = new HashSet<long>();
        foreach (var e in EntityTypeCatalog.Build(_tkb).Where(e => e.IsEnabled && e.HasSide && e.MacroPath is null))
            if (seen.Add((long)e.TkbType) && _tkb.TryGetByType(e.TkbType, out var t)) yield return ((long)e.TkbType, t.Name, t.DisType);
        foreach (long id in new[] { TkbEntityTypes.Objective, TkbEntityTypes.Trigger })
            if (seen.Add(id) && _tkb.TryGetByType((TkbTypeId)id, out var t)) yield return (id, t.Name, t.DisType);
    }

    private string Render(Dictionary<string, string> keepBefore)
    {
        var sb = new StringBuilder();
        sb.Append("# CE-4350 golden — what \"+ Add task\" offers each built-in TKB type (behaviours; orders are a separate, already class-based list).\n");
        sb.Append("# before = recorded at HEAD before the class filter, kept verbatim · after = the current code (HROT_GOLDEN_UPDATE=1 rewrites it).\n\n");
        foreach (var (id, name, dis) in Types().OrderBy(t => t.Id))
        {
            string key = id + " " + name;
            string now = string.Join(",", Offered(id, dis));
            sb.Append("## ").Append(key).Append("  DIS ").Append(dis.Value != 0 ? dis.ToString() : "none").Append('\n');
            sb.Append("before: ").Append(keepBefore.TryGetValue(key, out var b) ? b : now).Append('\n');
            sb.Append("after: ").Append(now).Append("\n\n");
        }
        return sb.ToString();
    }

    private static Dictionary<string, string> BeforeOf(string path)
    {
        var d = new Dictionary<string, string>();
        if (!File.Exists(path)) return d;
        string? key = null;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("## ")) { int dis = line.IndexOf("  DIS ", StringComparison.Ordinal); key = line.Substring(3, dis - 3); }
            else if (line.StartsWith("before: ") && key != null) d[key] = line.Substring(8);
        }
        return d;
    }

    [Fact]
    public void CE4350_EveryBuiltInType_IsOfferedExactlyTheGoldenList()
    {
        string path = GoldenPath();
        string text = Render(BeforeOf(path));
        if (Environment.GetEnvironmentVariable("HROT_GOLDEN_UPDATE") == "1") { File.WriteAllText(path, text); return; }
        Assert.True(File.Exists(path), "no golden yet — run once with HROT_GOLDEN_UPDATE=1");
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), text);
    }

    // ── the targeted rails: the user's report, one entity at a time ─────────────────────────────────────────────────────────────

    [Fact]
    public void CE4350_ATank_IsNotOfferedAnInfantryDrill_ButItsOwnVehicleBehaviours()
    {
        var tank = OfferedByTkb(TkbEntityTypes.Tank_M1Abrams);
        Assert.DoesNotContain("SquadMarch", tank);
        Assert.DoesNotContain("InfantrySquadMove", tank);
        Assert.DoesNotContain("PersonMove", tank);
        Assert.DoesNotContain("CivilianHsm", tank);
        Assert.Contains("PlatformMove", tank);
        Assert.Contains("VehicleFightFromCover", tank);
    }

    [Fact]
    public void CE4350_AnInfantrySquad_IsNotOfferedAVehicleBehaviour_ButItsOwnDrills()
    {
        var squad = OfferedByTkb(TkbEntityTypes.Unit_InfantrySquad);
        Assert.DoesNotContain("PlatformMove", squad);
        Assert.DoesNotContain("VehicleFightFromCover", squad);
        Assert.DoesNotContain("PersonMove", squad);
        Assert.Contains("InfantrySquadMove", squad);
        Assert.Contains("SquadMarch", squad);
        // the tank PLATOON is a unit too, but is not offered the infantry squad's own order behaviours (the class prefix names InfantrySquad)
        Assert.DoesNotContain("InfantrySquadMove", OfferedByTkb(TkbEntityTypes.Unit_TankPlatoon));
    }

    [Fact]
    public void CE4350_ARifleman_IsOfferedPersonMove_AndNotPlatformMove()
    {
        var man = OfferedByTkb(TkbEntityTypes.Infantry_Rifleman);
        Assert.Contains("PersonMove", man);
        Assert.DoesNotContain("PlatformMove", man);
        Assert.DoesNotContain("SquadMarch", man);
        Assert.DoesNotContain("WanderMilitary", man);
    }

    [Fact]
    public void CE4350_ACivilianIsOfferedTheCivilianBrain_AndASoldierIsNot()
    {
        Assert.Contains("CivilianHsm", OfferedByTkb(CivilianTkbCatalog.TkbCivilianAdultMale));
        Assert.DoesNotContain("CivilianHsm", OfferedByTkb(TkbEntityTypes.Infantry_Rifleman));
    }

    [Fact]
    public void CE4350_AnAircraftKeepsEject_AndAGroundVehicleDoesNot()
    {
        Assert.Contains("Eject", OfferedByTkb(TkbEntityTypes.Jet_AttackA10));
        Assert.DoesNotContain("Eject", OfferedByTkb(TkbEntityTypes.Tank_M1Abrams));
        Assert.Contains("MoveToLocation", OfferedByTkb(TkbEntityTypes.Heli_UH60));   // AllMilitary was offered to a helicopter before and still is
    }

    [Fact]
    public void CE4350_AnObjectiveAndATrigger_GetTheirOwnBrains_NotTheUnitsBehaviours()
    {
        var objective = OfferedByTkb(TkbEntityTypes.Objective);
        Assert.Contains("ReachArea", objective);
        Assert.DoesNotContain("PersonMove", objective);
        Assert.DoesNotContain("Trigger", objective);
        var trigger = OfferedByTkb(TkbEntityTypes.Trigger);
        Assert.Contains("Trigger", trigger);
        Assert.DoesNotContain("ReachArea", trigger);
    }

    /// <summary>§14 rule 2 — an authored asset (a registered behaviour no <c>[Behavior]</c> declares) is offered to EVERY entity: it cannot declare a class yet, and hiding it
    /// would take away what the user wrote. 🔴 Red-proof: have <c>BehaviorFit</c> answer false for an undeclared name ⇒ this fails for every type.</summary>
    [Fact]
    public void CE4350_AnAuthoredAsset_IsOfferedToEveryBuiltInType()
    {
        _registry.Register(9001, "MyAuthoredTree", new BehaviorDefinition { Name = "MyAuthoredTree", BrainTier = BehaviorConstants.BrainTierBTree });
        _registry.Register(9002, "MyAuthoredHsm", new BehaviorDefinition { Name = "MyAuthoredHsm", BrainTier = BehaviorConstants.BrainTierHsm });
        foreach (var (id, name, dis) in Types())
        {
            var offered = Offered(id, dis);
            Assert.True(offered.Contains("MyAuthoredTree") && offered.Contains("MyAuthoredHsm"), $"{id} {name} lost an authored asset");
        }
    }

    /// <summary>The golden's invariant, stated: the filter only ever removes DECLARED (curated) behaviours — never an authored one.</summary>
    [Fact]
    public void CE4350_TheFilterNeverRemovesAnUndeclaredBehaviour()
    {
        var declared = Hrot.Map.Definitions.Behavior.ParamsDtoRegistry.Default;
        var everything = _registry.GetRegisteredNames().Where(n => !declared.TryGetBehavior(n, out _)).ToList();
        Assert.NotEmpty(everything);   // the demo trees are registered and undeclared
        foreach (var (id, name, dis) in Types())
        {
            var offered = Offered(id, dis);
            foreach (var n in everything)
            {
                if (_registry.TryGetId(n, out int bid) && _registry.TryGetDefinition(bid, out var d) && d.CarriesSopOrders) continue;   // an SOP is never a task
                Assert.True(offered.Contains(n), $"{id} {name} lost the undeclared '{n}'");
            }
        }
    }
}
