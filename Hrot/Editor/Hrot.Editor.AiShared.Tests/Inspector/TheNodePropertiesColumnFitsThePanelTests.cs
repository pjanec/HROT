using System;
using System.Numerics;
using Fbt;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior;
using Hrot.BTree.Editor.Inspector;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Inspector.ActionBinding;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Inspector;

/// <summary>
/// ⭐ CE-4399 (R-4138; 🔒 user, 2026-11-10: <i>"bt-window-duel ▸ peek and fire ▸ Node Properties: Method and Blueprint cut mid-word, Visual ID loses its right side; narrowing the panel hides the right side of every edit box"</i>)
/// — a BTree Action node's facet drawn by the REAL drawers (the binding drawer + the struct table) in a real frame at several panel widths; what the frame drew (the draw lists' vertices) may not pass the window's
/// content right edge, and a long method name is ELIDED inside its button, never clipped at both ends. Skips without a display.
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class TheNodePropertiesColumnFitsThePanelTests
{
    private static BehaviorTreeAsset MakeAsset()
    {
        var blob = new BehaviorTreeBlob { TreeName = "T", Nodes = Array.Empty<NodeDefinition>(), MethodNames = Array.Empty<string>(), FloatParams = Array.Empty<float>(), IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>() };
        return BehaviorTreeAssetProjector.Project(blob, null, null, Guid.NewGuid(), "T", "/t.cs", false, "", "");
    }

    /// <summary>The furthest a drawn vertex reaches past the window's content right edge, in px (window frame excluded by a baseline frame with no table).</summary>
    private static (float Over, float ValueRight) Measure(float width, bool withTable)
    {
        var asset = MakeAsset();
        var catalogue = BTreePickerDrawerFactory.BuildCatalogue(asset, new BehaviorRegistry(), openPicker: (_, _) => { });
        var facet = new BTreeActionFacet
        {
            Action = new BehaviorActionBindingFacet { MethodFqn = "Hrot.AI.Behaviors.Brains.PeekAndFireNodes.Action_PeekAndFireTowardsTheNearestContact" },
            VisualId = "3f0c2d9e-8a41-4b6c-9d57-2e1f7a6b8c90",
        };
        using var session = AiFacetEditService.Build().Open(facet, typeof(BTreeActionFacet));
        var drawer = new ComponentEditDrawer(session, pickerCtx: null, types: catalogue);
        float over = float.NegativeInfinity, right = 0f;
        using var frame = UiFrameHarness.Begin(900, 500);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        for (int i = 0; i < 6; i++)
        {
            frame.Step(() =>
            {
                ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(width, 360));
                ImGui.Begin("Details##u10", ImGuiWindowFlags.NoSavedSettings);
                right = ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X;
                if (withTable && StructTable.Begin("##u10t", label: "Property")) { drawer.DrawRoot(session.Document.Root); StructTable.End(); }
                ImGui.End();
            });
        }
        var dd = ImGui.GetDrawData();
        for (int n = 0; n < dd.CmdListsCount; n++)
        {
            var list = dd.CmdLists[n];
            for (int v = 0; v < list.VtxBuffer.Size; v++) over = Math.Max(over, list.VtxBuffer[v].pos.X - right);
        }
        return (over, right);
    }

    [SkippableTheory]
    [InlineData(260f)] [InlineData(340f)] [InlineData(520f)]
    public void TheValueColumn_EndsInsideThePanelsContent_AtEveryWidth(float width)
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        float baseline = Measure(width, withTable: false).Over;
        float over = Measure(width, withTable: true).Over;
        Assert.True(over <= baseline + 1f, $"at {width} px something in the facet reaches {over - baseline:F1} px past the panel's content edge");
    }

    /// <summary>🔴 Red-proof (compiles): <c>ElideToWidth</c> returning its text unchanged ⇒ a long method name is wider than the button ⇒ red.</summary>
    [SkippableFact]
    public void ALongMethodName_IsElidedToTheButton_NotClippedAtBothEnds()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        using var frame = UiFrameHarness.Begin(600, 300);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        const string name = "Hrot.AI.Behaviors.Brains.PeekAndFireNodes.Action_PeekAndFireTowardsTheNearestContact";
        var results = new System.Collections.Generic.List<(float W, string Shown, float Px)>();
        frame.Step(() =>
        {
            ImGui.Begin("t", ImGuiWindowFlags.NoSavedSettings);
            foreach (float w in new[] { 120f, 200f, 320f })
            {
                string shown = Hrot.Editor.AiShared.Inspector.PickerField.ElideToWidth(name, w);
                results.Add((w, shown, ImGui.CalcTextSize(shown).X));
            }
            results.Add((200f, Hrot.Editor.AiShared.Inspector.PickerField.ElideToWidth("short", 200f), 0f));
            ImGui.End();
        });
        foreach (var (w, shown, px) in results.GetRange(0, 3))
        {
            Assert.True(px <= w + 0.5f, $"{w}: '{shown}' is {px}");
            Assert.StartsWith("Hrot", shown); Assert.EndsWith("Contact", shown); Assert.Contains("…", shown);
        }
        Assert.Equal("short", results[3].Shown);
    }
}
