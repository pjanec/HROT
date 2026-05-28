using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization.Migrations.Internal;
using Xunit;

namespace Fdp.Core.Tests.Serialization.Migrations.Internal;

/// <summary>
/// Tests for <see cref="JsonPathApplicator"/> static methods (T1-192..T1-215).
/// </summary>
public sealed class JsonPathApplicatorTests
{
    // ---------------------------------------------------------------
    // Read — happy paths
    // ---------------------------------------------------------------

    // T1-192: Root path ($) returns the root object itself.
    [Fact]
    public void Read_RootPath_ReturnsRoot()
    {
        var root = JsonNode.Parse("{\"a\":1}")!.AsObject();
        var path = JsonPathParser.Parse("$");

        var result = JsonPathApplicator.Read(root, path);

        Assert.Same(root, result);
    }

    // T1-193: Quoted key segment (e.g., GUID key) happy path.
    [Fact]
    public void Read_QuotedKeySegment_HappyPath()
    {
        var json = "{\"a\":{\"00000000-0000-0000-0000-000000000001\":{\"x\":5}}}";
        var root = JsonNode.Parse(json)!.AsObject();
        var path = JsonPathParser.Parse("$.a['00000000-0000-0000-0000-000000000001'].x");

        var result = JsonPathApplicator.Read(root, path);

        Assert.NotNull(result);
        Assert.Equal(5, result!.GetValue<int>());
    }

    // T1-194: Array index in-bounds returns the correct element.
    [Fact]
    public void Read_ArrayIndex_InBounds_ReturnsElement()
    {
        var root = JsonNode.Parse("{\"items\":[10,20,30]}")!.AsObject();
        var path = JsonPathParser.Parse("$.items[1]");

        var result = JsonPathApplicator.Read(root, path);

        Assert.NotNull(result);
        Assert.Equal(20, result!.GetValue<int>());
    }

    // T1-195: Array index out of bounds returns null.
    [Fact]
    public void Read_ArrayIndex_OutOfBounds_ReturnsNull()
    {
        var root = JsonNode.Parse("{\"items\":[10]}")!.AsObject();
        var path = JsonPathParser.Parse("$.items[5]");

        var result = JsonPathApplicator.Read(root, path);

        Assert.Null(result);
    }

    // T1-196: Dotted segment on a non-object node returns null.
    [Fact]
    public void Read_DottedSegment_NotAnObject_ReturnsNull()
    {
        var root = JsonNode.Parse("{\"items\":[1,2,3]}")!.AsObject();
        var path = JsonPathParser.Parse("$.items.x");

        var result = JsonPathApplicator.Read(root, path);

        Assert.Null(result);
    }

    // T1-197: Quoted key segment on a non-object node returns null.
    [Fact]
    public void Read_QuotedKeySegment_NotAnObject_ReturnsNull()
    {
        var root = JsonNode.Parse("{\"x\":42}")!.AsObject();
        var path = JsonPathParser.Parse("$.x['key']");

        var result = JsonPathApplicator.Read(root, path);

        Assert.Null(result);
    }

