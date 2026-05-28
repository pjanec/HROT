using System.Text.Json.Nodes;
using Fdp.Core.Serialization.Migrations;
using Fdp.Core.Serialization.Migrations.Internal;

namespace Fdp.Core.Tests.Serialization.Migrations.Internal;

public class JsonPathTests
{
    // ---------------------------------------------------------------
    // Parser — happy paths
    // ---------------------------------------------------------------

    // T1-160: Root-only path is valid.
    [Fact]
    public void Parse_RootOnly_ReturnsEmptySegmentList()
    {
        var path = JsonPathParser.Parse("$");
        Assert.Equal("$", path.Original);
        Assert.Empty(path.Segments);
    }

    // T1-161: Single dotted identifier.
    [Fact]
    public void Parse_SingleDottedKey_ReturnsDottedSegment()
    {
        var path = JsonPathParser.Parse("$.entities");
        Assert.Single(path.Segments);
        Assert.IsType<DottedSegment>(path.Segments[0]);
        Assert.Equal("entities", ((DottedSegment)path.Segments[0]).Identifier);
    }

    // T1-162: Nested dotted path.
    [Fact]
    public void Parse_NestedDottedPath_ReturnsMultipleSegments()
    {
        var path = JsonPathParser.Parse("$.a.b.c");
        Assert.Equal(3, path.Segments.Count);
        Assert.All(path.Segments, s => Assert.IsType<DottedSegment>(s));
    }

    // T1-163: Bracketed string key.
    [Fact]
    public void Parse_BracketedKey_ReturnsQuotedKeySegment()
    {
        string guid = "00000000-0000-0000-0000-000000000001";
        var path = JsonPathParser.Parse($"$['{guid}']");
        Assert.Single(path.Segments);
        var seg = Assert.IsType<QuotedKeySegment>(path.Segments[0]);
        Assert.Equal(guid, seg.Key);
    }

    // T1-164: Array index [0].
    [Fact]
    public void Parse_ArrayIndex_ReturnsArrayIndexSegment()
    {
        var path = JsonPathParser.Parse("$.list[0]");
        Assert.Equal(2, path.Segments.Count);
        var seg = Assert.IsType<ArrayIndexSegment>(path.Segments[1]);
        Assert.Equal(0, seg.Index);
    }

    // T1-165: Mixed dotted + bracket + index.
    [Fact]
    public void Parse_MixedSegments_ReturnsAllThreeTypes()
    {
        string guid = "00000000-0000-0000-0000-000000000002";
        var path = JsonPathParser.Parse($"$.entities['{guid}'].tags[1]");
        Assert.Equal(4, path.Segments.Count);
        Assert.IsType<DottedSegment>(path.Segments[0]);
        Assert.IsType<QuotedKeySegment>(path.Segments[1]);
        Assert.IsType<DottedSegment>(path.Segments[2]);
        Assert.IsType<ArrayIndexSegment>(path.Segments[3]);
    }

    // T1-166: Escaped single quote inside bracketed key.
    [Fact]
    public void Parse_EscapedSingleQuoteInKey_Unescaped()
    {
        var path = JsonPathParser.Parse("$['it\\'s']");
        var seg = Assert.IsType<QuotedKeySegment>(path.Segments[0]);
        Assert.Equal("it's", seg.Key);
    }

    // T1-167: Escaped backslash inside bracketed key.
    [Fact]
    public void Parse_EscapedBackslashInKey_Unescaped()
    {
        var path = JsonPathParser.Parse("$['a\\\\b']");
        var seg = Assert.IsType<QuotedKeySegment>(path.Segments[0]);
        Assert.Equal("a\\b", seg.Key);
    }

    // ---------------------------------------------------------------
    // Parser — rejection cases
    // ---------------------------------------------------------------

