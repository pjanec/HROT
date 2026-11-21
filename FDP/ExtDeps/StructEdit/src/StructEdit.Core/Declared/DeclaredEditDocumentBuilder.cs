using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using StructEdit.Core.Bindings;
using StructEdit.Core.Plugins;

namespace StructEdit.Core.Declared;

/// <summary>One declared field: a name, its CLR type (the host resolves its own type ids — e.g. <c>ParamTypes</c> — before it gets here), and how it reads.</summary>
/// <param name="Name">The JSON member name the value is stored under.</param>
/// <param name="ClrType">The value's type — a leaf, a structure (its public fields become rows) or a list.</param>
/// <param name="DisplayName">The row's label; null ⇒ the table's humanizer.</param>
/// <param name="Unit">The value's unit, as <c>[EditUnit]</c> would give.</param>
/// <param name="Default">The value when the JSON has none (and what "changed" is measured against, S7); null ⇒ the type's default.</param>
/// <param name="ReadOnly">The row is greyed (S8).</param>
public sealed record FieldDeclaration(string Name, Type ClrType, string? DisplayName = null, string? Unit = null, JsonNode? Default = null, bool ReadOnly = false);

/// <summary>
/// ⭐ S2 (📄 docs/DESIGN_Struct_Editor.md §6 S2, §10.5 gate step 6, CE-3383) — a document whose SHAPE comes from DECLARATIONS, with its values in a <see cref="JsonObject"/>:
/// the scenario parameters, a preset's values, a blackboard — anything with no C# owner. ⭐ The same <see cref="EditNode"/> tree as the reflection builder, so the ONE table
/// draws it (the leaves by type through the same <see cref="ICustomFieldEditor"/>s, S3).
/// <para>Each top-level value is read ONCE from the JSON (with the host's <see cref="JsonSerializerOptions"/> — the file's converters, as the gate measured) and cached by its
/// binding; a set writes the JSON member at once. ⇒ a steady frame deserialises nothing. A structure's fields bind through the cached value (<c>NestedMemberBinding</c>);
/// a list is a container over the cached list (<see cref="IContainerOps"/>), written back on every change.</para>
/// <para>⚠ Not an <see cref="IEditDocumentBuilder"/>: that seam builds from a component type over an edit buffer; a declared document has neither (as-built, §2's class diagram).</para>
/// </summary>
public sealed class DeclaredEditDocumentBuilder
{
    private readonly JsonSerializerOptions _options;
    private readonly IReadOnlyDictionary<Type, ICustomFieldEditor> _fieldEditors;

    /// <param name="options">The host's JSON options (its converters — EntityRef, AreaLink …); the values are read and written with them.</param>
    /// <param name="fieldEditors">The leaf types (S3's Shape part): a type registered here is ONE leaf node, as in the reflection builder.</param>
    public DeclaredEditDocumentBuilder(JsonSerializerOptions options, IReadOnlyDictionary<Type, ICustomFieldEditor>? fieldEditors = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _fieldEditors = fieldEditors ?? new Dictionary<Type, ICustomFieldEditor>();
    }

    /// <summary>The document over <paramref name="values"/> for <paramref name="fields"/>; the root is a <see cref="EditNodeKind.Class"/> of type <see cref="JsonObject"/>.</summary>
    public EditDocument Build(IReadOnlyList<FieldDeclaration> fields, JsonObject values) => Build(fields, values, null);

