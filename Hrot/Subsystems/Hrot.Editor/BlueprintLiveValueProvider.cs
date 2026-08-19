using System;
using System.Collections.Generic;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor.Inspector;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Selection;
using Fdp.Core;

namespace Hrot.Editor;

/// <summary>
/// ⭐⭐ <b>The one call <see cref="BlueprintLiveValueProvider"/> needs</b> — a blueprint state read by
/// entity and asset. ⛔ Deliberately NOT <c>IBlueprintDebugSession</c>: that is 36 members, and a
/// provider that demands all of them cannot be railed without stubbing all of them.
/// </summary>
public delegate BlueprintStateSnapshot? ReadBlueprintState(Entity self, Guid assetId);

/// <summary>
/// ⭐⭐⭐ <b><c>88a</c> — Blueprint's live-value provider. 📌 <c>Q32</c> §4 row 58's unbuilt half.</b>
///
/// <para>🔴 <b>What was missing.</b> 📐 <c>EditorSubsystem</c> passed a provider for BTree
/// *(<c>:2178</c>)* and HSM *(<c>:2188</c>)* and <b><c>null</c> for Blueprint</b>; ⛔ <b>zero
/// <see cref="ILiveBlackboardValueProvider"/> implementations existed under <c>Hrot.Blueprints.*</c></b>.
/// ⇒ the Details Value column rendered <c>(pending)</c> — which is the DESIGNED output for a source
/// with no reader, so nothing looked broken. ⚠ Row 58 was merged with half of it unbuilt.</para>
///
/// <para>⭐⭐ <b>ROUTING, not construction.</b> Everything below already shipped:
/// <see cref="IBlueprintDebugSession.CaptureLiveState"/> is the live read by entity and asset, and
/// <c>BlueprintRuntimeInspectorPane.ResolveInspectorSnapshot</c> already owns the
/// paused-pointer-vs-live decision. ⛔ <b>No byte reader is built here</b>, and none should be — the
/// snapshot hands back decoded <c>FieldValues</c> keyed by name.</para>
///
/// <para>⭐⭐⭐ <b>Why NOT <see cref="LiveBlackboardValueProvider"/> (ruling 9 is satisfied, not
/// broken).</b> 📐 That one reads through <c>BehaviorRegistry</c> → <c>BehaviorState</c> →
/// <c>BrainBlackboard</c>, gated on a behavior-name match — <b>BTree/HSM-shaped end to end</b>.
/// Blueprint state lives in the <c>BlueprintBlackboard{16384,4096,1024}</c> partitions and is reached
/// through the debug session. ⇒ ⭐ <b>ONE interface, ONE formatter, TWO adapters</b> — that is one
/// concept with two sources, ⛔ not two implementations of one concept.</para>
///
/// <para>⭐ <b>The formatter is SHARED</b> — <see cref="LiveBlackboardValueProvider.FormatValue"/>.
/// ⛔ A second one here would be 📌 <c>C8</c>/<c>BP-01</c>'s regression: a hex string where the
/// designer expects a value.</para>
///
/// <para>⚠⚠ <b>Honest emptiness.</b> No selected entity, no blueprint session, or no snapshot ⇒ an
/// EMPTY map, which the table renders as <c>(pending)</c>. ⛔ <b>Never a zero that looks like a
/// value</b> — 📌 that distinction is the whole reason <c>(pending)</c> exists.</para>
/// </summary>
public sealed class BlueprintLiveValueProvider : ILiveBlackboardValueProvider
{
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    private readonly Func<ReadBlueprintState?> _readerFactory;
    private readonly EditorSelectionStore      _store;

    /// <param name="readerFactory">
    /// ⭐⭐ <b>The NARROW seam — exactly what this provider uses, and nothing else.</b>
    ///
    /// <para>⚠ An earlier draft took the whole <c>IDebugSessionRegistry</c> and type-tested for
    /// <c>IBlueprintDebugSession</c> inside. 📐 <b>Measured: that interface has 36 members</b>, so a
    /// rail had to stub 36 methods to assert one behaviour — ⛔ <b>a dependency that wide is a rail
    /// nobody writes</b>, which is how a provider ends up untested. ⭐ Mirrors
    /// <see cref="LiveBlackboardValueProvider"/>'s own <c>Func&lt;…&gt;</c> factories.</para>
    ///
    /// <para>⚠ 📌 <b><c>R-66</c>:</b> a session existing means <i>"a blueprint DOCUMENT is open"</i>,
    /// ⛔ NOT <i>"the sim is up"</i>. ⭐ Liveness is decided by the SNAPSHOT being non-null — the honest
    /// "nothing to read yet".</para>
    /// </param>
    /// <param name="store">Owns <see cref="EditorSelectionStore.SelectedEntity"/>.</param>
    public BlueprintLiveValueProvider(Func<ReadBlueprintState?> readerFactory, EditorSelectionStore store)
    {
        _readerFactory = readerFactory ?? throw new ArgumentNullException(nameof(readerFactory));
        _store         = store         ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> GetLiveVariableValues(IEditableAsset asset)
    {
        try
        {
            if (asset is null) return Empty;

            // ⭐ 1 — an entity must be selected. ⛔ Without one there is no blackboard to read, and a
            //   guess would be a value the designer did not ask for.
            var entity = _store.SelectedEntity;
            if (entity is null) return Empty;

            // ⭐ 2 — a blueprint state reader must be available. ⚠ The debug registry is shared with
            //   BTree and HSM, so the composition root resolves the blueprint session and hands the
            //   READ, not the session — this class never type-tests.
            var read = _readerFactory();
            if (read is null) return Empty;

            // ⭐⭐ 3 — the snapshot. ⛔ Which snapshot (paused-pointer vs live) is decided by
            //   BlueprintRuntimeInspectorPane.ResolveInspectorSnapshot at the composition root, which
            //   already owns that rule — re-deciding it here would be a second answer to one question.
            var snapshot = read(entity.Value, asset.AssetId);
            if (snapshot is null) return Empty;

            var result = new Dictionary<string, string>(snapshot.FieldValues.Count);
            foreach (var field in snapshot.FieldValues)
            {
                try
                {
                    // ⚠ A null field value is rendered as empty, ⛔ not as "0" — the same honesty rule
                    //   the empty map carries.
                    result[field.Key] = field.Value is null
                        ? string.Empty
                        : LiveBlackboardValueProvider.FormatValue(field.Value, field.Value.GetType());
                }
                catch
                {
                    // ⛔ One unformattable field must not blank the whole column — skip it, keep the rest.
                }
            }
            return result;
        }
        catch
        {
            // ⛔ Never throw into the UI — the interface's own contract.
            return Empty;
        }
    }
}
