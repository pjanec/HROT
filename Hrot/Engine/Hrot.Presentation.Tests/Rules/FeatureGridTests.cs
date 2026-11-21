using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Toolkit.Orchestration;
using Hrot.Presentation.Windows;
using Xunit;

namespace Hrot.Presentation.Tests.Rules;

/// <summary>
/// ⭐ CE-4247 (rules runnable ⑨c; DESIGN_Node_Features F-11 / S2) — the operator's feature grid: nodes × features with availability, state and source; a toggle is a REPLACEMENT list of cluster overrides that keeps the meaning of every other entry.
/// </summary>
public sealed class FeatureGridTests
{
    private static HashSet<string> S(params string[] ids) => ids.ToHashSet();
    private static readonly FeatureGridNode Player = new("Player-1", "Player", S("hud.kill-feed", "hud.coaching"), S("hud.kill-feed"));
    private static readonly FeatureGridNode Editor = new("Editor", "Editor", S("hud.kill-feed", "gm.palette"), S("gm.palette", "hud.kill-feed"));
    private static readonly FeatureGridNode[] Nodes = { Player, Editor };

    /// <summary>The grid lists every advertised feature once; a node that does not advertise it is NotAvailable; an override naming the node (or all) is the source. ✅ Red-proof: availability ignored ⇒ gm.palette reads Off on the Player ⇒ red.</summary>
    [Fact]
    public void TheGrid_ShowsAvailability_State_AndSource()
    {
        var rows = FeatureGridModel.Build(Nodes, new[] { new FeatureOverrideSpec("hud.coaching", new[] { "Player-1" }, "{\"Enabled\":false}"), new FeatureOverrideSpec("hud.kill-feed", Array.Empty<string>(), "{\"Enabled\":true}") });
        Assert.Equal(new[] { "gm.palette", "hud.coaching", "hud.kill-feed" }, rows.Select(r => r.FeatureId));
        Assert.Equal(FeatureCellState.NotAvailable, rows[0].Cells[0].State);                       // gm.palette: the Player does not advertise it
        Assert.Equal((FeatureCellState.On, FeatureGridModel.DefaultSource), (rows[0].Cells[1].State, rows[0].Cells[1].Source));
        Assert.Equal((FeatureCellState.Off, FeatureGridModel.OverrideSource), (rows[1].Cells[0].State, rows[1].Cells[0].Source));   // named override
        Assert.Equal(new[] { FeatureGridModel.OverrideSource, FeatureGridModel.OverrideSource }, rows[2].Cells.Select(c => c.Source));   // an all-nodes override
    }

    /// <summary>A toggle sends ONE entry for the node, splits an all-nodes entry across the others, drops the node from other named entries, leaves other features alone. ✅ Red-proof: the all-nodes entry kept as is ⇒ it also re-applies to the toggled node ⇒ red.</summary>
    [Fact]
    public void ATogglePutsOneEntryForTheNode_AndKeepsTheMeaningOfTheOthers()
    {
        var all = new FeatureOverrideSpec("hud.kill-feed", Array.Empty<string>(), "{\"Enabled\":false}");
        var other = new FeatureOverrideSpec("gm.palette", new[] { "Editor" }, "{\"Enabled\":false}");
        var list = FeatureGridModel.Toggled(new[] { all, other }, Nodes, "hud.kill-feed", "Player-1", turnOn: true);
        Assert.Equal(3, list.Length);
        Assert.Contains(other, list);
        Assert.Contains(list, o => o.Feature == "hud.kill-feed" && o.Nodes.SequenceEqual(new[] { "Editor" }) && o.Json == "{\"Enabled\":false}");   // everyone else keeps "off"
        Assert.Contains(list, o => o.Feature == "hud.kill-feed" && o.Nodes.SequenceEqual(new[] { "Player-1" }) && o.Json == "{\"Enabled\":true}");
        Assert.Equal(1, FeatureGridModel.Toggled(list, Nodes, "hud.kill-feed", "Player-1", turnOn: false).Count(o => o.Feature == "hud.kill-feed" && o.Nodes.Contains("Player-1")));   // toggling again replaces, never stacks
    }

    /// <summary>The window's Set sends the new list for an advertised feature and refuses one the node does not advertise. ✅ Red-proof: the availability check dropped ⇒ the Player's gm.palette is sent ⇒ red.</summary>
    [Fact]
    public void TheWindow_SendsTheReplacementList_AndRefusesAFeatureTheNodeLacks()
    {
        FeatureOverrideSpec[]? sent = null;
        var w = new FeatureGridWindow("grid", "Scenario", default, () => Nodes, () => Array.Empty<FeatureOverrideSpec>(), l => sent = l);
        Assert.True(w.Set("hud.kill-feed", "Player-1", false));
        Assert.Equal(new[] { "hud.kill-feed" }, sent!.Select(o => o.Feature));
        Assert.Equal("{\"Enabled\":false}", sent![0].Json);
        sent = null;
        Assert.False(w.Set("gm.palette", "Player-1", true));
        Assert.Null(sent);
        w.SimulateDrawClientArea();
        Assert.Equal(new[] { "gm.palette: - | on", "hud.coaching: off | -", "hud.kill-feed: on | on" }, w.Probe.Rows);
    }
}
