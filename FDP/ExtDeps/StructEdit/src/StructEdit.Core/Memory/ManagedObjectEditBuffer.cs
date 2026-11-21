using System.Text.Json;

namespace StructEdit.Core.Memory;

/// <summary>
/// Edit buffer for managed classes and records. Deep clones the object on construction
/// to isolate nested reference types from the live component instance.
/// </summary>
internal sealed class ManagedObjectEditBuffer : IEditBuffer
{
    private object _clone;
    private bool _isDirty;

    private static readonly JsonSerializerOptions _cloneOptions = new()
    {
        IncludeFields = true
    };

    public ManagedObjectEditBuffer(Type componentType, object obj)
    {
        ComponentType = componentType ?? throw new ArgumentNullException(nameof(componentType));
        if (obj is null) throw new ArgumentNullException(nameof(obj));
        string json = JsonSerializer.Serialize(obj, componentType, _cloneOptions);
        _clone = JsonSerializer.Deserialize(json, componentType, _cloneOptions)
            ?? throw new InvalidOperationException($"Failed to deep clone component of type {componentType}.");
        CopyGetOnlyMaps(componentType, obj, _clone);
    }

    /// <summary>⭐ CE-4352 — the JSON clone leaves a GET-ONLY map empty (the serializer cannot set it): its entries are copied from the source, so the
    /// editor shows them (read-only — the struct editor has no map kind).</summary>
    private static void CopyGetOnlyMaps(Type type, object source, object clone)
    {
        foreach (var pi in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (pi.GetMethod == null || pi.GetIndexParameters().Length > 0 || pi.SetMethod?.IsPublic == true) continue;
            if (!typeof(System.Collections.IDictionary).IsAssignableFrom(pi.PropertyType)) continue;
            try
            {
                if (pi.GetValue(source) is not System.Collections.IDictionary from || pi.GetValue(clone) is not System.Collections.IDictionary to || to.IsReadOnly) continue;
                to.Clear();
                foreach (System.Collections.DictionaryEntry kv in from) to[kv.Key] = kv.Value;
            }
            catch { /* a map that cannot be read or written stays as the clone made it */ }
        }
    }

    public Type ComponentType { get; }
    public bool IsNative => false;
    public bool IsDirty => _isDirty;

    public void MarkDirty() => _isDirty = true;

    public bool TryGetRootSpan(out Span<byte> bytes)
    {
        bytes = Span<byte>.Empty;
        return false;
    }

    public IValueBinding CreateRootBinding() => new ManagedRootBinding(this);

    public object Box() => _clone;

    public void Dispose() { /* managed; nothing to free */ }

    // ---------- inner binding ----------

    private sealed class ManagedRootBinding : IValueBinding
    {
        private readonly ManagedObjectEditBuffer _buffer;

        internal ManagedRootBinding(ManagedObjectEditBuffer buffer) => _buffer = buffer;

        public Type ValueType => _buffer.ComponentType;

        public object? GetBoxed() => _buffer._clone;

        public void SetBoxed(object? value)
        {
            _buffer._clone = value ?? throw new ArgumentNullException(nameof(value));
            _buffer._isDirty = true;
        }

        public bool TryGetSpan(out Span<byte> bytes)
        {
            bytes = Span<byte>.Empty;
            return false;
        }
    }
}
