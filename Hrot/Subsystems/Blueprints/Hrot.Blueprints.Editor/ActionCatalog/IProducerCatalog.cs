using System;
using System.Collections.Generic;
using Hrot.Blueprints.Core.Compiler;

namespace Hrot.Blueprints.Editor.ActionCatalog;

/// <summary>
/// Which source contributed a producer entry.
///
/// <para>
/// ⭐ Both members have a real contributor — there is deliberately no member that nothing supplies.
/// That is the repo's silent-default rule applied to an enum: an unassigned source member is a
/// picker option that can never appear, and the register of those is already three rails long.
/// </para>
/// </summary>
public enum ProducerSource
{
    /// <summary>
    /// A Function graph exported by a <c>Library</c>-dispatch blueprint. ⭐ The resolver design's
    /// primary shape (§8.2 <c>E1</c>): pure scalar-in / scalar-out, invoked by its generated FQN.
    /// </summary>
    LibraryFunction,

    /// <summary>
    /// A hand-written static CLR method. 📄 The resolver design names this the interim escape hatch
    /// (§8.2 <c>E3</c>: <i>"<c>FunctionCallNode</c> 'CLR method' mode is an interim escape hatch to
    /// hand-call a conversion helper without new node kinds"</i>).
    /// </summary>
    ClrMethod,
}

/// <summary>
/// ⭐⭐⭐ <b>One named producer of values.</b>
///
/// <para>
/// 📄 Plan §4c, ruling 9 — <c>G7</c>'s <i>"parameter resolver: None / Pick / Create"</i> and
/// <c>W10</c>'s <i>"initializer picker"</i> are the same concept: <b>pick a named producer from a
/// contributing catalog</b>. ⛔ Two catalogs would be exactly the duplication ruling 9 forbids.
/// </para>
/// </summary>
/// <param name="Fqn">
/// ⭐⭐ <b>The identity — the generated FQN, NOT the AssetId</b> (architect <c>AQ2</c>,
/// <c>blueprint-finalize/TASK-DETAIL.md:248</c>, non-negotiable). For a Library function that is
/// <c>Hrot.AI.Behaviors.Generated.{SanitizedName}_{BlueprintId:X8}_Bp.{FunctionName}</c>; for a CLR
/// method it is <c>{DeclaringType.FullName}.{MethodName}</c>. ⭐ This is also what is PERSISTED.
/// </param>
/// <param name="DisplayName">Human-readable label for the picker.</param>
/// <param name="Category">Optional grouping label (the owning asset / declaring type).</param>
/// <param name="Inputs">Positional inputs, reusing the compiler's own signature record.</param>
/// <param name="Outputs">Positional outputs — what this producer can supply.</param>
/// <param name="Source">Which source contributed it.</param>
public sealed record ProducerEntry(
    string                           Fqn,
    string                           DisplayName,
    string?                          Category,
    IReadOnlyList<BlueprintParamSig> Inputs,
    IReadOnlyList<BlueprintParamSig> Outputs,
    ProducerSource                   Source);

/// <summary>
/// Unified facade over every source of named producers. ⭐ Deliberately shaped like
/// <see cref="IBehaviorActionCatalog"/> — 📄 <c>AN7-REPORT.md:73–95</c> is the named precedent:
/// <i>"add a source enum member + contributing catalog, not a new picker."</i>
/// </summary>
public interface IProducerCatalog
{
    /// <summary>Every known producer, from every source. A snapshot; safe to iterate unlocked.</summary>
    IReadOnlyList<ProducerEntry> GetProducers();

    /// <summary>
    /// The producers that can supply a value of <paramref name="typeId"/> — i.e. those with exactly
    /// one output of that type. ⭐ This is what makes ONE catalog serve both callers: a parameter
    /// resolver and a variable initializer differ in what CONSUMES the value, not in what produces it.
    /// </summary>
    IReadOnlyList<ProducerEntry> GetProducersReturning(string typeId);

    /// <summary>The producer with this FQN, or null when the stored id no longer resolves.</summary>
    ProducerEntry? Lookup(string fqn);

    /// <summary>Raised after the snapshot is rebuilt.</summary>
    event Action? Changed;
}
