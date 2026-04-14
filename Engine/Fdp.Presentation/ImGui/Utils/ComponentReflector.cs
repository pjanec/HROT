using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.ImGui.Abstractions;
using Fdp.Toolkit.ImGui.Renderers;
using ImGuiNET;

using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Toolkit.ImGui.Utils;

/// <summary>
/// Helper that draws all ECS components attached to an entity as collapsible headers,
/// with optional custom per-component summary and details renderers (auto-discovered
/// via <see cref="ImGuiRendererRegistry"/>).
///
/// <para><b>Collapse behaviour:</b> headers are closed by default.
/// Set <see cref="ForceExpandAll"/> or <see cref="ForceCollapseAll"/> to override once;
/// both flags are consumed automatically at the end of <see cref="DrawComponents"/>.</para>
///
/// <para><b>Details body:</b> Uses <see cref="ImGuiPropertyTree.Render"/> (hierarchical
/// read-only tree). A registered <see cref="IImGuiRenderer.RenderValue"/> returning
/// <c>true</c> replaces the tree entirely.</para>
/// </summary>
internal class ComponentReflector
{
    /// <summary>Set to <c>true</c> this frame to force-expand all component headers.</summary>
    public bool ForceExpandAll   { get; set; }

    /// <summary>Set to <c>true</c> this frame to force-collapse all component headers.</summary>
    public bool ForceCollapseAll { get; set; }

    // ── Byte-cache change detection (BD1-P6T1) ────────────────────────────────
    /// <summary>Entity whose component bytes are currently cached.</summary>
    private Entity _lastInspectedEntity = Entity.Null;

    /// <summary>
    /// Per-type byte snapshots of unmanaged components from the previous frame.
    /// Populated only for value-type (unmanaged) components; managed class components
    /// are never compared and never stored here.
    /// </summary>
    private readonly Dictionary<Type, byte[]> _unmanagedCache = new();

    /// <summary>
    /// Draws all components attached to <paramref name="e"/> as collapsible sections.
    /// Value-type (unmanaged) components whose bytes differ from the previous frame
    /// have their header drawn in <b>yellow</b> for that frame.
    /// Consumes <see cref="ForceExpandAll"/> / <see cref="ForceCollapseAll"/> after rendering.
    /// </summary>
    public void DrawComponents(IInspectableSession session, Entity e)
    {
        // ── Entity switch: invalidate the per-type byte cache ─────────────────
        if (!e.Equals(_lastInspectedEntity))
        {
            _unmanagedCache.Clear();
            _lastInspectedEntity = e;
        }

        var allTypes = session.GetAllComponentTypes().OrderBy(t => t.Name).ToList();

        int componentIndex = 0;
        foreach (var type in allTypes)
        {
            if (!session.HasComponent(e, type)) continue;

            object? data = session.GetComponent(e, type);

            // ── Byte-level change detection (value types only) ────────────────
            bool changed = false;
            if (type.IsValueType && data != null)
            {
                try
                {
                    int size = Marshal.SizeOf(type);
                    // Rent a pooled buffer to avoid per-frame native heap allocation.
                    // The buffer is returned immediately after comparison so it never
                    // escapes this scope; the cache stores a separately-owned managed copy.
                    byte[] pooled = ArrayPool<byte>.Shared.Rent(size);
                    try
                    {
                        var pinHandle = GCHandle.Alloc(pooled, GCHandleType.Pinned);
                        try
                        {
                            Marshal.StructureToPtr(data, pinHandle.AddrOfPinnedObject(), fDeleteOld: false);
                        }
                        finally
                        {
                            pinHandle.Free();
                        }

                        if (_unmanagedCache.TryGetValue(type, out var cached))
                        {
                            for (int i = 0; i < size; i++)
                            {
                                if (pooled[i] != cached[i]) { changed = true; break; }
                            }
                            // Update cached snapshot in-place (avoids a managed alloc every frame).
                            Array.Copy(pooled, cached, size);
                        }
                        else
                        {
                            // First visit: set baseline silently (no highlight on first render).
                            var baseline = new byte[size];
                            Array.Copy(pooled, baseline, size);
                            _unmanagedCache[type] = baseline;
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(pooled);
                    }
                }
                catch { /* skip types Marshal cannot measure (e.g. generics) */ }
            }
            // Managed class components (reference types): no comparison, no cache entry.

            // Push a stable unique ID scope so the "##ptree" table inside
            // ImGuiPropertyTree.Render() gets a different ImGui ID per component.
            // This prevents table-state collisions when multiple components are expanded.
            ImGuiApi.PushID(componentIndex++);

            // Apply bulk open/close request for this header
            if (ForceExpandAll)
                ImGuiApi.SetNextItemOpen(true,  ImGuiCond.Always);
            else if (ForceCollapseAll)
                ImGuiApi.SetNextItemOpen(false, ImGuiCond.Always);

            string label = BuildHeaderLabel(type, data);

            // Draw header in yellow when the component was mutated since last frame.
            if (changed)
                ImGuiApi.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 0f, 1f));

            // Headers are collapsed by default (no DefaultOpen flag)
            bool open = ImGuiApi.CollapsingHeader(label);

            if (changed)
                ImGuiApi.PopStyleColor();

            if (open && data != null)
            {
                ImGuiApi.Indent();

                var renderer = ImGuiRendererRegistry.GetRenderer(type);
                bool handled = renderer != null && renderer.RenderValue(data);

                if (!handled)
                    ImGuiPropertyTree.Render(data, contextType: type);

                ImGuiApi.Unindent();
            }

            ImGuiApi.PopID();
        }

