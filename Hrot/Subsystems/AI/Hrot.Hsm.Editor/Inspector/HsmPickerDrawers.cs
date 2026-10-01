using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Editing;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Hsm.Editor.Model;
using StructEdit.Core;

namespace Hrot.Hsm.Editor.Inspector;

/// <summary>
/// Mutable context shared between <see cref="HsmFacetMapper"/> (writer) and
/// <see cref="HsmBlackboardFieldPickerDrawer"/> (reader) so the picker can filter
/// blackboard variables by the DtoType of the currently-selected transition's action FQN,
/// and so the Promote gesture can bind the newly-created variable back to the transition.
///
/// <para>
/// Lifecycle: one instance per open HSM asset.  Created alongside
/// <see cref="HsmPickerDrawerFactory.BuildDrawers"/> and the HSM facet dispatcher.
/// <see cref="HsmFacetMapper.GetTransitionFacet"/> sets <see cref="CurrentActionFqn"/> and
/// <see cref="CurrentVisualId"/> before returning; the drawer reads them in the same frame.
/// </para>
/// </summary>
public sealed class HsmFacetFqnContext
{
    /// <summary>
    /// The method FQN of the transition's action currently selected in the inspector,
    /// or <see langword="null"/> when no transition with an action is selected.
    /// </summary>
    public string? CurrentActionFqn { get; set; }

    /// <summary>
    /// The VisualId (as a GUID string) of the transition/global-transition currently selected.
    /// Written alongside <see cref="CurrentActionFqn"/> so
    /// <see cref="HsmBlackboardFieldPickerDrawer"/> can call
    /// <see cref="HsmBlackboardFieldPickerDrawer.Promote"/> with the correct identity.
    /// </summary>
    public string? CurrentVisualId { get; set; }
}

/// <summary>
/// StructEdit <see cref="IImGuiFieldDrawer"/> for fields marked with
/// <see cref="HsmActionPickerAttribute"/>.
///
/// <para>⭐⭐⭐ <b><c>CE-386</c> — it offers the REGISTERED HSM activities, not only the names this
/// asset already mentions.</b> 📄 <c>DESIGN_Hsm_Blueprint_Behaviour_Authoring.md</c> §3.3.</para>
///
/// <para>🔴 <b>What was broken.</b> The list was built ONLY from the asset's own bindings ⇒ a name
/// could be picked once it was already in use, and the FIRST binding was UNMAKEABLE from the
/// inspector. A picker that can only offer what you have already chosen is not a picker.</para>
///
/// <para>⚠ <b>The self-referential list SURVIVES as the no-catalog fallback</b> (the headless host,
/// and every fixture that constructs a drawer with one argument) — ⛔ but a production caller that
/// HAS the exporter must pass it, which is what the rail asserts. ⭐ The union is deliberate even
/// WITH a catalog: an asset may name an action the current assembly no longer exports, and dropping
/// it from the list would hide a dangling binding instead of showing it.</para>
/// </summary>
public sealed class HsmActionPickerDrawer : IImGuiFieldDrawer, IPickerListSource
{
    private readonly HsmAsset _asset;
    private readonly IActionSchemaExporter? _schema;

    public HsmActionPickerDrawer(HsmAsset asset) : this(asset, null) { }

    public HsmActionPickerDrawer(HsmAsset asset, IActionSchemaExporter? schema)
    {
        _asset  = asset ?? throw new ArgumentNullException(nameof(asset));
        _schema = schema;
    }

    public Type TargetType => typeof(string);

    /// <summary>Every registered HSM ACTIVITY, unioned with the names this asset already binds.</summary>
    public IReadOnlyList<string> GetItems()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        // ⭐ CE-386 — the catalog half. HsmActivity, not Hsm: a bare Hsm entry may be a GUARD, and
        //   offering a guard as an activity is the wrong-kind defect CE-396 fixed one layer up.
        if (_schema != null)
            foreach (var kv in _schema.All)
                if (kv.Value.Hosting.HasFlag(ActionHosting.HsmActivity))
                    names.Add(kv.Key);

