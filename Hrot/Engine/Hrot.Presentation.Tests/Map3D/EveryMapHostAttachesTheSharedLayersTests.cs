using Fdp.Testing;
using System.Text.RegularExpressions;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐⭐⭐ CE-1033 — "not on every host" cannot happen BY CONSTRUCTION. 🔒 User, 2026-10-10: <i>"we should be unifying and sharing
/// from the day zero so something like 'not on all host' can not happen by construction."</i>
/// <para>① every host that builds the map machinery (<c>MapInteractionPack.Build(</c>) also attaches the shared layers
/// (<c>MapInteractionPack.AttachMapLayers(</c>) — which is where the gizmo layer, the 3-D layers and the 2-D ↔ 3-D switch come
/// from; ② nothing outside the pack builds those pieces itself, so a host cannot grow a private variant.</para>
/// 📄 docs/DESIGN_Map_3D_Mode.md §6b.
/// </summary>
public sealed class EveryMapHostAttachesTheSharedLayersTests
{
    private static readonly string[] ProductionTrees = { "Hrot", "Stride" };
    private const string PackFile = "Hrot/Engine/Hrot.Presentation/ScenarioEditor/Map/MapInteractionPack.cs";

    private static IEnumerable<(string Rel, string Text)> ProductionFiles()
    {
        var root = TestRepo.Root;
        foreach (var tree in ProductionTrees)
        {
            var dir = Path.Combine(root, tree);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.Contains(".Tests/") || rel.Contains(".Tests.")
                    || rel.Contains("/Examples/")) continue;
                // Code only: a doc comment that NAMES the pack is not a host building a map.
                var code = string.Join('\n', File.ReadLines(f).Where(l => !l.TrimStart().StartsWith("//")));
                yield return (rel, code);
            }
        }
    }

    /// <summary>The subsystem a file belongs to: <c>Hrot/Subsystems/Hrot.SimHost/…</c> ⇒ <c>Hrot/Subsystems/Hrot.SimHost</c>.</summary>
    private static string HostOf(string rel)
    {
        var parts = rel.Split('/');
        return parts.Length >= 3 ? string.Join('/', parts.Take(3)) : rel;
    }

    [Fact]
    public void EveryHostThatBuildsTheMap_AttachesTheSharedLayers()
    {
        var files = ProductionFiles().ToList();
        var builders = files.Where(f => f.Text.Contains("MapInteractionPack.Build(")).Select(f => HostOf(f.Rel)).Distinct().ToList();
        var attachers = files.Where(f => f.Text.Contains("MapInteractionPack.AttachMapLayers(")).Select(f => HostOf(f.Rel)).ToHashSet();

        Assert.True(builders.Count >= 5, "anti-vacuity: expected the five map hosts, found " + string.Join(", ", builders));
        var missing = builders.Where(h => !attachers.Contains(h)).ToList();
        Assert.True(missing.Count == 0, "map hosts that do not attach the shared layers (no 3-D, no shared gizmo layer): "
                                        + string.Join(", ", missing));
    }

    /// <summary>
    /// ⭐ <c>CE-1289</c> (+ <c>CE-3151</c>) — every host that attaches the shared map layers (and so gets the cursor's reading on its canvas)
    /// also shows it: it registers the status-bar section. ⛔ Red-proof: delete one host's <c>MapCursorStatusBarSection.Register(</c> and
    /// this names that host.
    /// </summary>
    [Fact]
    public void EveryHostThatAttachesTheSharedLayers_ShowsTheCursorInItsStatusBar()
    {
        var files = ProductionFiles().ToList();
        var attachers = files.Where(f => f.Text.Contains("MapInteractionPack.AttachMapLayers(")).Select(f => HostOf(f.Rel)).Distinct().ToList();
        var showers = files.Where(f => f.Text.Contains("MapCursorStatusBarSection.Register(")).Select(f => HostOf(f.Rel)).ToHashSet();

        Assert.True(attachers.Count >= 5, "anti-vacuity: expected the five map hosts, found " + string.Join(", ", attachers));
        var missing = attachers.Where(h => !showers.Contains(h)).ToList();
        Assert.True(missing.Count == 0, "map hosts with no cursor readout in the status bar: " + string.Join(", ", missing));
    }

    /// <summary>
    /// ⭐ <c>CE-1446</c> (R-1046) S4 — <b>only the Player's map is eager, and it says so through its profile</b>: no host passes the map's build policy to the shared attach except the IG, which passes its profile's
    /// <c>MapBuildPolicy</c> (never a literal), and nothing outside the pack / the profile names <c>Map3DBuildPolicy.Eager</c> — so CGF, SimHost, the editor and the Replay Browser start in 2-D and build on the first request.
    /// 🔴 Mutation: a host passing <c>map3DBuildPolicy: Eager</c> (or the IG a literal) ⇒ this names it.
    /// </summary>
    [Fact]
    public void CE1446_OnlyThePlayersMapIsEager_AndItReadsItsProfile()
    {
        var files = ProductionFiles().ToList();
        var passers = files.Where(f => f.Text.Contains("map3DBuildPolicy:") && !f.Rel.EndsWith("MapInteractionPack.cs")).ToList();
        Assert.Equal(new[] { "Hrot/Subsystems/Hrot.IG" }, passers.Select(f => HostOf(f.Rel)).Distinct().ToArray());
        Assert.Contains("map3DBuildPolicy: RunProfile.MapBuildPolicy", passers.Single().Text);
        var eager = files.Where(f => f.Text.Contains("Map3DBuildPolicy.Eager")).Select(f => f.Rel).OrderBy(r => r).ToArray();
        Assert.Equal(new[] { "Hrot/Engine/Hrot.Presentation/RunMode/NodeRunControls.cs" }, eager);
    }

    [Theory]
    [InlineData(@"MapInteractionPack\.BuildRenderLayer\(")]
    [InlineData(@"new\s+(Fdp\.Toolkit\.Vis3D\.)?TerrainLayer3D\(")]
    [InlineData(@"new\s+([\w.]+\.)?TerrainLayer2D\(")]   // ⭐ CE-1227
    [InlineData(@"new\s+([\w.]+\.)?EntityBodyLayer3D\(")]
    [InlineData(@"new\s+(Fdp\.Toolkit\.Vis3D\.)?MapViewSwitch\(")]
    [InlineData(@"new\s+([\w.]+\.)?MapViewModeLayer\(")]
    [InlineData(@"new\s+([\w.]+\.)?MapCameraLayer\(")]
    [InlineData(@"new\s+([\w.]+\.)?FilmCaptureLayer\(")]   // ⭐ CE-3289 — the film is every map's, by construction
    [InlineData(@"new\s+([\w.]+\.)?GridMapLayer\(")]       // ⭐ CE-1288 — the 2-D helper grid is the shared attach's (the editor used to add its own, above the entities)
    [InlineData(@"new\s+([\w.]+\.)?MapCursorLayer\(")]     // ⭐ CE-1289 — the cursor's reading is measured once, the same on every host
    public void NoHostBuildsAMapLayerOfItsOwn(string pattern)
    {
        var re = new Regex(pattern);
        var offenders = ProductionFiles().Where(f => f.Rel != PackFile && re.IsMatch(f.Text)).Select(f => f.Rel).ToList();
        Assert.True(offenders.Count == 0, $"built outside MapInteractionPack ({pattern}): " + string.Join(", ", offenders));
    }

    /// <summary>⭐ CE-1033 S5 (R-260, R-140) — the camera entity needs NO per-host wiring: the shared map attach adds its layer, and the
    /// shared <c>EntityCreationPack</c> (which every creating host composes) registers the creation request on the world's bus, which the layer
    /// publishes on (⭐ <c>CE-3228</c>: the bus, not a queue singleton — Entity Creation §3.4d).</summary>
    [Fact]
    public void S5_TheCameraNeedsNoPerHostWiring()
    {
        var pack = File.ReadAllText(Path.Combine(TestRepo.Root, PackFile));
        Assert.Contains("new Hrot.UI.Common.Map3D.MapCameraLayer(viewSwitch, worldProvider)", pack);
        var creation = File.ReadAllText(Path.Combine(TestRepo.Root, "Hrot/Engine/Hrot.Common/EntityCreation/EntityCreationPack.cs"));
        Assert.Contains("ctx.World.RegisterManagedEvent<EntityCreationRequest>();", creation);
        Assert.Contains("new BusEntityCreationRequestSource(ctx.World.Bus)", creation);
        var hostMentions = ProductionFiles().Where(f => f.Rel.StartsWith("Hrot/Subsystems/") && System.Text.RegularExpressions.Regex.IsMatch(f.Text, @"MapCamera(Layer|Tkb|Pose)\b")).Select(f => f.Rel).ToList();
        Assert.True(hostMentions.Count == 0, "a host wires the map camera itself: " + string.Join(", ", hostMentions));
    }

    /// <summary>⭐ WL-1 (DESIGN_Weapon_Layers W-9) — the turret pose reaches the 3-D bodies at ONE place, the shared attach; no host
    /// passes a pose of its own.</summary>
    [Fact]
    public void WL1_TheTurretPoseNeedsNoPerHostWiring()
    {
        var pack = File.ReadAllText(Path.Combine(TestRepo.Root, PackFile));
        Assert.Contains("new Hrot.UI.Common.Map3D.EntityBodyLayer3D(worldProvider, tkb, turretPoses.PoseOf, Hrot.UI.Common.Map3D.TrafficAmbientBodies.Of)", pack);
        var hostMentions = ProductionFiles().Where(f => f.Rel.StartsWith("Hrot/Subsystems/") && f.Text.Contains("TurretPoseSource")).Select(f => f.Rel).ToList();
        Assert.True(hostMentions.Count == 0, "a host wires the turret pose itself: " + string.Join(", ", hostMentions));
    }

    /// <summary>⭐ CE-4031 (AQ93 §15.8) — SUMO's far people and cars reach the 3-D bodies at ONE place, the shared attach (the
    /// silent-default rule: the one production caller that has the source passes it); no host draws them itself.</summary>
    [Fact]
    public void CE4031_SumoFarAgents_ReachTheBodies_AtTheSharedAttach()
    {
        var pack = File.ReadAllText(Path.Combine(TestRepo.Root, PackFile));
        Assert.Contains("Hrot.UI.Common.Map3D.TrafficAmbientBodies.Of)", pack);
        var hostMentions = ProductionFiles().Where(f => f.Rel.StartsWith("Hrot/Subsystems/") && f.Text.Contains("TrafficAmbientBodies")).Select(f => f.Rel).ToList();
        Assert.True(hostMentions.Count == 0, "a host wires SUMO's far bodies itself: " + string.Join(", ", hostMentions));
    }

    /// <summary>⭐ CE-1203 FL-8 (docs/DESIGN_Terrain_Combat_Tuning.md §5e) — the layer control applies the set of the view on screen: the
    /// shared attach hands it the view switch, found by the buffer <c>Build</c> made — so ① a host's attach reaches it with no wiring of
    /// its own, and ② every host hands the attach THAT buffer. 🔴 Red-proof: without the hand-over the layer control stays on the 2-D set
    /// in 3-D.</summary>
    [Fact]
    public void CE1203_TheLayerControl_FollowsTheView_OnEveryMapHost()
    {
        var world = new Fdp.Core.EntityRepository();
        var mi = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(new Hrot.ScenarioEditor.Map.MapInteractionContext { World = world, LayerSettingsPath = "" });
        Assert.Null(mi.LayerControl.Is3D);
        var canvas = new Fdp.Toolkit.Vis2D.MapCanvas(input: null) { Camera = new Fdp.Toolkit.Vis2D.Components.MapCamera() };
        var layers = Hrot.ScenarioEditor.Map.MapInteractionPack.AttachMapLayers(canvas, mi.Buffer, mi.InteractionBus, () => world);
        Assert.NotNull(mi.LayerControl.Is3D);
        Assert.False(mi.LayerControl.Is3D!());
        layers.ViewSwitch.Set(true, animate: false);
        Assert.True(mi.LayerControl.Is3D!());

        // ② every attaching host passes the buffer its Build made
        var files = ProductionFiles().ToList();
        var attachers = files.Where(f => f.Text.Contains("MapInteractionPack.AttachMapLayers(")).Select(f => HostOf(f.Rel)).Distinct().ToList();
        var re = new Regex(@"=\s*\w*[Mm]apInteraction\w*\.Buffer\s*;");
        var without = attachers.Where(h => !files.Any(f => HostOf(f.Rel) == h && re.IsMatch(f.Text))).ToList();
        Assert.True(attachers.Count >= 5, "anti-vacuity: " + string.Join(", ", attachers));
        Assert.True(without.Count == 0, "hosts whose map buffer is not the one MapInteractionPack.Build made: " + string.Join(", ", without));
    }

    /// <summary>⭐ CE-1234 — the layer panel works in 3-D on every map host: the gizmo's state reaches the map's own panel (the local
    /// transport the shared attach hands the render layer), so in 3-D the panel shows the 3-D set and a click on it changes the 3-D mask.
    /// 🔒 User, 2026-10-20: <i>"map layer controls (checkbox) does not seem to work in 3d (nothing chages on chekcbox click in 3d) - only in
    /// 2d"</i>. 🔴 Red-proof: with no state reaching the panel its copy still read "2-D map", and the click was dropped.</summary>
    [Fact]
    public void CE1234_TheLayerPanel_ClickedIn3D_ChangesThe3DLayers()
    {
        var world = new Fdp.Core.EntityRepository();
        var mi = Hrot.ScenarioEditor.Map.MapInteractionPack.Build(new Hrot.ScenarioEditor.Map.MapInteractionContext { World = world, LayerSettingsPath = "" });
        var canvas = new Fdp.Toolkit.Vis2D.MapCanvas(input: null) { Camera = new Fdp.Toolkit.Vis2D.Components.MapCamera() };
        var layers = Hrot.ScenarioEditor.Map.MapInteractionPack.AttachMapLayers(canvas, mi.Buffer, mi.InteractionBus, () => world);
        Assert.NotNull(layers.GizmoLayer.UiStates);
        layers.ViewSwitch.Set(true, animate: false);

        // the panel opens and the gizmo publishes its (3-D) set
        mi.InteractionBus.Publish(new Hrot.Common.Diagnostics.Gizmos.OpenLayerEditorEvent());
        mi.InteractionBus.SwapBuffers();
        mi.LayerControl.UpdateAndDraw(world, 0f, new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer());
        var states = new List<GizmoMap.Network.GizmoUiState>();
        layers.GizmoLayer.UiStates!.PollAndApply(states.Add);
        var state = Assert.Single(states);

        // the panel's side: its document takes the state, the operator unticks Rounds, the panel sends its document back
        var registry = new GizmoMap.Presentation.GizmoSchemaRegistry();
        Hrot.ScenarioEditor.Map.MapInteractionPack.RegisterGizmoSchemas(registry);
        Assert.True(registry.TryGet(Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo.SchemaHash, out var doc) && doc != null);
        StructEdit.Json.EditDocumentJsonSerializer.Deserialize(state.EditDocumentJson, doc!);
        StructEdit.Core.EditNode Find(StructEdit.Core.EditNode n, string path)
            => n.JsonPath.EndsWith(path, StringComparison.Ordinal) ? n : n.Children.Select(c => Find(c, path)).FirstOrDefault(x => x != null)!;
        Assert.Equal(Hrot.Common.Diagnostics.Gizmos.LayerControlSets.View3D, Find(doc!.Root, "View").Binding!.GetBoxed());
        Find(doc!.Root, "Rounds").Binding!.SetBoxed(false);
        mi.LayerControl.OnStructUpdate(StructEdit.Json.EditDocumentJsonSerializer.Serialize(doc!));

        Assert.False(mi.LayerControl.ActiveLayers.IsSet(Fdp.Toolkit.Diagnostics.Gizmos.DebugTraceLayers.Rounds), "the 3-D click took");
        Assert.False(mi.LayerControl.SetOf(is3D: true).Units.Rounds);
        Assert.True(mi.LayerControl.SetOf(is3D: false).Units.Rounds, "the 2-D set is its own");
    }
}