        ForceExpandAll   = false;
        ForceCollapseAll = false;
    }

    private static string BuildHeaderLabel(Type type, object? data)
    {
        if (data == null) return type.Name;

        var renderer = ImGuiRendererRegistry.GetRenderer(type);
        string? summary = renderer?.GetSummary(data);

        if (!string.IsNullOrEmpty(summary))
            // Append ###{type.Name} to lock the ID
            return $"{type.Name}  [{summary}]###{type.Name}";

        string? auto = GetAutoSummary(data, type);
        // Append ###{type.Name} to lock the ID
        return auto != null ? $"{type.Name}  {auto}###{type.Name}" : type.Name;
    }

    private static string? GetAutoSummary(object data, Type type)
    {
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string))
            .Take(3)
            .ToArray();

        if (fields.Length == 0) return null;

        var parts = fields.Select(f =>
        {
            var v  = f.GetValue(data);
            string vs = v is float  fl ? fl.ToString("G4")
                      : v is double db ? db.ToString("G4")
                      : v?.ToString() ?? "null";
            return $"{f.Name}:{vs}";
        });

        return "(" + string.Join("  ", parts) + ")";
    }
}

/// <summary>
/// Internal shim to call generic Repository methods via reflection dynamically.
/// Uses caching to minimize reflection overhead.
/// </summary>
internal static class RepoReflector
{
    private static readonly Dictionary<Type, MethodInfo> _hasComponentCache = new();
    private static readonly Dictionary<Type, MethodInfo> _getComponentCache = new();
    private static readonly Dictionary<Type, MethodInfo> _setComponentCache = new();
    private static readonly Dictionary<Type, MethodInfo> _setManagedComponentCache = new();

    private static readonly MethodInfo _genericHasComponent;
    private static readonly MethodInfo _genericGetComponent;
    private static readonly MethodInfo _genericSetComponent;
    private static readonly MethodInfo _genericSetManagedComponent;

    static RepoReflector()
    {
        var methods = typeof(EntityRepository).GetMethods(BindingFlags.Public | BindingFlags.Instance);

        _genericHasComponent = methods.First(m => 
            m.Name == "HasComponent" && 
            m.IsGenericMethod && 
            m.GetParameters().Length == 1);

        // GetComponent returns ref readonly T, but Invoke handles it.
        // We look for GetComponent<T>(Entity)
        _genericGetComponent = methods.First(m => 
            m.Name == "GetComponent" && 
            m.IsGenericMethod && 
            m.GetParameters().Length == 1);

        // SetComponent<T>(Entity, T)
        _genericSetComponent = methods.First(m => 
            m.Name == "SetComponent" && 
            m.IsGenericMethod && 
            m.GetParameters().Length == 2);
            
        // SetManagedComponent<T>(Entity, T) - Safe Upsert for managed types
        _genericSetManagedComponent = methods.First(m =>
            m.Name == "SetManagedComponent" &&
            m.IsGenericMethod &&
            m.GetParameters().Length == 2);
    }
    
    public static bool HasComponent(EntityRepository repo, Entity e, Type t) 
    {
        if (!_hasComponentCache.TryGetValue(t, out var method))
        {
            method = _genericHasComponent.MakeGenericMethod(t);
            _hasComponentCache[t] = method;
        }
        return (bool)method.Invoke(repo, new object[] { e })!;
    }

    public static object? GetComponent(EntityRepository repo, Entity e, Type t)
    {
        if (!_getComponentCache.TryGetValue(t, out var method))
        {
            method = _genericGetComponent.MakeGenericMethod(t);
            _getComponentCache[t] = method;
        }
        return method.Invoke(repo, new object[] { e });
    }
    
    public static void SetComponent(EntityRepository repo, Entity e, Type t, object component)
    {
        if (!t.IsValueType)
        {
            // Managed type safest upsert
            if (!_setManagedComponentCache.TryGetValue(t, out var method))
            {
                method = _genericSetManagedComponent.MakeGenericMethod(t);
                _setManagedComponentCache[t] = method;
            }
            method.Invoke(repo, new object[] { e, component });
        }
        else
        {
            // Struct types (Unmanaged)
            if (!_setComponentCache.TryGetValue(t, out var method))
            {
                method = _genericSetComponent.MakeGenericMethod(t);
                _setComponentCache[t] = method;
            }
            method.Invoke(repo, new object[] { e, component });
        }
    }
}
