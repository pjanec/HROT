using System.Reflection;
using System.Runtime.InteropServices;
using StructEdit.Core.Bindings;
using StructEdit.Core.Memory;

namespace StructEdit.Core.UnionSupport;

/// <summary>
/// Context passed to <see cref="IBufferViewProvider"/> during document build.
/// Provides helpers to read sibling field values and project the buffer as a typed overlay.
/// </summary>
public sealed class BufferViewRequest
{
    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>The root component type being edited (e.g. <c>ProjectileComponent</c>).</summary>
    public required Type ComponentType { get; init; }

    /// <summary>JSON path of the fixed-buffer field (e.g. <c>$.Payload</c>).</summary>
    public required EditPath BufferPath { get; init; }

    /// <summary>The binding for the raw fixed-buffer field.</summary>
    public required IValueBinding BufferBinding { get; init; }

    /// <summary>Optional external context supplied by the session consumer.</summary>
    public required EditContext? ExternalContext { get; init; }

    // ── Internal state (set by ReflectionEditDocumentBuilder) ─────────────

    /// <summary>The live edit buffer, used for reading sibling values.</summary>
    internal IEditBuffer Buffer { get; init; } = null!;

    /// <summary>Native byte offset of the fixed-buffer field within the component.</summary>
    internal int NativeOffset { get; init; }

    /// <summary>Shared ID counter used to assign <see cref="EditNodeId"/> values.</summary>
    internal IdAllocator IdAlloc { get; init; } = null!;

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the current boxed value of the sibling field at <paramref name="siblingPath"/>
    /// from the edit buffer.  Useful for reading discriminator fields such as a Mode enum.
    /// </summary>
    public T? ReadSibling<T>(EditPath siblingPath)
    {
        var boxed = Buffer.Box();

        // Strip leading "$." or "$"
        var path = siblingPath.Value;
        if (path.StartsWith("$.", StringComparison.Ordinal)) path = path[2..];
        else if (path.StartsWith('$')) path = path[1..];

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        object? current = boxed;
        foreach (var seg in segments)
        {
            if (current == null) return default;
            var t = current.GetType();
            var fi = t.GetField(seg, BindingFlags.Public | BindingFlags.Instance);
            if (fi != null) { current = fi.GetValue(current); continue; }
            var pi = t.GetProperty(seg, BindingFlags.Public | BindingFlags.Instance);
            if (pi != null) { current = pi.GetValue(current); continue; }
            return default;
        }
        if (current is T tVal) return tVal;
        try { return (T?)Convert.ChangeType(current, typeof(T)); }
        catch { return default; }
    }

    /// <summary>
    /// Projects the fixed-buffer bytes as <paramref name="viewType"/>, creating a
    /// <see cref="EditNodeKind.BufferView"/> node with child scalar/enum nodes for each
    /// public field of <paramref name="viewType"/>.  Only works when the buffer is native
    /// (unmanaged blittable struct).
    /// </summary>
    public BufferViewResult ProjectBufferAs(Type viewType, string viewName)
    {
        var nodeId = new EditNodeId(IdAlloc.Next());
        var children = new List<EditNode>();
        IValueBinding? viewBinding = null;

        if (Buffer.IsNative && Buffer is NativeStructEditBuffer native)
        {
            // Provide a root binding so the UI drawer sees a valid, populated object
            // rather than null when the BufferView node is opened as a whole.
            viewBinding = new NativeViewBinding(native, NativeOffset, viewType);

            foreach (var fi in viewType.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!TryGetSizeOf(fi.FieldType, out int fieldSize)) continue;

                int fieldOffset = NativeOffset;
                if (viewType.IsValueType)
                {
                    try { fieldOffset += (int)(nint)Marshal.OffsetOf(viewType, fi.Name); }
                    catch { continue; }
                }

                var binding = new NativeFieldBinding(native, fieldOffset, fieldSize, fi.FieldType);
                var kind = SimpleKindOf(fi.FieldType);
                var childId = new EditNodeId(IdAlloc.Next());
                children.Add(new EditNode(childId, fi.Name,
                    $"{BufferPath.Value}.{fi.Name}", kind, fi.FieldType, binding));
            }
        }

        var node = new EditNode(nodeId, viewName, BufferPath.Value,
            EditNodeKind.BufferView, viewType, viewBinding, children);
        return new BufferViewResult { ViewName = viewName, ViewType = viewType, Node = node };
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private static bool TryGetSizeOf(Type t, out int size)
    {
        try { size = RuntimeTypeOpsFactory.Get(t).SizeOf; return true; }
        catch { size = 0; return false; }
    }

    private static EditNodeKind SimpleKindOf(Type t)
    {
        if (t == typeof(bool)) return EditNodeKind.Boolean;
        if (t == typeof(string)) return EditNodeKind.String;
        if (t == typeof(Guid)) return EditNodeKind.Guid;
        if (t == typeof(DateTime)) return EditNodeKind.DateTime;
        if (t.IsEnum) return EditNodeKind.Enum;
        if (IsNumericPrimitive(t)) return EditNodeKind.Scalar;
        if (t.IsValueType) return EditNodeKind.Struct;
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

    // ── Private binding for the BufferView root node ───────────────────────

    /// <summary>
    /// Read-only binding for the root <see cref="EditNodeKind.BufferView"/> node.
    /// Marshals the unmanaged bytes into a boxed DTO so the UI drawer sees a
    /// valid, populated object instead of <c>null</c>.
    /// Edits happen seamlessly through the child <c>NativeFieldBinding</c> leaves.
    /// </summary>
    private sealed unsafe class NativeViewBinding : IValueBinding
    {
        private readonly NativeStructEditBuffer _buffer;
        private readonly int _offset;

        public Type ValueType { get; }

        public NativeViewBinding(NativeStructEditBuffer buffer, int offset, Type valueType)
        {
            _buffer = buffer;
            _offset = offset;
            ValueType = valueType;
        }

        public object? GetBoxed()
        {
            if (!_buffer.TryGetRootSpan(out var span)) return null;
            fixed (byte* ptr = span)
            {
                return Marshal.PtrToStructure((IntPtr)(ptr + _offset), ValueType);
            }
        }

        // The root view node is display-only; edits flow through child NativeFieldBindings.
        public void SetBoxed(object? value) { }

        public bool TryGetSpan(out Span<byte> bytes) { bytes = default; return false; }
    }
}