        foreach (var t in _asset.AllTransitions)
        {
            if (!string.IsNullOrEmpty(t.ActionFunction)) names.Add(t.ActionFunction!);
            if (!string.IsNullOrEmpty(t.Source?.OnEntryAction)) names.Add(t.Source.OnEntryAction!);
            if (!string.IsNullOrEmpty(t.Source?.OnExitAction))  names.Add(t.Source.OnExitAction!);
        }
        foreach (var s in _asset.AllStates)
        {
            if (!string.IsNullOrEmpty(s.OnEntryAction)) names.Add(s.OnEntryAction!);
            if (!string.IsNullOrEmpty(s.OnExitAction))  names.Add(s.OnExitAction!);
            if (!string.IsNullOrEmpty(s.ActivityAction)) names.Add(s.ActivityAction!);
            if (!string.IsNullOrEmpty(s.TimerAction))   names.Add(s.TimerAction!);
        }
        foreach (var g in _asset.AllGlobalTransitions)
            if (!string.IsNullOrEmpty(g.ActionFunction)) names.Add(g.ActionFunction!);
        return names.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// ⭐ <c>CE-462</c> (E4 ④) — the SHOWN text: name + technology. ⛔ <see cref="GetItems"/> still returns
    /// the stored FQNs, so every consumer that binds by value is unchanged.
    /// </summary>
    public string Label(string fqn) => Hrot.Editor.AiShared.Blackboard.AiPrimitiveNaming.PickerLabel(fqn, _schema);

    /// <inheritdoc/>
    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;
        return HsmPickerHelper.RenderCombo(ref value, "##hsmact", GetItems(), Label);
    }
}

/// <summary>Internal rendering helpers shared by all HSM picker drawers.</summary>
internal static class HsmPickerHelper
{
    internal static bool RenderCombo(ref object value, string id, IReadOnlyList<string> items,
                                     Func<string, string>? label = null)
    {
        var current = value as string ?? string.Empty;
        label ??= s => s;
        bool changed = false;
        if (ImGuiNET.ImGui.BeginCombo(id, label(current)))
        {
            // Allow clearing.
            if (ImGuiNET.ImGui.Selectable("(none)", string.IsNullOrEmpty(current)) && !string.IsNullOrEmpty(current))
            {
                value = string.Empty;
                changed = true;
            }
            foreach (var name in items)
            {
                bool sel = name == current;
                // ⚠ "##"+name keeps the ImGui id the VALUE, so two entries with the same label stay distinct.
                if (ImGuiNET.ImGui.Selectable($"{label(name)}##{name}", sel) && !sel)
                {
                    value   = name;
                    changed = true;
                }
                if (sel) ImGuiNET.ImGui.SetItemDefaultFocus();
            }
            ImGuiNET.ImGui.EndCombo();
        }
        return changed;
    }
}

/// <summary>
/// StructEdit <see cref="IImGuiFieldDrawer"/> for fields marked with
/// <see cref="HsmGuardPickerAttribute"/>. Lists guard function names from transitions.
/// </summary>
public sealed class HsmGuardPickerDrawer : IImGuiFieldDrawer, IPickerListSource
{
    private readonly HsmAsset _asset;
    private readonly IActionSchemaExporter? _schema;

    public HsmGuardPickerDrawer(HsmAsset asset) : this(asset, null) { }

    /// <summary>⭐⭐ <c>CE-386</c> — see <see cref="HsmActionPickerDrawer"/> for why the catalog half
    /// exists and why the asset's own names stay in the union.</summary>
    public HsmGuardPickerDrawer(HsmAsset asset, IActionSchemaExporter? schema)
    {
        _asset  = asset ?? throw new ArgumentNullException(nameof(asset));
        _schema = schema;
    }

    public Type TargetType => typeof(string);

