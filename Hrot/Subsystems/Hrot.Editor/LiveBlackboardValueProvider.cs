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
public sealed class LiveBlackboardValueProvider : ILiveBlackboardValueProvider, ILiveVariableProjection
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

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 90 (<c>90c</c>) — the RESOLVE step, split out from the two projections.</b>
    ///
    /// <para>📐 Steps 1–5 below were the first two thirds of <c>GetLiveVariableValues</c>, and they
    /// are the half that decides <b>whether this asset is live on this entity at all</b>. ⭐ Splitting
    /// them means the byte arm and the string arm cannot disagree about that question — ⛔ two copies
    /// of a five-step gate is exactly how the same rule drifts.</para>
    ///
    /// <para>⚠ <b>Behaviour-neutral for the string arm</b>: the steps, their order and their early
    /// returns are unchanged; only their home moved.</para>
    /// </summary>
    private bool TryResolve(IEditableAsset asset, out BrainBlackboard bb, out IReadOnlyList<ManagedBlackboardVariable> vars)
    {
        bb   = default;
        vars = Array.Empty<ManagedBlackboardVariable>();

        // Step 1: must have a selected entity.
        var entity = _store.SelectedEntity;
        if (entity == null) return false;

        // Step 2: must have a live session with this entity alive.
        var session = _sessionFactory();
        if (session == null || !session.IsAlive(entity.Value)) return false;

        // Step 3: entity must carry BehaviorState.
        if (!session.HasComponent(entity.Value, typeof(BehaviorState))) return false;
        var bsObj = session.GetComponent(entity.Value, typeof(BehaviorState));
        if (bsObj is not BehaviorState bs) return false;

        // Step 4: name-match gate.
        var registry = _registryFactory();
        if (registry == null) return false;
        if (!registry.TryGetId(asset.Name, out int id)) return false;
        if (id != bs.ActiveBehaviorHash) return false;

        // Step 5: load definition and BrainBlackboard.
        if (!registry.TryGetDefinition(id, out var def)) return false;
        if (def.ManagedBlackboardVariables is not { Count: > 0 } declared) return false;

        var bbObj = session.GetComponent(entity.Value, typeof(BrainBlackboard));
        if (bbObj is not BrainBlackboard blackboard) return false;

        bb   = blackboard;
        vars = declared;
        return true;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> GetLiveVariableValues(IEditableAsset asset)
    {
        try
        {
            if (!TryResolve(asset, out var bb, out var vars)) return Empty;

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

    /// <inheritdoc/>
    /// <remarks>
    /// ⭐⭐⭐ <b>Batch 90 (<c>90c</c>) — the BYTE arm, and this host needs NO new arm on the row.</b>
    ///
    /// <para>📐 This provider already walks <c>(BrainBlackboard, Type, ByteOffset)</c> and only formats
    /// at the very end ⇒ ⭐ <b>it HAS the bytes</b>, and the row source's <c>readRaw</c> seam has been
    /// <c>null</c> since it was built. ⇒ ⛔ <b>BTree/HSM must NOT go through the object arm</b>: bytes
    /// keep §4a's change highlight LIVE, and objects would make it inert for no gain.</para>
    ///
    /// <para>⚠ <b>Why the string arm is not <c>format(project(...))</c>, asked and answered.</b>
    /// 📐 <c>ProjectAndFormat</c> is <c>FormatValue(Marshal.PtrToStructure(ptr, type))</c> — it decodes
    /// via the MARSHALLER. Formatting the raw bytes instead would need a byte decoder, which is a
    /// DIFFERENT mechanism *(<c>MarshalFromBytes</c>, and it lives above this assembly)*. ⇒ ⭐ the two
    /// arms share the RESOLVE step and diverge at projection, which is as far as the split honestly
    /// goes. ⛔ Forcing it further would invent a second decode path — the handoff said not to force
    /// it, and it was right.</para>
    ///
    /// <para>⛔ <b>No padding.</b> A variable whose projection throws is OMITTED, so its cell reads
    /// <c>(pending)</c> — 📌 guide row <c>C9</c>. ⭐ Absence is the signal, and it is free.</para>
    /// </remarks>
    public IReadOnlyDictionary<string, byte[]>? GetLiveBytes(IEditableAsset asset)
    {
        try
        {
            if (asset is null) return null;
            if (!TryResolve(asset, out var bb, out var vars)) return null;

            var result = new Dictionary<string, byte[]>(vars.Count);
            foreach (var v in vars)
            {
                try
                {
                    var bytes = ProjectBytes(bb, v.Type, v.ByteOffset);
                    if (bytes.Length > 0) result[v.Name] = bytes;
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
            return null;
        }
    }

    /// <summary>
    /// ⭐⭐ Copies a variable's RAW bytes out of <c>BehaviorParameters + byteOffset</c>.
    /// ⛔ Deliberately no decode: the ONE decoder is the formatter's injected
    /// <c>DecodeRawValue</c>, and a second one here would be the duplication <c>S3</c> collapsed.
    /// </summary>
    internal static unsafe byte[] ProjectBytes(BrainBlackboard bb, Type type, int byteOffset)
    {
        int size = Marshal.SizeOf(type);
        if (size <= 0) return Array.Empty<byte>();

        var bytes = new byte[size];
        Marshal.Copy((IntPtr)(bb.BehaviorParameters + byteOffset), bytes, 0, size);
        return bytes;
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
