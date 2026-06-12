using FluentAssertions;
using NodeEditor.UI.Picker;
using Xunit;

namespace NodeEditor.UI.Tests.Picker;

public sealed class PickerTreeBuilderTests
{
    [Fact]
    public void Build_GroupsByCategoryPath_IntoNestedFolders()
    {
        var items = new List<(int, string?, string)>
        {
            (0, "Blueprint/AI",       "BT_Task"),
            (1, "Blueprint/AI",       "BT_Sequence"),
            (2, "HSM",                "HSM_Idle"),
            (3, "Blueprint",          "BP_Enemy"),
        };

        var root = PickerTreeBuilder.Build(items);

        // Root should have 2 folders: "Blueprint" and "HSM" (sorted).
        root.Folders.Should().HaveCount(2);
        root.Folders[0].Name.Should().Be("Blueprint");
        root.Folders[0].FullPath.Should().Be("Blueprint");
        root.Folders[1].Name.Should().Be("HSM");
        root.Folders[1].FullPath.Should().Be("HSM");

        // Blueprint folder has sub-folder "AI" and 1 direct leaf.
        var bp = root.Folders[0];
        bp.Folders.Should().HaveCount(1);
        bp.Folders[0].Name.Should().Be("AI");
        bp.Folders[0].FullPath.Should().Be("Blueprint/AI");
        bp.Leaves.Should().HaveCount(1);
        bp.Leaves[0].Name.Should().Be("BP_Enemy");
        bp.Leaves[0].FilteredIndex.Should().Be(3);

        // AI folder has 2 leaves.
        var ai = bp.Folders[0];
        ai.Leaves.Should().HaveCount(2);
        ai.Leaves[0].Name.Should().Be("BT_Task");
        ai.Leaves[0].FilteredIndex.Should().Be(0);
        ai.Leaves[1].Name.Should().Be("BT_Sequence");
        ai.Leaves[1].FilteredIndex.Should().Be(1);

        // HSM folder has 1 leaf.
        var hsm = root.Folders[1];
        hsm.Folders.Should().BeEmpty();
        hsm.Leaves.Should().HaveCount(1);
        hsm.Leaves[0].Name.Should().Be("HSM_Idle");
        hsm.Leaves[0].FilteredIndex.Should().Be(2);
    }

    [Fact]
    public void Build_OmitsEmptyFolders_DrivenByFilteredList()
    {
        // Even though we think of "Blueprint/AI" and "Blueprint/Combat" as valid categories,
        // only "Blueprint/AI" items appear in the filtered input → "Combat" folder should be absent.
        var items = new List<(int, string?, string)>
        {
            (0, "Blueprint/AI", "BT_Task"),
        };

        var root = PickerTreeBuilder.Build(items);

        root.Folders.Should().HaveCount(1);
        root.Folders[0].Name.Should().Be("Blueprint");
        root.Folders[0].Folders.Should().HaveCount(1);
        root.Folders[0].Folders[0].Name.Should().Be("AI");

        // "Combat" folder is absent — no item has that category in the input.
    }

    [Fact]
    public void Build_UncategorizedEntries_BecomeRootLeaves()
    {
        var items = new List<(int, string?, string)>
        {
            (0, null,  "RootItem1"),
            (1, "",    "RootItem2"),
            (2, "Cat", "CatItem"),
        };

        var root = PickerTreeBuilder.Build(items);

        root.Leaves.Should().HaveCount(2);
        root.Leaves[0].Name.Should().Be("RootItem1");
        root.Leaves[0].FilteredIndex.Should().Be(0);
        root.Leaves[1].Name.Should().Be("RootItem2");
        root.Leaves[1].FilteredIndex.Should().Be(1);

        root.Folders.Should().HaveCount(1);
        root.Folders[0].Name.Should().Be("Cat");
        root.Folders[0].Leaves.Should().HaveCount(1);
        root.Folders[0].Leaves[0].Name.Should().Be("CatItem");
    }

