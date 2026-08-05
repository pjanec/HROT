using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using Hrot.Editor.AiShared;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// <see cref="IMyBlueprintModel"/> that projects the active <see cref="BlueprintAsset"/>
/// into the NodeEdit "My Blueprint" panel.
///
/// <para><b>Real sections</b> (data projected from the asset):</para>
/// <list type="bullet">
///   <item>Graphs — from <see cref="BlueprintAsset.Graphs"/></item>
///   <item>Custom Events — from <see cref="BlueprintAsset.CustomEvents"/></item>
///   <item>Variables — from <see cref="BlueprintAsset.Variables"/> (name/type/category/accent)</item>
/// </list>
///
/// <para><b>Faked/empty sections</b> (no data model yet in v1):</para>
/// <list type="bullet">
///   <item>Functions — always empty list; section header still present in fixed order</item>
///   <item>Macros — always empty list; section header still present in fixed order</item>
/// </list>
///
/// <para>
/// <b>BP-12c — no Event Dispatchers section.</b> BP-09 established that dispatchers are superseded
/// by <c>PublishEvent</c>/<c>EventEntry</c> and deleted the six dispatcher/squad node kinds from the
/// palette; nothing in the editor or the compiler consumes
/// <see cref="BlueprintAsset.EventDispatchers"/>, and no shipped asset declares one. Rather than
/// wire a create path for an abandoned concept, the section is gone. The field stays on the asset so
/// hand-authored JSON still round-trips.
/// </para>
///
/// Fixed section order per D.6.2 spec: Graphs, Functions, Macros, Custom Events, Variables.
///
/// Fire <see cref="Changed"/> when the model should be refreshed (e.g. after
/// <see cref="Retarget"/> is called with a new asset, or when the subscribed
/// <see cref="IEditableAsset.Changed"/> event fires).
/// </summary>
public sealed class BlueprintMyBlueprintModel : IMyBlueprintModel
{
    // ── Section ids (stable string keys) ─────────────────────────────────────

    public const string SectionGraphs      = "graphs";
    public const string SectionFunctions   = "functions";
    public const string SectionMacros      = "macros";
    public const string SectionCustomEvents = "customevents";
    public const string SectionVariables   = "variables";

    // ── Fixed section descriptors (D.6.2 order) ────────────────────────────

    private static readonly IReadOnlyList<MyBlueprintSectionDescriptor> _sections =
        new List<MyBlueprintSectionDescriptor>
        {
            new(SectionGraphs,       "Graphs",           0, null, false, false, null),
            new(SectionFunctions,    "Functions",        1, null, true,  true,  "editor.create-function"),
            new(SectionMacros,       "Macros",           2, null, true,  true,  "editor.create-macro"),
            new(SectionCustomEvents, "Custom Events",    3, null, true,  true,  "editor.create-custom-event"),
            new(SectionVariables,    "Variables",        4, null, true,  true,  "editor.create-variable"),
        };

    // ── State ─────────────────────────────────────────────────────────────────

    private BlueprintAsset? _asset;
    private IEditableAsset? _editableAsset;

    // ── IMyBlueprintModel ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<MyBlueprintSectionDescriptor> Sections => _sections;

    /// <inheritdoc/>
    public event System.Action? Changed;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Switch the model to project a different asset (or null to show nothing).
    /// Unsubscribes from the previous asset's <c>Changed</c> event and subscribes
    /// to the new one.  Fires <see cref="Changed"/> after retargeting.
    /// </summary>
    public void Retarget(IEditableAsset? editableAsset, BlueprintAsset? blueprintAsset)
    {
        // Unsubscribe old
        if (_editableAsset != null)
            _editableAsset.Changed -= OnAssetChanged;

        _editableAsset = editableAsset;
        _asset         = blueprintAsset;

        // Subscribe new
        if (_editableAsset != null)
            _editableAsset.Changed += OnAssetChanged;

        Changed?.Invoke();
    }

    /// <summary>
    /// Returns the items for the requested section, projected from the current asset.
    /// Returns an empty list when there is no active asset, or for the faked
    /// Functions/Macros sections.
    /// </summary>
    public IReadOnlyList<MyBlueprintItem> GetItems(string sectionId)
    {
        if (_asset == null)
            return Array.Empty<MyBlueprintItem>();

        return sectionId switch
        {
            SectionGraphs       => BuildGraphItems(),
            SectionFunctions    => Array.Empty<MyBlueprintItem>(),   // faked/empty v1
            SectionMacros       => Array.Empty<MyBlueprintItem>(),   // faked/empty v1
            SectionCustomEvents => BuildCustomEventItems(),
            SectionVariables    => BuildVariableItems(),
            _                   => Array.Empty<MyBlueprintItem>(),
        };
    }

    // ── Private builders ──────────────────────────────────────────────────────

    private IReadOnlyList<MyBlueprintItem> BuildGraphItems()
    {
        var result = new List<MyBlueprintItem>(_asset!.Graphs.Count);
        foreach (var g in _asset.Graphs)
        {
            result.Add(new MyBlueprintItem(
                ItemId:       $"graph:{g.Id}",
                SectionId:    SectionGraphs,
                DisplayName:  g.Name,
                CategoryPath: null,
                IconKey:      null,
                BadgeText:    null,
                AccentColor:  null,
                Children:     null,
                IsRenamable:  false,
                IsDeletable:  false,
                IsHostDefined: true,
                Tooltip:      null));
        }
        return result;
    }

    private IReadOnlyList<MyBlueprintItem> BuildCustomEventItems()
    {
        var result = new List<MyBlueprintItem>(_asset!.CustomEvents.Count);
        foreach (var evt in _asset.CustomEvents)
        {
            result.Add(new MyBlueprintItem(
                ItemId:       $"evt:{evt.Id}",
                SectionId:    SectionCustomEvents,
                DisplayName:  evt.Name,
                CategoryPath: null,
                IconKey:      null,
                BadgeText:    null,
                AccentColor:  null,
                Children:     null,
                IsRenamable:  true,
                IsDeletable:  true,
                IsHostDefined: false,
                Tooltip:      null));
        }
        return result;
    }

    private IReadOnlyList<MyBlueprintItem> BuildVariableItems()
    {
        var result = new List<MyBlueprintItem>(_asset!.Variables.Count);
        foreach (var v in _asset.Variables)
        {
            var accent = GetVariableAccentColor(v.Type?.TypeId ?? "");
            // FC-2/LV-4: a fixed-list variable shows its capacity as a "[N]" badge so lists are
            // recognizable at a glance in the My Blueprint tree.
            var badge = v.Type is { Capacity: > 0 } ? $"[{v.Type.Capacity}]" : null;
            result.Add(new MyBlueprintItem(
                ItemId:       $"var:{v.Id}",
                SectionId:    SectionVariables,
                DisplayName:  v.Name,
                CategoryPath: v.Category,
                IconKey:      null,
                BadgeText:    badge,
                AccentColor:  accent,
                Children:     null,
                IsRenamable:  true,
                IsDeletable:  true,
                IsHostDefined: false,
                Tooltip:      v.Tooltip));
        }
        return result;
    }

    /// <summary>
    /// Returns the accent color for a variable using the same palette as
    /// <see cref="BlueprintTypeSystem"/> so wire colors match panel dots.
    /// </summary>
    private static Vector4? GetVariableAccentColor(string typeId)
    {
        // Reuse the BlueprintTypeSystem palette directly.
        var color = BlueprintTypeSystem.GetAccentColorForTypeId(typeId);
        return color;
    }

    private void OnAssetChanged() => Changed?.Invoke();
}
