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

    private static string ExtractShortName(string fqn)
    {
        int lastDot = fqn.LastIndexOf('.');
        return lastDot >= 0 ? fqn.Substring(lastDot + 1) : fqn;
    }
}
