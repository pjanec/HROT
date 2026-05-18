using System.Globalization;
using System.Text;
using System.Text.Json;
using StructEdit.Core;

namespace StructEdit.Json;

/// <summary>
/// Serializes and deserializes the current state of an <see cref="EditDocument"/> to/from JSON.
/// </summary>
/// <remarks>
/// JSON schema (version 1.0):
/// <code>
/// {
///   "structedit_version": "1.0",
///   "rootTypeName": "My.NS.MyType, MyAssembly, ...",
///   "scope": "$",
///   "nodes": [ ... ]
/// }
/// </code>
/// The <c>nodes</c> array is a flat list of serializable entries.  Container nodes
/// (Struct / Class / Record) are not emitted as entries — their leaf descendants are.
/// DynamicArray nodes carry <c>count</c> and a <c>children</c> array (one entry per element).
/// InlineArray and FixedBuffer nodes carry a <c>values</c> array.
/// </remarks>
public static class EditDocumentJsonSerializer
{
    private const string SchemaVersion = "1.0";

    // ── Serialize ──────────────────────────────────────────────────────────

    /// <summary>
    /// Serializes the current binding state of <paramref name="document"/> to a JSON string.
    /// </summary>
    public static string Serialize(EditDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var options = new JsonWriterOptions { Indented = true };
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms, options);

        writer.WriteStartObject();
        writer.WriteString("structedit_version", SchemaVersion);
        writer.WriteString("rootTypeName", document.RootComponentType.AssemblyQualifiedName);
        writer.WriteString("scope", document.Root.JsonPath);

        writer.WriteStartArray("nodes");
        WriteChildrenFlat(document.Root, writer);
        writer.WriteEndArray();

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Recurse into a container node and write serializable entries for all leaf/array descendants.
    /// Container nodes themselves produce no entry; only their serializable descendants do.
    /// </summary>
    private static void WriteChildrenFlat(EditNode node, Utf8JsonWriter writer)
    {
        foreach (var child in node.Children)
            WriteNode(child, writer);
    }

    private static void WriteNode(EditNode node, Utf8JsonWriter writer)
    {
        switch (node.Kind)
        {
            case EditNodeKind.Struct:
            case EditNodeKind.Class:
            case EditNodeKind.Record:
            case EditNodeKind.SelectionRoot:
            case EditNodeKind.BufferView:
                // Container: recurse without emitting an entry for the container itself
                WriteChildrenFlat(node, writer);
                return;

            case EditNodeKind.DynamicArray:
                WriteDynamicArrayEntry(node, writer);
                return;

            case EditNodeKind.InlineArray:
            case EditNodeKind.FixedBuffer:
                WriteContainerValuesEntry(node, writer);
                return;

            case EditNodeKind.Unsupported:
            case EditNodeKind.Custom:
            case EditNodeKind.Union:
                // Skip nodes without serializable values
                return;

            default:
                WriteLeafEntry(node, writer);
                return;
        }
    }

    private static void WriteLeafEntry(EditNode node, Utf8JsonWriter writer)
    {
        if (node.Binding is null) return;

        writer.WriteStartObject();
        writer.WriteString("path", node.JsonPath);
        writer.WriteString("kind", node.Kind.ToString());
        writer.WritePropertyName("value");
        WriteValue(writer, node.Binding.GetBoxed(), node.ClrType);
        writer.WriteEndObject();
    }

