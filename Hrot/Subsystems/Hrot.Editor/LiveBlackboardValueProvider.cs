using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor;

/// <summary>
/// Production implementation of <see cref="ILiveBlackboardValueProvider"/>.
/// Reads live <see cref="BrainBlackboard"/> values for the selected entity,
/// gated on the name-match between the asset and the entity's active behavior.
/// </summary>
public sealed class LiveBlackboardValueProvider : ILiveBlackboardValueProvider
{
    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>();

    private readonly Func<IInspectableSession?> _sessionFactory;
    private readonly Func<BehaviorRegistry?> _registryFactory;
    private readonly EditorSelectionStore _store;

    /// <param name="sessionFactory">Produces the current live <see cref="IInspectableSession"/>, or null when no simulation is running.</param>
    /// <param name="registryFactory">Produces the current <see cref="BehaviorRegistry"/>, or null when not yet initialised.</param>
    /// <param name="store">Editor selection store that owns <see cref="EditorSelectionStore.SelectedEntity"/>.</param>
    public LiveBlackboardValueProvider(
        Func<IInspectableSession?> sessionFactory,
        Func<BehaviorRegistry?> registryFactory,
        EditorSelectionStore store)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _registryFactory = registryFactory ?? throw new ArgumentNullException(nameof(registryFactory));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> GetLiveVariableValues(IEditableAsset asset)
    {
        try
        {
            // Step 1: must have a selected entity.
            var entity = _store.SelectedEntity;
            if (entity == null) return Empty;

            // Step 2: must have a live session with this entity alive.
            var session = _sessionFactory();
            if (session == null || !session.IsAlive(entity.Value)) return Empty;

            // Step 3: entity must carry BehaviorState.
            if (!session.HasComponent(entity.Value, typeof(BehaviorState))) return Empty;
            var bsObj = session.GetComponent(entity.Value, typeof(BehaviorState));
            if (bsObj is not BehaviorState bs) return Empty;

            // Step 4: name-match gate.
            var registry = _registryFactory();
            if (registry == null) return Empty;
            if (!registry.TryGetId(asset.Name, out int id)) return Empty;
            if (id != bs.ActiveBehaviorHash) return Empty;

            // Step 5: load definition and BrainBlackboard.
            if (!registry.TryGetDefinition(id, out var def)) return Empty;
            if (def.ManagedBlackboardVariables is not { Count: > 0 } vars) return Empty;

            var bbObj = session.GetComponent(entity.Value, typeof(BrainBlackboard));
            if (bbObj is not BrainBlackboard bb) return Empty;

            // Step 6: project each variable.
            var result = new Dictionary<string, string>(vars.Count);
            foreach (var v in vars)
            {
                try
                {
                    result[v.Name] = ProjectAndFormat(bb, v.Type, v.ByteOffset);
                }
                catch
                {
                    // Skip this variable on any projection failure — never throw into the UI.
                }
            }
            return result;
        }
        catch
        {
            return Empty;
        }
    }

    /// <summary>
    /// Projects a typed struct from <paramref name="bb"/>.<c>BehaviorParameters + byteOffset</c>
    /// and formats it as a compact one-line string.
    /// For multi-field structs: <c>"Field1=val1, Field2=val2"</c>.
    /// For primitives: <c>value.ToString()</c>.
    /// </summary>
    internal static unsafe string ProjectAndFormat(BrainBlackboard bb, Type type, int byteOffset)
    {
        object boxed = Marshal.PtrToStructure((IntPtr)(bb.BehaviorParameters + byteOffset), type)!;
        return FormatValue(boxed, type);
    }

    /// <summary>
    /// Formats a boxed struct value as a compact one-line string.
    /// Reflects all public instance fields and properties; if none, falls back to ToString().
    /// </summary>
    internal static string FormatValue(object value, Type type)
    {
        // Collect public instance fields.
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        // Collect public instance properties (readable, non-indexed).
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var parts = new List<string>();
        foreach (var f in fields)
        {
            var fval = f.GetValue(value);
            parts.Add($"{f.Name}={fval}");
        }
        foreach (var p in props)
        {
            if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
            var pval = p.GetValue(value);
            parts.Add($"{p.Name}={pval}");
        }

        if (parts.Count == 0)
            return value?.ToString() ?? "";

        return string.Join(", ", parts);
    }
}
