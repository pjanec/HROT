using ImGuiNET;
using Hrot.Blueprints.Core.Compiler.Format;
using Hrot.Blueprints.Editor.Variables;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// BP-201 — the Details rows that declare a <b>type per format placeholder</b>, shared by
/// <c>Print String</c> and <c>Format String</c>.
///
/// <para>
/// ⭐ <b>Why this had to exist.</b> <c>PrintStringNode.ArgTypes</c> / <c>FormatStringNode.ArgTypes</c>
/// is what types each derived data-in pin (<c>BuiltInNodeRegistry.AppendArgPins</c>), and
/// <c>grep -rn "ArgTypes" Hrot.Blueprints.Editor/</c> returned <b>nothing</b>: the editor never wrote
/// it. Every pin fell back to <c>System.Object</c>, so the value the designer wired was formatted
/// through an untyped placeholder — the user's *"every second I got `[AI.Behavior.Blueprint] 0` — the
/// value NOT following the Count variable"*. The graph compiled perfectly; only the printed value was
/// wrong, which is exactly the class of defect a compile-only test cannot see.
/// </para>
///
/// <para>
/// ⚠ <b>Second instance of BP-116's shape in three batches</b> — a node property the compiler needs
/// that the editor never populates. BP-116 was <c>CallablePeers</c>; this is <c>ArgTypes</c>.
/// </para>
///
/// <para>
/// The type list is <see cref="BlueprintTypeChoices"/> — the same projection of the compiler's
/// <c>StaticTypeRegistry</c> the parameter picker uses, so a chosen type is always resolvable
/// (BP-87's lesson). An entry may also be absent, which is the <c>(auto)</c> row: the pin stays
/// <c>System.Object</c> and takes its type from whatever is wired into it.
/// </para>
/// </summary>
internal static class FormatArgTypeRows
{
    /// <summary>The label for "no declared type — take it from the wire".</summary>
    internal const string AutoLabel = "(auto -- from wire)";

    /// <summary>
    /// Draws one type combo per placeholder in <paramref name="format"/>, in the same
    /// first-appearance order the pins are derived in, and calls <paramref name="onPick"/> with the
    /// placeholder name and the chosen type id (empty string = revert to auto).
    /// </summary>
    internal static void Draw(
        string? format,
        IReadOnlyDictionary<string, string> argTypes,
        Action<string, string> onPick)
    {
        var parsed = BlueprintFormatString.Parse(format);
        if (!parsed.IsValid || parsed.Names.Count == 0) return;

        ImGui.Separator();
        ImGui.TextDisabled("Argument types");

        var choices = BlueprintTypeChoices.TypeIds;

        foreach (var name in parsed.Names)
        {
            argTypes.TryGetValue(name, out var current);
            var preview = string.IsNullOrEmpty(current) ? AutoLabel : current;

            if (!ImGui.BeginCombo(name, preview)) continue;

            if (ImGui.Selectable(AutoLabel, string.IsNullOrEmpty(current)))
                onPick(name, "");

            for (int i = 0; i < choices.Count; i++)
            {
                bool selected = string.Equals(choices[i], current, StringComparison.Ordinal);
                if (ImGui.Selectable(choices[i], selected))
                    onPick(name, choices[i]);
                if (selected) ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }
    }
}