    /// <summary>Every registered HSM GUARD, unioned with the guards this asset already binds.</summary>
    public IReadOnlyList<string> GetItems()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        if (_schema != null)
            foreach (var kv in _schema.All)
                if (kv.Value.Hosting.HasFlag(ActionHosting.HsmGuard))
                    names.Add(kv.Key);

        foreach (var t in _asset.AllTransitions)
            if (!string.IsNullOrEmpty(t.GuardFunction)) names.Add(t.GuardFunction!);
        foreach (var g in _asset.AllGlobalTransitions)
            if (!string.IsNullOrEmpty(g.GuardFunction)) names.Add(g.GuardFunction!);
        return names.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// ⭐ <c>CE-462</c> (E4 ④) — the SHOWN text: name + technology. ⛔ <see cref="GetItems"/> still returns
    /// the stored FQNs, so every consumer that binds by value is unchanged.
    /// </summary>
    public string Label(string fqn) => Hrot.Editor.AiShared.Blackboard.AiPrimitiveNaming.PickerLabel(fqn, _schema);

    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;
        return HsmPickerHelper.RenderCombo(ref value, "##hsmguard", GetItems(), Label);
    }
}

/// <summary>
/// StructEdit <see cref="IImGuiFieldDrawer"/> for fields marked with
/// <see cref="HsmStateSelectorAttribute"/>. Lists state names from the asset.
/// </summary>
public sealed class HsmStateSelectorDrawer : IImGuiFieldDrawer, IPickerListSource
{
    private readonly HsmAsset _asset;

    public HsmStateSelectorDrawer(HsmAsset asset)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
    }

    public Type TargetType => typeof(string);

    public IReadOnlyList<string> GetItems()
        => _asset.AllStates
                 .Where(s => s != _asset.RootState      // not the synthetic root
                          && !s.Name.StartsWith("__"))  // not compiler-internal pseudo-roots
                 .Select(s => s.Name)
                 .OrderBy(n => n)
                 .ToList();

    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;
        return HsmPickerHelper.RenderCombo(ref value, "##hsmstate", GetItems());
    }
}

/// <summary>
/// StructEdit <see cref="IImGuiFieldDrawer"/> for fields marked with
/// <see cref="HsmEventPickerAttribute"/>. Lists event names from the asset.
/// </summary>
public sealed class HsmEventPickerDrawer : IImGuiFieldDrawer, IPickerListSource
{
    private readonly HsmAsset _asset;

    public HsmEventPickerDrawer(HsmAsset asset)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
    }

    public Type TargetType => typeof(string);

    public IReadOnlyList<string> GetItems()
        => _asset.AllEvents
                 .Select(e => e.Name)
                 .OrderBy(n => n)
                 .ToList();

    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;
        var current = value is ushort uid ? _asset.AllEvents.FirstOrDefault(e => e.EventId == uid)?.Name ?? string.Empty : string.Empty;
        bool changed = false;
        if (ImGuiNET.ImGui.BeginCombo("##hsmev", current))
        {
            foreach (var ev in _asset.AllEvents)
            {
                bool sel = ev.Name == current;
                if (ImGuiNET.ImGui.Selectable(ev.Name, sel) && !sel)
                {
                    value   = ev.EventId;
                    changed = true;
                }
                if (sel) ImGuiNET.ImGui.SetItemDefaultFocus();
            }
            ImGuiNET.ImGui.EndCombo();
        }
        return changed;
    }
}

/// <summary>
/// StructEdit <see cref="IImGuiFieldDrawer"/> for fields marked with
/// <see cref="HsmSyncGroupPickerAttribute"/>. Provides a ushort combo from
/// known sync group IDs in the asset's transitions.
/// </summary>
public sealed class HsmSyncGroupPickerDrawer : IImGuiFieldDrawer, IPickerListSource
{
    private readonly HsmAsset _asset;

