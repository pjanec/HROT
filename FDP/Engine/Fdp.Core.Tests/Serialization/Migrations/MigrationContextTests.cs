using Fdp.Core.Serialization.Migrations;

namespace Fdp.Core.Tests.Serialization.Migrations;

public class MigrationContextTests
{
    // ---------------------------------------------------------------
    // CurrentPath / scope mechanics
    // ---------------------------------------------------------------

    // T1-090: Returns "$" with no active scopes.
    [Fact]
    public void CurrentPath_NoScopes_ReturnsRoot()
    {
        var ctx = new MigrationContext("Test.Doc", null);
        Assert.Equal("$", ctx.CurrentPath);
    }

    // T1-091: WithItem("entities") produces "$.entities".
    [Fact]
    public void WithItem_IdentifierKey_BuildsDottedPath()
    {
        var ctx = new MigrationContext("Test.Doc", null);
        using (ctx.WithItem("entities"))
        {
            Assert.Equal("$.entities", ctx.CurrentPath);
        }
    }

    // T1-092: WithItem with a UUID key produces bracketed path.
    [Fact]
    public void WithItem_HyphenatedKey_BuildsBracketedPath()
    {
        var ctx = new MigrationContext("Test.Doc", null);
        string guid = "00000000-0000-0000-0000-000000000001";
        using (ctx.WithItem(guid))
        {
            Assert.Equal($"$['{guid}']", ctx.CurrentPath);
        }
    }

    // T1-093: Two nested WithItems produce concatenated path.
    [Fact]
    public void WithItem_NestedScopes_ConcatenatesPath()
    {
        var ctx = new MigrationContext("Test.Doc", null);
        using (ctx.WithItem("entities"))
        using (ctx.WithItem("name"))
        {
            Assert.Equal("$.entities.name", ctx.CurrentPath);
        }
    }

    // T1-094: After disposing the using-block, CurrentPath reverts.
    [Fact]
    public void WithItem_DisposeUnwindsScope()
    {
        var ctx = new MigrationContext("Test.Doc", null);
        using (ctx.WithItem("entities"))
        {
            Assert.Equal("$.entities", ctx.CurrentPath);
        }
        Assert.Equal("$", ctx.CurrentPath);
    }

    // T1-095: LIFO unwinding behavior verified.
    [Fact]
    public void WithItem_NestedDispose_LifoOrder()
    {
        var ctx = new MigrationContext("Test.Doc", null);
        var outerScope = ctx.WithItem("outer");
        var innerScope = ctx.WithItem("inner");

        Assert.Equal("$.outer.inner", ctx.CurrentPath);

        innerScope.Dispose();
        Assert.Equal("$.outer", ctx.CurrentPath);

        outerScope.Dispose();
        Assert.Equal("$", ctx.CurrentPath);
    }

    // T1-096: WithIndex(3) produces "[3]" segment.
    [Fact]
    public void WithIndex_ProducesBracketedNumeric()
    {
        var ctx = new MigrationContext("Test.Doc", null);
        using (ctx.WithItem("list"))
        using (ctx.WithIndex(3))
        {
            Assert.Equal("$.list[3]", ctx.CurrentPath);
        }
    }

    // T1-097: Multi-segment suffix appends without re-encoding.
    [Fact]
    public void WithPathSuffix_PreCanonicalized_AppendsVerbatim()
    {
        var ctx = new MigrationContext("Test.Doc", null);
        using (ctx.WithItem("root"))
        using (ctx.WithPathSuffix(".child[0]"))
        {
            Assert.Equal("$.root.child[0]", ctx.CurrentPath);
        }
    }

    // T1-098: Key with single quote is escaped to \'.
    [Fact]
    public void WithItem_QuoteInKey_EscapesProperly()
    {
        var ctx = new MigrationContext("Test.Doc", null);
        using (ctx.WithItem("it's"))
        {
            Assert.Equal("$['it\\'s']", ctx.CurrentPath);
        }
    }

    // T1-099: Key with backslash is escaped to \\.
    [Fact]
    public void WithItem_BackslashInKey_EscapesProperly()
    {
        var ctx = new MigrationContext("Test.Doc", null);
        using (ctx.WithItem("a\\b"))
        {
            Assert.Equal("$['a\\\\b']", ctx.CurrentPath);
        }
    }

    // ---------------------------------------------------------------
    // AddWarning
    // ---------------------------------------------------------------

    // T1-100: Warning's Path field reflects the active scope.
    [Fact]
    public void AddWarning_InsideScope_CapturesCurrentPath()
    {
        var ctx = new MigrationContext("Test.Doc", null);
        using (ctx.WithItem("entities"))
        {
            ctx.AddWarning("something is off");
        }

        Assert.Single(ctx.Report.Warnings);
        Assert.Equal("$.entities", ctx.Report.Warnings[0].Path);
        Assert.Equal("something is off", ctx.Report.Warnings[0].Message);
    }

    // T1-101: Warning's Path field is "$" when no scope active.
    [Fact]
    public void AddWarning_OutsideScope_CapturesRootPath()
    {
        var ctx = new MigrationContext("Test.Doc", null);
        ctx.AddWarning("root warning");

        Assert.Single(ctx.Report.Warnings);
        Assert.Equal("$", ctx.Report.Warnings[0].Path);
    }

    // T1-102: MigrationReport.AddWarning(string) creates warning with "$" path.
    [Fact]
    public void MigrationReport_AddWarningStringOverload_AddsWarningWithRootPath()
    {
        var ctx = new MigrationContext("Test.Doc", null);

        // Call the string-only overload on the report directly (line 60 in MigrationReport.cs).
        ctx.Report.AddWarning("direct report warning");

        Assert.Single(ctx.Report.Warnings);
        Assert.Equal("$", ctx.Report.Warnings[0].Path);
        Assert.Equal("direct report warning", ctx.Report.Warnings[0].Message);
    }
}
