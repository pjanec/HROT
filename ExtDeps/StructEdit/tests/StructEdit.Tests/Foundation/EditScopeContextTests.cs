using FluentAssertions;
using StructEdit.Core;
using StructEdit.Core.Memory;
using StructEdit.Reflection;

namespace StructEdit.Tests.Foundation;

// ── TASK-T004 scope fixtures ──────────────────────────────────────────────────

file struct ScopeVec2 { public float X; public float Y; }
file struct ScopeParent { public ScopeVec2 Pos; public int Count; }

public class EditScopeTests
{
    [Fact]
    public void WholeComponent_HasEmptyIncludedPaths()
    {
        EditScope.WholeComponent.IncludedPaths.Count.Should().Be(0);
    }

    [Fact]
    public void ForField_SetsOnePath()
    {
        var scope = EditScope.ForField("$.X");
        scope.IncludedPaths.Count.Should().Be(1);
        scope.IncludedPaths[0].Value.Should().Be("$.X");
    }

    [Fact]
    public void ForFields_SetsMultiplePaths()
    {
        var scope = EditScope.ForFields("$.A", "$.B");
        scope.IncludedPaths.Count.Should().Be(2);
    }

    [Fact]
    public void ForField_IncludeChildren_DefaultsToTrue()
    {
        var scope = EditScope.ForField("$.X");
        scope.IncludeChildren.Should().BeTrue();
    }

    [Fact]
    public void ForField_IncludeParentsForContext_DefaultsToFalse()
    {
        var scope = EditScope.ForField("$.X");
        scope.IncludeParentsForContext.Should().BeFalse();
    }

    [Fact]
    public void WholeComponent_IsSingleton()
    {
        EditScope.WholeComponent.Should().BeSameAs(EditScope.WholeComponent);
    }
}

public class EditContextTests
{
    [Fact]
    public void With_Get_RoundTrip_ReturnsStoredValue()
    {
        var ctx = new EditContext().With("k", "v");
        ctx.Get<string>("k").Should().Be("v");
    }

    [Fact]
    public void With_OriginalContext_IsUnchanged()
    {
        var original = new EditContext();
        original.With("k", "v");
        original.Get("k").Should().BeNull();
    }

    [Fact]
    public void Get_MissingKey_ReturnsNull()
    {
        var ctx = new EditContext();
        ctx.Get("missing").Should().BeNull();
    }

    [Fact]
    public void Get_Generic_WrongType_ReturnsDefault()
    {
        var ctx = new EditContext().With("k", 42);
        ctx.Get<string>("k").Should().BeNull();
    }

    [Fact]
    public void With_MultipleValues_AllAccessible()
    {
        var ctx = new EditContext()
            .With("a", 1)
            .With("b", "hello");
        ctx.Get<int>("a").Should().Be(1);
        ctx.Get<string>("b").Should().Be("hello");
    }
}

// ─── TASK-T004: Additional scope tests ───────────────────────────────────────

file static class ScopeBuilder
{
    public static ReflectionEditDocumentBuilder Builder { get; } = new();

    public static NativeStructEditBuffer NativeFor<T>(T value) where T : unmanaged
    {
        var ops = RuntimeTypeOpsFactory.Get(typeof(T));
        return new NativeStructEditBuffer(typeof(T), value, ops);
    }
}

public class AdditionalScopeTests
{
    // T004-1: ForFields(["$.Pos", "$.Count"]) scope — only those nodes present
    [Fact]
    public void ForFields_Scope_OnlyRequestedFieldsInDocument()
    {
        using var buf = ScopeBuilder.NativeFor(new ScopeParent { Pos = default, Count = 5 });
        var scope = EditScope.ForFields("$.Pos", "$.Count");
        var doc = ScopeBuilder.Builder.Build(buf, typeof(ScopeParent), scope, null);

        doc.Root.Kind.Should().Be(EditNodeKind.SelectionRoot);
        doc.Root.Children.Should().HaveCount(2);
        doc.Root.Children.Select(c => c.Name).Should().Contain(new[] { "Pos", "Count" });
    }

    // T004-2: IncludeParentsForContext = true → parent node present and read-only
    [Fact]
    public void Scope_IncludeParentsForContext_True_ParentNodePresent()
    {
        using var buf = ScopeBuilder.NativeFor(new ScopeParent());
        var scope = new EditScope
        {
            IncludedPaths = new[] { EditPath.Parse("$.Pos.X") },
            IncludeChildren = true,
            IncludeParentsForContext = true,
        };
        var doc = ScopeBuilder.Builder.Build(buf, typeof(ScopeParent), scope, null);

        // Root has child Pos (read-only) with child X (editable)
        var posNode = doc.Root.Children.FirstOrDefault(c => c.Name == "Pos");
        posNode.Should().NotBeNull("Pos must appear as context parent");
        posNode!.IsReadOnly.Should().BeTrue("context-only parent must be read-only");
        posNode.Children.Should().Contain(c => c.Name == "X");
    }

    // T004-3: IncludeParentsForContext = false → parent node absent
    [Fact]
    public void Scope_IncludeParentsForContext_False_ParentNodeAbsent()
    {
        using var buf = ScopeBuilder.NativeFor(new ScopeParent());
        var scope = new EditScope
        {
            IncludedPaths = new[] { EditPath.Parse("$.Pos.X") },
            IncludeChildren = true,
            IncludeParentsForContext = false,
        };
        var doc = ScopeBuilder.Builder.Build(buf, typeof(ScopeParent), scope, null);

        // Without IncludeParentsForContext, the result should be just the X node directly
        doc.Root.Should().NotBeNull();
        // Root should be X itself (not Pos), since parents are stripped
        doc.Root.Name.Should().Be("X");
    }
}
