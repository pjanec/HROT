using Hrot.Editor.AiShared.Browser;

namespace Hrot.Editor.AiShared.Tests.Browser;

public sealed class FolderTreePickerTests
{
    // ── Build_NestedPaths_ProducesCorrectHierarchy ───────────────────

    [Fact]
    public void Build_NestedPaths_ProducesCorrectHierarchy()
    {
        var paths = new[] { "a/b/x", "a/b/y", "a/z" };

        var root = FolderTreePicker.Build(paths);

        // Root.
        Assert.Equal("", root.Name);
        Assert.Equal("", root.FullPath);
        Assert.False(root.IsLeaf);

        // Root has 1 child: folder "a" (the only top-level entry).
        Assert.Single(root.Children);
        var a = root.Children[0];
        Assert.Equal("a", a.Name);
        Assert.Equal("a", a.FullPath);
        Assert.False(a.IsLeaf);

        // "a" has 2 children: folder "b" (group 0) then leaf "z" (group 1).
        Assert.Equal(2, a.Children.Count);

        // First child: folder "b".
        var b = a.Children[0];
        Assert.Equal("b", b.Name);
        Assert.Equal("a/b", b.FullPath);
        Assert.False(b.IsLeaf);

        // Second child: leaf "z".
        var z = a.Children[1];
        Assert.Equal("z", z.Name);
        Assert.Equal("a/z", z.FullPath);
        Assert.True(z.IsLeaf);
        Assert.Empty(z.Children);

        // "b" has 2 leaves: "x" then "y" (both leaves, alphabetical).
        Assert.Equal(2, b.Children.Count);
        var x = b.Children[0];
        Assert.Equal("x", x.Name);
        Assert.Equal("a/b/x", x.FullPath);
        Assert.True(x.IsLeaf);
        Assert.Empty(x.Children);

        var y = b.Children[1];
        Assert.Equal("y", y.Name);
        Assert.Equal("a/b/y", y.FullPath);
        Assert.True(y.IsLeaf);
        Assert.Empty(y.Children);
    }

    // ── Build_EmptyAndRootLevelLeaves_Handled ────────────────────────

    [Fact]
    public void Build_EmptyAndRootLevelLeaves_Handled()
    {
        // Empty input.
        var emptyRoot = FolderTreePicker.Build(Array.Empty<string>());
        Assert.NotNull(emptyRoot);
        Assert.False(emptyRoot.IsLeaf);
        Assert.Empty(emptyRoot.Children);

        // Null collection (via Enumerable).
        var nullRoot = FolderTreePicker.Build(null!);
        Assert.NotNull(nullRoot);
        Assert.False(nullRoot.IsLeaf);
        Assert.Empty(nullRoot.Children);

        // Only null/empty entries skipped.
        var skippedRoot = FolderTreePicker.Build(new string?[] { null, "", "   ", null });
        Assert.NotNull(skippedRoot);
        Assert.False(skippedRoot.IsLeaf);
        // "   " is not null or empty (it's whitespace) — treated as a root-level leaf name.
        Assert.Single(skippedRoot.Children);
        Assert.Equal("   ", skippedRoot.Children[0].Name);
        Assert.True(skippedRoot.Children[0].IsLeaf);

        // Root-level leaf (no slash).
        var root = FolderTreePicker.Build(new[] { "x" });
        Assert.Single(root.Children);
        var leaf = root.Children[0];
        Assert.Equal("x", leaf.Name);
        Assert.Equal("x", leaf.FullPath);
        Assert.True(leaf.IsLeaf);
        Assert.Empty(leaf.Children);

        // Mix of root-level leaf and nested path.
        var mixed = FolderTreePicker.Build(new[] { "nested/a/b", "standalone" });
        Assert.Equal(2, mixed.Children.Count);

        // Folders first → "nested" (folder) before "standalone" (leaf).
        var nested = mixed.Children[0];
        Assert.Equal("nested", nested.Name);
        Assert.False(nested.IsLeaf);

        var standalone = mixed.Children[1];
        Assert.Equal("standalone", standalone.Name);
        Assert.True(standalone.IsLeaf);
    }

