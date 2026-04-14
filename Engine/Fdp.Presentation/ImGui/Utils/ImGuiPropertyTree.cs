using System.Collections;
using System.Numerics;
using System.Reflection;
using Fdp.Presentation.Renderers;
using ImGuiNET;
using ImGuiApi = ImGuiNET.ImGui;

namespace Fdp.Presentation.Utils;

/// <summary>
/// Shared utility that renders any object's public fields and properties as a
/// two-column hierarchical ImGui table tree (Property | Value).
///
/// <para><b>Rendering rules:</b>
/// <list type="bullet">
///   <item>Primitive / string / enum values are <em>leaf</em> rows (no expand arrow).</item>
///   <item>Well-known compact types (Vector2/3/4, Quaternion) rendered inline via
///   built-in <see cref="IImGuiRenderer"/>s.</item>
///   <item>Struct / class values open as collapsible tree nodes; value column is empty.</item>
///   <item>Collections show <c>[N]</c> in the value column and children are indexed rows.</item>
///   <item>Non-foldable siblings are aligned with foldable ones via
///   <c>ImGuiTreeNodeFlags.Leaf | NoTreePushOnOpen</c>.</item>
///   <item>Custom <see cref="IImGuiRenderer"/>s from <see cref="ImGuiRendererRegistry"/> are
///   consulted; returning <c>true</c> from <see cref="IImGuiRenderer.RenderValue"/> replaces
///   the default cell content.</item>
/// </list>
/// </para>
///
/// <para>The tree is rendered inside its own <c>BeginTable/EndTable</c> pair — do not nest
/// an outer table around the <see cref="Render"/> call.</para>
/// </summary>
public static class ImGuiPropertyTree
{
    private const int   MaxDepth     = 8;
    private const float NameColWidth = 180f;

    // Process-wide member cache keyed by Type.
    private static readonly Dictionary<Type, MemberInfo[]> _memberCache = new();

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Renders <paramref name="obj"/> as a fully-framed property tree table.
    /// Handles <c>BeginTable / EndTable</c> internally.
    /// </summary>
    /// <param name="obj">The object to render. <c>null</c> shows a placeholder.</param>
    /// <param name="contextType">
    /// Optional ECS component / outer type used for context-specific renderer lookup.
    /// </param>
    public static void Render(object? obj, Type? contextType = null)
    {
        if (obj == null)
        {
            ImGuiApi.TextDisabled("(null)");
            return;
        }

        // Unique ID per call within the same frame/window avoids ImGui table state collisions
        // when multiple Render() calls appear inside the same window (e.g. several component
        // headers each calling Render for their data).  The caller is responsible for
        // establishing a unique ImGui ID scope (via PushID/PopID) before calling Render().
        string tableId = "##ptree";

        if (!ImGuiApi.BeginTable(tableId, 2,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable |
            ImGuiTableFlags.SizingFixedFit))
            return;

        ImGuiApi.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, NameColWidth);
        ImGuiApi.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        ImGuiApi.TableHeadersRow();

        RenderRows(obj, obj.GetType(), contextType, 0);

