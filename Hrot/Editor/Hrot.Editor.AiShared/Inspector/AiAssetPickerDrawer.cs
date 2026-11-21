using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Catalog;
using Fdp.Presentation.Editing;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Inspector;

/// <summary>
/// ⭐⭐⭐ <b>The one field drawer that offers ASSETS.</b> Fields marked
/// <see cref="AiAssetPickerAttribute"/> render as a dropdown of the catalogue's assets of that kind.
/// 📄 <c>AI_Editor_Shared_Infrastructure.md</c> §7.1a.
///
/// <para>⭐ <b>No new catalogue member was needed:</b> <see cref="IAssetCatalog.All"/> and its
/// <c>Changed</c> event already exist, so this is a filter over a live list.</para>
///
/// <para>⚠⚠ <b>The list is read PER DRAW, never snapshotted.</b> 🔒 The <c>CE-343</c> lesson: a
/// catalogue captured at construction pins whatever was loaded then, and both hosts build their
/// pickers during initialisation — before the contributors have finished populating. ⛔ A cached
/// list here would show an empty dropdown for the life of the process, which is exactly the class
/// of defect this programme keeps finding.</para>
///
/// <para>⭐ Headless-safe, like every picker here: <see cref="GetItems"/> works with no ImGui context
/// so the item list is testable without a window, and <see cref="DrawInput"/> guards on the context.</para>
/// </summary>
public sealed class AiAssetPickerDrawer : IImGuiFieldDrawer, IPickerListSource
{
    private readonly IAssetCatalog _catalog;
    private readonly AssetKind     _kind;
    private readonly PickerField   _field;

    /// <param name="field">⭐ <c>CE-1008</c> (§7.1b) — the searchable choice field; null ⇒ a plain combo.</param>
    public AiAssetPickerDrawer(IAssetCatalog catalog, AssetKind kind, PickerField? field = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _kind    = kind;
        _field   = field ?? new PickerField(null);
    }

    /// <summary>⭐ CE-1008 — the choice field (the forwarding rail reads it).</summary>
    public PickerField Field => _field;

    public Type TargetType => typeof(string);

    /// <summary>
    /// ⭐ The names of every catalogue asset of this picker's kind, sorted.
    /// ⚠ Read live — see the class remarks on why this is not cached.
    /// </summary>
    public IReadOnlyList<string> GetItems()
        => _catalog.All
                   .Where(a => a.Kind == _kind)
                   .Select(a => a.Name)
                   .Where(n => !string.IsNullOrWhiteSpace(n))
                   .Distinct(StringComparer.Ordinal)
                   .OrderBy(n => n, StringComparer.Ordinal)
                   .ToList();

    /// <inheritdoc/>
    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;

        var current = value as string ?? string.Empty;
        var items   = GetItems();

        if (items.Count == 0)
        {
            // ⚠ Say WHICH kind is missing. "(none)" sends the reader to the wrong place — the
            //   catalogue may be fine and simply hold no asset of this kind yet.
            ImGuiNET.ImGui.TextDisabled($"(no {_kind} assets in the catalog)");
            return false;
        }

        // ⭐ An explicit "none" entry (the field's own): a hosted subtree is OPTIONAL, and a designer must be able to UNSET one.
        // ⭐ CE-1008 — the ONE searchable picker (a plain combo where the host passed none).
        bool changed = false;
        if (_field.Draw("aiassetpicker", node, current, PickerChoices.Flat(items), $"{_kind} asset", out var picked))
        {
            value   = picked ?? string.Empty;
            changed = true;
        }

        // ⚠ A reference whose asset is absent must stay VISIBLE, not silently blank: the combo shows
        //   the stored name even when it is not in the list, and this says why.
        if (!string.IsNullOrEmpty(current) && !items.Contains(current, StringComparer.Ordinal))
            ImGuiNET.ImGui.TextDisabled($"⚠ '{current}' is not in the catalog");

        return changed;
    }
}
