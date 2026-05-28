using System;
using Fdp.Core.Serialization.Migrations;
using Xunit;

namespace Fdp.Core.Tests.Serialization.Migrations;

/// <summary>
/// Tests for <see cref="MigrationException"/> constructors (T1-050, T1-051).
/// </summary>
public sealed class MigrationExceptionTests
{
    // T1-050: Full constructor sets all properties correctly.
    [Fact]
    public void MigrationException_FullConstructor_PropertiesSetCorrectly()
    {
        var ex = new MigrationException(
            "msg",
            "Test.Doc",
            1,
            2,
            "path/to/file.json",
            "$.items[0]");

        Assert.Equal("msg", ex.Message);
        Assert.Equal("Test.Doc", ex.DocType);
        Assert.Equal(1, ex.FromVersion);
        Assert.Equal(2, ex.ToVersion);
        Assert.Equal("path/to/file.json", ex.SourcePath);
        Assert.Equal("$.items[0]", ex.Path);
        Assert.Null(ex.InnerException);
    }

    // T1-051: Message+inner constructor preserves the inner exception.
    [Fact]
    public void MigrationException_MessageAndInner_HasInnerException()
    {
        var inner = new Exception("cause");
        var ex = new MigrationException("msg", inner);

        Assert.Equal("msg", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }
}
