using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Editing;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Hsm.Editor.Model;
using StructEdit.Core;

namespace Hrot.Hsm.Editor.Inspector;

/// <summary>
/// StructEdit <see cref="IImGuiFieldDrawer"/> for fields marked with
/// <see cref="HsmActionPickerAttribute"/>. Lists action function names from the
/// active HSM asset's transitions + global transitions.
/// </summary>
public sealed class HsmActionPickerDrawer : IImGuiFieldDrawer, IPickerListSource
{
    private readonly HsmAsset _asset;

    public HsmActionPickerDrawer(HsmAsset asset)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
    }

    public Type TargetType => typeof(string);

    /// <summary>Returns all distinct action function names from the asset's transitions.</summary>
    public IReadOnlyList<string> GetItems()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
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
        return names.OrderBy(n => n).ToList();
    }

    /// <inheritdoc/>
    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;
        return HsmPickerHelper.RenderCombo(ref value, "##hsmact", GetItems());
    }
}

/// <summary>Internal rendering helpers shared by all HSM picker drawers.</summary>
internal static class HsmPickerHelper
{
    internal static bool RenderCombo(ref object value, string id, IReadOnlyList<string> items)
    {
        var current = value as string ?? string.Empty;
        bool changed = false;
        if (ImGuiNET.ImGui.BeginCombo(id, current))
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
                if (ImGuiNET.ImGui.Selectable(name, sel) && !sel)
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

    public HsmGuardPickerDrawer(HsmAsset asset)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
    }

    public Type TargetType => typeof(string);

    public IReadOnlyList<string> GetItems()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in _asset.AllTransitions)
            if (!string.IsNullOrEmpty(t.GuardFunction)) names.Add(t.GuardFunction!);
        foreach (var g in _asset.AllGlobalTransitions)
            if (!string.IsNullOrEmpty(g.GuardFunction)) names.Add(g.GuardFunction!);
        return names.OrderBy(n => n).ToList();
    }

    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;
        return HsmPickerHelper.RenderCombo(ref value, "##hsmguard", GetItems());
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
    ///         <see cref="HsmStateSelectorAttribute"/>, and <see cref="HsmEventPickerAttribute"/>.</item>
    ///   <item>A <see cref="HsmSyncGroupPickerDrawer"/> keyed by <c>typeof(ushort)</c> for
    ///         sync-group fields.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyDictionary<Type, IImGuiFieldDrawer> BuildDrawers(HsmAsset asset)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));

        var composite = new HsmCompositeStringDrawer()
            .Register<HsmActionPickerAttribute>(new HsmActionPickerDrawer(asset))
            .Register<HsmGuardPickerAttribute>(new HsmGuardPickerDrawer(asset))
            .Register<HsmStateSelectorAttribute>(new HsmStateSelectorDrawer(asset))
            .Register<HsmEventPickerAttribute>(new HsmEventPickerDrawer(asset));

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
    private readonly Dictionary<Type, IImGuiFieldDrawer> _byAttribute = new();

    public Type TargetType => typeof(string);

    public HsmCompositeStringDrawer Register<TAttribute>(IImGuiFieldDrawer drawer) where TAttribute : Attribute
    {
        _byAttribute[typeof(TAttribute)] = drawer;
        return this;
    }

    public IImGuiFieldDrawer? Resolve(EditNode node)
    {
        if (node is null) return null;
        foreach (var attr in node.Metadata.CustomAttributes)
        {
            if (_byAttribute.TryGetValue(attr.GetType(), out var drawer))
                return drawer;
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
