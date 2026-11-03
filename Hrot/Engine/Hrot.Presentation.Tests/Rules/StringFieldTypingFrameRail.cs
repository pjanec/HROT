using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.ScenarioRules.Actions;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Behavior;
using Hrot.Presentation.Tests.Map3D;
using ImGuiNET;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Rules;

/// <summary>
/// ⭐ CE-4245 — a STRING field of a step's args form (the ONE <see cref="ActionListFieldDrawer"/> the Do tab, the game-master palette and the AI section's rows share) keeps its keyboard focus while characters arrive:
/// click the field, type a word one character per frame, the whole word is in the step's args. The live miss: every keystroke lost the focus, so a Message text could not be typed.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class StringFieldTypingFrameRail
{
    private static void Pos(ImGuiIOPtr io, float x, float y) { io.AddMousePosEvent(x, y); Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }

    [SkippableFact]
    public void TypingIntoAStepsStringField_KeepsTheFocus_AndTheWholeTextArrives()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        _ = typeof(Hrot.AI.Behaviors.Brains.Objectives.ObjectiveStateKeepers).Assembly;
        var drawer = new ActionListFieldDrawer(ComponentEditServices.CreateBuilder().Build(), new ActionListContext());
        object value = new List<ActionStep> { new() { Act = "Message", Args = new JsonObject() } };
        using var frame = UiFrameHarness.Begin(1000, 700);
        void Step(Action<ImGuiIOPtr>? input = null, int n = 1)
        {
            for (int i = 0; i < n; i++) frame.Step(() =>
            {
                input?.Invoke(ImGui.GetIO());
                ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(980, 680));
                if (ImGui.Begin("rail")) drawer.DrawInput(ref value, null!);
                ImGui.End();
            });
        }
        Step(null, 6);
        // find the text field: click down the value column until the UI wants text
        float x = 700f; float found = -1;
        for (float y = 60; y < 400 && found < 0; y += 6)
        {
            Step(io => Pos(io, x, y), 2);
            Step(io => { Pos(io, x, y); io.AddMouseButtonEvent(0, true); });
            Step(io => { Pos(io, x, y); io.AddMouseButtonEvent(0, false); }, 2);
            bool wants = false; Step(io => wants = io.WantTextInput);
            if (wants) found = y;
        }
        Assert.True(found > 0, "no text field took the click");
        foreach (char ch in "hello") Step(io => io.AddInputCharacter(ch));
        Step(null, 3);
        var list = (List<ActionStep>)value;
        string json = list[0].Args?.ToJsonString() ?? "{}";
        Assert.Contains("hello", json);
    }
}
