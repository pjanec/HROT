using System;
using System.Collections.Generic;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Editing.Types;
using Hrot.Editor.AiShared.Catalog;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Inspector;

/// <summary>
/// ⭐⭐⭐ CE-396 / CE-3378 (N-1) — ONE field-rule editor for <see cref="AiAssetPickerAttribute"/>: the drawer is chosen PER KIND from the attribute INSTANCE the field carries
/// (<c>[AiAssetPicker(BTree)]</c> on <c>StateFacet.SubtreeName</c>, <c>[AiAssetPicker(Blueprint)]</c> on a blueprint field), each kind's <see cref="AiAssetPickerDrawer"/> built once and kept.
/// ⛔ Never one drawer hard-wired to a kind: a picker offering the wrong asset kind is worse than one offering nothing (CE-396).
/// </summary>
public sealed class AiAssetPickerRuleEditor : TypeEditor<string>, IDrawerSource
{
    private readonly IAssetCatalog _catalog;
    private readonly PickerField _field;
    private readonly Dictionary<AssetKind, AiAssetPickerDrawer> _byKind = new();

    public AiAssetPickerRuleEditor(IAssetCatalog catalog, PickerField field)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _field = field ?? throw new ArgumentNullException(nameof(field));
    }

    public override bool CanDraw => true;

    public override bool Draw(in TypeDrawContext ctx, ref object value)
        => ((IDrawerSource)this).DrawerFor(ctx.Node) is { } d && d.DrawInput(ref value, ctx.Node);

    /// <summary>The per-kind drawer for the node's attribute (built on first use), or null when the node carries none.</summary>
    IImGuiFieldDrawer? IDrawerSource.DrawerFor(EditNode node)
    {
        var attrs = node.Metadata.CustomAttributes;
        for (int i = 0; i < attrs.Count; i++)
        {
            if (attrs[i] is not AiAssetPickerAttribute a) continue;
            if (!_byKind.TryGetValue(a.Kind, out var d)) _byKind[a.Kind] = d = new AiAssetPickerDrawer(_catalog, a.Kind, _field);
            return d;
        }
        return null;
    }
}