    /// <summary>As <see cref="Build(IReadOnlyList{FieldDeclaration}, JsonObject)"/>, collecting the top-level bindings into <paramref name="bindings"/> (the session flushes them).</summary>
    public EditDocument Build(IReadOnlyList<FieldDeclaration> fields, JsonObject values, List<JsonMemberBinding>? bindings)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(values);
        int next = 1;
        var children = new List<EditNode>(fields.Count);
        foreach (var f in fields)
        {
            var binding = new JsonMemberBinding(values, f, _options);
            bindings?.Add(binding);
            children.Add(BuildNode(ref next, f.Name, "$." + f.Name, f.ClrType, binding, Meta(f), f.ReadOnly, depth: 0));
        }
        var root = new EditNode(new EditNodeId(0), "", "$", EditNodeKind.Class, typeof(JsonObject), new JsonRootBinding(values), children);
        return new EditDocument(root, typeof(JsonObject), EditScope.WholeComponent);
    }

    private static EditNodeMetadata Meta(FieldDeclaration f)
        => f.DisplayName is null && f.Unit is null ? EditNodeMetadata.Empty : new EditNodeMetadata { DisplayName = f.DisplayName, Unit = f.Unit };

    private EditNode BuildNode(ref int next, string name, string path, Type type, IValueBinding binding, EditNodeMetadata meta, bool readOnly, int depth)
    {
        var id = new EditNodeId(next++);
        if (_fieldEditors.TryGetValue(type, out var editor) && editor.CreateNode(id, name, path, binding, meta) is { } custom)
            return custom;
        var kind = KindOf(type);
        if (depth > 8) return new EditNode(id, name, path, EditNodeKind.Unsupported, type, binding, metadata: meta, isReadOnly: true);
        switch (kind)
        {
            case EditNodeKind.Struct or EditNodeKind.Class or EditNodeKind.Record:
            {
                var kids = new List<EditNode>();
                foreach (var m in Members(type))
                {
                    var child = new NestedMemberBinding(m, binding);
                    kids.Add(BuildNode(ref next, m.Name, path + "." + m.Name, child.ValueType, child, EditNodeMetadata.Empty, readOnly, depth + 1));
                }
                return new EditNode(id, name, path, kind, type, binding, kids, meta, readOnly);
            }
            case EditNodeKind.DynamicArray:
            {
                var element = type.IsArray ? type.GetElementType()! : type.GetGenericArguments()[0];
                var list = binding.GetBoxed() ?? ContainerOps.DefaultOf(type);
                if (list is null) return new EditNode(id, name, path, EditNodeKind.Unsupported, type, binding, metadata: meta, isReadOnly: true);
                var container = new DynamicArrayBinding(list, binding, element);
                var kids = new List<EditNode>(container.Count);
                for (int i = 0; i < container.Count; i++)
                    kids.Add(BuildNode(ref next, "[" + i + "]", path + "[" + i + "]", element, container.GetElementBinding(i), EditNodeMetadata.Empty, readOnly, depth + 1));
                return new EditNode(id, name, path, kind, type, container, kids, meta, readOnly);
            }
            default:
                return new EditNode(id, name, path, kind, type, binding, metadata: meta, isReadOnly: readOnly);
        }
    }

    private static IEnumerable<MemberInfo> Members(Type t)
    {
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) yield return f;
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0) yield return p;
    }

    /// <summary>The node kind of a type — the same classification as the reflection builder.</summary>
    public static EditNodeKind KindOf(Type t)
    {
        if (t == typeof(bool)) return EditNodeKind.Boolean;
        if (t == typeof(string)) return EditNodeKind.String;
        if (t == typeof(Guid)) return EditNodeKind.Guid;
        if (t == typeof(DateTime)) return EditNodeKind.DateTime;
        if (t.IsEnum) return EditNodeKind.Enum;
        if (t.IsPrimitive || t == typeof(decimal)) return EditNodeKind.Scalar;
        if (t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))) return EditNodeKind.DynamicArray;
        if (t.IsValueType) return EditNodeKind.Struct;
        if (t.IsClass) return EditNodeKind.Class;
        return EditNodeKind.Unsupported;
    }

    /// <summary>The root: the JSON object itself (a set replaces every member).</summary>
    private sealed class JsonRootBinding : IValueBinding
    {
        private readonly JsonObject _values;
        public JsonRootBinding(JsonObject values) => _values = values;
        public Type ValueType => typeof(JsonObject);
        public object? GetBoxed() => _values;
        public void SetBoxed(object? value)
        {
            if (value is not JsonObject o || ReferenceEquals(o, _values)) return;
            _values.Clear();
            foreach (var kv in o) _values[kv.Key] = kv.Value?.DeepClone();
        }
        public bool TryGetSpan(out Span<byte> bytes) { bytes = default; return false; }
    }
}

/// <summary>
/// ⭐ One declared value in a <see cref="JsonObject"/>: read ONCE (the JSON member, else the declaration's default, else the type's — never null for a value type), cached; a set
/// writes the member with the host's options. ⭐ 0 B per steady frame: <see cref="GetBoxed"/> returns the cached box.
/// </summary>
public sealed class JsonMemberBinding : IValueBinding
{
    private readonly JsonObject _values;
    private readonly FieldDeclaration _field;
    private readonly JsonSerializerOptions _options;
    private object? _cached;
    private bool _read;

    public JsonMemberBinding(JsonObject values, FieldDeclaration field, JsonSerializerOptions options)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
        _field = field ?? throw new ArgumentNullException(nameof(field));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Type ValueType => _field.ClrType;

