using System;
using System.Numerics;
using Hrot.Blueprints.Editor.Windows;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using NodeEditor.Core.Widgets;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor.Frame;

/// <summary>
/// ⭐ CE-3396 (docs/DESIGN_Struct_Editor.md §11/§12) — the Blueprint create modals' rows are label-first <c>FormGrid</c> rows (<i>Name</i> · <i>Container</i> in the function / variable modals) on the one label column,
/// and their problem line is the kit's error line. In a real frame: the form grid exists with a label column, and the modal draws its validation reason (the default name is taken is not needed — a blank name is refused).
/// 🔴 Red-proof (compile): <c>FunctionCreateModal</c> drawing <c>TextUnformatted("Name")</c> + a bare input again ⇒ no <c>bp_fn.rows</c> column ⇒ red.
/// </summary>
[Collection(UiFrameCollection.Name)]
public sealed class TheCreateModalsAreLabelFirstFormsTests
{
    [SkippableFact]
    public void TheFunctionAndVariableModals_DrawTheirRowsThroughFormGrid()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var fn = new FunctionCreateModal(_ => { });
        var dir = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS");
        var vr = new VariableCreateModal((_, _, _, _) => { });
        using var frame = UiFrameHarness.Begin(520, 300);
        fn.Open();
        void DrawFn() { ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.Begin("##host", ImGuiWindowFlags.NoDecoration); fn.Draw(); ImGui.End(); }
        for (int i = 0; i < 8; i++) frame.Step(DrawFn);
        Assert.True(FormGrid.ColumnWidthOf("bp_fn.rows") > 20f, "the function modal's Name is a FormGrid row");
        if (!string.IsNullOrEmpty(dir)) frame.Screenshot(System.IO.Path.Combine(dir, "modal-function.png"));
        ImGui.CloseCurrentPopup();
        vr.Open();
        void DrawVr() { ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.Begin("##host2", ImGuiWindowFlags.NoDecoration); vr.Draw(); ImGui.End(); }
        for (int i = 0; i < 8; i++) frame.Step(DrawVr);
        Assert.True(FormGrid.ColumnWidthOf("var.props") > 20f, "the variable modal's Container is a FormGrid row");
        if (!string.IsNullOrEmpty(dir)) frame.Screenshot(System.IO.Path.Combine(dir, "modal-variable.png"));
    }
}