    public HsmSyncGroupPickerDrawer(HsmAsset asset)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
    }

    public Type TargetType => typeof(ushort);

    public IReadOnlyList<string> GetItems()
    {
        var ids = new HashSet<ushort>();
        foreach (var t in _asset.AllTransitions)
            if (t.SyncGroupId != 0) ids.Add(t.SyncGroupId);
        return ids.OrderBy(x => x).Select(x => x.ToString()).ToList();
    }

    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;
        var current = value is ushort u ? u : (ushort)0;
        var items   = _asset.AllTransitions
                           .Where(t => t.SyncGroupId != 0)
                           .Select(t => t.SyncGroupId)
                           .Distinct()
                           .OrderBy(x => x)
                           .ToList();
        bool changed = false;
        if (ImGuiNET.ImGui.BeginCombo("##hsmsg", current == 0 ? "(none)" : current.ToString()))
        {
            if (ImGuiNET.ImGui.Selectable("(none)", current == 0) && current != 0)
            {
                value   = (ushort)0;
                changed = true;
            }
            foreach (var id in items)
            {
                bool sel = id == current;
                if (ImGuiNET.ImGui.Selectable(id.ToString(), sel) && !sel)
                {
                    value   = id;
                    changed = true;
                }
                if (sel) ImGuiNET.ImGui.SetItemDefaultFocus();
            }
            ImGuiNET.ImGui.EndCombo();
        }
        return changed;
    }
}

/// <summary>
/// StructEdit <see cref="IImGuiFieldDrawer"/> for fields marked with
/// <see cref="HsmBlackboardFieldPickerAttribute"/>. Shows field names from the
/// active HSM asset's blackboard schema, filtered by the DtoType of the currently-selected
/// transition's action FQN when an <see cref="IActionSchemaExporter"/> and
/// <see cref="HsmFacetFqnContext"/> are provided.
///
/// <para>Headless-safe: <see cref="GetItems"/> and <see cref="HasNoCompatibleVariables"/>
/// are usable without an ImGui context.</para>
///
/// <para><b>Promote→bind (B-2 / Corrective Task 0):</b> when "Promote to new variable"
/// is clicked, the drawer creates the <c>_auto_{visualId:N}</c> variable via
/// <see cref="Promote"/> and immediately sets <paramref name="value"/> to the new name,
/// returning <c>true</c> so StructEdit's normal write-back flows to
/// <see cref="HsmFacetDispatcher.ApplyFacet"/> which persists <c>ExpressionTargetField</c>.</para>
/// </summary>
public sealed class HsmBlackboardFieldPickerDrawer : IImGuiFieldDrawer, IPickerListSource
{
    private readonly HsmAsset               _asset;
    private readonly IActionSchemaExporter? _exporter;
    private readonly Func<string?>?         _fqnAccessor;
    /// <summary>Shared context that also carries the current visual id for Promote.</summary>
    private readonly HsmFacetFqnContext?    _fqnContext;

    /// <summary>
    /// Constructs a drawer without type-filtering.  All blackboard variables are shown.
    /// </summary>
    public HsmBlackboardFieldPickerDrawer(HsmAsset asset)
        : this(asset, null, null, null)
    {
    }

    /// <summary>
    /// Constructs a drawer with optional type-filtering.
    /// When <paramref name="exporter"/> and <paramref name="fqnAccessor"/> are both non-null,
    /// <see cref="GetItems"/> returns only variables whose type matches the action's DtoType.
    /// </summary>
    public HsmBlackboardFieldPickerDrawer(
        HsmAsset               asset,
        IActionSchemaExporter? exporter,
        Func<string?>?         fqnAccessor)
        : this(asset, exporter, fqnAccessor, null)
    {
    }