    // T1-198: Array index on a non-array node returns null.
    [Fact]
    public void Read_ArrayIndexOnNonArray_ReturnsNull()
    {
        var root = JsonNode.Parse("{\"x\":{\"y\":1}}")!.AsObject();
        var path = JsonPathParser.Parse("$.x[0]");

        var result = JsonPathApplicator.Read(root, path);

        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // TryWrite
    // ---------------------------------------------------------------

    // T1-199: Writing to root path ($) returns false.
    [Fact]
    public void TryWrite_RootPath_ReturnsFalse()
    {
        var root = JsonNode.Parse("{\"a\":1}")!.AsObject();
        var path = JsonPathParser.Parse("$");

        var result = JsonPathApplicator.TryWrite(root, path, JsonValue.Create(99));

        Assert.False(result);
    }

    // T1-200: Writing via quoted key segment succeeds.
    [Fact]
    public void TryWrite_QuotedKeySegment_HappyPath()
    {
        var root = JsonNode.Parse("{\"map\":{}}")!.AsObject();
        var path = JsonPathParser.Parse("$.map['my-hyphen-key']");

        var result = JsonPathApplicator.TryWrite(root, path, JsonValue.Create("hello"));

        Assert.True(result);
        var val = root["map"]!.AsObject()["my-hyphen-key"];
        Assert.NotNull(val);
        Assert.Equal("hello", val!.GetValue<string>());
    }

    // T1-201: Writing to an in-bounds array index succeeds.
    [Fact]
    public void TryWrite_ArrayIndexSegment_InBounds_WritesValue()
    {
        var root = JsonNode.Parse("{\"arr\":[1,2,3]}")!.AsObject();
        var path = JsonPathParser.Parse("$.arr[1]");

        var result = JsonPathApplicator.TryWrite(root, path, JsonValue.Create(99));

        Assert.True(result);
        Assert.Equal(99, root["arr"]![1]!.GetValue<int>());
    }

    // T1-202: Writing to an out-of-bounds array index returns false.
    [Fact]
    public void TryWrite_ArrayIndexSegment_OutOfBounds_ReturnsFalse()
    {
        var root = JsonNode.Parse("{\"arr\":[1]}")!.AsObject();
        var path = JsonPathParser.Parse("$.arr[10]");

        var result = JsonPathApplicator.TryWrite(root, path, JsonValue.Create(99));

        Assert.False(result);
    }

    // T1-203: Last segment is dotted but parent is an array — returns false.
    [Fact]
    public void TryWrite_LastSegment_NotAnObject_ForDotted_ReturnsFalse()
    {
        // $.arr.name — arr is an array, not an object; parent of "name" is array
        var root = JsonNode.Parse("{\"arr\":[1,2]}")!.AsObject();
        var path = JsonPathParser.Parse("$.arr.name");

        var result = JsonPathApplicator.TryWrite(root, path, JsonValue.Create("x"));

        Assert.False(result);
    }

    // T1-204: Missing intermediate parent returns false.
    [Fact]
    public void TryWrite_MissingIntermediateParent_ReturnsFalse()
    {
        var root = JsonNode.Parse("{\"a\":{}}")!.AsObject();
        var path = JsonPathParser.Parse("$.b.c");

        var result = JsonPathApplicator.TryWrite(root, path, JsonValue.Create(1));

        Assert.False(result);
    }

    // ---------------------------------------------------------------
    // TryRemove
    // ---------------------------------------------------------------

    // T1-205: Removing root path ($) returns false.
    [Fact]
    public void TryRemove_RootPath_ReturnsFalse()
    {
        var root = JsonNode.Parse("{\"a\":1}")!.AsObject();
        var path = JsonPathParser.Parse("$");

        var result = JsonPathApplicator.TryRemove(root, path);

        Assert.False(result);
    }

    // T1-206: Removing an existing dotted property removes it and returns true.
    [Fact]
    public void TryRemove_ExistingDottedProperty_RemovesAndReturnsTrue()
    {
        var root = JsonNode.Parse("{\"a\":1,\"b\":2}")!.AsObject();
        var path = JsonPathParser.Parse("$.a");

        var result = JsonPathApplicator.TryRemove(root, path);

        Assert.True(result);
        Assert.False(root.ContainsKey("a"));
        Assert.True(root.ContainsKey("b"));
    }

    // T1-207: Removing a missing dotted property returns true (idempotent).
    [Fact]
    public void TryRemove_MissingDottedProperty_ReturnsTrue()
    {
        var root = JsonNode.Parse("{\"a\":1}")!.AsObject();
        var path = JsonPathParser.Parse("$.b");

        var result = JsonPathApplicator.TryRemove(root, path);

        Assert.True(result);
    }

    // T1-208: Removing a quoted key segment removes it and returns true.
    [Fact]
    public void TryRemove_QuotedKeySegment_RemovesAndReturnsTrue()
    {
        var root = JsonNode.Parse("{\"map\":{\"my-key\":\"val\"}}")!.AsObject();
        var path = JsonPathParser.Parse("$.map['my-key']");

        var result = JsonPathApplicator.TryRemove(root, path);

        Assert.True(result);
        Assert.False(root["map"]!.AsObject().ContainsKey("my-key"));
    }

    // T1-209: Removing an in-bounds array index removes the element.
    [Fact]
    public void TryRemove_ArrayIndexSegment_InBounds_RemovesAndReturnsTrue()
    {
        var root = JsonNode.Parse("{\"items\":[10,20,30]}")!.AsObject();
        var path = JsonPathParser.Parse("$.items[1]");

        var result = JsonPathApplicator.TryRemove(root, path);

        Assert.True(result);
        var arr = root["items"]!.AsArray();
        Assert.Equal(2, arr.Count);
        Assert.Equal(10, arr[0]!.GetValue<int>());
        Assert.Equal(30, arr[1]!.GetValue<int>());
    }

    // T1-210: Removing an out-of-bounds array index returns true (already absent).
    [Fact]
    public void TryRemove_ArrayIndexSegment_OutOfBounds_ReturnsTrue()
    {
        var root = JsonNode.Parse("{\"items\":[10]}")!.AsObject();
        var path = JsonPathParser.Parse("$.items[5]");

        var result = JsonPathApplicator.TryRemove(root, path);

        Assert.True(result);
        Assert.Single(root["items"]!.AsArray());
    }

    // T1-211: Final dotted segment with array parent returns false.
    [Fact]
    public void TryRemove_ParentIsNotJsonObject_ForDottedFinalSegment_ReturnsFalse()
    {
        var root = JsonNode.Parse("{\"arr\":[1,2]}")!.AsObject();
        var path = JsonPathParser.Parse("$.arr.x");

        var result = JsonPathApplicator.TryRemove(root, path);

        Assert.False(result);
    }

    // T1-212: Missing intermediate parent returns false.
    [Fact]
    public void TryRemove_MissingIntermediateParent_ReturnsFalse()
    {
        var root = JsonNode.Parse("{\"a\":{}}")!.AsObject();
        var path = JsonPathParser.Parse("$.b.c");

        var result = JsonPathApplicator.TryRemove(root, path);

        Assert.False(result);
    }

    // ---------------------------------------------------------------
    // Descend helper (via TryWrite/TryRemove intermediate traversal)
    // ---------------------------------------------------------------

    // T1-213: Quoted key descent on a scalar node returns false.
    [Fact]
    public void Descend_QuotedKeyOnNonObject_ReturnsFalse()
    {
        // $.x['k'].deeper — x is scalar 42, so descent fails
        var root = JsonNode.Parse("{\"x\":42}")!.AsObject();
        var path = JsonPathParser.Parse("$.x['k'].deeper");

        var result = JsonPathApplicator.TryRemove(root, path);

        Assert.False(result);
    }

    // T1-214: Array index descent on a non-array node returns false.
    [Fact]
    public void Descend_ArrayIndexOnNonArray_ReturnsFalse()
    {
        // $.x[0].deeper — x is an object not array, so descent fails
        var root = JsonNode.Parse("{\"x\":{\"y\":1}}")!.AsObject();
        var path = JsonPathParser.Parse("$.x[0].deeper");

        var result = JsonPathApplicator.TryRemove(root, path);

        Assert.False(result);
    }

    // T1-215: Array index descent out-of-bounds on intermediate returns false.
    [Fact]
    public void Descend_ArrayIndex_OutOfBoundsOnIntermediateParent_ReturnsFalse()
    {
        // $.arr[5].key — arr has 1 element so index 5 is out of bounds
        var root = JsonNode.Parse("{\"arr\":[{\"key\":\"v\"}]}")!.AsObject();
        var path = JsonPathParser.Parse("$.arr[5].key");

        var result = JsonPathApplicator.TryRemove(root, path);

        Assert.False(result);
    }

    // ---------------------------------------------------------------
    // T1-216: Read with QuotedKey where key is absent returns null
    // ---------------------------------------------------------------
    [Fact]
    public void Read_QuotedKey_KeyNotFound_ReturnsNull()
    {
        // $.map['missing'] — 'missing' key does not exist in map
        var root = JsonNode.Parse("{\"map\":{\"other\":1}}")!.AsObject();
        var path = JsonPathParser.Parse("$.map['missing']");

        var result = JsonPathApplicator.Read(root, path);

        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // T1-217: TryWrite with QuotedKey final segment when parent is JsonArray
    // ---------------------------------------------------------------
    [Fact]
    public void TryWrite_QuotedKey_FinalParentIsArray_ReturnsFalse()
    {
        // $.items['key'] — items is an array, QuotedKey on non-object returns false
        var root = JsonNode.Parse("{\"items\":[1,2,3]}")!.AsObject();
        var path = JsonPathParser.Parse("$.items['key']");

        var result = JsonPathApplicator.TryWrite(root, path, JsonValue.Create(42));

        Assert.False(result);
    }

    // ---------------------------------------------------------------
    // T1-218: TryWrite with ArrayIndex final segment when parent is JsonObject
    // ---------------------------------------------------------------
    [Fact]
    public void TryWrite_ArrayIndex_FinalParentIsObject_ReturnsFalse()
    {
        // $.x.y[0] — y is a scalar, so parent of [0] is not a JsonArray
        var root = JsonNode.Parse("{\"x\":{\"y\":99}}")!.AsObject();
        var path = JsonPathParser.Parse("$.x.y[0]");

        var result = JsonPathApplicator.TryWrite(root, path, JsonValue.Create(0));

        Assert.False(result);
    }

    // ---------------------------------------------------------------
    // T1-219: TryRemove with QuotedKey final segment when parent is JsonArray
    // ---------------------------------------------------------------
    [Fact]
    public void TryRemove_QuotedKey_FinalParentIsArray_ReturnsFalse()
    {
        // $.items['key'] — items is an array, QuotedKey remove on non-object returns false
        var root = JsonNode.Parse("{\"items\":[1,2,3]}")!.AsObject();
        var path = JsonPathParser.Parse("$.items['key']");

        var result = JsonPathApplicator.TryRemove(root, path);

        Assert.False(result);
    }

    // ---------------------------------------------------------------
    // T1-220: TryRemove with ArrayIndex final segment when parent is JsonObject
    // ---------------------------------------------------------------
    [Fact]
    public void TryRemove_ArrayIndex_FinalParentIsObject_ReturnsFalse()
    {
        // $.x[0] — x is an object not array, returns false
        var root = JsonNode.Parse("{\"x\":{\"y\":1}}")!.AsObject();
        var path = JsonPathParser.Parse("$.x[0]");

        var result = JsonPathApplicator.TryRemove(root, path);

        Assert.False(result);
    }

    // ---------------------------------------------------------------
    // T1-221: Descend DottedSegment on intermediate non-object returns false
    // ---------------------------------------------------------------
    [Fact]
    public void Descend_DottedOnIntermediateNonObject_ReturnsFalse()
    {
        // $.arr.foo.bar — arr is array; descending DottedSegment("foo") on array fails
        var root = JsonNode.Parse("{\"arr\":[1,2,3]}")!.AsObject();
        var path = JsonPathParser.Parse("$.arr.foo.bar");

        var result = JsonPathApplicator.TryRemove(root, path);

        Assert.False(result);
    }

    // ---------------------------------------------------------------
    // T1-222: Descend QuotedKeySegment on intermediate JsonObject succeeds
    // ---------------------------------------------------------------
    [Fact]
    public void Descend_QuotedKeyOnIntermediateObject_Continues()
    {
        // $.map['k1'].value — map is object, 'k1' exists and is object;
        // descend through QuotedKeySegment as intermediate step
        var root = JsonNode.Parse("{\"map\":{\"k1\":{\"value\":42}}}")!.AsObject();
        var path = JsonPathParser.Parse("$.map['k1'].value");

        var result = JsonPathApplicator.TryRemove(root, path);

        Assert.True(result);
        // 'value' key should be removed from k1 object
        Assert.False(root["map"]!.AsObject()["k1"]!.AsObject().ContainsKey("value"));
    }
}
