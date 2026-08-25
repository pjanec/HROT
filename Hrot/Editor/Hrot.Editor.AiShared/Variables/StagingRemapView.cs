using System.Collections.Generic;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>BP-510</c> — the editor's view of the current load's staging⇄runtime network-id table.</b>
/// 📄 <c>DESIGN_Variable_Watch_Pinning.md</c> §5 *(restart survival BY TRANSLATION)</b> · §8a.
///
/// <para>⭐⭐ <b>Why a durable key must be the STAGING id.</b> 📐 <c>StagingEntityExtractor</c> Pass 1
/// allocates a NEW runtime network id for every authored entity on <b>every</b> load. ⇒ ⛔ a pin keyed on
/// a runtime id points at nothing — or worse, at a <i>different</i> entity — after a reload. ⭐ The
/// STAGING id is the stable authoring artefact: it is what the designer was looking at when they pinned.</para>
///
/// <para>⭐⭐ <b>Both directions are needed, and they are used at different moments:</b>
/// <list type="bullet">
///   <item>⭐ <see cref="ToStaging"/> — <b>at PIN time.</b> The editor has a live entity and therefore a
///   RUNTIME id; making the pin durable means inverting the table once.</item>
///   <item>⭐ <see cref="ToRuntime"/> — <b>at BIND time</b> *(a load, or a restore)*. The pin has a
///   staging id and needs this load's runtime id before an entity can be found.</item>
/// </list></para>
///
/// <para>⛔⛔ <b>This holds NO ECS knowledge and does NO lookup.</b> Turning a runtime id into an
/// <c>Entity</c> is <c>NetworkIdResolver</c>'s job, in <c>Fdp.Toolkits</c>. ⭐ Keeping the two apart is
/// what lets the whole translation be asserted headlessly.</para>
///
/// <para>⛔ <b>Not an editor-side copy of the remap</b> *(<c>R-79</c>)</b> — it holds the map the
/// extractor PUBLISHED, and computes only its inverse. ⚠ The remap LOGIC stays in the extractor; ⭐ if
/// this class ever grows a rule about <i>how</i> ids are allocated, that is the violation.</para>
/// </summary>
public sealed class StagingRemapView
{
    private Dictionary<long, long> _stagingToRuntime = new();
    private Dictionary<long, long> _runtimeToStaging = new();

    /// <summary>
    /// ⭐⭐ Bumped on every <see cref="Publish"/>. ⭐ A host watches this to know *"a load happened, rebind"*
    /// — ⛔ rather than the Watch polling the map for changes, which would be a per-tick read of a fact
    /// that changes once per load *(§4's two clocks)*.
    /// </summary>
    public int Generation { get; private set; }

    /// <summary>⭐ <c>false</c> until a load has published a table. ⛔ Then every translation is identity-free:
    /// <see cref="ToRuntime"/>/<see cref="ToStaging"/> answer <c>0</c>, not the input.</summary>
    public bool HasMap => _stagingToRuntime.Count > 0;

    /// <summary>
    /// ⭐⭐ Installs the table a load published, and builds the inverse.
    ///
    /// <para>⚠ <b>REPLACES rather than merges.</b> A new load's ids supersede the previous load's
    /// entirely — ⛔ merging would leave a stale staging→runtime pair that resolves to an entity from a
    /// world that no longer exists, which is the exact failure this class removes.</para>
    ///
    /// <para>⚠ <b>A duplicate runtime id in the inverse is impossible by construction</b> *(the allocator
    /// hands out each id once)*; if one ever appeared, last-writer-wins here would hide it — ⭐ so it is
    /// asserted in the rail rather than defended against with a silent branch.</para>
    /// </summary>
    public void Publish(IReadOnlyDictionary<long, long>? stagingToRuntime)
    {
        var forward = new Dictionary<long, long>();
        var inverse = new Dictionary<long, long>();

        if (stagingToRuntime != null)
            foreach (var (staging, runtime) in stagingToRuntime)
            {
                forward[staging] = runtime;
                inverse[runtime] = staging;
            }

        _stagingToRuntime = forward;
        _runtimeToStaging = inverse;
        Generation++;
    }

    /// <summary>
    /// ⭐ This load's runtime id for <paramref name="stagingId"/>, or <c>0</c> when the table does not
    /// know it.
    /// <para>⛔⛔ <b>It does NOT fall back to returning the input.</b> ⚠ A staging id and a runtime id are
    /// drawn from the same numeric space, so a pass-through would look like a successful translation and
    /// resolve to <i>whichever</i> entity happens to hold that number — 📌 the wrong-entity failure mode
    /// this whole mechanism exists to remove. ⭐ <c>0</c> means "not translatable", and the caller says so.</para>
    /// </summary>
    public long ToRuntime(long stagingId)
        => _stagingToRuntime.TryGetValue(stagingId, out var runtime) ? runtime : 0;

    /// <summary>
    /// ⭐ The staging id that produced <paramref name="runtimeId"/>, or <c>0</c> when this entity did not
    /// come from the scenario.
    /// <para>⚠ <b><c>0</c> is a real and common answer</b>, not an error: an entity spawned at runtime
    /// *(a shot, a reinforcement)* has no authored ancestor, so a pin on it is a WITHIN-SESSION pin.
    /// ⭐ <c>EntityBinding.IsPersistable</c> already reports exactly that, and the save path already
    /// skips-and-counts it.</para>
    /// </summary>
    public long ToStaging(long runtimeId)
        => _runtimeToStaging.TryGetValue(runtimeId, out var staging) ? staging : 0;
}