    private static void WriteDynamicArrayEntry(EditNode node, Utf8JsonWriter writer)
    {
        if (node.Binding is not IContainerBinding cb) return;

        var elementType = ResolveDynamicArrayElementType(node);

        writer.WriteStartObject();
        writer.WriteString("path", node.JsonPath);
        writer.WriteString("kind", "DynamicArray");
        writer.WriteNumber("count", cb.Count);
        writer.WriteStartArray("children");
        for (int i = 0; i < cb.Count; i++)
        {
            var elemValue = cb.GetElementBinding(i).GetBoxed();
            writer.WriteStartObject();
            writer.WriteNumber("index", i);
            writer.WritePropertyName("value");
            WriteValue(writer, elemValue, elementType);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteContainerValuesEntry(EditNode node, Utf8JsonWriter writer)
    {
        if (node.Binding is not IContainerBinding cb || cb.Count == 0) return;

        var elemType = cb.GetElementBinding(0).ValueType;

        writer.WriteStartObject();
        writer.WriteString("path", node.JsonPath);
        writer.WriteString("kind", node.Kind.ToString());
        writer.WriteStartArray("values");
        for (int i = 0; i < cb.Count; i++)
            WriteValue(writer, cb.GetElementBinding(i).GetBoxed(), elemType);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value, Type valueType)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        if (valueType.IsEnum) { writer.WriteStringValue(Enum.GetName(valueType, value)); return; }
        if (valueType == typeof(Guid)) { writer.WriteStringValue(((Guid)value).ToString("D")); return; }
        if (valueType == typeof(DateTime)) { writer.WriteStringValue(((DateTime)value).ToString("O")); return; }
        if (valueType == typeof(bool)) { writer.WriteBooleanValue((bool)value); return; }
        if (valueType == typeof(string)) { writer.WriteStringValue((string?)value); return; }
        if (valueType == typeof(float)) { writer.WriteNumberValue((float)value); return; }
        if (valueType == typeof(double)) { writer.WriteNumberValue((double)value); return; }
        if (valueType == typeof(int)) { writer.WriteNumberValue((int)value); return; }
        if (valueType == typeof(uint)) { writer.WriteNumberValue((uint)value); return; }
        if (valueType == typeof(long)) { writer.WriteNumberValue((long)value); return; }
        if (valueType == typeof(ulong)) { writer.WriteNumberValue((ulong)value); return; }
        if (valueType == typeof(short)) { writer.WriteNumberValue((short)value); return; }
        if (valueType == typeof(ushort)) { writer.WriteNumberValue((ushort)value); return; }
        if (valueType == typeof(byte)) { writer.WriteNumberValue((byte)value); return; }
        if (valueType == typeof(sbyte)) { writer.WriteNumberValue((sbyte)value); return; }
        if (valueType == typeof(decimal)) { writer.WriteNumberValue((decimal)value); return; }
        writer.WriteStringValue(value.ToString()); // fallback
    }

    /// <summary>
    /// Extracts the element type for a DynamicArray node from its CLR type,
    /// falling back to the first element's <see cref="IValueBinding.ValueType"/> when needed.
    /// </summary>
    private static Type ResolveDynamicArrayElementType(EditNode node)
    {
        var t = node.ClrType;
        if (t.IsArray) return t.GetElementType()!;
        if (t.IsGenericType) return t.GetGenericArguments()[0];

        // Fallback: infer from first element (Count must be > 0 at this point)
        if (node.Binding is IContainerBinding cb && cb.Count > 0)
            return cb.GetElementBinding(0).ValueType;

        return typeof(object);
    }

    // ── Deserialize ────────────────────────────────────────────────────────

    /// <summary>
    /// Deserializes <paramref name="json"/> into the current binding state of
    /// <paramref name="document"/>.  Does NOT rebuild the document tree — call
    /// <see cref="IEditSession.MarkStructuralChange"/> and
    /// <see cref="IEditSession.RebuildDocument"/> if structural changes (resize) occurred.
    /// </summary>
    /// <exception cref="EditJsonMismatchException">
    /// When the JSON schema version or root type name don't match the document.
    /// </exception>
    public static void Deserialize(string json, EditDocument document)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(document);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 1. Validate schema version
        if (!root.TryGetProperty("structedit_version", out var versionEl)
            || versionEl.GetString() != SchemaVersion)
        {
            var found = root.TryGetProperty("structedit_version", out var v) ? v.GetString() : "<missing>";
            throw new EditJsonMismatchException(
                "structedit_version",
                $"JSON schema version mismatch. Expected '{SchemaVersion}', found '{found}'.");
        }

        // 2. Validate root type name
        if (!root.TryGetProperty("rootTypeName", out var typeEl))
            throw new EditJsonMismatchException(
                "rootTypeName", "JSON is missing the 'rootTypeName' property.");

        var jsonType = typeEl.GetString();
        var docType  = document.RootComponentType.AssemblyQualifiedName;
        if (jsonType != docType)
            throw new EditJsonMismatchException(
                "rootTypeName",
                $"Type name mismatch. JSON declares '{jsonType}', document expects '{docType}'.");

        // 3. Build path → node map from the current document
        var pathMap = new Dictionary<string, EditNode>(StringComparer.Ordinal);
        BuildPathMap(document.Root, pathMap);

        // 4. Apply node values
        if (root.TryGetProperty("nodes", out var nodesEl)
            && nodesEl.ValueKind == JsonValueKind.Array)
        {
            ProcessNodes(nodesEl, pathMap);
        }
    }

    private static void BuildPathMap(EditNode node, Dictionary<string, EditNode> map)
    {
        map[node.JsonPath] = node;
        foreach (var child in node.Children)
            BuildPathMap(child, map);
    }

