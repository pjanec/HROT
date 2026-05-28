using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations.Internal;
using Xunit;

namespace Fdp.Core.Tests.Serialization.Migrations.Internal;

/// <summary>
/// Tests for <see cref="DiffToJournalConverter"/> (T1-240..T1-246).
/// </summary>
public sealed class DiffToJournalConverterTests
{
    // ---------------------------------------------------------------
    // T1-240: Null diff (identical DOMs) -> empty list
    // ---------------------------------------------------------------
    [Fact]
    public void Convert_EmptyDiff_ReturnsEmptyOperations()
    {
        var pre = JsonNode.Parse("{\"a\":1}")!.AsObject();

        // DomDiffer returns null for identical DOMs
        var ops = DiffToJournalConverter.Convert(null, pre);

        Assert.Empty(ops);
    }

    // ---------------------------------------------------------------
    // T1-241: Field in pre, absent in post -> Set op with original value
    // ---------------------------------------------------------------
    [Fact]
    public void Convert_FieldMissingInLossy_EmitsSetWithOriginalValue()
    {
        var pre = JsonNode.Parse("{\"a\":1,\"b\":42}")!.AsObject();
        var post = JsonNode.Parse("{\"a\":1}")!.AsObject();

        var diff = DomDiffer.Diff(pre, post);
        var ops = DiffToJournalConverter.Convert(diff, pre);

        Assert.Single(ops);
        Assert.Equal(JournalOpKind.Set, ops[0].Kind);
        Assert.Equal("$.b", ops[0].Path);
        Assert.Equal(42, ops[0].Value!.GetValue<int>());
    }

    // ---------------------------------------------------------------
    // T1-242: Field in post, absent in pre -> Remove op
    // ---------------------------------------------------------------
    [Fact]
    public void Convert_FieldPresentInLossyMissingInOriginal_EmitsRemove()
    {
        var pre = JsonNode.Parse("{\"a\":1}")!.AsObject();
        var post = JsonNode.Parse("{\"a\":1,\"b\":99}")!.AsObject();

        var diff = DomDiffer.Diff(pre, post);
        var ops = DiffToJournalConverter.Convert(diff, pre);

        Assert.Single(ops);
        Assert.Equal(JournalOpKind.Remove, ops[0].Kind);
        Assert.Equal("$.b", ops[0].Path);
        Assert.Null(ops[0].Value);
    }

    // ---------------------------------------------------------------
    // T1-243: Same path, different values -> Set with pre's value
    // ---------------------------------------------------------------
    [Fact]
    public void Convert_DifferentValues_EmitsSetWithOriginalValue()
    {
        var pre = JsonNode.Parse("{\"x\":\"original\"}")!.AsObject();
        var post = JsonNode.Parse("{\"x\":\"changed\"}")!.AsObject();

        var diff = DomDiffer.Diff(pre, post);
        var ops = DiffToJournalConverter.Convert(diff, pre);

        Assert.Single(ops);
        Assert.Equal(JournalOpKind.Set, ops[0].Kind);
        Assert.Equal("$.x", ops[0].Path);
        Assert.Equal("original", ops[0].Value!.GetValue<string>());
    }

    // ---------------------------------------------------------------
    // T1-244: Nested path uses canonical form
    // ---------------------------------------------------------------
    [Fact]
    public void Convert_NestedStructure_EmitsCorrectJsonPaths()
    {
        var pre = JsonNode.Parse("{\"foo\":{\"bar\":{\"baz\":1,\"qux\":100}}}")!.AsObject();
        var post = JsonNode.Parse("{\"foo\":{\"bar\":{\"baz\":1}}}")!.AsObject();

        var diff = DomDiffer.Diff(pre, post);
        var ops = DiffToJournalConverter.Convert(diff, pre);

        Assert.Single(ops);
        Assert.Equal(JournalOpKind.Set, ops[0].Kind);
        Assert.Equal("$.foo.bar.qux", ops[0].Path);
        Assert.Equal(100, ops[0].Value!.GetValue<int>());
    }

    // ---------------------------------------------------------------
    // T1-245: GUID key (contains hyphens) -> bracket form ['key']
    // ---------------------------------------------------------------
    [Fact]
    public void Convert_HyphenatedKey_EmitsBracketedPath()
    {
        const string guid = "3702ba5f-04ea-40e0-b1ee-893931426e75";
        var pre = JsonNode.Parse(
            $"{{\"entities\":{{\"{guid}\":{{\"TkbIdentity\":{{\"TkbType\":101}}}}}}}}")!.AsObject();
        var post = JsonNode.Parse(
            $"{{\"entities\":{{\"{guid}\":{{}}}}}}")!.AsObject();

        var diff = DomDiffer.Diff(pre, post);
        var ops = DiffToJournalConverter.Convert(diff, pre);

        Assert.Single(ops);
        Assert.Equal(JournalOpKind.Set, ops[0].Kind);
        Assert.Equal($"$.entities['{guid}'].TkbIdentity", ops[0].Path);
        Assert.NotNull(ops[0].Value);
    }

    // ---------------------------------------------------------------
    // T1-246: Array parent in pre -> integer index [N] form in path
    // ---------------------------------------------------------------
    [Fact]
    public void Convert_ArrayElement_EmitsIndexedPath()
    {
        // preMigrationDom has an array at $.items
        var pre = JsonNode.Parse("{\"items\":[{\"x\":1},{\"x\":2}]}")!.AsObject();

        // Manually construct a DiffNode tree simulating a change to items[0].
        // DomDiffer treats arrays as leaf DiffValues, so we build the tree by
        // hand to exercise the array-index segment path in the converter.
        var child = new DiffValue("0", "{\"x\":1}", "null", JsonValueKind.Object, true);
        var itemsNode = new DiffObject("items");
        itemsNode.Children.Add(child);
        itemsNode.EvaluateModificationState();
        var root = new DiffObject("$");
        root.Children.Add(itemsNode);
        root.EvaluateModificationState();

        var ops = DiffToJournalConverter.Convert(root, pre);

        Assert.Single(ops);
        Assert.Equal(JournalOpKind.Set, ops[0].Kind);
        Assert.Equal("$.items[0]", ops[0].Path);
        // Value should be items[0] from pre = {"x":1}
        Assert.NotNull(ops[0].Value);
    }
}
