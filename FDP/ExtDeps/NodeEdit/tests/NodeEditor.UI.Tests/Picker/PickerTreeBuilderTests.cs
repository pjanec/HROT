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
}
