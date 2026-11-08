using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Fdp.Toolkit.Scenario;

/// <summary>
/// ⭐ CE-3399 follow-up (Y-8) — ONE cached <typeparamref name="T"/> per DISTINCT preset set (a field's <c>[MapPickableEntity]</c> presets), found by the array's REFERENCE on the hot path (0 B) but SHARED BY CONTENT: a document rebuild makes new attribute instances (new arrays with the
/// same presets) — they map to the same <typeparamref name="T"/>, so the cache never grows per rebuild; the reference side is a <see cref="ConditionalWeakTable{TKey,TValue}"/> and dies with the arrays.
/// </summary>
public sealed class PresetSetCache<T> where T : class, new()
{
    private readonly ConditionalWeakTable<string[], T> _byReference = new();
    private readonly Dictionary<string, T> _byContent = new(StringComparer.Ordinal);

    /// <summary>How many DISTINCT preset sets were seen (a rail asserts it stays 1 across rebuilds).</summary>
    public int Count => _byContent.Count;

    /// <summary>The value of <paramref name="presets"/> (created on first sight of its content).</summary>
    public T Of(string[] presets)
    {
        if (_byReference.TryGetValue(presets, out var known)) return known;
        string key = string.Join('\u001f', presets);   // once per new array, never per frame
        if (!_byContent.TryGetValue(key, out var value)) _byContent[key] = value = new T();
        _byReference.Add(presets, value);
        return value;
    }
}