    /// <summary>
    /// Full constructor including the optional <paramref name="fqnContext"/> for Promote→bind.
    /// When <paramref name="fqnContext"/> is supplied the Promote gesture reads
    /// <see cref="HsmFacetFqnContext.CurrentVisualId"/> to derive the auto-variable name.
    /// </summary>
    public HsmBlackboardFieldPickerDrawer(
        HsmAsset               asset,
        IActionSchemaExporter? exporter,
        Func<string?>?         fqnAccessor,
        HsmFacetFqnContext?    fqnContext)
    {
        _asset       = asset       ?? throw new ArgumentNullException(nameof(asset));
        _exporter    = exporter;
        _fqnAccessor = fqnAccessor;
        _fqnContext  = fqnContext;
    }

    public Type TargetType => typeof(string);

    /// <summary>
    /// Returns the subset of blackboard variable names compatible with the current action's
    /// DtoType, or all names when no exporter/accessor is configured.
    /// </summary>
    public IReadOnlyList<string> GetItems()
    {
        var entries = _asset.BlackboardVariables.ToList();
        if (_exporter is null || _fqnAccessor is null)
            return entries.Select(v => v.Name).OrderBy(n => n).ToList();

        var fqn = _fqnAccessor();
        if (fqn is null)
            return entries.Select(v => v.Name).OrderBy(n => n).ToList();

        var schemaEntry = _exporter.Lookup(fqn);
        if (schemaEntry is null)
            return entries.Select(v => v.Name).OrderBy(n => n).ToList();

        return entries
            .Where(v => v.FieldType == schemaEntry.DtoType)
            .Select(v => v.Name)
            .ToList();
    }

    /// <summary>
    /// True when the current action's FQN resolves to a known schema entry but no blackboard
    /// variable matches its DtoType.  Testable without an ImGui context.
    /// </summary>
    public bool HasNoCompatibleVariables
    {
        get
        {
            if (_exporter is null || _fqnAccessor is null) return false;
            var fqn = _fqnAccessor();
            if (fqn is null) return false;
            if (_exporter.Lookup(fqn) is null) return false;
            return GetItems().Count == 0;
        }
    }

    /// <summary>True when a promote has been requested via <see cref="TriggerPromote"/>.</summary>
    public bool PromoteRequested { get; private set; }

    /// <summary>Sets <see cref="PromoteRequested"/> to true.</summary>
    public void TriggerPromote() => PromoteRequested = true;

    /// <summary>Clears <see cref="PromoteRequested"/>.</summary>
    public void ResetPromoteRequest() => PromoteRequested = false;

    /// <summary>
    /// Creates a new auto-managed blackboard variable and returns its name, or
    /// <see langword="null"/> when the FQN cannot be resolved.
    /// </summary>
    public string? Promote(string facetVisualId)
    {
        if (_exporter is null || _fqnAccessor is null) return null;
        var fqn = _fqnAccessor();
        if (fqn is null) return null;
        var entry = _exporter.Lookup(fqn);
        if (entry is null) return null;

        // ⭐ ONE implementation, shared with the BTree picker (ruling 9). ⛔ The two bodies were
        //   character-for-character identical.
        return Hrot.Editor.AiShared.Blackboard.AutoManagedVariables
                   .PromoteForSite(_asset, facetVisualId, entry.DtoType);
    }

    /// <inheritdoc/>
    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;

        var current = value as string ?? string.Empty;
        var items   = GetItems();

        if (items.Count == 0 && HasNoCompatibleVariables)
        {
            ImGuiNET.ImGui.TextDisabled("(no compatible variables)");
            if (ImGuiNET.ImGui.SmallButton("Promote to new variable"))
            {
                // Corrective Task 0 / B-2: create the variable AND bind the field in one gesture.
                // _fqnContext?.CurrentVisualId gives us the owning transition's VisualId.
                var visualId = _fqnContext?.CurrentVisualId ?? string.Empty;
                var newName  = Promote(visualId);
                if (newName is not null)
                {
                    value = newName;
                    return true;
                }
                // Fallback: queue the flag for any external consumer.
                TriggerPromote();
            }
            return false;
        }

        if (items.Count == 0)
        {
            ImGuiNET.ImGui.TextDisabled("(no blackboard fields)");
            return false;
        }