    // ── Build_IsStable_Sorted ───────────────────────────────────────

    [Fact]
    public void Build_IsStable_Sorted()
    {
        // Same paths in different input order.
        var order1 = new[] { "c/file2", "a/b/y", "a/z", "a/b/x", "c/file1" };
        var order2 = new[] { "a/b/x", "a/b/y", "a/z", "c/file1", "c/file2" };
        // Also test reverse order.
        var order3 = new[] { "c/file1", "c/file2", "a/z", "a/b/x", "a/b/y" };

        var root1 = FolderTreePicker.Build(order1);
        var root2 = FolderTreePicker.Build(order2);
        var root3 = FolderTreePicker.Build(order3);

        // Verify stable: same structure regardless of input order.
        AssertTreesEqual(root1, root2);
        AssertTreesEqual(root2, root3);

        // Verify sort rule: folders before leaves, alphabetical within each group.
        // Root children: "a" (folder) then "c" (folder) — alphabetical.
        Assert.Equal(2, root1.Children.Count);
        Assert.Equal("a", root1.Children[0].Name);
        Assert.False(root1.Children[0].IsLeaf);
        Assert.Equal("c", root1.Children[1].Name);
        Assert.False(root1.Children[1].IsLeaf);

        // Under "a": folder "b" then leaf "z".
        var a = root1.Children[0];
        Assert.Equal(2, a.Children.Count);
        Assert.Equal("b", a.Children[0].Name);
        Assert.False(a.Children[0].IsLeaf); // folder b
        Assert.Equal("z", a.Children[1].Name);
        Assert.True(a.Children[1].IsLeaf);

        // Under "a/b": leaves "x" then "y" (alphabetical).
        var b = a.Children[0];
        Assert.Equal(2, b.Children.Count);
        Assert.Equal("x", b.Children[0].Name);
        Assert.True(b.Children[0].IsLeaf);
        Assert.Equal("y", b.Children[1].Name);
        Assert.True(b.Children[1].IsLeaf);

        // Under "c": leaves "file1" then "file2" (alphabetical).
        var c = root1.Children[1];
        Assert.Equal(2, c.Children.Count);
        Assert.Equal("file1", c.Children[0].Name);
        Assert.True(c.Children[0].IsLeaf);
        Assert.Equal("file2", c.Children[1].Name);
        Assert.True(c.Children[1].IsLeaf);
    }

    // ── Deeply nested single path ───────────────────────────────────

    [Fact]
    public void Build_SingleDeepPath_CreatesChain()
    {
        var root = FolderTreePicker.Build(new[] { "a/b/c/d" });

        var a = Assert.Single(root.Children);
        Assert.Equal("a", a.Name);
        Assert.False(a.IsLeaf);

        var b = Assert.Single(a.Children);
        Assert.Equal("b", b.Name);
        Assert.False(b.IsLeaf);

        var c = Assert.Single(b.Children);
        Assert.Equal("c", c.Name);
        Assert.False(c.IsLeaf);

        var d = Assert.Single(c.Children);
        Assert.Equal("d", d.Name);
        Assert.True(d.IsLeaf);
    }

    // ── Node that is both folder and leaf ───────────────────────────

    [Fact]
    public void Build_FolderThatIsAlsoLeaf_IsLeafTrue()
    {
        // "shared/x" makes "shared" a folder; "shared" (bare) also makes it a leaf.
        var root = FolderTreePicker.Build(new[] { "shared/x", "shared" });

        var shared = Assert.Single(root.Children);
        Assert.Equal("shared", shared.Name);
        Assert.True(shared.IsLeaf, "Node should be a leaf when a path ends there.");
        Assert.Single(shared.Children); // also has child "x" as a folder
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static void AssertTreesEqual(FolderTreeNode a, FolderTreeNode b)
    {
        Assert.Equal(a.Name, b.Name);
        Assert.Equal(a.FullPath, b.FullPath);
        Assert.Equal(a.IsLeaf, b.IsLeaf);
        Assert.Equal(a.Children.Count, b.Children.Count);
        for (int i = 0; i < a.Children.Count; i++)
            AssertTreesEqual(a.Children[i], b.Children[i]);
    }
}
