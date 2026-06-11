using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using Fdp.Presentation.Icons;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// Main-toolbar section that renders one toggle per perspective as a radio group
/// (exactly one toggled = <see cref="WindowManager.CurrentPerspective"/>). Clicking a non-active
/// perspective calls <see cref="WindowManager.SwitchPerspective"/>.
/// </summary>
/// <remarks>
/// <para>Face resolution (§8.1):</para>
/// <list type="bullet">
///   <item>If the perspective has a resolvable <c>IconKey</c> (via
///         <see cref="WindowManager.GetPerspectiveIconKey"/> + <see cref="IIconProvider.TryGet"/>),
///         the entry renders a <see cref="IconWidgets.ToggleIcon(in IconHandle, string, Vector2, ref bool, bool, Vector4?)"/>.</item>
///   <item>When the key is missing or unresolvable, the entry falls back to a text-label button.</item>
/// </list>
/// <para>
/// <b>Testable seams:</b> <see cref="BuildRadioModel"/> produces a pure data model
/// (no ImGui calls); <see cref="OnSelect"/> dispatches to <see cref="WindowManager.SelectPerspective"/>.
/// </para>
/// </remarks>
public sealed class PerspectiveToolbarSection
{
    private readonly WindowManager _wm;
    private readonly IIconProvider _iconProvider;
    private static readonly Vector2 DefaultIconSize = new(64f, 64f);

    /// <summary>
    /// Creates the section and self-registers a toolbar entry with
    /// <paramref name="toolbar"/>.
    /// </summary>
    /// <param name="wm">The window manager — source of perspectives and current state.</param>
    /// <param name="iconProvider">Resolves <c>IconKey</c> → <see cref="IconHandle"/>.</param>
    /// <param name="toolbar">Target toolbar to register the render delegate into.</param>
    /// <param name="sortOrder">Ascending sort order within the toolbar.</param>
    /// <param name="perspective">
    /// Optional perspective filter for the toolbar entry. When <c>null</c> the entry
    /// is global (always visible). When set, the entry only renders when the active
    /// perspective matches.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when any required argument is null.</exception>
    public PerspectiveToolbarSection(
        WindowManager wm,
        IIconProvider iconProvider,
        MainToolbarManager toolbar,
        int sortOrder,
        string? perspective = null)
    {
        _wm = wm ?? throw new ArgumentNullException(nameof(wm));
        _iconProvider = iconProvider ?? throw new ArgumentNullException(nameof(iconProvider));

        ArgumentNullException.ThrowIfNull(toolbar);

        toolbar.RegisterEntry("PerspectiveGroup", sortOrder, DefaultIconSize.Y, Render, perspective);
    }

    // ── Testable seams ────────────────────────────────────────────────────

    /// <summary>
    /// Pure data model for one perspective entry in the radio group.
    /// No ImGui calls — headlessly testable (§8.1).
    /// </summary>
    /// <param name="Perspective">The perspective name.</param>
    /// <param name="IsToggled">
    /// <c>true</c> when this perspective is the <see cref="WindowManager.CurrentPerspective"/>.
    /// Exactly one entry in the list has this set.
    /// </param>
    /// <param name="HasIcon">
    /// <c>true</c> when the perspective has a resolvable <c>IconKey</c>;
    /// <c>false</c> when the key is missing or the <see cref="IIconProvider"/> cannot resolve it
    /// (triggers a text-label fallback in the render path).
    /// </param>
    public readonly record struct PerspectiveRadioEntry(
        string Perspective,
        bool IsToggled,
        bool HasIcon
    );

    /// <summary>
    /// Builds the radio-group model from the current <see cref="WindowManager"/> state.
    /// Returns one entry per distinct perspective, sorted, with toggled and icon-resolution flags.
    /// </summary>
    public IReadOnlyList<PerspectiveRadioEntry> BuildRadioModel()
    {
        var perspectives = _wm.GetPerspectives();
        var result = new List<PerspectiveRadioEntry>(perspectives.Count);

        foreach (var p in perspectives)
        {
            bool isToggled = _wm.IsPerspectiveActive(p);
            string? iconKey = _wm.GetPerspectiveIconKey(p);
            bool hasIcon = iconKey != null && _iconProvider.TryGet(iconKey, out _);

            result.Add(new PerspectiveRadioEntry(p, isToggled, hasIcon));
        }

        return result;
    }

    /// <summary>
    /// Selects a perspective by calling <see cref="WindowManager.SelectPerspective"/>.
    /// This is the testable dispatch seam — toolbar button clicks call this.
    /// </summary>
    public void OnSelect(string perspective)
        => _wm.SelectPerspective(perspective);

    // ── Render ────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders one <c>ToggleIcon</c> per perspective as a radio group.
    /// Must be called inside an active ImGui frame (registered as the toolbar entry's
    /// render delegate).
    /// </summary>
    public void Render()
    {
        var model = BuildRadioModel();

        for (int i = 0; i < model.Count; i++)
        {
            var entry = model[i];

            if (i > 0)
                Gui.SameLine();

            if (entry.HasIcon)
            {
                RenderToggleIconEntry(entry);
            }
            else
            {
                RenderTextFallbackEntry(entry);
            }
        }
    }

    // ── Private render helpers ────────────────────────────────────────────

    /// <summary>
    /// Renders a single perspective as a <see cref="IconWidgets.ToggleIcon(in IconHandle, string, Vector2, ref bool, bool, Vector4?)"/>
    /// with radio-group behaviour: the toggled (active) entry stays toggled;
    /// clicking a non-active entry switches to that perspective.
    /// </summary>
    private void RenderToggleIconEntry(PerspectiveRadioEntry entry)
    {
        string iconKey = _wm.GetPerspectiveIconKey(entry.Perspective)!;
        if (!_iconProvider.TryGet(iconKey, out var iconHandle))
            return; // defensive — should not happen since BuildRadioModel already checked

        // Radio-group behaviour: ToggleIcon flips the ref bool on click.
        // We capture the toggled state BEFORE calling ToggleIcon and correct
        // post-hoc if the operator clicked the already-active entry (no-op).
        bool wasToggled = entry.IsToggled;
        bool isToggled = wasToggled;

        bool clicked = IconWidgets.ToggleIcon(in iconHandle, $"##persp_{entry.Perspective}",
            DefaultIconSize, ref isToggled, enabled: true);

        if (clicked)
        {
            if (wasToggled)
            {
                // Clicked the already-active entry — restore toggle (no-op).
                isToggled = true;
            }
            else
            {
                // Clicked a non-active entry — switch to it.
                OnSelect(entry.Perspective);
                isToggled = true;
            }
        }

        // Tooltip
        if (Gui.IsItemHovered())
            Gui.SetTooltip(entry.Perspective);
    }

    /// <summary>
    /// Renders a text-label button fallback for a perspective whose <c>IconKey</c>
    /// is missing or unresolvable.
    /// </summary>
    private void RenderTextFallbackEntry(PerspectiveRadioEntry entry)
    {
        string label = entry.IsToggled ? $"[{entry.Perspective}]" : $" {entry.Perspective} ";

        if (Gui.Button(label, DefaultIconSize) && !entry.IsToggled)
        {
            OnSelect(entry.Perspective);
        }

        if (Gui.IsItemHovered())
            Gui.SetTooltip(entry.Perspective);
    }
}