    // T1-168: Must start with '$'.
    [Fact]
    public void Parse_MissingRootAnchor_Throws()
    {
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("entities.name"));
    }

    // T1-169: Wildcard in dotted position.
    [Fact]
    public void Parse_DottedWildcard_Throws()
    {
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$.*"));
    }

    // T1-170: Wildcard inside bracket.
    [Fact]
    public void Parse_BracketWildcard_Throws()
    {
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$[*]"));
    }

    // T1-171: Recursive descent is rejected.
    [Fact]
    public void Parse_RecursiveDescent_Throws()
    {
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$..name"));
    }

    // T1-172: Filter expression is rejected.
    [Fact]
    public void Parse_FilterExpression_Throws()
    {
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$[?(@.x > 1)]"));
    }

    // T1-173: Negative index is rejected.
    [Fact]
    public void Parse_NegativeIndex_Throws()
    {
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$[-1]"));
    }

    // T1-174: Slice is rejected.
    [Fact]
    public void Parse_SliceExpression_Throws()
    {
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$[1:3]"));
    }

    // T1-175: Null input throws ArgumentNullException.
    [Fact]
    public void Parse_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => JsonPathParser.Parse(null!));
    }

    // ---------------------------------------------------------------
    // Canonical output
    // ---------------------------------------------------------------

    // T1-176: Bracketed key that is a valid identifier normalises to dotted.
    [Fact]
    public void Parse_BracketedValidIdentifier_CanonicalisesToDotted()
    {
        var path = JsonPathParser.Parse("$['entities']");
        Assert.Equal("$.entities", path.ToString());
    }

    // T1-177: UUID key remains bracketed.
    [Fact]
    public void Parse_UuidKey_CanonicalIsBracketed()
    {
        string guid = "00000000-0000-0000-0000-000000000001";
        var path = JsonPathParser.Parse($"$['{guid}']");
        Assert.Equal($"$['{guid}']", path.ToString());
    }

    // T1-178: Round-trip for a complex path.
    [Fact]
    public void Parse_ComplexPath_RoundTripCanonical()
    {
        string guid = "00000000-0000-0000-0000-000000000003";
        string input = $"$.root['{guid}'].tags[2]";
        var path = JsonPathParser.Parse(input);
        Assert.Equal(input, path.ToString());
    }

    // ---------------------------------------------------------------
    // Read
    // ---------------------------------------------------------------

    // T1-185: Read an existing dotted property.
    [Fact]
    public void Read_ExistingDottedProperty_ReturnsValue()
    {
        var root = JsonNode.Parse("{\"name\":\"Alice\"}")!.AsObject();
        var path = JsonPathParser.Parse("$.name");
        var result = path.Read(root);
        Assert.NotNull(result);
        Assert.Equal("Alice", result!.GetValue<string>());
    }

    // T1-186: Read a nested property.
    [Fact]
    public void Read_NestedProperty_ReturnsValue()
    {
        var root = JsonNode.Parse("{\"a\":{\"b\":42}}")!.AsObject();
        var path = JsonPathParser.Parse("$.a.b");
        var result = path.Read(root);
        Assert.Equal(42, result!.GetValue<int>());
    }

    // T1-187: Read a missing path returns null.
    [Fact]
    public void Read_MissingPath_ReturnsNull()
    {
        var root = JsonNode.Parse("{\"a\":1}")!.AsObject();
        var path = JsonPathParser.Parse("$.b");
        Assert.Null(path.Read(root));
    }

    // T1-188: In System.Text.Json.Nodes, JSON null maps to C# null — Read returns null for JSON null,
    // same as for a missing path. The Read method does not distinguish them.
    [Fact]
    public void Read_JsonNullLiteral_ReturnsNull()
    {
        var root = JsonNode.Parse("{\"x\":null}")!.AsObject();
        var path = JsonPathParser.Parse("$.x");
        // JSON null literals are represented as C# null in System.Text.Json.Nodes.
        var result = path.Read(root);
        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // TryWrite
    // ---------------------------------------------------------------

    // T1-189: TryWrite creates a new property and returns true.
    [Fact]
    public void TryWrite_NewProperty_ReturnsTrueAndSetsValue()
    {
        var root = JsonNode.Parse("{\"a\":1}")!.AsObject();
        var path = JsonPathParser.Parse("$.b");
        bool ok = path.TryWrite(root, JsonValue.Create(99));
        Assert.True(ok);
        Assert.Equal(99, root["b"]!.GetValue<int>());
    }

    // T1-190: TryWrite with missing intermediate parent returns false.
    [Fact]
    public void TryWrite_MissingParent_ReturnsFalse()
    {
        var root = JsonNode.Parse("{\"a\":1}")!.AsObject();
        var path = JsonPathParser.Parse("$.z.nested");
        bool ok = path.TryWrite(root, JsonValue.Create("x"));
        Assert.False(ok);
    }

    // T1-191: TryWrite on a nested existing path overwrites the value.
    [Fact]
    public void TryWrite_ExistingNestedProperty_Overwrites()
    {
        var root = JsonNode.Parse("{\"a\":{\"b\":1}}")!.AsObject();
        var path = JsonPathParser.Parse("$.a.b");
        bool ok = path.TryWrite(root, JsonValue.Create(42));
        Assert.True(ok);
        Assert.Equal(42, root["a"]!.AsObject()["b"]!.GetValue<int>());
    }

    // ---------------------------------------------------------------
    // TryRemove
    // ---------------------------------------------------------------

    // T1-192: TryRemove existing property returns true.
    [Fact]
    public void TryRemove_ExistingProperty_ReturnsTrueAndRemoves()
    {
        var root = JsonNode.Parse("{\"a\":1,\"b\":2}")!.AsObject();
        var path = JsonPathParser.Parse("$.a");
        bool ok = path.TryRemove(root);
        Assert.True(ok);
        Assert.Null(root["a"]);
    }

    // T1-193: TryRemove already-absent property returns true.
    [Fact]
    public void TryRemove_AlreadyAbsent_ReturnsTrue()
    {
        var root = JsonNode.Parse("{\"a\":1}")!.AsObject();
        var path = JsonPathParser.Parse("$.z");
        bool ok = path.TryRemove(root);
        Assert.True(ok);
    }

    // T1-194: TryRemove with missing intermediate parent returns false.
    [Fact]
    public void TryRemove_MissingParent_ReturnsFalse()
    {
        var root = JsonNode.Parse("{\"a\":1}")!.AsObject();
        var path = JsonPathParser.Parse("$.z.nested");
        bool ok = path.TryRemove(root);
        Assert.False(ok);
    }

    // ---------------------------------------------------------------
    // Parser — error paths not yet covered (T1-195..T1-207)
    // ---------------------------------------------------------------

    // T1-195: Path ending with '.' throws.
    [Fact]
    public void Parse_PathEndingWithDot_Throws()
    {
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$."));
    }

    // T1-196: Empty identifier after dot (next char is '[') throws.
    [Fact]
    public void Parse_EmptyIdentifierAfterDot_Throws()
    {
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$.[0]"));
    }

    // T1-197: Invalid identifier starting with digit throws.
    [Fact]
    public void Parse_InvalidIdentifierStartsWithDigit_Throws()
    {
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$.123bad"));
    }

    // T1-198: Unclosed '[' at end of path throws.
    [Fact]
    public void Parse_UnclosedBracketAtEnd_Throws()
    {
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$["));
    }

    // T1-199: Escape sequence at end of string throws.
    [Fact]
    public void Parse_EscapeAtEndOfString_Throws()
    {
        // $['key\   <- escape then end of string
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$['key\\"));
    }

    // T1-200: Unsupported escape character throws.
    [Fact]
    public void Parse_UnsupportedEscapeChar_Throws()
    {
        // $['key\n']  <- \n is not a supported escape (only \' and \\)
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$['key\\n']"));
    }

    // T1-201: Unclosed quoted key throws.
    [Fact]
    public void Parse_UnclosedQuotedKey_Throws()
    {
        // $['unclosed  <- no closing quote
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$['unclosed"));
    }

    // T1-202: Quoted key with no closing ']' throws.
    [Fact]
    public void Parse_QuotedKeyWithoutClosingBracket_Throws()
    {
        // $['key'x  <- closing quote present but no ']'
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$['key'x"));
    }

    // T1-203: Array index with no closing ']' throws.
    [Fact]
    public void Parse_ArrayIndexWithoutClosingBracket_Throws()
    {
        // $[0x  <- digits then unexpected char
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$[0x"));
    }

    // T1-204: Unexpected character after '[' throws.
    [Fact]
    public void Parse_UnexpectedCharAfterBracket_Throws()
    {
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$[^stuff]"));
    }

    // T1-205: Unexpected character at root level throws.
    [Fact]
    public void Parse_UnexpectedRootLevelChar_Throws()
    {
        Assert.Throws<MigrationException>(() => JsonPathParser.Parse("$#foo"));
    }

    // T1-206: BuildCanonical with a key that requires quoting (contains single quote).
    [Fact]
    public void BuildCanonical_KeyWithSingleQuote_UsesBracketEscape()
    {
        // Parsing $['it\'s'] successfully decodes to key "it's".
        // BuildCanonical must re-encode it as $['it\'s'].
        var path = JsonPathParser.Parse("$['it\\'s']");
        string canonical = path.ToString();
        // Key "it's" is not a plain identifier, so it stays bracketed and the ' is escaped.
        Assert.Contains("\\'", canonical);
    }

    // T1-207: BuildCanonical with a key containing backslash.
    [Fact]
    public void BuildCanonical_KeyWithBackslash_UsesBracketEscape()
    {
        // $['a\\b'] decodes to key "a\b".
        // BuildCanonical must re-encode as $['a\\b'].
        var path = JsonPathParser.Parse("$['a\\\\b']");
        string canonical = path.ToString();
        Assert.Contains("\\\\", canonical);
    }
}