    private static void ProcessNodes(JsonElement nodesEl, Dictionary<string, EditNode> pathMap)
    {
        foreach (var nodeEl in nodesEl.EnumerateArray())
        {
            if (!nodeEl.TryGetProperty("path", out var pathEl)) continue;
            var path = pathEl.GetString();
            if (path is null || !pathMap.TryGetValue(path, out var docNode)) continue;

            // Determine kind from JSON (with fallback to document node kind)
            EditNodeKind kind = docNode.Kind;
            if (nodeEl.TryGetProperty("kind", out var kindEl)
                && kindEl.GetString() is string kindStr)
                Enum.TryParse(kindStr, out kind);

            switch (kind)
            {
                case EditNodeKind.DynamicArray:
                    ApplyDynamicArray(nodeEl, docNode);
                    break;

                case EditNodeKind.InlineArray:
                case EditNodeKind.FixedBuffer:
                    ApplyContainerValues(nodeEl, docNode);
                    break;

                case EditNodeKind.Struct:
                case EditNodeKind.Class:
                case EditNodeKind.Record:
                case EditNodeKind.SelectionRoot:
                case EditNodeKind.BufferView:
                    // Container entries carry no direct value; skip (children were flattened)
                    break;

                default:
                    // Leaf: apply value
                    if (docNode.Binding is not null
                        && nodeEl.TryGetProperty("value", out var valEl))
                    {
                        var value = ConvertValue(valEl, docNode.ClrType);
                        docNode.Binding.SetBoxed(value);
                    }
                    break;
            }
        }
    }

    private static void ApplyDynamicArray(JsonElement nodeEl, EditNode docNode)
    {
        if (docNode.Binding is not IContainerBinding cb) return;

        // Resize if needed
        if (nodeEl.TryGetProperty("count", out var countEl))
        {
            int newCount = countEl.GetInt32();
            if (newCount != cb.Count)
                cb.Resize(newCount);
        }

        // Set element values
        if (!nodeEl.TryGetProperty("children", out var childrenEl)
            || childrenEl.ValueKind != JsonValueKind.Array)
            return;

        var elementType = ResolveDynamicArrayElementType(docNode);
        foreach (var child in childrenEl.EnumerateArray())
        {
            if (!child.TryGetProperty("index", out var idxEl)) continue;
            if (!child.TryGetProperty("value", out var valEl)) continue;
            int idx = idxEl.GetInt32();
            if (idx < 0 || idx >= cb.Count) continue;
            var value = ConvertValue(valEl, elementType);
            cb.GetElementBinding(idx).SetBoxed(value);
        }
    }

    private static void ApplyContainerValues(JsonElement nodeEl, EditNode docNode)
    {
        if (docNode.Binding is not IContainerBinding cb || cb.Count == 0) return;
        if (!nodeEl.TryGetProperty("values", out var valuesEl)
            || valuesEl.ValueKind != JsonValueKind.Array)
            return;

        var elemType = cb.GetElementBinding(0).ValueType;
        int i = 0;
        foreach (var val in valuesEl.EnumerateArray())
        {
            if (i >= cb.Count) break;
            cb.GetElementBinding(i).SetBoxed(ConvertValue(val, elemType));
            i++;
        }
    }

    private static object? ConvertValue(JsonElement element, Type targetType)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (targetType.IsEnum) return Enum.Parse(targetType, element.GetString()!);
        if (targetType == typeof(Guid)) return Guid.Parse(element.GetString()!);
        if (targetType == typeof(DateTime))
            return DateTime.Parse(element.GetString()!, null, DateTimeStyles.RoundtripKind);
        if (targetType == typeof(bool)) return element.GetBoolean();
        if (targetType == typeof(string)) return element.GetString();
        if (targetType == typeof(float)) return (float)element.GetDouble();
        if (targetType == typeof(double)) return element.GetDouble();
        if (targetType == typeof(int)) return element.GetInt32();
        if (targetType == typeof(uint)) return element.GetUInt32();
        if (targetType == typeof(long)) return element.GetInt64();
        if (targetType == typeof(ulong)) return element.GetUInt64();
        if (targetType == typeof(short)) return (short)element.GetInt32();
        if (targetType == typeof(ushort)) return (ushort)element.GetInt32();
        if (targetType == typeof(byte)) return (byte)element.GetInt32();
        if (targetType == typeof(sbyte)) return (sbyte)element.GetInt32();
        if (targetType == typeof(decimal)) return element.GetDecimal();
        throw new NotSupportedException(
            $"Cannot convert JSON value to CLR type '{targetType.FullName}'.");
    }
}
