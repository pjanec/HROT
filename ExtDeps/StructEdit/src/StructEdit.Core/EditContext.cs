using System.Collections.Immutable;

namespace StructEdit.Core;

/// <summary>
/// Optional caller-provided context bag passed through to IBufferViewProvider implementations.
/// Allows discriminators that are not stored inside the component.
/// Immutable: With() returns a new instance.
/// </summary>
public sealed class EditContext
{
    private readonly ImmutableDictionary<string, object> _data;

    public EditContext() : this(ImmutableDictionary<string, object>.Empty) { }

    private EditContext(ImmutableDictionary<string, object> data) => _data = data;

    /// <summary>Returns the value for the given key, or null if not present.</summary>
    public object? Get(string key) => _data.TryGetValue(key, out var v) ? v : null;

    /// <summary>Returns the value for the given key cast to T, or default if not present or wrong type.</summary>
    public T? Get<T>(string key) => _data.TryGetValue(key, out var v) && v is T t ? t : default;

    /// <summary>Returns a new EditContext with the key set to the given value.</summary>
    public EditContext With(string key, object value) =>
        new(_data.SetItem(key, value));
}
