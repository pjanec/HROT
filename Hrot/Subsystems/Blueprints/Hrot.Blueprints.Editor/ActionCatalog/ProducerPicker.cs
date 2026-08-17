using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.ActionCatalog;

/// <summary>
/// ⭐⭐⭐ <b>What a picker holds: a producer, or nothing.</b>
///
/// <para>
/// ⭐ <b>"None" is a FIRST-CLASS value, not the absence of one.</b> It is the shipped answer for every
/// declaration today, so it has to round-trip as deliberately as a chosen producer does — a picker
/// whose "unset" is indistinguishable from "not yet loaded" is the shape that makes a lost selection
/// unnoticeable.
/// </para>
/// </summary>
public readonly record struct ProducerSelection
{
    private ProducerSelection(string? fqn) => Fqn = fqn;

    /// <summary>⭐ The generated FQN (architect <c>AQ2</c>), or null for <see cref="None"/>.</summary>
    public string? Fqn { get; }

    /// <summary>The first-class "no producer" value.</summary>
    public static ProducerSelection None => new(null);

    /// <summary>A selection naming <paramref name="entry"/>.</summary>
    public static ProducerSelection Of(ProducerEntry entry)
        => new((entry ?? throw new ArgumentNullException(nameof(entry))).Fqn);

    /// <summary>A selection naming a raw FQN (used when restoring).</summary>
    public static ProducerSelection OfFqn(string fqn)
        => string.IsNullOrWhiteSpace(fqn) ? None : new(fqn);

    public bool IsNone => string.IsNullOrEmpty(Fqn);

    /// <summary>
    /// ⭐⭐ <b>What goes to disk: the FQN string, and null for None.</b> ⛔ Never an AssetId — an
    /// AssetId would round-trip just as happily, which is exactly why the rail asserts the STORED
    /// STRING rather than "reload works".
    /// </summary>
    public string? Persisted => IsNone ? null : Fqn;

    /// <summary>Reads back what <see cref="Persisted"/> wrote.</summary>
    public static ProducerSelection FromPersisted(string? stored) => OfFqn(stored ?? string.Empty);
}

/// <summary>One row in the picker's drop-down. <c>Entry</c> is null for the "None" row.</summary>
public sealed record ProducerOption(string Label, ProducerEntry? Entry)
{
    public ProducerSelection Selection => Entry is null ? ProducerSelection.None : ProducerSelection.Of(Entry);
}

/// <summary>
/// ⭐⭐⭐ <b><c>G7</c> + <c>W10</c>: ONE producer picker.</b> Headless — it decides what is offered,
/// what is selected and what is stored; drawing is the caller's.
///
/// <para>
/// ⭐ <b>Offered over the UNION</b> (📄 <c>PLAN_Cross_Host_Sequencing.md:176</c>) — the target may be a
/// <c>Variable</c>, a <c>WorkingState</c> entry <b>or</b> a <c>Parameter</c>. ⛔ Not <c>Variables</c>
/// alone: since <c>U-12</c> the three are one declaration list with a kind tag, and a picker that
/// offered on only one of them would be back to the spelling rule the unification retired.
/// </para>
///
/// <para>
/// ⛔ <b>Out of scope, deliberately:</b> "Create resolver" scaffolding (<c>E5</c>), "detach authored
/// shape" / divergence detection (<c>E6</c>), Library-asset authoring (<c>E1</c>). This is the
/// <i>None / Pick</i> control and the catalog behind it.
/// </para>
/// </summary>
public sealed class ProducerPicker
{
    /// <summary>The label of the first row. ⭐ Always present, always first.</summary>
    public const string NoneLabel = "None";

    private readonly IProducerCatalog _catalog;

    public ProducerPicker(IProducerCatalog catalog)
        => _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    /// <summary>The current selection; <see cref="ProducerSelection.None"/> until something is picked.</summary>
    public ProducerSelection Selected { get; private set; } = ProducerSelection.None;

    /// <summary>
    /// ⭐ The offer list for one target declaration: <b>"None" first</b>, then every producer that can
    /// supply <paramref name="targetTypeId"/>.
    /// </summary>
    /// <param name="kind">
    /// ⭐ The target's declaration kind. Every member of the union is accepted; the parameter exists so
    /// that "which kinds may have a producer" is answered here rather than by whichever caller asks.
    /// </param>
    public IReadOnlyList<ProducerOption> OptionsFor(DeclarationKind kind, string targetTypeId)
    {
        _ = kind;   // ⭐ every kind in the union is offerable -- see OffersOverTheWholeUnion.
        var options = new List<ProducerOption> { new(NoneLabel, null) };
        options.AddRange(_catalog.GetProducersReturning(targetTypeId)
            .Select(p => new ProducerOption(p.DisplayName, p)));
        return options;
    }

    /// <summary>Selects a row. Passing the "None" row clears the selection.</summary>
    public void Select(ProducerSelection selection) => Selected = selection;

    /// <summary>Selects by option row.</summary>
    public void Select(ProducerOption option)
        => Selected = (option ?? throw new ArgumentNullException(nameof(option))).Selection;

    /// <summary>⭐ What to write to the asset: the FQN, or null for None.</summary>
    public string? Persist() => Selected.Persisted;

    /// <summary>⭐ Reads a stored value back. An unknown FQN is KEPT, not silently cleared — see
    /// <see cref="IsResolvable"/>.</summary>
    public void Restore(string? stored) => Selected = ProducerSelection.FromPersisted(stored);

    /// <summary>
    /// ⚠ <b>Whether the current selection still names a producer the catalog knows.</b> A stored FQN
    /// whose Library asset was renamed or deleted becomes unresolvable — ⛔ and the picker does NOT
    /// quietly reset it to None, because that would turn a broken reference into a plausible-looking
    /// deliberate choice. <see cref="ProducerSelection.None"/> is trivially resolvable.
    /// </summary>
    public bool IsResolvable => Selected.IsNone || _catalog.Lookup(Selected.Fqn!) is not null;

    /// <summary>The entry the selection names, or null (None, or dangling).</summary>
    public ProducerEntry? SelectedEntry => Selected.IsNone ? null : _catalog.Lookup(Selected.Fqn!);
}
