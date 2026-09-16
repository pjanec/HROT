using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐ <b>The editable state of ONE declaration, as the form holds it.</b>
/// ⚠ Mutable and public on purpose — this is an ImGui form's backing store, ⛔ not a domain model.
/// ⭐ A member the kind does not carry is simply never drawn *(<see cref="VariablePropertyFields"/>)*
/// and never committed.
/// </summary>
public sealed class VariablePropertyState
{
    public string Name             = "";
    public string TypeId           = "";
    public string DefaultValueJson = "";
    public string Tooltip          = "";
    public string Comment          = "";
    public string Category         = "";
    public bool   IsEditable;
    public bool   IsExposedOnSpawn;
}

/// <summary>
/// ⭐⭐⭐ <b>Batch 99 (<c>99a</c>) — the Properties form, and it is CUSTOM by ruling.</b>
///
/// <para>📌 <b><c>R-109</c></b> *(user, `2026-08-20`)*: ⛔ <b>Properties cannot be a StructEdit
/// document.</b> A struct commit means <i>"here is the new struct, apply it"</i>, and ⭐⭐ <b>two of
/// these fields are OPERATIONS, not writes</b>:
/// <list type="bullet">
///   <item><b><c>Name</c></b> is a <b>RENAME</b> ⇒ it must run the refactor service
///   *(<see cref="VariableRenameCommit"/>)*. 📌 <c>M-15</c> — on BTree/HSM the binding stores the NAME
///   STRING, so skipping that dangles it.</item>
///   <item><b><c>Type</c></b> is a <b>RETYPE MIGRATION</b> — <c>DefaultValueJson</c> may not convert,
///   offsets move, and <b><c>StructureHash</c> moves with the field list</b> *(<c>R-24</c>)*.</item>
/// </list>
/// ⇒ a struct commit would have to be <b>diffed against the old declaration and dispatched per field</b>
/// — ⭐ a custom controller wearing a StructEdit costume.</para>
///
/// <para>⭐⭐⭐ <b>THE SCHEMA IS THE FILTER — ⛔ not a per-field flag.</b>
/// <see cref="VariablePropertySchema.For"/> decides which controls appear, and it was measured off the
/// carriers *(8 / 5 / 4)*. ⇒ a property with no backing member <b>cannot be drawn</b>, which is what
/// makes <i>"a control with nowhere to save"</i> unrepresentable rather than merely avoided.
/// ⚠ 📌 <c>R-109</c> again: the per-field read-only flag this looked like it needed <b>was never
/// needed</b> — read-only is <b>DIALOG-LEVEL</b> *(<paramref name="enabled"/>)* and Batch 96 already
/// built it.</para>
///
/// <para>⭐⭐ <b>The type list is INJECTED.</b> Blueprint offers
/// <c>BlueprintTypeSystem.SelectableTypeIds</c> *(📌 <c>S5</c>, Batch 65 — ONE offerable set)*; a host
/// with a different vocabulary passes its own. ⛔ Hard-coding Blueprint's would make this form
/// Blueprint-only for no reason, and 📌 <c>U-6</c> is moving every variable surface the other way.</para>
///
/// <para>⛔ <b><c>Role</c>/<c>Scope</c> is not here and is not an omission</b> — user ruling
/// <c>2026-08-16</c>: <b>the SECTION is the classification.</b> ⛔ <b>Replication and Range</b> are
/// excluded because <b>no carrier has a backing member</b>.</para>
/// </summary>
public static class VariablePropertyFields
{
    /// <summary>⭐ The warning colour every other modal in this codebase uses. ⛔ Not a fourth one.</summary>
    private static readonly Vector4 Warn = new(0.95f, 0.55f, 0.20f, 1f);

    /// <summary>
    /// ⭐⭐ Draws the controls <see cref="VariablePropertySchema.For"/> names for
    /// <paramref name="kind"/>, into <paramref name="state"/>.
    /// </summary>
    /// <param name="idPrefix">
    /// ⭐ ImGui id scope. ⚠ <b>Load-bearing</b>: 📌 <c>VariableCreateModal</c>'s own doc records that two
    /// instances sharing one id is <b>one window both append into</b> — the locals "+" created a global.
    /// ⇒ every host passes its own.
    /// </param>
    /// <param name="typeIds">
    /// ⭐ The offerable set, in display order. ⛔ Empty means the Type control cannot be offered — see
    /// <paramref name="typeDisabledReason"/>.
    /// </param>
    /// <param name="shortName">⭐ How a type id is shown. ⛔ The form does not invent a spelling.</param>
    /// <param name="enabled">
    /// ⭐⭐ <b>DIALOG-LEVEL read-only</b>, from <c>VariableEditPolicy</c> — 📌 the design's matrix:
    /// planning ⇒ editable · running/paused ⇒ read-only *("you cannot retype a variable mid-run")* ·
    /// replay ⇒ read-only. ⛔ <b>Not a second matrix</b> *(ruling 9)*: the caller passes what
    /// <c>VariableEditGesture.Decide</c> already decided.
    /// </param>
    /// <param name="typeDisabledReason">
    /// ⭐⭐ Non-null ⇒ <c>Type</c> is drawn <b>DISABLED with this reason</b>. 📌 The handoff:
    /// <i>"if a retype cannot be made SAFE in this batch — ship it DISABLED with its reason"</i>,
    /// ⛔ never a silent write that leaves <c>DefaultValueJson</c> unconvertible.
    /// </param>
    public static void Draw(
        VariableDeclarationKind kind,
        VariablePropertyState   state,
        string                  idPrefix,
        IReadOnlyList<string>   typeIds,
        Func<string, string>    shortName,
        bool                    enabled            = true,
        string?                 typeDisabledReason = null,
        string?                 nameDisabledReason = null)
        // ⭐⭐⭐ THE SCHEMA IS THE FILTER — this overload is the one "Properties…" uses, and it is what
        //    the rail asserts. ⛔ The kind decides the set; nothing here hand-keeps a list.
        => Draw(VariablePropertySchema.For(kind), state, idPrefix, typeIds, shortName,
                enabled, typeDisabledReason, nameDisabledReason);

