using System;
using System.Linq;
using System.Reflection;
using NodeEditor.Core.Commands;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// ⭐⭐⭐ <b>THE UNION-COVERAGE RAIL — the MCP command route must reach EVERY <c>GraphCommand</c> variant,
/// or say in writing which one it deliberately does not.</b>
/// 📄 <c>docs/DESIGN_Mcp_Authoring.md</c> §11.1 *(the union, measured)* · §11.2 *(expose it, don't curate)*.
///
/// <para>⭐⭐⭐ <b>Why this rail is the one that matters most in the whole slice.</b> §11.2's argument is
/// that <i>"a hand-picked verb list WILL lag the union"</i> — and it already had: the four shipped typed
/// verbs could not express a BTree decorator or an HSM region. ⛔ Replacing four hand-picked verbs with
/// <b>thirty-five</b> hand-written parse arms fixes today and reintroduces the same decay tomorrow, the
/// first time NodeEdit gains a variant. ⇒ ⭐⭐ this rail is what makes the fix PERMANENT rather than
/// momentary: it reflects over the union itself, so a new variant fails a test the day it appears.</para>
///
/// <para>⭐ <b>It is a UNIT rail on purpose</b> — reflection over types, no editor, no HTTP, ~milliseconds.
/// ⛔ The coverage claim does not need a booted host, and putting it in the slow lane would mean nobody
/// runs it on the edit that breaks it.</para>
///
/// <para>⚠ <b>What it does NOT prove:</b> that each arm builds the RIGHT command — that is the T3
/// round-trip rail's job, which applies variants to a real host and reads the result back. ⭐ This one
/// proves only that none is MISSING, which is the failure mode a round-trip rail cannot see.</para>
/// </summary>
public class TheCommandRouteCoversTheWholeUnionTests
{
    /// <summary>
    /// ⭐ The union, by reflection: every <c>sealed record</c> nested inside <see cref="GraphCommand"/>.
    /// </summary>
    private static string[] UnionVariants() =>
        typeof(GraphCommand)
            .GetNestedTypes(BindingFlags.Public)
            .Where(t => t.IsSealed && typeof(GraphCommand).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// ⭐ Reaches the internal schema table through the assembly's own <c>InternalsVisibleTo</c>, so the
    /// rail asserts against the REAL table rather than a copy of it.
    /// </summary>
    private static (System.Collections.Generic.IReadOnlyDictionary<string, string[]> Schema,
                    System.Collections.Generic.IReadOnlyDictionary<string, string> Unsupported) Tables()
    {
        var t = typeof(Hrot.Editor.DebugApi.DebugApiService).Assembly
                    .GetType("Hrot.Editor.DebugApi.GraphCommandJson", throwOnError: true)!;

        var schema = (System.Collections.Generic.IReadOnlyDictionary<string, string[]>)
            t.GetField("Schema", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var unsupported = (System.Collections.Generic.IReadOnlyDictionary<string, string>)
            t.GetField("Unsupported", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        return (schema, unsupported);
    }

    [Fact]
    public void Every_GraphCommand_variant_is_either_reachable_or_declared_unsupported()
    {
        var (schema, unsupported) = Tables();
        var variants = UnionVariants();

        Assert.True(variants.Length > 20,
            $"reflection found only {variants.Length} GraphCommand variants — the union should have ~35. "
          + "Either the type moved or the reflection filter is wrong, and a rail that finds nothing "
          + "passes vacuously.");

        var missing = variants
            .Where(v => !schema.ContainsKey(v) && !unsupported.ContainsKey(v))
            .ToArray();

        Assert.True(missing.Length == 0,
            $"{missing.Length} GraphCommand variant(s) are unreachable over MCP and undeclared: "
          + string.Join(", ", missing)
          + ".\nAdd a parse arm to GraphCommandJson.Build plus a row in Schema, or — if it genuinely "
          + "cannot be exposed — a row in Unsupported with the reason. ⛔ Silence is the one option this "
          + "rail removes: it is exactly how the four typed verbs came to lag the union.");
    }

    [Fact]
    public void No_schema_row_names_a_variant_that_does_not_exist()
    {
        var (schema, unsupported) = Tables();
        var variants = UnionVariants().ToHashSet(StringComparer.Ordinal);

        // ⛔ The rail runs BOTH ways. A row for a variant NodeEdit no longer has is dead weight that
        //   reads as coverage — the same rot in the opposite direction.
        var phantom = schema.Keys.Concat(unsupported.Keys)
                            .Where(k => !variants.Contains(k))
                            .ToArray();

        Assert.True(phantom.Length == 0,
            $"the command table names {phantom.Length} variant(s) that no longer exist in GraphCommand: "
          + string.Join(", ", phantom)
          + ". Remove the rows — they advertise a payload the route cannot build.");
    }

    [Fact]
    public void Every_reachable_variant_declares_at_least_one_field_or_is_deliberately_empty()
    {
        var (schema, _) = Tables();

        // ⚠ A variant with an EMPTY field list would be advertised through GET .../graph/command with no
        //   way to fill it in. Only a genuinely field-less command may be empty, and the union has none.
        var fieldless = schema.Where(kv => kv.Value.Length == 0).Select(kv => kv.Key).ToArray();

        Assert.True(fieldless.Length == 0,
            $"{fieldless.Length} variant(s) advertise NO fields: {string.Join(", ", fieldless)}. "
          + "An agent reading GET /assets/{id}/graph/command would have no way to build the payload.");
    }
}
