using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Adapters;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Fonts;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using StructEdit.Core;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ CE-3403 (Y-9; DESIGN_Struct_Editor §10.2 / §12 N-10) — the raw inspector window (<c>ComponentEditWindow</c>) in a REAL frame: its body draws in the ONE struct table (<c>StructTable</c>, Field · Value) and a steady frame allocates nothing (R-2084 — it had no frame harness,
/// so it was unmeasured). Renders <c>inspector-window.png</c> under <c>HROT_RAIL_SHOTS</c>. 🔴 Red-proof (compiles): a per-frame string built in <c>DrawClientArea</c> ⇒ the byte assert fails.
/// </summary>
[Collection(Hrot.Presentation.Tests.ComponentTypeRegistryMutatorCollection.Name)]
public sealed class ComponentEditWindowFrameRail : IDisposable
{
    private readonly EntityRepository _repo;
    public ComponentEditWindowFrameRail()
    {
        ComponentTypeRegistry.Clear();
        _repo = new EntityRepository();
        _repo.RegisterComponent<EntityInfo>();
        _repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
    }
    public void Dispose() => _repo.Dispose();

    [SkippableFact]
    public void TheInspectorWindowsBody_DrawsTheStructTable_AndASteadyFrameAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var e = _repo.CreateEntity();
        var info = new EntityInfo { Name = new FixedString64("Alpha 1-1"), ForceId = ForceId.Friend };
        _repo.AddComponent(e, info);
        var adapter = new RepositoryAdapter(_repo);
        var service = ComponentEditServices.CreateBuilder().Build();
        var session = service.Open(info, typeof(EntityInfo), EditScope.WholeComponent, new EditContext());
        var window = new ComponentEditWindow("rail_cedit", "Edit EntityInfo", "Scenario", session, e, typeof(EntityInfo), () => adapter);

        using var frame = UiFrameHarness.Begin(700, 360);
        new EditorFontService().Initialize(1f, 1f);
        long bytes = -1;
        void Draw()
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(680, 340));
            ImGui.Begin("##cedit-host", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
            long before = GC.GetAllocatedBytesForCurrentThread();
            window.DrawBodyForRail();
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ImGui.End();
        }
        for (int k = 0; k < 3; k++) { frame.StepN(15, Draw); System.Threading.Thread.Sleep(220); }
        frame.StepN(5, Draw);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "inspector-window.png"));
        Assert.True(window.IsOpen, "the body drew with the entity alive");
        Assert.True(bytes == 0, $"a steady inspector window allocates nothing ({bytes} bytes)");
    }

    /// <summary>⭐ CE-4358 — the struct editor's name | value divider is DRAGGABLE: dragging it left narrows the name column, and the width is kept on the next frames (ImGui keeps it per table id, i.e. per window).
    /// ✅ Red-proof: <c>Resizable</c> dropped from <c>StructTable.Flags</c> ⇒ the width does not move ⇒ red.</summary>
    [SkippableFact]
    public void CE4358_TheNameValueDivider_CanBeDragged_AndTheWidthIsKept()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var e = _repo.CreateEntity();
        var info = new EntityInfo { Name = new FixedString64("Alpha 1-1"), ForceId = ForceId.Friend };
        _repo.AddComponent(e, info);
        var adapter = new RepositoryAdapter(_repo);
        var session = ComponentEditServices.CreateBuilder().Build().Open(info, typeof(EntityInfo), EditScope.WholeComponent, new EditContext());
        var window = new ComponentEditWindow("rail_cedit2", "Edit EntityInfo", "Scenario", session, e, typeof(EntityInfo), () => adapter);
        using var frame = UiFrameHarness.Begin(700, 360);
        new EditorFontService().Initialize(1f, 1f);
        var sim = new WidgetKitFrameRail.Sim();
        void Draw()
        {
            sim.Feed();
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(680, 340));
            ImGui.Begin("##cedit-host2", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings);
            window.DrawBodyForRail();
            ImGui.End();
        }
        frame.StepN(15, Draw);
        float before = StructTable.LastLabelWidth;
        Assert.True(before > 50f);
        float dividerX = 10f + ImGui.GetStyle().WindowPadding.X + before, y = 10f + ImGui.GetStyle().WindowPadding.Y + ImGui.GetFrameHeight() * 0.5f;
        sim.Drag(frame, Draw, new Vector2(dividerX, y), new Vector2(dividerX - 120f, y));
        float after = StructTable.LastLabelWidth;
        Assert.True(after < before - 60f, $"the name column narrowed by the drag ({before} -> {after})");
        frame.StepN(10, Draw);
        Assert.True(Math.Abs(StructTable.LastLabelWidth - after) < 1f, "the width is kept on the next frames");
    }
}