        ImGuiApi.EndTable();
    }

    // ── Row rendering ─────────────────────────────────────────────────────────

    private static void RenderRows(object obj, Type type, Type? contextType, int depth)
    {
        if (depth >= MaxDepth) return;

        var members = GetMembers(type);

        for (int i = 0; i < members.Length; i++)
        {
            var member    = members[i];
            string name   = member.Name;
            Type   mType  = GetMemberType(member);
            object? value;

            try   { value = GetValue(member, obj); }
            catch { value = null; }

            Type effectiveType = value?.GetType() ?? mType;
            bool isFoldable = IsFoldable(effectiveType, value);

            ImGuiApi.TableNextRow();
            ImGuiApi.TableSetColumnIndex(0);

            // ── Name / tree node column ────────────────────────────────
            bool opened;
            if (isFoldable)
            {
                opened = ImGuiApi.TreeNodeEx(
                    $"{name}##{depth}_{i}",
                    ImGuiTreeNodeFlags.SpanAvailWidth);
            }
            else
            {
                // Leaf: always "open" visually but never pushes indent/children.
                // Using Leaf + NoTreePushOnOpen gives us aligned indent (same slot
                // as the foldable arrow) without actually pushing a new level.
                ImGuiApi.TreeNodeEx(
                    $"{name}##{depth}_{i}",
                    ImGuiTreeNodeFlags.Leaf |
                    ImGuiTreeNodeFlags.NoTreePushOnOpen |
                    ImGuiTreeNodeFlags.SpanAvailWidth);
                opened = false;
            }

            // ── Value column ───────────────────────────────────────────
            ImGuiApi.TableSetColumnIndex(1);
            RenderValueCell(value, effectiveType, contextType, isFoldable);

            // ── Recurse into children ──
            if (opened && value != null)
            {
                if (IsCollectionType(effectiveType))
                    RenderCollectionRows(value, depth + 1);
                else
                    RenderRows(value, effectiveType, contextType, depth + 1);

                ImGuiApi.TreePop();
            }
        }
    }

    private static void RenderValueCell(object? value, Type mType, Type? contextType, bool isFoldable)
    {
        if (value == null)
        {
            ImGuiApi.TextDisabled("null");
            return;
        }

        // Try a registered custom renderer first.
        var renderer = ImGuiRendererRegistry.GetRenderer(mType, contextType);
        if (renderer != null && renderer.RenderValue(value))
            return;

        // Default rendering
        if (isFoldable)
        {
            if (IsCollectionType(mType))
            {
                ImGuiApi.TextDisabled($"[{GetCount(value)}]");
            }
            else if (renderer != null)
            {
                // Foldable node with a renderer: show summary inline in the value cell.
                string? summary = renderer.GetSummary(value);
                if (!string.IsNullOrEmpty(summary))
                    ImGuiApi.TextDisabled(summary);
                // else: leave blank (default behaviour)
            }
            // else: complex struct/class — value cell is intentionally blank
        }
        else
        {
            ImGuiApi.Text(FormatLeaf(value));
        }
    }

    private static void RenderCollectionRows(object collection, int depth)
    {
        if (depth >= MaxDepth) return;

        int idx = 0;
        foreach (var item in (IEnumerable)collection)
        {
            bool foldable = item != null && IsFoldable(item.GetType(), item);

            ImGuiApi.TableNextRow();
            ImGuiApi.TableSetColumnIndex(0);

            bool opened;
            if (foldable)
            {
                opened = ImGuiApi.TreeNodeEx(
                    $"[{idx}]##{depth}_{idx}",
                    ImGuiTreeNodeFlags.SpanAvailWidth);
            }
            else
            {
                ImGuiApi.TreeNodeEx(
                    $"[{idx}]##{depth}_{idx}",
                    ImGuiTreeNodeFlags.Leaf |
                    ImGuiTreeNodeFlags.NoTreePushOnOpen |
                    ImGuiTreeNodeFlags.SpanAvailWidth);
                opened = false;
            }

            ImGuiApi.TableSetColumnIndex(1);
            if (item == null)
                ImGuiApi.TextDisabled("null");
            else if (!foldable)
                ImGuiApi.Text(FormatLeaf(item));

            if (opened && item != null)
            {
                RenderRows(item, item.GetType(), null, depth + 1);
                ImGuiApi.TreePop();
            }

            if (++idx > 500)
            {
                ImGuiApi.TableNextRow();
                ImGuiApi.TableSetColumnIndex(0);
                ImGuiApi.TextDisabled("  … (truncated at 500)");
                break;
            }
        }
    }

    // ── Type classification helpers ───────────────────────────────────────────

    /// <summary>True for types rendered inline (primitives, string, enum, compact value types).</summary>
    public static bool IsLeafType(Type t)
    {
        if (t.IsPrimitive || t == typeof(string) || t.IsEnum) return true;
        if (t == typeof(decimal) || t == typeof(Guid)) return true;
        // Compact well-known structs rendered inline by built-in renderers
        if (t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4)) return true;
        if (t == typeof(Quaternion)) return true;
        // Nullable<T> wrapping a leaf
        var u = Nullable.GetUnderlyingType(t);
        if (u != null && IsLeafType(u)) return true;
        return false;
    }

    private static bool IsCollectionType(Type t)
    {
        if (t == typeof(string)) return false;
        if (t.IsArray) return true;
        return t.GetInterfaces().Any(i =>
            i == typeof(IEnumerable) ||
            (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)));
    }

    private static bool IsFoldable(Type t, object? value)
    {
        if (value == null)            return false;
        if (IsLeafType(t))            return false;
        // Note: a custom renderer (e.g. EntityRenderer) may still want the node to be
        // expandable — it indicates this by returning false from RenderValue().  So we
        // no longer block foldability for all renderer-equipped types here.  Leaf-like
        // compact types (Vector2/3/4, Quaternion) are already handled above by IsLeafType.
        if (IsCollectionType(t))      return true;
        return GetMembers(t).Length > 0;
    }

    private static int GetCount(object collection)
    {
        if (collection is ICollection c) return c.Count;
        int n = 0;
        foreach (var _ in (IEnumerable)collection) n++;
        return n;
    }

    // ── Leaf formatting ───────────────────────────────────────────────────────

    /// <summary>
    /// Formats a leaf value as a compact string.
    /// Used both inside the tree and externally (e.g. event browser summary).
    /// </summary>
    public static string FormatLeaf(object? value)
    {
        if (value == null) return "null";
        if (value is float  f) return f.ToString("G6");
        if (value is double d) return d.ToString("G6");
        if (value is Vector2 v2) return $"[{v2.X:G4}, {v2.Y:G4}]";
        if (value is Vector3 v3) return $"[{v3.X:G4}, {v3.Y:G4}, {v3.Z:G4}]";
        if (value is Vector4 v4) return $"[{v4.X:G4}, {v4.Y:G4}, {v4.Z:G4}, {v4.W:G4}]";
        if (value is Quaternion q)
        {
            // Delegate to quaternion renderer if available
            var r = ImGuiRendererRegistry.GetRenderer(typeof(Quaternion));
            if (r != null) return r.GetSummary(value) ?? value.ToString() ?? "null";
        }
        return value.ToString() ?? "null";
    }

    // ── Reflection helpers (cached) ───────────────────────────────────────────

    private static MemberInfo[] GetMembers(Type type)
    {
        if (_memberCache.TryGetValue(type, out var hit)) return hit;

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

        // Skip properties for plain value types (only fields are relevant there)
        MemberInfo[] props = type.IsValueType
            ? Array.Empty<MemberInfo>()
            : (MemberInfo[])type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)
                .ToArray();

        var all = fields.Cast<MemberInfo>().Concat(props).ToArray();
        _memberCache[type] = all;
        return all;
    }

    private static Type GetMemberType(MemberInfo m) => m switch
    {
        FieldInfo    f => f.FieldType,
        PropertyInfo p => p.PropertyType,
        _              => typeof(object)
    };

    private static object? GetValue(MemberInfo m, object obj) => m switch
    {
        FieldInfo    f => f.GetValue(obj),
        PropertyInfo p => p.GetValue(obj),
        _              => null
    };
}
