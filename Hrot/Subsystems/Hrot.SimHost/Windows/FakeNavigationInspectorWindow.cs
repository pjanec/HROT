using System;
using ImGuiNET;
using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.EngineBacked;
using Fdp.Toolkit.Navigation.Fake;

namespace Hrot.SimHost.Windows;

/// <summary>
/// NAV-P7-T1: Four-tab ImGui diagnostic window for fake navigation backends.
/// Registered via SimHostSubsystem.RegisterWindows in non-headless mode.
/// Detects active provider type at draw time (NAV-P6-T7).
/// </summary>
internal sealed class FakeNavigationInspectorWindow : ManagedWindow
{
    private readonly Func<EntityRepository?> _repoGetter;

    public FakeNavigationInspectorWindow(Func<EntityRepository?> repoGetter)
        : base("fake_nav_inspector", "Fake Navigation Backends", "SimHost", WindowScope.PerspectiveBound)
    {
        _repoGetter = repoGetter;
    }

    protected override void DrawClientArea()
    {
        var repo = _repoGetter();
        if (repo == null)
        {
            ImGui.TextDisabled("No world available.");
            return;
        }

        DrawHeader(repo);

        if (ImGui.BeginTabBar("nav_tabs"))
        {
            if (ImGui.BeginTabItem("Navmesh"))   { DrawNavmeshTab(repo);    ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Crowd"))     { DrawCrowdTab(repo);      ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Volumetric")){ DrawVolumetricTab(repo); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Paths"))     { DrawPathsTab(repo);      ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    private void DrawHeader(EntityRepository repo)
    {
        // NAV-P6-T7: detect active backend at draw time via INavmeshProvider singleton.
        var navmesh = repo.GetSingletonManaged<INavmeshProvider>();
        string backendLabel = navmesh switch
        {
            EngineBackedNavmeshProvider => "Backend: EngineBacked (road graph + direct-line)",
            FakeNavmeshProvider         => "Backend: Fake (FakeNavmeshProvider + FakeDtCrowdProvider + FakeVolumetricPathProvider)",
            null                        => "Backend: none (no providers registered)",
            _                           => $"Backend: {navmesh.GetType().Name}",
        };
        ImGui.TextDisabled(backendLabel);
        ImGui.Separator();
    }

    private void DrawNavmeshTab(EntityRepository repo)
    {
        var navmesh = repo.GetSingletonManaged<INavmeshProvider>();
        if (navmesh is EngineBackedNavmeshProvider)
        {
            ImGui.TextDisabled("No navmesh layers loaded -- direct-line provider in use.");
            ImGui.TextDisabled("All IsWalkable queries return true.");
            return;
        }
        if (navmesh is FakeNavmeshProvider)
        {
            ImGui.Text("FakeNavmeshProvider active.");
            ImGui.TextDisabled("(Detailed polygon tree not yet implemented -- NAV-P7-T1 Phase 2)");
            return;
        }
        ImGui.TextDisabled("No navmesh provider registered.");
    }

    private void DrawCrowdTab(EntityRepository repo)
    {
        // IDtCrowdProvider is not registered as a singleton by EngineBackedNavigationModule.
        // Infer crowd backend from INavmeshProvider type.
        var navmesh = repo.GetSingletonManaged<INavmeshProvider>();
        if (navmesh is EngineBackedNavmeshProvider)
        {
            ImGui.TextDisabled("Crowd avoidance disabled -- stub provider in use.");
            ImGui.TextDisabled("Humanoids move via LinearKinematicsSystem.");
            return;
        }
        if (navmesh is FakeNavmeshProvider)
        {
            ImGui.Text("FakeDtCrowdProvider active.");
            ImGui.TextDisabled("(Agent list not yet implemented -- NAV-P7-T1 Phase 2)");
            return;
        }
        ImGui.TextDisabled("No crowd provider registered.");
    }

    private void DrawVolumetricTab(EntityRepository repo)
    {
        // IVolumetricPathProvider is not registered as a singleton by EngineBackedNavigationModule.
        // Infer volumetric backend from INavmeshProvider type.
        var navmesh = repo.GetSingletonManaged<INavmeshProvider>();
        if (navmesh is EngineBackedNavmeshProvider)
        {
            ImGui.TextDisabled("Volumetric path provider: direct-line stub.");
            ImGui.TextDisabled("All IsFlyable queries return true.");
            return;
        }
        if (navmesh is FakeNavmeshProvider)
        {
            ImGui.Text("FakeVolumetricPathProvider active.");
            ImGui.TextDisabled("(No-fly zone list not yet implemented -- NAV-P7-T1 Phase 2)");
            return;
        }
        ImGui.TextDisabled("No volumetric path provider registered.");
    }

    private void DrawPathsTab(EntityRepository repo)
    {
        var pathReg = repo.GetSingletonManaged<IPathRegistry>();
        if (pathReg == null)
            ImGui.TextDisabled("No path registry registered.");
        else
        {
            ImGui.Text($"Path registry: {pathReg.GetType().Name}");
            ImGui.TextDisabled("(Path pool table not yet implemented -- NAV-P7-T1 Phase 2)");
        }

        ImGui.Separator();

        // NAV-P7-T2: JSON snapshot export button.
        if (ImGui.Button("Snapshot JSON"))
            ImGui.SetClipboardText(NavigationSnapshotBuilder.Build(repo));
        ImGui.SameLine();
        ImGui.TextDisabled("(copies to clipboard)");

        ImGui.Separator();

        // NAV-P7-T3 (Option C): corridor preview waypoint table.
        // StreamCorridorPreview flag management via entity selection is deferred (no selection infra present).
        DrawCorridorPreviewTable(repo);
    }

    private static void DrawCorridorPreviewTable(EntityRepository repo)
    {
        ImGui.TextDisabled("Corridor preview (entities with NavigationCorridorPreview active):");
        var query = repo.Query().With<NavigationCorridorPreview>().Build();
        bool any = false;
        foreach (var entity in query)
        {
            any = true;
            var preview = repo.GetComponent<NavigationCorridorPreview>(entity);
            string header = $"Entity {entity.Index}  v{preview.PreviewVersion}  seg+{preview.GlobalSegmentStart}  {preview.WaypointCount} wp";
            if (ImGui.TreeNode(header))
            {
                int n = preview.WaypointCount;
                if (n > 0) DrawPreviewWaypoint(preview.GlobalSegmentStart + 0, in preview.W0);
                if (n > 1) DrawPreviewWaypoint(preview.GlobalSegmentStart + 1, in preview.W1);
                if (n > 2) DrawPreviewWaypoint(preview.GlobalSegmentStart + 2, in preview.W2);
                if (n > 3) DrawPreviewWaypoint(preview.GlobalSegmentStart + 3, in preview.W3);
                if (n > 4) DrawPreviewWaypoint(preview.GlobalSegmentStart + 4, in preview.W4);
                if (n > 5) DrawPreviewWaypoint(preview.GlobalSegmentStart + 5, in preview.W5);
                if (n > 6) DrawPreviewWaypoint(preview.GlobalSegmentStart + 6, in preview.W6);
                if (n > 7) DrawPreviewWaypoint(preview.GlobalSegmentStart + 7, in preview.W7);
                ImGui.TreePop();
            }
        }
        if (!any)
            ImGui.TextDisabled("  (none -- set FlagBitStreamCorridorPreview on NavigationIntent to activate)");
    }

    private static void DrawPreviewWaypoint(int index, in PreviewWaypoint wp)
    {
        ImGui.Text($"  [{index}] ({wp.Position.X:F1},{wp.Position.Y:F1},{wp.Position.Z:F1})  trav={wp.Traversal}  surf={wp.Surface}");
    }
}
