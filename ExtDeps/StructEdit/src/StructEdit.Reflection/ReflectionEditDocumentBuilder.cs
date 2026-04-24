using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using StructEdit.Core;
using StructEdit.Core.Attributes;
using StructEdit.Core.Bindings;
using StructEdit.Core.Memory;
using StructEdit.Core.Plugins;
using StructEdit.Core.UnionSupport;

namespace StructEdit.Reflection;

/// <summary>
/// Reflection-based <see cref="IEditDocumentBuilder"/> that scans public fields and properties
/// once per session and builds an immutable <see cref="EditDocument"/> tree.
/// </summary>
public sealed class ReflectionEditDocumentBuilder : IEditDocumentBuilder
{
    private readonly IReadOnlyList<IBufferViewProvider> _providers;
    private readonly IReadOnlyDictionary<Type, ICustomFieldEditor> _fieldEditors;

    /// <summary>Creates a builder with no registered providers or field editors.</summary>
    public ReflectionEditDocumentBuilder() : this(Array.Empty<IBufferViewProvider>()) { }

    /// <summary>Creates a builder with the supplied provider list.</summary>
    public ReflectionEditDocumentBuilder(IReadOnlyList<IBufferViewProvider> providers)
        : this(providers, new Dictionary<Type, ICustomFieldEditor>()) { }

    /// <summary>Creates a builder with provider list and custom field editors.</summary>
    public ReflectionEditDocumentBuilder(
        IReadOnlyList<IBufferViewProvider> providers,
        IReadOnlyDictionary<Type, ICustomFieldEditor> fieldEditors)
    {
        _providers = providers ?? Array.Empty<IBufferViewProvider>();
        _fieldEditors = fieldEditors ?? new Dictionary<Type, ICustomFieldEditor>();
    }

    public EditDocument Build(IEditBuffer buffer, Type componentType, EditScope scope, EditContext? context)
    {
        var idAlloc = new IdAllocator(0);
        var fullRoot = BuildNode(buffer, "$", componentType.Name, componentType,
            nativeOffset: 0, fi: null, pi: null, idAlloc, new HashSet<Type>(), _providers, _fieldEditors, context);
        var filteredRoot = ApplyScope(fullRoot, scope);
        return new EditDocument(filteredRoot, componentType, scope);
    }

    // ── node construction ──────────────────────────────────────────────────

    private static EditNode BuildNode(
        IEditBuffer buffer,
        string jsonPath,
        string name,
        Type nodeType,
        int nativeOffset,
        FieldInfo? fi,
        PropertyInfo? pi,
        IdAllocator idAlloc,
        HashSet<Type> visited,
        IReadOnlyList<IBufferViewProvider> providers,
        IReadOnlyDictionary<Type, ICustomFieldEditor> fieldEditors,
        EditContext? context)
    {
        bool isFixedBuffer = fi?.GetCustomAttribute<FixedBufferAttribute>() != null;
        var kind = isFixedBuffer ? EditNodeKind.FixedBuffer : DetermineKind(nodeType);

        // circular reference guard (copy-on-write per DFS branch)
        if (kind is EditNodeKind.Struct or EditNodeKind.Class or EditNodeKind.Record)
        {
            if (visited.Contains(nodeType))
                return new EditNode(new EditNodeId(idAlloc.Next()), name, jsonPath, EditNodeKind.Unsupported, nodeType);
            visited = new HashSet<Type>(visited) { nodeType };
        }

        IValueBinding? binding = null;
        List<EditNode>? children = null;

        // Check for custom field editor before the default switch
        if (!isFixedBuffer && fieldEditors.TryGetValue(nodeType, out var customFieldEditor))
        {
            var leafBinding = CreateLeafBinding(buffer, nativeOffset, fi, pi, nodeType);
            var customMetadata = ReadMetadata(fi, pi);
            var nodeId = new EditNodeId(idAlloc.Next());
            return customFieldEditor.CreateNode(nodeId, name, jsonPath, leafBinding!, customMetadata);
        }

        switch (kind)
        {
            case EditNodeKind.Struct:
            case EditNodeKind.Class:
            case EditNodeKind.Record:
                children = BuildChildren(buffer, jsonPath, nodeType, nativeOffset, idAlloc, visited, providers, fieldEditors, context);
                break;

            case EditNodeKind.InlineArray:
            {
                var attr = nodeType.GetCustomAttribute<InlineArrayAttribute>()!;
                var elemType = GetInlineArrayElementType(nodeType);
                if (buffer.IsNative && TryGetSizeOf(elemType, out int elemSize))
                    binding = new InlineArrayBinding((NativeStructEditBuffer)buffer, nativeOffset, elemType, elemSize, attr.Length);
                break;
            }

            case EditNodeKind.FixedBuffer:
            {
                var attr = fi!.GetCustomAttribute<FixedBufferAttribute>()!;
                if (buffer.IsNative && TryGetSizeOf(attr.ElementType, out int elemSize))
                {
                    var fixedBinding = new FixedBufferBinding(
                        (NativeStructEditBuffer)buffer, nativeOffset, attr.ElementType, elemSize, attr.Length);
                    binding = fixedBinding;

                    // Provider check: if any provider claims this buffer, return its node instead.
                    if (providers.Count > 0)
                    {
                        var request = new BufferViewRequest
                        {
                            ComponentType = buffer.ComponentType,
                            BufferPath = EditPath.Parse(jsonPath),
                            BufferBinding = fixedBinding,
                            ExternalContext = context,
                            Buffer = buffer,
                            NativeOffset = nativeOffset,
                            IdAlloc = idAlloc,
                        };
                        foreach (var provider in providers)
                        {
                            if (provider.CanCreateView(request))
                                return provider.CreateView(request).Node;
                        }
                    }
                }
                break;
            }

            case EditNodeKind.DynamicArray:
            {
                var parentBinding = CreateLeafBinding(buffer, nativeOffset, fi, pi, nodeType);
                if (parentBinding != null)
                {
                    var container = parentBinding.GetBoxed();
                    if (container != null)
                        binding = new DynamicArrayBinding(container, parentBinding, GetArrayElementType(nodeType));
                }
                break;
            }

            default:
                binding = CreateLeafBinding(buffer, nativeOffset, fi, pi, nodeType);
                break;
        }

        var metadata = ReadMetadata(fi, pi);
        return new EditNode(new EditNodeId(idAlloc.Next()), name, jsonPath, kind, nodeType, binding, children, metadata);
    }

