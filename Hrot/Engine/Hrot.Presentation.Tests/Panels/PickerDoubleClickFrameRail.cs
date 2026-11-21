using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐ CE-4223 (Part 0 b; found live in W-R5b) — the generic picker (CE-4168): a DOUBLE-CLICK on a row confirms it, exactly as OK does. In a real frame: open a request, double-click the second row, the host callback gets THAT entry.
/// ⚠ Measured: it ALREADY does, in both layouts, with the filter typed first (the live miss of W-R5b was a scripted double-click against a 50-90 ms editor frame, not a picker defect) — this rail pins it.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class PickerDoubleClickFrameRail
{
    private static void Pos(ImGuiIOPtr io, float x, float y) { io.AddMousePosEvent(x, y); Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }

    [SkippableTheory]
    [InlineData(PickerLayout.Standard)]
    [InlineData(PickerLayout.Tree)]
    public void ADoubleClickOnARow_ConfirmsIt(PickerLayout layout)
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var registry = new PickerRegistry();
        PickerResult? got = null;
        registry.OpenPicker(new PickerRequest
        {
            ContextKey = "rail.dblclick", Title = "Add a step", Layout = layout, SelectionMode = PickerSelectionMode.Single,
            ItemsProvider = () => new[] { "Alpha", "Message", "Zulu" }.Select(n => new PickerEntry(n, n, null, null, null, null, n)),
        }, r => got = r);
        using var frame = UiFrameHarness.Begin(1000, 700);
        void Step(Action<ImGuiIOPtr>? input = null, int n = 1)
        {
            for (int i = 0; i < n; i++) frame.Step(() => { input?.Invoke(ImGui.GetIO()); registry.DrawFrame(); });
        }
        Step(null, 6);
        foreach (char ch in "Message") Step(io => io.AddInputCharacter(ch));   // the live miss: the filter was typed first, then the only row double-clicked
        Step(null, 3);
        frame.Screenshot(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "picker-rail.png"));
        float x = 120f; float y = RowY(layout);
        Step(io => Pos(io, x, y), 2);
        Step(io => { Pos(io, x, y); io.AddMouseButtonEvent(0, true); });
        Step(io => { Pos(io, x, y); io.AddMouseButtonEvent(0, false); });
        Step(io => { Pos(io, x, y); io.AddMouseButtonEvent(0, true); });
        Step(io => { Pos(io, x, y); io.AddMouseButtonEvent(0, false); });
        Step(null, 4);
        Assert.NotNull(got);
        Assert.Equal("Message", got!.First?.Id);
    }

    private static float RowY(PickerLayout layout) => 123f;
}