    /// <summary>True when the JSON holds a value for this field (false ⇒ the default is shown — S7's grey).</summary>
    public bool IsSet => _values.ContainsKey(_field.Name);

    public object? GetBoxed()
    {
        if (_read) return _cached;
        _read = true;
        var node = _values.TryGetPropertyValue(_field.Name, out var v) ? v : _field.Default;
        _cached = node is null ? ContainerOps.DefaultOf(_field.ClrType) : Read(node);
        return _cached;
    }

    /// <summary>The declaration's default as a value (S7: what "changed" compares against).</summary>
    public object? DefaultValue() => _field.Default is null ? ContainerOps.DefaultOf(_field.ClrType) : Read(_field.Default);

    private object? Read(JsonNode node)
    {
        try { return node.Deserialize(_field.ClrType, _options) ?? ContainerOps.DefaultOf(_field.ClrType); }
        catch (Exception e) when (e is JsonException or NotSupportedException or FormatException or InvalidOperationException) { return ContainerOps.DefaultOf(_field.ClrType); }
    }

    public void SetBoxed(object? value)
    {
        _cached = value;
        _read = true;
        _values[_field.Name] = value is null ? null : JsonSerializer.SerializeToNode(value, _field.ClrType, _options);
    }

    /// <summary>Back to the default: the member is REMOVED from the JSON (S7 ↺ — a reset is "not set", not "set to the default").</summary>
    public void Reset()
    {
        _values.Remove(_field.Name);
        _read = false;
        _cached = null;
    }

    /// <summary>Writes a cached REFERENCE value (a list, a class) back to the JSON — a list element set or a move mutates it in place, never through <see cref="SetBoxed"/>.
    /// An unset member that still equals its default stays unset.</summary>
    public void Flush()
    {
        if (!_read || _cached is null || _field.ClrType.IsValueType) return;
        var node = JsonSerializer.SerializeToNode(_cached, _field.ClrType, _options);
        if (!IsSet)
        {
            var def = DefaultValue();
            if (def is not null && JsonNode.DeepEquals(node, JsonSerializer.SerializeToNode(def, _field.ClrType, _options))) return;
        }
        _values[_field.Name] = node;
    }

    public bool TryGetSpan(out Span<byte> bytes) { bytes = default; return false; }
}

/// <summary>
/// ⭐ The session over a declared document — what the table needs (<see cref="IEditSession.RebuildState"/>, <see cref="IEditSession.MarkStructuralChange"/>), with
/// <see cref="Commit"/> answering the <see cref="JsonObject"/> (the host writes it where it belongs — its own undo, §10.4).
/// </summary>
public sealed class DeclaredEditSession : IEditSession
{
    private readonly DeclaredEditDocumentBuilder _builder;
    private readonly IReadOnlyList<FieldDeclaration> _fields;
    private readonly JsonObject _values;
    private readonly string _original;
    private readonly List<JsonMemberBinding> _bindings = new();

    public DeclaredEditSession(DeclaredEditDocumentBuilder builder, IReadOnlyList<FieldDeclaration> fields, JsonObject values)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        _values = values ?? throw new ArgumentNullException(nameof(values));
        _original = values.ToJsonString();
        Document = builder.Build(fields, values, _bindings);
    }

    public EditDocument Document { get; private set; }
    /// <summary>The values, with every in-place change flushed in.</summary>
    public JsonObject Values { get { Flush(); return _values; } }
    public bool IsDirty { get { Flush(); return _values.ToJsonString() != _original; } }
    private void Flush() { for (int i = 0; i < _bindings.Count; i++) _bindings[i].Flush(); }
    public EditRebuildState RebuildState { get; private set; } = EditRebuildState.Stable;
    public void MarkStructuralChange() => RebuildState = EditRebuildState.RebuildRequired;
    public void RebuildDocument() { Flush(); _bindings.Clear(); Document = _builder.Build(_fields, _values, _bindings); RebuildState = EditRebuildState.Stable; }
    public ValidationResult Validate() => ValidationResult.Ok();
    public object Commit() { Flush(); return _values; }
    public void Cancel()
    {
        var back = JsonNode.Parse(_original)!.AsObject();
        _values.Clear();
        foreach (var kv in back.ToList()) { back.Remove(kv.Key); _values[kv.Key] = kv.Value; }
        _bindings.Clear();
        Document = _builder.Build(_fields, _values, _bindings);
        RebuildState = EditRebuildState.Stable;
    }
    public void Dispose() { }
}
