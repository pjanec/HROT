using Fdp.Core;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>⭐⭐ §3's TWO KINDS, made explicit. 📌 <c>R-78</c>.</summary>
public enum EntityBindingKind
{
    /// <summary>⭐ <b>This variable, on THIS entity</b> — the one selected when the pin was made.</summary>
    Concrete,

    /// <summary>⭐⭐ <b>This variable, on WHOEVER is selected</b> — re-resolved on selection change.</summary>
    Chameleon,
}

/// <summary>
/// ⭐⭐⭐ <b>Which entity a watch row is ABOUT — <c>BP-501</c>, §3's two kinds with a real shape.</b>
/// 📄 <c>DESIGN_Variable_Watch_Pinning.md</c> §3 *(<c>R-78</c>)* · §4 *(the binding clock)*.
///
/// <para>⛔⛔ <b>What was wrong before.</b> The two kinds existed only as *"is
/// <c>Origin.Entity</c> equal to <c>default</c>?"* — 📐 <c>StagedWriteView.EntityFor</c> and
/// <c>VariableChangeMonitor</c> both read that sentinel. ⚠ It WORKS, and it is not enough for two
/// reasons: <b>(a)</b> a designer had <b>no way to choose</b> — a pin took whatever the row happened to
/// carry, so the chameleon was reachable only by pinning a row that was already sentinel-bound; and
/// <b>(b)</b> an <c>Entity</c> is a slot+generation handle that is <b>recycled</b>, so it can never be
/// written to a file. ⇒ ⭐ a pin needs a durable id AND an in-session handle, and it needs to say which
/// KIND it is rather than have it inferred.</para>
///
/// <para>⭐⭐ <b>Two identities, on purpose:</b>
/// <list type="bullet">
///   <item>⭐ <see cref="StagingNetworkId"/> — the durable one *(the AUTHORED
///   <c>NetworkIdentity.Value</c>)*, used for PERSISTENCE and for telling one concrete pin from another;</item>
///   <item>⭐ <see cref="Captured"/> — the in-session <c>Entity</c>, used to RESOLVE the value this
///   session, because that is what every reader downstream already takes.</item>
/// </list></para>
///
/// <para>⭐⭐⭐ <b>RESTART SURVIVAL, built by <c>94g</c> (<c>BP-511</c>).</b> §5 keys a concrete pin on the
/// <b>STAGING</b> id and re-resolves it through the <c>oldToNewMap</c> the extractor now publishes on the
/// control-plane bus *(<c>StagingRemapPublishedEvent</c>)</b> → <see cref="StagingRemapView"/> →
/// <c>NetworkIdResolver</c>.
/// ⚠⚠ <b>SUPERSEDES the earlier remark here</b>, which said this stored the RUNTIME id and therefore did
/// not survive a reload — 📐 true at <c>BP-501</c>, and the reason was that publishing the map edited
/// files a concurrent batch owned. ⭐ That batch (<c>HN-037</c>) has merged.</para>
///
/// <para>⚠ <b>The staging id can legitimately be <c>0</c>:</b> an entity spawned at RUNTIME has no
/// authored ancestor, so it has nothing durable to key on. ⭐ <see cref="IsPersistable"/> reports that,
/// and the save path skips-and-counts it — ⛔ it is not an error and not a within-session failure.</para>
/// </summary>
/// <param name="Kind">Concrete or chameleon.</param>
/// <param name="StagingNetworkId">
/// ⭐⭐ <b>The AUTHORED (staging) <c>NetworkIdentity.Value</c></b> — the durable key for a concrete
/// binding; <c>0</c> for a chameleon *(bound to a ROLE, not an entity)* and <c>0</c> for a runtime-spawned
/// entity that has no authored ancestor.
/// ⛔⛔ <b>NOT the runtime id.</b> Pass 1 of every load hands out fresh runtime ids, so a runtime key
/// points at a different entity after a reload — see the class remarks.
/// </param>
/// <param name="Captured">
/// ⭐ The in-session handle for a concrete binding; <c>default</c> for a chameleon.
/// ⛔ Never persisted — see the class remarks.
/// </param>
public readonly record struct EntityBinding(
    EntityBindingKind Kind,
    long              StagingNetworkId,
    Entity            Captured)
{
    /// <summary>⭐⭐ *"This variable, on whoever is selected."*</summary>
    public static EntityBinding Chameleon { get; } =
        new(EntityBindingKind.Chameleon, 0, default);

    /// <summary>
    /// ⭐ *"This variable, on THIS entity."*
    /// <param name="stagingNetworkId">
    /// ⭐⭐ The <b>AUTHORED</b> id — a caller holding a RUNTIME id translates it first
    /// *(<see cref="StagingRemapView.ToStaging"/>)</b>. ⚠ <c>0</c> when the entity carries no
    /// <c>NetworkIdentity</c> or has no authored ancestor — legal, and it means the pin is
    /// within-session. <see cref="IsPersistable"/> says so rather than the caller having to know.
    /// </param>
    /// </summary>
    public static EntityBinding Concrete(long stagingNetworkId, Entity captured)
        => new(EntityBindingKind.Concrete, stagingNetworkId, captured);

    /// <summary>
    /// ⭐⭐⭐ <b><c>BP-511</c> — the same binding, re-bound to a new load's entity.</b>
    /// ⭐ Keeps the durable key and replaces only the in-session handle, so a rebind cannot quietly
    /// change WHICH authored entity the pin is about. ⛔ Chameleons are returned unchanged — they carry no
    /// id and follow the selection.
    /// </summary>
    public EntityBinding RebindTo(Entity captured)
        => Kind == EntityBindingKind.Chameleon ? this : this with { Captured = captured };

    /// <summary>
    /// ⭐⭐⭐ <b>The entity a <c>VariableRowOrigin</c> should carry for this binding</b> — and the reason
    /// nothing downstream had to change.
    ///
    /// <para>📐 <c>StagedWriteView.EntityFor</c> and <c>VariableChangeMonitor</c> already read
    /// <c>default(Entity)</c> as *"ask the selection"*. ⇒ ⭐ a chameleon projects to <c>default</c> and a
    /// concrete projects to its captured handle, which is <b>exactly the sentinel convention already in
    /// place</b>. ⛔ Introducing a second way to say *"follow the selection"* would be two encodings of
    /// one concept.</para>
    /// </summary>
    public Entity OriginEntity => Kind == EntityBindingKind.Chameleon ? default : Captured;

    /// <summary>
    /// ⭐ True when this binding can be written to a file and read back meaningfully.
    /// <para>⭐ A chameleon always can — it stores a ROLE, not an entity. ⚠ A concrete binding can only
    /// when its entity has an AUTHORED id; an editor-only or runtime-spawned entity has none, and a pin
    /// on it is a within-session pin. ⛔ Reported, never silently dropped.</para>
    /// </summary>
    public bool IsPersistable => Kind == EntityBindingKind.Chameleon || StagingNetworkId != 0;

    /// <summary>⭐ Short text for a tooltip or a header — ⛔ not a format anything parses.</summary>
    public override string ToString()
        => Kind == EntityBindingKind.Chameleon
            ? "chameleon (follows selection)"
            : StagingNetworkId != 0 ? $"entity #{StagingNetworkId}" : "entity (no authored id)";
}
