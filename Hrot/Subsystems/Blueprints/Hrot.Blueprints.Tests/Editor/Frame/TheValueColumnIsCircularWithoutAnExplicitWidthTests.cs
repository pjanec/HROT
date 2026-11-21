using System.Numerics;
using Hrot.Editor.UiFrameRail;
using ImGuiNET;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor.Frame;

/// <summary>
/// ⭐⭐ <b>Batch 100 (<c>100a</c>) — the MECHANISM, isolated: why an auto-resized popup starves a
/// stretch column.</b>
///
/// <para>⚠⚠ <b>THIS IS A REPLICA, and saying so is the whole point</b> *(📌 <c>M-29</c>)*. It does NOT
/// render <c>VariableEditModal</c>; it renders the container-and-table shape that modal uses, so that
/// the <b>value column's own avail width</b> — the number <c>ComponentEditDrawer</c> clamps and the
/// designer loses — can be measured <b>without touching the drawer</b>, which is `Fdp.Presentation`
/// infrastructure with five other working callers.</para>
///
/// <para>⭐⭐⭐ <b>Its job is to keep the DIAGNOSIS true, not to gate the fix.</b> The fix is gated by
/// <see cref="TheEditDialogHasRoomForTheNumberTests"/> on the real modal. ⭐ This one answers the
/// question that rail cannot: <i>"is the cause still what we said it was?"</i> — ⛔ so that if someone
/// later removes the <c>SetNextWindowSize</c> believing the stretch column is fine, the belief itself
/// goes red.</para>
///
/// <para>📐 <b>MEASURED here:</b> without an explicit width the value column gets <b>60.0 px</b> — the
/// clamp floor exactly — and with one it gets <b>305.0 px</b>. ⭐ These are the two numbers the
/// coordinator's standalone probe found, ⚠ <b>reproduced independently inside the test suite</b>
/// rather than copied.</para>
/// </summary>
[Collection(UiFrameCollection.Name)]
public sealed class TheValueColumnIsCircularWithoutAnExplicitWidthTests
{
    /// <summary>⭐ The drawer's own clamp — <c>ComponentEditDrawer:253</c>. A column that measures
    /// EXACTLY this is a column that got nothing and was floored.</summary>
    private const float DrawerClampFloor = 60f;

    /// <summary>
    /// ⭐⭐ Renders the modal's exact container + table shape and returns the value column's avail width.
    /// </summary>
    /// <param name="explicitWidth">⭐ <c>null</c> reproduces the defect; a value applies the fix.</param>
    private static float MeasureValueColumn(float? explicitWidth)
    {
        const string PopupId = "replica##b100";
        float avail = -1f;

        using var frame = UiFrameHarness.Begin();
        frame.StepN(6, () =>
        {
            if (frame.FramesRendered == 0) ImGui.OpenPopup(PopupId);

            if (explicitWidth is { } w)
                ImGui.SetNextWindowSize(new Vector2(w, 0), ImGuiCond.Appearing);

            bool open = true;
            if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;

            // ⭐ VariableEditModal:297–:302, verbatim: the same four flags and the same two columns.
            if (ImGui.BeginTable("##vedit", 2,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                    ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit))
            {
                ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 180f);
                ImGui.TableSetupColumn("Value",    ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TreeNodeEx("Count", ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen);
                ImGui.TableSetColumnIndex(1);

                avail = UiProbe.AvailWidth();

                // ⭐ InputInt, because the SHAPE matters: it draws a field PLUS `−`/`+` step buttons
                //   sized as one group, which is what leaves the digits with nowhere to go at 60 px.
                int v = 11;
                ImGui.SetNextItemWidth(avail < DrawerClampFloor ? DrawerClampFloor : avail);
                ImGui.InputInt("##v", ref v);

                ImGui.EndTable();
            }
            ImGui.EndPopup();
        });

        return avail;
    }

    /// <summary>
    /// 🔴 <b>The defect: a stretch column inside an auto-resizing popup resolves to the clamp floor.</b>
    /// ⭐ Asserted as <b>at most the floor</b>, ⛔ not equal to it — the claim is <i>"it got nothing"</i>,
    /// and pinning the exact float would make a padding change look like a regression.
    /// </summary>
    [SkippableFact]
    public void WithoutAnExplicitWidth_TheValueColumnCollapsesToTheClampFloor()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        float avail = MeasureValueColumn(explicitWidth: null);

        Assert.True(avail <= DrawerClampFloor,
            $"Expected the circular layout to starve the value column, but it got {avail:F1} px. " +
            "If this passes generously, the mechanism changed and the diagnosis in 100b is stale.");
    }

    /// <summary>✅ <b>And one <c>SetNextWindowSize</c> breaks the circularity.</b> ⭐ The same shape, the
    /// same drawer clamp, the only difference being that the popup no longer sizes to its content.</summary>
    [SkippableFact]
    public void WithAnExplicitWidth_TheValueColumnGetsRealRoom()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);

        float avail = MeasureValueColumn(explicitWidth: 520f);

        Assert.True(avail > 200f,
            $"An explicitly sized popup should give the value column real room; got {avail:F1} px.");
    }
}