    [Fact]
    public void Build_FolderGrouping_IsCaseInsensitive()
    {
        var items = new List<(int, string?, string)>
        {
            (0, "AI/x", "Item1"),
            (1, "ai/y", "Item2"),
        };

        var root = PickerTreeBuilder.Build(items);

        // Both should group under a single "AI" folder (case-insensitive).
        root.Folders.Should().HaveCount(1);
        var aiFolder = root.Folders[0];
        // The folder name comes from the first segment encountered.
        // It should have two sub-folders "x" and "y".
        aiFolder.Folders.Should().HaveCount(2);
        aiFolder.Folders[0].Name.Should().Be("x");
        aiFolder.Folders[0].Leaves.Should().HaveCount(1);
        aiFolder.Folders[0].Leaves[0].Name.Should().Be("Item1");
        aiFolder.Folders[1].Name.Should().Be("y");
        aiFolder.Folders[1].Leaves.Should().HaveCount(1);
        aiFolder.Folders[1].Leaves[0].Name.Should().Be("Item2");
    }

    [Fact]
    public void Build_LeafCountMatchesInput()
    {
        var items = new List<(int, string?, string)>
        {
            (0, "A/B",   "L1"),
            (1, "A",     "L2"),
            (2, null,    "L3"),
            (3, "C/D/E", "L4"),
        };

        var root = PickerTreeBuilder.Build(items);

        int CountLeaves(PickerTreeBuilder.Node node)
        {
            int count = node.Leaves.Count;
            foreach (var f in node.Folders)
                count += CountLeaves(f);
            return count;
        }

        CountLeaves(root).Should().Be(4);
    }

    // ── VisualRows flattening (BATCH-48 / BUG-A14) ──────────────────────────

    /// <summary>
    /// Pure helper that flattens a <see cref="PickerTreeBuilder.Node"/> tree +
    /// an <see cref="ExpandedFolders"/> set into the expected VisualRows order
    /// (folders + leaves, DFS; children are shown only when their folder IS in expandedFolders).
    /// </summary>
    private static List<PickerState.TreeRow> FlattenVisualRows(
        PickerTreeBuilder.Node node,
        HashSet<string> expandedFolders,
        int depth = 0)
    {
        var rows = new List<PickerState.TreeRow>();

        foreach (var folder in node.Folders)
        {
            // Folder row.
            rows.Add(new PickerState.TreeRow(
                IsFolder: true, FolderPath: folder.FullPath, FilteredIndex: -1, Depth: depth));

            bool isExpanded = expandedFolders.Contains(folder.FullPath);
            if (isExpanded)
            {
                // Recurse into folder (handles its sub-folders + direct leaves).
                rows.AddRange(FlattenVisualRows(folder, expandedFolders, depth + 1));
            }
        }

        // Root-level leaves.
        foreach (var leaf in node.Leaves)
            rows.Add(new PickerState.TreeRow(
                IsFolder: false, FolderPath: "", FilteredIndex: leaf.FilteredIndex, Depth: depth));

        return rows;
    }

    [Fact]
    public void FlattenVisualRows_AllExpanded_ProducesDfsOrder()
    {
        // Build: A → {A/L1, A/B → {A/B/L2}}, C → {C/L3}, root-leaf L4
        var items = new List<(int, string?, string)>
        {
            (0, "A",     "L1"),
            (1, "A/B",   "L2"),
            (2, "C",     "L3"),
            (3, null,    "L4"),
        };

        var root = PickerTreeBuilder.Build(items);
        var expanded = new HashSet<string> { "A", "A/B", "C" }; // all folders expanded

        var rows = FlattenVisualRows(root, expanded, 0);

        // Expected DFS order (sub-folders before leaves, matching TreeLayout rendering):
        //   A (folder, depth 0)
        //   A/B (folder, depth 1)
        //   A/B/L2 (leaf, depth 2)
        //   A/L1 (leaf, depth 1)
        //   C (folder, depth 0)
        //   C/L3 (leaf, depth 1)
        //   L4 (root leaf, depth 0)
        rows.Should().HaveCount(7);

        // Folder A
        rows[0].IsFolder.Should().BeTrue();
        rows[0].FolderPath.Should().Be("A");
        rows[0].FilteredIndex.Should().Be(-1);
        rows[0].Depth.Should().Be(0);

        // Folder A/B
        rows[1].IsFolder.Should().BeTrue();
        rows[1].FolderPath.Should().Be("A/B");
        rows[1].FilteredIndex.Should().Be(-1);
        rows[1].Depth.Should().Be(1);

        // Leaf A/B/L2
        rows[2].IsFolder.Should().BeFalse();
        rows[2].FilteredIndex.Should().Be(1);
        rows[2].Depth.Should().Be(2);

        // Leaf A/L1
        rows[3].IsFolder.Should().BeFalse();
        rows[3].FilteredIndex.Should().Be(0);
        rows[3].Depth.Should().Be(1);

        // Folder C
        rows[4].IsFolder.Should().BeTrue();
        rows[4].FolderPath.Should().Be("C");
        rows[4].Depth.Should().Be(0);

        // Leaf C/L3
        rows[5].IsFolder.Should().BeFalse();
        rows[5].FilteredIndex.Should().Be(2);
        rows[5].Depth.Should().Be(1);

        // Root leaf L4
        rows[6].IsFolder.Should().BeFalse();
        rows[6].FilteredIndex.Should().Be(3);
        rows[6].Depth.Should().Be(0);
    }