    /// <summary>
    /// ⭐⭐ Draws an EXPLICIT subset of the controls.
    ///
    /// <para>⚠ <b>Why this overload exists, and why it is not a loophole.</b> The CREATE gesture offers
    /// <c>Name</c> and <c>Type</c> and nothing else — a new declaration has no comment to edit and its
    /// <b>container</b> *(Single / List, Capacity, Initial Length)* is create-only and deliberately
    /// <b>absent from <see cref="VariablePropertySchema"/></b>. ⇒ ⭐ <b>Create asks for a subset;
    /// Properties asks the SCHEMA.</b> ⛔ Letting Create pass a whole kind would draw it three fields it
    /// cannot author — the exact <i>"a control with nowhere to save"</i> this design forbids.</para>
    /// </summary>
    public static void Draw(
        IReadOnlyList<VariableProperty> offered,
        VariablePropertyState           state,
        string                          idPrefix,
        IReadOnlyList<string>           typeIds,
        Func<string, string>            shortName,
        bool                            enabled            = true,
        string?                         typeDisabledReason = null,
        string?                         nameDisabledReason = null)
    {
        if (offered is null)   throw new ArgumentNullException(nameof(offered));
        if (state is null)     throw new ArgumentNullException(nameof(state));
        if (shortName is null) throw new ArgumentNullException(nameof(shortName));
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;   // ⭐ headless-safe, as every modal is

        if (!enabled) ImGui.BeginDisabled();

        foreach (var property in offered)
        {
            switch (property)
            {
                case VariableProperty.Name:
                    Label("Name");
                    DrawGuarded(nameDisabledReason,
                        () => ImGui.InputText($"##{idPrefix}_name", ref state.Name, 128));
                    break;

                case VariableProperty.Type:
                    Label("Type");
                    DrawTypeCombo(state, idPrefix, typeIds, shortName, typeDisabledReason);
                    break;

                case VariableProperty.DefaultValue:
                    Label("Default Value");
                    ImGui.InputText($"##{idPrefix}_default", ref state.DefaultValueJson, 512);
                    break;

                case VariableProperty.Tooltip:
                    Label("Tooltip");
                    ImGui.InputText($"##{idPrefix}_tooltip", ref state.Tooltip, 512);
                    break;

                case VariableProperty.Comment:
                    Label("Comment");
                    ImGui.InputText($"##{idPrefix}_comment", ref state.Comment, 512);
                    break;

                case VariableProperty.Category:
                    Label("Category");
                    ImGui.InputText($"##{idPrefix}_category", ref state.Category, 128);
                    break;

                case VariableProperty.IsEditable:
                    ImGui.Checkbox($"Editable##{idPrefix}_editable", ref state.IsEditable);
                    break;

                case VariableProperty.IsExposedOnSpawn:
                    ImGui.Checkbox($"Exposed on Spawn##{idPrefix}_exposed", ref state.IsExposedOnSpawn);
                    break;
            }
        }

        if (!enabled) ImGui.EndDisabled();
    }

    private static void Label(string text)
    {
        ImGui.TextUnformatted(text);
        ImGui.SetNextItemWidth(220f);
    }

    /// <summary>
    /// ⭐ The type picker, over the ONE offerable set. ⛔ Disabled — with its reason visible — rather
    /// than absent when a retype cannot be made safe: 📌 the visual guide's <c>F3</c>, <i>"every
    /// refusal GREYED WITH A TOOLTIP, not a click that dead-ends"</i>, and a field that VANISHES teaches
    /// nothing *(the standing <c>Q26-B2</c> ruling)*.
    /// </summary>
    /// <summary>
    /// ⭐⭐ Draws <paramref name="control"/>, DISABLED with a visible reason when there is one.
    ///
    /// <para>⛔ <b>Disabled-with-a-reason, never absent.</b> 📌 The visual guide's <c>F3</c> — <i>"every
    /// refusal GREYED WITH A TOOLTIP, not a click that dead-ends"</i> — and the standing <c>Q26-B2</c>
    /// ruling that a surface which VANISHES teaches nothing.</para>
    /// </summary>
    private static void DrawGuarded(string? disabledReason, Action control)
    {
        bool blocked = disabledReason is not null;
        if (blocked) ImGui.BeginDisabled();
        control();
        if (blocked) ImGui.EndDisabled();

        if (!blocked) return;
        ImGui.TextColored(Warn, disabledReason!);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(disabledReason);
    }

    private static void DrawTypeCombo(
        VariablePropertyState state, string idPrefix,
        IReadOnlyList<string> typeIds, Func<string, string> shortName, string? disabledReason)
    {
        // ⭐ An EMPTY offerable set is a refusal too, and it gets a reason rather than an empty combo.
        var reason = disabledReason
                  ?? (typeIds is null || typeIds.Count == 0 ? "No types are offered here." : null);

        DrawGuarded(reason, () =>
        {
            if (!ImGui.BeginCombo($"##{idPrefix}_type", shortName(state.TypeId))) return;
            for (int i = 0; i < typeIds!.Count; i++)
            {
                bool selected = string.Equals(typeIds[i], state.TypeId, StringComparison.Ordinal);
                if (ImGui.Selectable(shortName(typeIds[i]), selected)) state.TypeId = typeIds[i];
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        });
    }
}
