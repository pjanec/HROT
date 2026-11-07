using System.Collections;
using NodeEditor.Core.Widgets;
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
    /// <summary>The FormGrid id every property tree shares (so every component's tree lines up on ONE name column).</summary>
    public const string TableFormId = "##ptree";

    /// <summary>The name column of the property tree drawn this frame (a nested table in a value cell mirrors it); 180 before any tree has been drawn.</summary>
    public static float NameColWidth { get { float w = FormGrid.ColumnWidthOf(TableFormId); return w > 0f ? w : 180f; } }

    // Process-wide member cache keyed by Type.
    private static readonly Dictionary<Type, MemberInfo[]> _memberCache = new();

    /// <summary>⭐ CE-4310 (D6) — a RAIL's eye on what the tree draws: called once per drawn row with its path, HOW it was drawn (<c>leaf</c> text · <c>renderer</c> · <c>summary</c> ·
    /// <c>fold</c> = an expandable node with nothing in the value cell) and its text. <c>null</c> in production (one reference test per row, nothing allocated).</summary>
    internal static Action<string, string, string>? RowObserver;

    /// <summary>⭐ CE-4310 (D6) — a bulk request for the nested nodes: <c>true</c> = open every foldable node (the reflector's Expand All), <c>false</c> = close them, <c>null</c> = leave them as they are.
    /// Set by <c>ComponentReflector.DrawComponents</c> around its loop (UI thread only).</summary>
    public static bool? ForceOpen;

    /// <summary>A collection of at most this many elements is open by default (a longer one is one click away — the tree stays readable).</summary>
    private const int OpenByDefaultUpTo = 16;

    /// <summary>⭐ CE-4310 — the flags of a foldable node: a nested struct is open by default (the raw tab is a debugging tool: the person asked for the fields); a collection when it is short.</summary>
    private static ImGuiTreeNodeFlags FoldFlags(object? value, int shortLimit = OpenByDefaultUpTo)
    {
        var flags = ImGuiTreeNodeFlags.SpanAvailWidth;
        if (ForceOpen is bool force) ImGuiApi.SetNextItemOpen(force, ImGuiCond.Always);
        else if (value != null && (!IsCollectionType(value.GetType()) || GetCount(value) <= shortLimit)) flags |= ImGuiTreeNodeFlags.DefaultOpen;
        return flags;
    }

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
        Render(obj, contextType, out _);
    }

    /// <summary>
    /// Renders <paramref name="obj"/> as a fully-framed property tree table and reports
    /// which field (if any) was double-clicked during this render.
    /// </summary>
    /// <param name="obj">The object to render. <c>null</c> shows a placeholder.</param>
    /// <param name="contextType">
    /// Optional ECS component / outer type used for context-specific renderer lookup.
    /// </param>
    /// <param name="doubleClickedPath">
    /// Set to the JSON path of the double-clicked row (e.g. <c>"$.Position.X"</c>);
    /// <c>null</c> when no row was double-clicked during this render.
    /// </param>
    public static void Render(object? obj, Type? contextType, out string? doubleClickedPath)
        => Render(obj, contextType, include: null, out doubleClickedPath);

    /// <summary>
    /// ⭐ <c>CE-1315</c> (Brain View §12b) — as above, drawing only the ROOT members <paramref name="include"/> accepts (e.g. a blueprint
    /// instance's <c>State</c> without its <c>Cursor</c> and <c>Params</c>, which the Brain tab shows elsewhere). Nested members are
    /// all drawn.
    /// </summary>
    public static void Render(object? obj, Type? contextType, Func<MemberInfo, bool>? include, out string? doubleClickedPath)
    {
        doubleClickedPath = null;
        if (obj == null)
        {
            ImGuiApi.TextDisabled("(null)");
            return;
        }

        // Unique ID per call within the same frame/window avoids ImGui table state collisions
        // when multiple Render() calls appear inside the same window (e.g. several component
        // headers each calling Render for their data).  The caller is responsible for
        // establishing a unique ImGui ID scope (via PushID/PopID) before calling Render().

        // ⭐ U-13 (C4): the label column is FormGrid's ONE rule (§3.3: ≤ 25 % of the width, learned from the rows via Note) — no private fixed 180 px
        if (!FormGrid.BeginTree(TableFormId))
            return;
        //ImGuiApi.TableHeadersRow();

        string? dcPath = null;
        RenderRows(obj, obj.GetType(), contextType, 0, "$", ref dcPath, include);

        FormGrid.End();

        doubleClickedPath = dcPath;
    }

    // ── Row rendering ─────────────────────────────────────────────────────────

    private static void RenderRows(object obj, Type type, Type? contextType, int depth,
        string jsonPath, ref string? doubleClickedPath, Func<MemberInfo, bool>? include = null)
    {
        if (depth >= MaxDepth) return;

        var members = GetMembers(type);

        for (int i = 0; i < members.Length; i++)
        {
            var member    = members[i];
            if (include != null && !include(member)) continue;
            string name   = member.Name;
            Type   mType  = GetMemberType(member);
            string memberPath = jsonPath + "." + name;
            object? value;

            try   { value = GetValue(member, obj); }
            catch { value = null; }

            // ⭐ CE-1315 (Brain View §12b BV-45) — a fixed list (int Count + one [InlineArray(N)] buffer, Fdp.Core.FixedListShape) shows
            //   its Count items, clamped (F2: min(max(Count,0),N)), not its whole buffer.
            if (value != null && FixedListItems.TryFrom(value, out var fixedList)) value = fixedList;

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
                    FoldFlags(value));
                FormGrid.Note(ImGuiApi.CalcTextSize(name).X + (depth + 1) * ImGuiApi.GetStyle().IndentSpacing + ImGuiApi.GetTreeNodeToLabelSpacing());
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
                FormGrid.Note(ImGuiApi.CalcTextSize(name).X + (depth + 1) * ImGuiApi.GetStyle().IndentSpacing + ImGuiApi.GetTreeNodeToLabelSpacing());
                opened = false;
            }

            // Double-click detection: only the first hit per Render call wins.
            if (doubleClickedPath == null
                && ImGuiApi.IsItemHovered()
                && ImGuiApi.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                doubleClickedPath = memberPath;

            // ── Value column ───────────────────────────────────────────
            ImGuiApi.TableSetColumnIndex(1);
            RenderValueCell(value, effectiveType, contextType, isFoldable, memberPath);

            // ── Recurse into children ──
            if (opened && value != null)
            {
                bool isExtractedCollection = false;
                if (member is FieldInfo field)
                {
                    var fixedAttr = field.GetCustomAttribute<System.Runtime.CompilerServices.FixedBufferAttribute>();
                    if (fixedAttr != null)
                    {
                        value = ExtractBuffer(value, fixedAttr.ElementType, fixedAttr.Length, field.Name);
                        effectiveType = value.GetType();
                        isExtractedCollection = true;
                    }
                    else
                    {
                        var inlineAttr = effectiveType.GetCustomAttribute<System.Runtime.CompilerServices.InlineArrayAttribute>();
                        if (inlineAttr != null)
                        {
                            var elementField = effectiveType
                                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                .FirstOrDefault();
                            if (elementField != null)
                            {
                                value = ExtractBuffer(value, elementField.FieldType, inlineAttr.Length, field.Name);
                                effectiveType = value.GetType();
                                isExtractedCollection = true;
                            }
                        }
                    }
                }

                if (isExtractedCollection || IsCollectionType(effectiveType))
                    RenderCollectionRows(value, depth + 1, memberPath, ref doubleClickedPath);
                else
                    RenderRows(value, effectiveType, contextType, depth + 1, memberPath, ref doubleClickedPath);

                ImGuiApi.TreePop();
            }
        }
    }

    private static void RenderValueCell(object? value, Type mType, Type? contextType, bool isFoldable, string path)
    {
        if (value == null)
        {
            ImGuiApi.TextDisabled("null");
            RowObserver?.Invoke(path, "null", "null");
            return;
        }

        // Try a registered custom renderer first.
        var renderer = ImGuiRendererRegistry.GetRenderer(mType, contextType);
        if (renderer != null && renderer.RenderValue(value))
        {
            RowObserver?.Invoke(path, "renderer", renderer.GetSummary(value) ?? string.Empty);
            return;
        }

        // Default rendering
        if (isFoldable)
        {
            if (value is FixedListItems fl)
            {
                ImGuiApi.TextDisabled($"[{fl.Count} of {fl.Capacity}]");
                RowObserver?.Invoke(path, "fold", $"[{fl.Count} of {fl.Capacity}]");
            }
            else if (IsCollectionType(mType) || IsCollectionType(value.GetType()))
            {
                ImGuiApi.TextDisabled($"[{GetCount(value)}]");
                RowObserver?.Invoke(path, "fold", $"[{GetCount(value)}]");
            }
            else if (renderer != null)
            {
                // Foldable node with a renderer: show summary inline in the value cell.
                string? summary = renderer.GetSummary(value);
                if (!string.IsNullOrEmpty(summary))
                    ImGuiApi.TextDisabled(summary);
                RowObserver?.Invoke(path, "summary", summary ?? string.Empty);
                // else: leave blank (default behaviour)
            }
            else RowObserver?.Invoke(path, "fold", string.Empty);   // complex struct/class — value cell is intentionally blank
        }
        else
        {
            ImGuiApi.Text(FormatLeaf(value));
            RowObserver?.Invoke(path, "leaf", FormatLeaf(value));
        }
    }

    private static void RenderCollectionRows(object collection, int depth,
        string jsonPath, ref string? doubleClickedPath)
    {
        if (depth >= MaxDepth) return;

        int idx = 0;
        int count = GetCount(collection);
        foreach (var item in (IEnumerable)collection)
        {
            string elementPath = jsonPath + "[" + idx + "]";
            bool foldable = item != null && IsFoldable(item.GetType(), item);

            ImGuiApi.TableNextRow();
            ImGuiApi.TableSetColumnIndex(0);

            bool opened;
            if (foldable)
            {
                opened = ImGuiApi.TreeNodeEx(
                    $"[{idx}]##{depth}_{idx}",
                    FoldFlags(item, shortLimit: 0) | (count <= 4 ? ImGuiTreeNodeFlags.DefaultOpen : 0));
                FormGrid.Note(ImGuiApi.CalcTextSize("[000]").X + (depth + 1) * ImGuiApi.GetStyle().IndentSpacing + ImGuiApi.GetTreeNodeToLabelSpacing());
            }
            else
            {
                ImGuiApi.TreeNodeEx(
                    $"[{idx}]##{depth}_{idx}",
                    ImGuiTreeNodeFlags.Leaf |
                    ImGuiTreeNodeFlags.NoTreePushOnOpen |
                    ImGuiTreeNodeFlags.SpanAvailWidth);
                FormGrid.Note(ImGuiApi.CalcTextSize("[000]").X + (depth + 1) * ImGuiApi.GetStyle().IndentSpacing + ImGuiApi.GetTreeNodeToLabelSpacing());
                opened = false;
            }

            // Double-click detection: only the first hit per Render call wins.
            if (doubleClickedPath == null
                && ImGuiApi.IsItemHovered()
                && ImGuiApi.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                doubleClickedPath = elementPath;

            ImGuiApi.TableSetColumnIndex(1);
            if (item == null)
                ImGuiApi.TextDisabled("null");
            else if (!foldable)
            {
                ImGuiApi.Text(FormatLeaf(item));
                RowObserver?.Invoke(elementPath, "leaf", FormatLeaf(item));
            }

            if (opened && item != null)
            {
                RenderRows(item, item.GetType(), null, depth + 1, elementPath, ref doubleClickedPath);
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
        t = Fdp.Core.SemanticValueTypes.StorageTypeOf(t);   // ⭐ S0 item 6 — a WorldPoint is a leaf as the Vector3 was
        if (t.IsPrimitive || t == typeof(string) || t.IsEnum) return true;
        if (t == typeof(decimal) || t == typeof(Guid)) return true;
        // Compact well-known structs rendered inline by built-in renderers
        if (t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4)) return true;
        if (t == typeof(Quaternion)) return true;
        // ⭐ CE-4310 (D6) — the SAME types the edit forms draw as ONE leaf (the reference / picked types: EntityRef, EntityLink, PointLink, AreaLink, WorldPoint, Bearing, RefKey) and the
        //   sim time types read as their text, not as an expandable struct of raw members
        if (Fdp.Presentation.Editing.ReferenceLeafDrawers.Handles(t) || t == typeof(Fdp.Core.SimInstant) || t == typeof(Fdp.Core.SimDuration)) return true;
        // Nullable<T> wrapping a leaf
        var u = Nullable.GetUnderlyingType(t);
        if (u != null && IsLeafType(u)) return true;
        return false;
    }

    /// <summary>⭐ <c>CE-1315</c> — a fixed list's live items (its Count, clamped to its capacity), as a collection the tree folds.</summary>
    public sealed class FixedListItems : IEnumerable
    {
        private readonly IList _items;
        public int Count => _items.Count;
        public int Capacity { get; }
        private FixedListItems(IList items, int capacity) { _items = items; Capacity = capacity; }
        public IEnumerator GetEnumerator() => _items.GetEnumerator();

        public static bool TryFrom(object value, out FixedListItems list)
        {
            list = null!;
            var t = value.GetType();
            if (!Fdp.Core.FixedListShape.TryGet(t, out var element, out _, out int capacity, out var countField, out var bufferField))
                return false;
            int count = Math.Min(Math.Max((int)countField.GetValue(value)!, 0), capacity);
            var items = Fdp.Toolkit.Diagnostics.DtoDiagnosticMapper.ReadBufferElements(bufferField.GetValue(value)!, element, count);
            list = new FixedListItems((IList)items, capacity);
            return true;
        }
    }

    private static object ExtractBuffer(object bufferStruct, Type elementType, int length, string fieldName)
    {
        // ⭐ CE-2030 — the ONE exact buffer reader (DtoDiagnosticMapper.ReadBufferElements). ⛔ This marshalled its own copy:
        //   an enum element threw (CE-476's crash, fixed in the mapper and not here) and a bool element was read 4 wide.
        var list = Fdp.Toolkit.Diagnostics.DtoDiagnosticMapper.ReadBufferElements(bufferStruct, elementType, length);
        if (fieldName == "EntityIds")
            for (int i = 0; i < list.Count; i++)
                if (TryGetPackedEntityValue(list[i], out ulong packedEntityValue))
                    list[i] = new Fdp.Core.Entity(packedEntityValue);
        return list;
    }

    private static bool TryGetPackedEntityValue(object? value, out ulong packedValue)
    {
        packedValue = 0;
        switch (value)
        {
            case ulong u64:
                packedValue = u64;
                return true;
            case long i64:
                packedValue = unchecked((ulong)i64);
                return true;
            case uint u32:
                packedValue = u32;
                return true;
            case int i32:
                packedValue = unchecked((ulong)i32);
                return true;
            default:
                return false;
        }
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
        value = Fdp.Core.SemanticValueTypes.ToStorage(value);   // ⭐ S0 item 6 — a WorldPoint / WorldRotation / Bearing formats as its storage value did
        if (value is float  f) return f.ToString("G6");
        if (value is double d) return d.ToString("G6");
        if (value is Vector2 v2) return $"[{v2.X:G4}, {v2.Y:G4}]";
        if (value is Vector3 v3) return $"[{v3.X:G4}, {v3.Y:G4}, {v3.Z:G4}]";
        if (value is Vector4 v4) return $"[{v4.X:G4}, {v4.Y:G4}, {v4.Z:G4}, {v4.W:G4}]";
        if (value is Quaternion q)
        {
            // Delegate to quaternion renderer if available
            var r = ImGuiRendererRegistry.GetRenderer(typeof(Quaternion));
            if (r != null) return r.GetSummary(value) ?? value!.ToString() ?? "null";
        }
        return value!.ToString() ?? "null";
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
