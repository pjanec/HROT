using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐ U-18 / RX2 (docs/UX/DESIGN_Rule_Editor_UX.md §2.2.1) — the <b>Rule</b> Details tab: offered for ONE selected rule entity of the host's Rules window, not for a
/// unit; drawing it draws THROUGH the window (the window's selection follows) and the first ask attaches the window (its pane goes, a pick selects the rule).
/// 🔴 Red-proof (compiles): <c>RuleDetailsTabDescriptor</c>'s AppliesTo without <c>h.Offers</c> (always true) ⇒ the unit is offered ⇒ red.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TheRuleDetailsTabTests
{
    [SkippableFact]
    public void U18_RX2_TheRuleTab_IsOfferedForARule_NotForAUnit_AndDrawsThroughTheWindow()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var world = new EntityRepository();
        world.RegisterComponent<NetworkIdentity>(); world.RegisterComponent<ObjectiveStatus>(); world.RegisterComponent<ObjectiveLinks>();
        world.RegisterManagedComponent<Fdp.Toolkit.Behavior.Components.BehaviorStartRecord>();
        var rule = world.CreateEntity();
        world.AddComponent(rule, new NetworkIdentity(10));
        world.SetManagedComponent(rule, new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { BehaviorName = "WeightedMean", JsonParams = """{"common":{"title":"Mission","subject":900}}""", InstanceId = 1 });
        var unit = world.CreateEntity();
        world.AddComponent(unit, new NetworkIdentity(900));

        var picked = new List<int>();
        var window = new Hrot.Presentation.Windows.RulesWindow(() => world, () => null, null, "rx2_rules", "Scenario", System.Numerics.Vector4.One, picked.Add, _ => { });
        var desc = RuleDetailsTabDescriptor.For(() => window);
        DetailsContext Ctx(Entity e) => new(Hrot.Editor.AiShared.Selection.SelectionOrigin.Unknown, Array.Empty<Hrot.Editor.AiShared.Selection.IAssetSubSelection>(), new[] { e }, null, "Scenario", VariableRunState.Planning);

        using var frame = UiFrameHarness.Begin(700, 420);
        bool forRule = false, forUnit = true;
        frame.StepN(3, () =>
        {
            forRule = desc.AppliesTo(Ctx(rule));
            forUnit = desc.AppliesTo(Ctx(unit));
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10)); ImGui.SetNextWindowSize(new System.Numerics.Vector2(460, 300));
            ImGui.Begin("Details##rx2");
            if (forRule) desc.Create().Draw(Ctx(rule), "rx2");
            ImGui.End();
        });
        Assert.True(forRule);
        Assert.False(forUnit);
        Assert.True(window.DetailsInPanel);              // the first ask attached the window
        Assert.Equal(10, window.SelectedNetworkId);      // the tab drew through the window: its selection follows
        Assert.Contains(window.Probe.Details, d => d.Contains("Authored"));
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "rule-details-tab.png"));
    }
}
