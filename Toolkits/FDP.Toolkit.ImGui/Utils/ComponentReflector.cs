using System.Numerics;
using System.Reflection;
using System.Linq;
using Fdp.Kernel;
using FDP.Toolkit.ImGui.Abstractions;
using ImGuiNET;

using ImGuiApi = ImGuiNET.ImGui;

namespace FDP.Toolkit.ImGui.Utils;

/// <summary>
/// Helper class for rendering component properties using reflection.
/// Supports EDITABLE fields with write-back to ECS.
/// </summary>
internal class ComponentReflector
{
    // Cache reflection info to avoid repeated lookups
    private readonly Dictionary<Type, List<FieldInfo>> _fieldCache = new();

    /// <summary>
    /// Draws all components attached to an entity with editable fields.
    /// </summary>
    public void DrawComponents(IInspectableSession session, Entity e)
    {
        var allTypes = session.GetAllComponentTypes();

        foreach (var type in allTypes)
        {
            // Generic "HasComponent" check
            if (!session.HasComponent(e, type)) continue;

            bool open = ImGuiApi.CollapsingHeader(type.Name, ImGuiTreeNodeFlags.DefaultOpen);
            
            if (open)
            {
                ImGuiApi.Indent();
                object? data = session.GetComponent(e, type);
                if (data != null)
                {
                    // Draw with edit support
                    DrawObjectProperties(data, e, session, type);
                }
                ImGuiApi.Unindent();
            }
        }
    }

    /// <summary>
    /// Draws object properties as EDITABLE ImGui widgets.
    /// Writes changes back to ECS with proper versioning.
    /// </summary>
    private void DrawObjectProperties(object obj, Entity entity, IInspectableSession session, Type componentType)
    {
        Type t = obj.GetType();
        
        if (!_fieldCache.TryGetValue(t, out var fields))
        {
            fields = new List<FieldInfo>(t.GetFields(BindingFlags.Public | BindingFlags.Instance));
            _fieldCache[t] = fields;
        }

        if (ImGuiApi.BeginTable($"props_{t.Name}_{entity.Index}", 2))
        {
            ImGuiApi.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGuiApi.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

            bool modified = false;
            
            foreach (var field in fields)
            {
                ImGuiApi.TableNextRow();
                ImGuiApi.TableSetColumnIndex(0);
                ImGuiApi.TextDisabled(field.Name);
                
                ImGuiApi.TableSetColumnIndex(1);
                var val = field.GetValue(obj);
                
                // Draw editable widget based on type
                bool fieldModified = false;
                if (!session.IsReadOnly)
                {
                    fieldModified = DrawEditableField(field, ref val, $"##{field.Name}_{entity.Index}");
                }
                else
                {
                    ImGuiApi.Text(val?.ToString() ?? "null");
                }
                
                if (fieldModified)
                {
                    field.SetValue(obj, val);
                    modified = true;
                }
            }
            
            ImGuiApi.EndTable();
            
            // Write back to ECS if any field was modified
            if (modified && !session.IsReadOnly)
            {
                session.SetComponent(entity, componentType, obj);
            }
        }
    }

    /// <summary>
    /// Draws an editable ImGui widget for a field value.
    /// Returns true if the value was modified.
    /// </summary>
    private bool DrawEditableField(FieldInfo field, ref object? value, string id)
    {
        Type fieldType = field.FieldType;
        
        // Handle different types
        if (fieldType == typeof(float))
        {
            float val = (float)(value ?? 0f);
            if (ImGuiApi.InputFloat(id, ref val))
            {
                value = val;
                return ImGuiApi.IsItemDeactivatedAfterEdit();
            }
        }
        else if (fieldType == typeof(int))
        {
            int val = (int)(value ?? 0);
            if (ImGuiApi.InputInt(id, ref val))
            {
                value = val;
                return ImGuiApi.IsItemDeactivatedAfterEdit();
            }
        }
        else if (fieldType == typeof(Vector2))
        {
            Vector2 val = (Vector2)(value ?? Vector2.Zero);
            if (ImGuiApi.InputFloat2(id, ref val))
            {
                value = val;
                return ImGuiApi.IsItemDeactivatedAfterEdit();
            }
        }
        else if (fieldType == typeof(Vector3))
        {
            Vector3 val = (Vector3)(value ?? Vector3.Zero);
            if (ImGuiApi.InputFloat3(id, ref val))
            {
                value = val;
                return ImGuiApi.IsItemDeactivatedAfterEdit();
            }
        }
        else if (fieldType == typeof(bool))
        {
            bool val = (bool)(value ?? false);
            if (ImGuiApi.Checkbox(id, ref val))
            {
                value = val;
                return true; // Checkbox is immediate
            }
        }
        else
        {
            // Fallback: Read-only text display for complex types
            ImGuiApi.Text(value?.ToString() ?? "null");
        }
        
        return false;
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