        return HsmPickerHelper.RenderCombo(ref value, "##hsmbbpicker", items);
    }
}

/// <summary>
/// Factory: builds the <see cref="IReadOnlyDictionary{Type,IImGuiFieldDrawer}"/> consumed by
/// <see cref="Hrot.Editor.AiShared.Windows.InspectorWindow.SetFacetEditService"/> for a specific
/// HSM asset. Called by EditorSubsystem from the <c>ActiveChanged</c> callback whenever the
/// active HSM document switches (SE2).
/// </summary>
public static class HsmPickerDrawerFactory
{
    /// <summary>
    /// Creates a fresh custom-drawers map for <paramref name="asset"/>.
    /// The map contains:
    /// <list type="bullet">
    ///   <item>A <see cref="HsmCompositeStringDrawer"/> keyed by <c>typeof(string)</c>, dispatching
    ///         <see cref="HsmActionPickerAttribute"/>, <see cref="HsmGuardPickerAttribute"/>,
    ///         <see cref="HsmStateSelectorAttribute"/>, <see cref="HsmEventPickerAttribute"/>,
    ///         and <see cref="HsmBlackboardFieldPickerAttribute"/>.</item>
    ///   <item>A <see cref="HsmSyncGroupPickerDrawer"/> keyed by <c>typeof(ushort)</c> for
    ///         sync-group fields.</item>
    /// </list>
    /// When <paramref name="exporter"/> and <paramref name="fqnContext"/> are provided, the
    /// <see cref="HsmBlackboardFieldPickerDrawer"/> filters variables by the current action's DtoType.
    /// </summary>
    /// <param name="catalog">
    /// ⭐⭐ §11.1a — the asset catalogue that feeds the <c>[AiAssetPicker]</c> on
    /// <c>StateFacet.SubtreeName</c>. ⚠ Optional so headless fixtures need not supply one; ⛔ a
    /// production host HAS one and must pass it, or the hosted-subtree field silently offers
    /// nothing — the exact silent-default shape this codebase keeps paying for.
    /// </param>
    public static IReadOnlyDictionary<Type, IImGuiFieldDrawer> BuildDrawers(
        HsmAsset               asset,
        IActionSchemaExporter? exporter   = null,
        HsmFacetFqnContext?    fqnContext  = null,
        Hrot.Editor.AiShared.Catalog.IAssetCatalog? catalog = null)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));

        Func<string?>? fqnAccessor = fqnContext is not null
            ? () => fqnContext.CurrentActionFqn
            : null;

        var bbDrawer = new HsmBlackboardFieldPickerDrawer(asset, exporter, fqnAccessor, fqnContext);

        // ⭐⭐⭐ CE-386 — the exporter reaches the two pickers that had been ignoring it.
        // 📐 The plumbing was already there: AiFacetPickerBinder.Rebuild:95, the ONE production
        //    site, already passes services.ActionSchema into this factory, and the factory already
        //    took it — it simply only ever reached HsmBlackboardFieldPickerDrawer. ⇒ the forwarding
        //    was never the defect; the CONSUMPTION was (design §9 rail ⑧, re-aimed).
        var composite = new HsmCompositeStringDrawer()
            .Register<HsmActionPickerAttribute>(new HsmActionPickerDrawer(asset, exporter))
            .Register<HsmGuardPickerAttribute>(new HsmGuardPickerDrawer(asset, exporter))
            .Register<HsmStateSelectorAttribute>(new HsmStateSelectorDrawer(asset))
            .Register<HsmEventPickerAttribute>(new HsmEventPickerDrawer(asset))
            .Register<HsmBlackboardFieldPickerAttribute>(bbDrawer);

        // ⭐⭐ §11.1a — the asset pickers. ⚠ Registered only when a catalogue exists: a drawer over a
        //    null catalogue could only ever draw an empty list, and an empty dropdown reads as
        //    "there are no such assets" rather than "this host wired nothing".
        // ⭐⭐⭐ CE-396 — ONE drawer PER KIND, chosen from the attribute the field carries, so
        //    `[AiAssetPicker(BTree)]` (StateFacet.SubtreeName) and `[AiAssetPicker(Blueprint)]`
        //    (CE-385's activity/guard fields) each offer their own kind. ⛔ The previous
        //    registration hard-wired BTree and would have answered for both.
        if (catalog is not null)
        {
            var byKind = new Dictionary<Hrot.Editor.AiShared.AssetKind,
                                        Hrot.Editor.AiShared.Inspector.AiAssetPickerDrawer>();
            composite.Register<Hrot.Editor.AiShared.Inspector.AiAssetPickerAttribute>(attr =>
            {
                if (!byKind.TryGetValue(attr.Kind, out var d))
                    byKind[attr.Kind] = d =
                        new Hrot.Editor.AiShared.Inspector.AiAssetPickerDrawer(catalog, attr.Kind);
                return d;
            });
        }

        return new Dictionary<Type, IImGuiFieldDrawer>
        {
            [typeof(string)] = composite,
            [typeof(ushort)] = new HsmSyncGroupPickerDrawer(asset),
        };
    }
}