    [Fact]
    public void FlattenVisualRows_CollapsedFolder_HidesDescendants()
    {
        // Build: A → {A/L1, A/B → {A/B/L2}}, C → {C/L3}
        var items = new List<(int, string?, string)>
        {
            (0, "A",     "L1"),
            (1, "A/B",   "L2"),
            (2, "C",     "L3"),
        };

        var root = PickerTreeBuilder.Build(items);

        // A is NOT expanded — its children (L1, sub-folder B, and B's leaf L2) should be hidden.
        // C is expanded so its children show.
        var expanded = new HashSet<string> { "C" };

        var rows = FlattenVisualRows(root, expanded, 0);

        // Expected: A (folder), C (folder), C/L3 (leaf). No A's children.
        rows.Should().HaveCount(3);
        rows[0].IsFolder.Should().BeTrue();
        rows[0].FolderPath.Should().Be("A");
        rows[1].IsFolder.Should().BeTrue();
        rows[1].FolderPath.Should().Be("C");
        rows[2].IsFolder.Should().BeFalse();
        rows[2].FilteredIndex.Should().Be(2); // C/L3
    }

    [Fact]
    public void FlattenVisualRows_DeeplyNested_CollapseRespectsHierarchy()
    {
        // A/B/C with leaf L1, plus A/L2
        var items = new List<(int, string?, string)>
        {
            (0, "A/B/C", "L1"),
            (1, "A",     "L2"),
        };

        var root = PickerTreeBuilder.Build(items);

        // All expanded: A → {L2, B → {C → {L1}}}
        {
            var expanded = new HashSet<string> { "A", "A/B", "A/B/C" };
            var rows = FlattenVisualRows(root, expanded, 0);
            rows.Should().HaveCount(5); // A(f), A/B(f), A/B/C(f), A/B/C/L1, A/L2

            rows[0].FolderPath.Should().Be("A");
            rows[0].Depth.Should().Be(0);
            rows[1].FolderPath.Should().Be("A/B");
            rows[1].Depth.Should().Be(1);
            rows[2].FolderPath.Should().Be("A/B/C");
            rows[2].Depth.Should().Be(2);
            rows[3].FilteredIndex.Should().Be(0); // A/B/C/L1
            rows[3].Depth.Should().Be(3);
            rows[4].FilteredIndex.Should().Be(1); // A/L2
            rows[4].Depth.Should().Be(1);
        }

        // A expanded, A/B NOT expanded: hides C and L1, but L2 and A/B still visible.
        {
            var expanded = new HashSet<string> { "A" };
            var rows = FlattenVisualRows(root, expanded, 0);
            rows.Should().HaveCount(3); // A(f), A/B(f), A/L2 — no C or L1
            rows[0].FolderPath.Should().Be("A");
            rows[1].FolderPath.Should().Be("A/B");
            rows[2].FilteredIndex.Should().Be(1); // L2
        }

        // Nothing expanded: only A shows.
        {
            var expanded = new HashSet<string>();
            var rows = FlattenVisualRows(root, expanded, 0);
            rows.Should().HaveCount(1); // just A(f)
            rows[0].FolderPath.Should().Be("A");
        }
    }
}
