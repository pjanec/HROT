using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb;
using Hrot.CGF.Configuration;
using Hrot.Common.Events;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.UiFrameRail;
using Hrot.Map.Common;
using Hrot.Map.Common.Events;
using Hrot.UI.Common.Adapters;
using Xunit;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐⭐ CE-4350 (docs/DESIGN_Orders_Params_Entity_Classes.md §14) — a REAL ImGui frame of the Orders tab with its <b>+ Add task</b> popup open, over the
/// production behaviour registry and the real <c>ScenarioMissionService</c>: the entries it lists are the ones that FIT the picked entity — a tank is offered
/// the vehicle behaviours and not the infantry drills, a squad the drills and not the vehicle ones. Under <c>xvfb-run -a</c>; skipped without a display —
/// ⛔ a skip is not a gate row. 🔴 Red-proof: have <c>ScenarioMissionService.FittingBehaviours</c> keep every behaviour ⇒ the tank lists <c>SquadMarch</c>.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheOrdersAddTaskPopupFitsTheEntityTests : IDisposable
{
    private readonly EntityRepository _repo = new();
    private readonly BehaviorRegistry _registry = new();
    private readonly TkbDatabase _tkb = Hrot.Map.Common.HrotEnvironment.CreateTkb();

    public TheOrdersAddTaskPopupFitsTheEntityTests()
    {
        _repo.RegisterComponent<TkbIdentity>();
        _repo.RegisterComponent<NetworkIdentity>();
        _repo.RegisterManagedComponent<ActiveMissionPlan>();
        _repo.RegisterEvent<MissionControlAckEvent>();
        CgfBehaviorSetup.LoadFromAiAssembly(_registry);
    }

    public void Dispose() => _repo.Dispose();

    private IReadOnlyList<string> PopupOf(long tkbType)
    {
        Assert.True(_tkb.TryGetByType((TkbTypeId)tkbType, out var t));
        var e = _repo.CreateEntity();
        _repo.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)tkbType });
        _repo.AddComponent(e, new NetworkIdentity { Value = 4000 + e.Index });
        _repo.SetDisType(e, t.DisType);
        var svc = new ScenarioMissionService(_repo.Bus, _repo, _registry);
        var tab = new OrdersTab(() => svc, () => _repo);
        var ctx = new DetailsContext(SelectionOrigin.Unknown, Array.Empty<IAssetSubSelection>(), new[] { e }, null, "Scenario", VariableRunState.Planning);
        using var frame = UiFrameHarness.Begin();
        bool opened = false;
        frame.StepN(4, () =>
        {
            ImGuiNET.ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
            ImGuiNET.ImGui.SetNextWindowSize(new Vector2(620, 480), ImGuiNET.ImGuiCond.Always);
            ImGuiNET.ImGui.Begin("Orders (CE-4350)");
            if (!opened) { ImGuiNET.ImGui.OpenPopup("orders_add"); opened = true; }
            tab.Draw(ctx, "t");
            ImGuiNET.ImGui.End();
        });
        return tab.LastAddMenu.ToList();
    }

    [SkippableFact]
    public void CE4350_ATanksPopup_ListsVehicleBehaviours_NotTheInfantryDrills()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var menu = PopupOf(TkbEntityTypes.Tank_M1Abrams);
        Assert.Equal("Wait", menu[0]);
        Assert.Contains("VehicleFightFromCover", menu);
        Assert.Contains("PlatformMove", menu);
        Assert.DoesNotContain("SquadMarch", menu);
        Assert.DoesNotContain("InfantrySquadMove", menu);
        Assert.DoesNotContain("PersonMove", menu);
        Assert.Contains("Order.Move", menu);   // the orders list was already by class (CE-2143)
    }

    [SkippableFact]
    public void CE4350_ASquadsPopup_ListsItsDrills_NotTheVehicleBehaviours()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var menu = PopupOf(TkbEntityTypes.Unit_InfantrySquad);
        Assert.Contains("SquadMarch", menu);
        Assert.Contains("InfantrySquadMove", menu);
        Assert.DoesNotContain("VehicleFightFromCover", menu);
        Assert.DoesNotContain("PlatformMove", menu);
        Assert.DoesNotContain("WanderMilitary", menu);
    }
}