/// <summary>
/// Composite string drawer for HSM: dispatches to attribute-specific sub-drawers
/// when a recognised HSM picker attribute is present on the <see cref="EditNode"/>'s field.
/// Falls through to a plain text input when no marker attribute matches.
/// </summary>
internal sealed class HsmCompositeStringDrawer : IImGuiFieldDrawer
{
    // ⭐⭐⭐ CE-396 — the registry maps an attribute TYPE to a factory over the attribute INSTANCE,
    //    not to a fixed drawer.
    //
    // 🔴 WHY IT HAD TO CHANGE. `AiAssetPickerAttribute` CARRIES A KIND (`[AiAssetPicker(BTree)]`,
    //    `[AiAssetPicker(Blueprint)]`), and the old registry keyed by type alone ⇒ whichever kind was
    //    registered first answered for EVERY kind. With only `StateFacet.SubtreeName` in the tree
    //    that was invisible; CE-385's blueprint pickers would have silently drawn the BTREE list —
    //    a dropdown full of plausible, wrong names. ⛔ A picker that offers the wrong asset kind is
    //    worse than one that offers nothing: nothing is diagnosable.
    // ⭐ Every other picker here is kind-less and registers through the constant overload unchanged.
    private readonly Dictionary<Type, Func<Attribute, IImGuiFieldDrawer?>> _byAttribute = new();

    public Type TargetType => typeof(string);

    public HsmCompositeStringDrawer Register<TAttribute>(IImGuiFieldDrawer drawer) where TAttribute : Attribute
    {
        _byAttribute[typeof(TAttribute)] = _ => drawer;
        return this;
    }

    /// <summary>⭐ <c>CE-396</c> — register a drawer chosen FROM the attribute instance, for marker
    /// attributes that carry a discriminator (today: <c>AiAssetPickerAttribute.Kind</c>).</summary>
    public HsmCompositeStringDrawer Register<TAttribute>(Func<TAttribute, IImGuiFieldDrawer?> factory)
        where TAttribute : Attribute
    {
        _byAttribute[typeof(TAttribute)] = a => factory((TAttribute)a);
        return this;
    }

    public IImGuiFieldDrawer? Resolve(EditNode node)
    {
        if (node is null) return null;
        foreach (var attr in node.Metadata.CustomAttributes)
        {
            if (_byAttribute.TryGetValue(attr.GetType(), out var factory))
                return factory(attr);
        }
        return null;
    }

    public bool DrawInput(ref object value, EditNode node)
    {
        var sub = Resolve(node);
        if (sub is not null)
            return sub.DrawInput(ref value, node);

        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;
        var s = value as string ?? string.Empty;
        if (ImGuiNET.ImGui.InputText("##str", ref s, 256))
        {
            value = s;
            return true;
        }
        return false;
    }
}
