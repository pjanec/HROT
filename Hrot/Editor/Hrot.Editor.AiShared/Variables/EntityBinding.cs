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
///   <item>⭐ <see cref="NetworkId"/> — the durable one *(<c>NetworkIdentity.Value</c>)*, used for
///   PERSISTENCE and for telling one concrete pin from another;</item>
///   <item>⭐ <see cref="Captured"/> — the in-session <c>Entity</c>, used to RESOLVE the value this
///   session, because that is what every reader downstream already takes.</item>
/// </list></para>
///
/// <para>⚠⚠ <b>RESTART SURVIVAL IS NOT BUILT HERE, and saying so is the point.</b> §5 keys a concrete
/// pin on the <b>STAGING</b> id and re-resolves it through <c>StagingEntityExtractor</c>'s
/// <c>oldToNewMap</c> published on the orchestration bus. ⛔ That map is still a local that dies inside
/// the extractor, and wiring it edits <c>EditorSubsystem</c>/<c>EditorApplication</c> — files the
/// concurrent allocator batch owns. ⇒ ⭐ this stores the id it CAN see *(the runtime
/// <c>NetworkIdentity</c>)*, and a concrete pin therefore <b>does not survive a scenario restart yet</b>.
/// 📌 Deferred deliberately by the dispatching handoff §2, not overlooked.</para>
/// </summary>
/// <param name="Kind">Concrete or chameleon.</param>
/// <param name="NetworkId">
/// ⭐ The durable entity id for a concrete binding; <c>0</c> for a chameleon *(which is bound to a
/// ROLE, not an entity, so it has no id to store)*.
/// </param>
/// <param name="Captured">
/// ⭐ The in-session handle for a concrete binding; <c>default</c> for a chameleon.
/// ⛔ Never persisted — see the class remarks.
/// </param>
public readonly record struct EntityBinding(
    EntityBindingKind Kind,
    long              NetworkId,
    Entity            Captured)
{
    /// <summary>⭐⭐ *"This variable, on whoever is selected."*</summary>
    public static EntityBinding Chameleon { get; } =
        new(EntityBindingKind.Chameleon, 0, default);

    /// <summary>
    /// ⭐ *"This variable, on the entity selected right now."*
    /// <param name="networkId">⚠ <c>0</c> when the entity carries no <c>NetworkIdentity</c> — legal, and
    /// it means the pin cannot be persisted. <see cref="IsPersistable"/> says so rather than the caller
    /// having to know.</param>
    /// </summary>
    public static EntityBinding Concrete(long networkId, Entity captured)
        => new(EntityBindingKind.Concrete, networkId, captured);

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
    /// when its entity actually had a <c>NetworkIdentity</c>; an editor-only entity has none, and a pin
    /// on it is a within-session pin. ⛔ Reported, never silently dropped.</para>
    /// </summary>
    public bool IsPersistable => Kind == EntityBindingKind.Chameleon || NetworkId != 0;

    /// <summary>⭐ Short text for a tooltip or a header — ⛔ not a format anything parses.</summary>
    public override string ToString()
        => Kind == EntityBindingKind.Chameleon
            ? "chameleon (follows selection)"
            : NetworkId != 0 ? $"entity #{NetworkId}" : "entity (no network id)";
}