    private static List<EditNode> BuildChildren(
        IEditBuffer buffer,
        string parentPath,
        Type parentType,
        int parentNativeOffset,
        IdAllocator idAlloc,
        HashSet<Type> visited,
        IReadOnlyList<IBufferViewProvider> providers,
        IReadOnlyDictionary<Type, ICustomFieldEditor> fieldEditors,
        EditContext? context)
    {
        var result = new List<EditNode>();
        var flags = BindingFlags.Public | BindingFlags.Instance;

        // public instance fields — skip compiler-generated (backing fields, etc.)
        foreach (var fi in parentType.GetFields(flags))
        {
            if (fi.Name.StartsWith('<') || fi.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
                continue;

            int childOffset = parentNativeOffset;
            if (buffer.IsNative && parentType.IsValueType)
                childOffset += (int)(nint)Marshal.OffsetOf(parentType, fi.Name);

            result.Add(BuildNode(buffer, $"{parentPath}.{fi.Name}", fi.Name, fi.FieldType,
                childOffset, fi, null, idAlloc, visited, providers, fieldEditors, context));
        }

        // public instance properties with getter — skip indexers and compiler-generated
        foreach (var pi in parentType.GetProperties(flags))
        {
            if (pi.GetMethod == null) continue;
            if (pi.GetIndexParameters().Length > 0) continue;
            if (pi.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)) continue;
            if (pi.Name == "EqualityContract") continue; // record internal

            result.Add(BuildNode(buffer, $"{parentPath}.{pi.Name}", pi.Name, pi.PropertyType,
                0, null, pi, idAlloc, visited, providers, fieldEditors, context));
        }

        return result;
    }

    // ── kind detection ─────────────────────────────────────────────────────

