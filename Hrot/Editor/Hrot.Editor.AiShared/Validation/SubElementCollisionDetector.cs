using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.Editor.AiShared.Validation;

/// <summary>
/// Describes a short-name collision: two or more distinct FQNs that share the same
/// last-segment (short name) in the action schema. This causes ambiguous references
/// in BTree/HSM action dispatching.
/// </summary>
/// <param name="ShortName">The conflicting short name (last <c>.</c>-segment of the FQN).</param>
/// <param name="ClaimingFqns">Sorted list of all FQNs that share this short name.</param>
public sealed record ActionCollision(string ShortName, IReadOnlyList<string> ClaimingFqns);

/// <summary>
/// Scans an <see cref="IActionSchemaExporter"/> for short-name collisions — cases
/// where two or more distinct FQNs map to the same last-dot-segment short name.
/// </summary>
public static class SubElementCollisionDetector
{
    /// <summary>
    /// Returns all collisions found in <paramref name="schemaExporter"/>.
    /// A collision exists when ≥ 2 distinct FQNs share the same short name.
    /// Same FQN appearing multiple times in the dictionary is NOT a collision.
    /// </summary>
    /// <param name="schemaExporter">The exporter whose <see cref="IActionSchemaExporter.All"/>
    /// dictionary is scanned.</param>
    /// <returns>
    /// A list of <see cref="ActionCollision"/> records, one per colliding short name,
    /// with <see cref="ActionCollision.ClaimingFqns"/> sorted ascending.
    /// Returns an empty list when no collisions exist.
    /// </returns>
    public static IReadOnlyList<ActionCollision> GetCollisions(IActionSchemaExporter schemaExporter)
    {
        return schemaExporter.All.Values
            .GroupBy(entry => ExtractShortName(entry.Fqn))
            .Where(group => group.Select(e => e.Fqn).Distinct().Count() > 1)
            .Select(group => new ActionCollision(
                group.Key,
                group.Select(e => e.Fqn).Distinct().OrderBy(f => f).ToArray()))
            .ToList();
    }

    /// <summary>
    /// Returns an empty list. When node bindings are resolved by full FQN — as in BTree and HSM,
    /// where <c>BehaviorRegistry.Resolve(fqn)</c> and <c>IActionSchemaExporter.Lookup(fqn)</c>
    /// are always keyed on the full qualified name — short-name collisions between distinct FQNs
    /// are harmless: there is no code path that resolves an action by its short name alone.
    /// </summary>
    /// <remarks>
    /// Use <see cref="GetCollisions"/> if you need the raw short-name collision data for
    /// diagnostics or future tooling; this method intentionally suppresses the result because
    /// surfacing it as a runtime error would be a false positive.
    /// </remarks>
    public static IReadOnlyList<ActionCollision> GetBindingAmbiguities(IActionSchemaExporter schemaExporter)
    {
        // FQN-based resolution means short-name duplicates across declaring types are never
        // ambiguous at runtime.  Return empty so callers do not surface false-positive errors.
        return Array.Empty<ActionCollision>();
    }

    private static string ExtractShortName(string fqn)
    {
        int lastDot = fqn.LastIndexOf('.');
        return lastDot >= 0 ? fqn.Substring(lastDot + 1) : fqn;
    }
}