    private static EditNodeKind DetermineKind(Type t)
    {
        if (t == typeof(bool)) return EditNodeKind.Boolean;
        if (t == typeof(string)) return EditNodeKind.String;
        if (t == typeof(Guid)) return EditNodeKind.Guid;
        if (t == typeof(DateTime)) return EditNodeKind.DateTime;
        if (t.IsEnum) return EditNodeKind.Enum;
        if (IsNumericPrimitive(t)) return EditNodeKind.Scalar;
        if (t.GetCustomAttribute<InlineArrayAttribute>() != null) return EditNodeKind.InlineArray;
        if (t.IsArray || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)))
            return EditNodeKind.DynamicArray;
        if (t.IsValueType) return EditNodeKind.Struct;      // covers record struct
        if (IsRecordClass(t)) return EditNodeKind.Record;
        if (t.IsClass) return EditNodeKind.Class;
        return EditNodeKind.Unsupported;
    }

    private static bool IsNumericPrimitive(Type t)
        => t == typeof(int) || t == typeof(uint)
        || t == typeof(long) || t == typeof(ulong)
        || t == typeof(short) || t == typeof(ushort)
        || t == typeof(byte) || t == typeof(sbyte)
        || t == typeof(float) || t == typeof(double)
        || t == typeof(decimal);

    private static bool IsRecordClass(Type t)
    {
        if (t.IsValueType) return false;
        return t.GetMethod("<Clone>$") != null
            || t.GetProperty("EqualityContract", BindingFlags.Instance | BindingFlags.NonPublic) != null;
    }

    // ── binding factory ────────────────────────────────────────────────────

    private static IValueBinding? CreateLeafBinding(IEditBuffer buffer, int nativeOffset,
        FieldInfo? fi, PropertyInfo? pi, Type valueType)
    {
        if (fi == null && pi == null) return null;

        if (buffer.IsNative && fi != null)
        {
            if (!TryGetSizeOf(valueType, out int fieldSize)) return null;
            return new NativeFieldBinding((NativeStructEditBuffer)buffer, nativeOffset, fieldSize, valueType);
        }

        var owner = buffer.Box();
        if (fi != null) return new ManagedFieldBinding(fi, owner, buffer.MarkDirty);
        if (pi != null) return new ManagedPropertyBinding(pi, owner, buffer.MarkDirty);
        return null;
    }

    private static bool TryGetSizeOf(Type t, out int size)
    {
        try { size = RuntimeTypeOpsFactory.Get(t).SizeOf; return true; }
        catch { size = 0; return false; }
    }

    // ── type helpers ───────────────────────────────────────────────────────

    private static Type GetInlineArrayElementType(Type t)
    {
        var field = t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).FirstOrDefault();
        return field?.FieldType ?? typeof(byte);
    }

    private static Type GetArrayElementType(Type t)
    {
        if (t.IsArray) return t.GetElementType()!;
        if (t.IsGenericType) return t.GetGenericArguments()[0];
        return typeof(object);
    }

    // ── metadata ──────────────────────────────────────────────────────────

    private static EditNodeMetadata ReadMetadata(FieldInfo? fi, PropertyInfo? pi)
    {
        if (fi == null && pi == null) return EditNodeMetadata.Empty;
        var provider = (ICustomAttributeProvider?)fi ?? pi!;

        T? GetAttr<T>() where T : Attribute
            => provider.GetCustomAttributes(typeof(T), false).FirstOrDefault() as T;

        var range = GetAttr<EditRangeAttribute>();
        var unit  = GetAttr<EditUnitAttribute>();
        var dn    = GetAttr<EditDisplayNameAttribute>();
        var ih    = GetAttr<InlineArrayHintAttribute>();
        var fh    = GetAttr<FixedBufferHintAttribute>();

        if (range == null && unit == null && dn == null && ih == null && fh == null)
            return EditNodeMetadata.Empty;

        return new EditNodeMetadata
        {
            Min         = range?.Min,
            Max         = range?.Max,
            Unit        = unit?.Unit,
            DisplayName = dn?.Name,
            FixedLength = ih?.Length ?? fh?.Length,
        };
    }

    // ── scope filtering ────────────────────────────────────────────────────

    private static EditNode ApplyScope(EditNode fullRoot, EditScope scope)
    {
        if (scope.IncludedPaths.Count == 0) return fullRoot;

        var filtered = FilterNode(fullRoot, scope);
        if (filtered != null) return filtered;

        // root not retained — collect first retained level from children
        var topLevel = new List<EditNode>();
        CollectRetainedTopLevel(fullRoot, scope, topLevel);
        return topLevel.Count switch
        {
            0 => new EditNode(new EditNodeId(0), "$", "$", EditNodeKind.SelectionRoot, fullRoot.ClrType),
            1 => topLevel[0],
            _ => new EditNode(new EditNodeId(0), "$", "$", EditNodeKind.SelectionRoot, fullRoot.ClrType,
                     children: topLevel),
        };
    }

    private static void CollectRetainedTopLevel(EditNode node, EditScope scope, List<EditNode> result)
    {
        foreach (var child in node.Children)
        {
            var f = FilterNode(child, scope);
            if (f != null) result.Add(f);
            else CollectRetainedTopLevel(child, scope, result);
        }
    }

    private static EditNode? FilterNode(EditNode node, EditScope scope)
    {
        var paths = scope.IncludedPaths;

        bool isDirectMatch = paths.Any(p => p.Value == node.JsonPath);

        bool isDescendant = scope.IncludeChildren
            && paths.Any(p => node.JsonPath.StartsWith(p.Value + "."));

        string ancestorPrefix = node.JsonPath == "$" ? "$." : node.JsonPath + ".";
        bool isAncestor = scope.IncludeParentsForContext
            && paths.Any(p => p.Value.StartsWith(ancestorPrefix));

        if (!isDirectMatch && !isDescendant && !isAncestor) return null;

        bool readOnly = isAncestor && !isDirectMatch && !isDescendant;

        // exact match + IncludeChildren=false: include node, strip children
        if (isDirectMatch && !scope.IncludeChildren)
        {
            return new EditNode(node.Id, node.Name, node.JsonPath, node.Kind, node.ClrType,
                node.Binding, null, node.Metadata, readOnly || node.IsReadOnly);
        }

        // recursively filter children
        var filteredChildren = new List<EditNode>();
        bool childrenChanged = false;
        foreach (var child in node.Children)
        {
            var fc = FilterNode(child, scope);
            if (fc == null) { childrenChanged = true; }
            else
            {
                filteredChildren.Add(fc);
                if (!ReferenceEquals(fc, child)) childrenChanged = true;
            }
        }

        if (!readOnly && !childrenChanged) return node;

        return new EditNode(node.Id, node.Name, node.JsonPath, node.Kind, node.ClrType,
            node.Binding, filteredChildren.Count > 0 ? filteredChildren : null,
            node.Metadata, readOnly || node.IsReadOnly);
    }
}
