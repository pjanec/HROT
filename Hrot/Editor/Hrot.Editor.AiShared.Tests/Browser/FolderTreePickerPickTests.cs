using Hrot.Editor.AiShared.Browser;

namespace Hrot.Editor.AiShared.Tests.Browser;

public sealed class FolderTreePickerPickTests
{
    // ── AddFolder ───────────────────────────────────────────────────────

    [Fact]
    public void AddFolder_CreatesNode_ReturnsRelPath()
    {
        var state = new FolderPickerState(null);

        // Adding "combat" under root returns "combat".
        var relPath = state.AddFolder("", "combat");
        Assert.Equal("combat", relPath);
        Assert.True(state.ContainsFolder("combat"));
        Assert.Equal("combat", state.SelectedRelPath);

        // Adding "patrol" under "combat" returns "combat/patrol".
        var nestedPath = state.AddFolder("combat", "patrol");
        Assert.Equal("combat/patrol", nestedPath);
        Assert.True(state.ContainsFolder("combat/patrol"));
        Assert.Equal("combat/patrol", state.SelectedRelPath);
    }

    [Fact]
    public void AddFolder_UpdatesSelectedRelPath()
    {
        var state = new FolderPickerState(new[] { "existing" });

        state.AddFolder("", "newFolder");
        Assert.Equal("newFolder", state.SelectedRelPath);

        state.AddFolder("existing", "child");
        Assert.Equal("existing/child", state.SelectedRelPath);
    }

    [Fact]
    public void AddFolder_EmptyName_Throws()
    {
        var state = new FolderPickerState(null);

        Assert.Throws<ArgumentException>(() => state.AddFolder("", ""));
        Assert.Throws<ArgumentException>(() => state.AddFolder("", "  "));
    }

    [Fact]
    public void AddFolder_UnknownParent_Throws()
    {
        var state = new FolderPickerState(null);

        var ex = Assert.Throws<ArgumentException>(
            () => state.AddFolder("nonexistent", "child"));
        Assert.Contains("nonexistent", ex.Message);
    }

    // ── Selection ───────────────────────────────────────────────────────

    [Fact]
    public void Selection_ReturnsRelPathRelativeToRoot()
    {
        var state = new FolderPickerState(new[] { "combat", "combat/patrol", "idle" });

        // Root selection.
        state.SelectedRelPath = "";
        Assert.Equal("", state.SelectedRelPath);

        // First-level folder.
        state.SelectedRelPath = "combat";
        Assert.Equal("combat", state.SelectedRelPath);

        // Nested folder.
        state.SelectedRelPath = "combat/patrol";
        Assert.Equal("combat/patrol", state.SelectedRelPath);
    }

    [Fact]
    public void Selection_UnknownFolder_Throws()
    {
        var state = new FolderPickerState(new[] { "combat" });

        Assert.Throws<ArgumentException>(() => state.SelectedRelPath = "nonexistent");
    }

    [Fact]
    public void Selection_NullFolder_Throws()
    {
        var state = new FolderPickerState(null);

        Assert.Throws<ArgumentNullException>(() => state.SelectedRelPath = null!);
    }

    // ── CannotEscapeRoot ────────────────────────────────────────────────

    [Fact]
    public void CannotEscapeRoot_DotDot_InName_Rejected()
    {
        var state = new FolderPickerState(null);

        Assert.Throws<ArgumentException>(() => state.AddFolder("", ".."));
        Assert.Throws<ArgumentException>(() => state.AddFolder("", "../escape"));
        Assert.Throws<ArgumentException>(() => state.AddFolder("", "foo..bar"));
    }

    [Fact]
    public void CannotEscapeRoot_SlashLeading_InName_Rejected()
    {
        var state = new FolderPickerState(null);

        Assert.Throws<ArgumentException>(() => state.AddFolder("", "/etc"));
        Assert.Throws<ArgumentException>(() => state.AddFolder("", "\\windows"));
    }

    [Fact]
    public void CannotEscapeRoot_DriveLetter_InName_Rejected()
    {
        var state = new FolderPickerState(null);

        Assert.Throws<ArgumentException>(() => state.AddFolder("", "C:"));
        Assert.Throws<ArgumentException>(() => state.AddFolder("", "D:stuff"));
    }

    [Fact]
    public void CannotEscapeRoot_PathSeparator_InName_Rejected()
    {
        var state = new FolderPickerState(null);

        Assert.Throws<ArgumentException>(() => state.AddFolder("", "a/b"));
        Assert.Throws<ArgumentException>(() => state.AddFolder("", "a\\b"));
    }

    [Fact]
    public void SanitizeFolderName_ValidNames_Accepted()
    {
        Assert.Equal("combat", FolderPickerState.SanitizeFolderName("combat"));
        Assert.Equal("patrol_zone", FolderPickerState.SanitizeFolderName("patrol_zone"));
        Assert.Equal("my-folder", FolderPickerState.SanitizeFolderName("my-folder"));
        Assert.Equal("Folder 1", FolderPickerState.SanitizeFolderName("Folder 1"));
    }

    [Fact]
    public void SanitizeRelPath_ValidPaths_Accepted()
    {
        Assert.Equal("a", FolderPickerState.SanitizeRelPath("a"));
        Assert.Equal("a/b", FolderPickerState.SanitizeRelPath("a/b"));
        Assert.Equal("a/b/c", FolderPickerState.SanitizeRelPath("a/b/c"));

        // Empty/whitespace maps to "".
        Assert.Equal("", FolderPickerState.SanitizeRelPath(""));
        Assert.Equal("", FolderPickerState.SanitizeRelPath("  "));
    }

    [Fact]
    public void SanitizeRelPath_UnsafePaths_Rejected()
    {
        Assert.Null(FolderPickerState.SanitizeRelPath(".."));
        Assert.Null(FolderPickerState.SanitizeRelPath("a/../b"));
        Assert.Null(FolderPickerState.SanitizeRelPath("C:\\Windows"));
        Assert.Null(FolderPickerState.SanitizeRelPath("/etc/passwd"));
        Assert.Null(FolderPickerState.SanitizeRelPath("/a/b"));
        Assert.Null(FolderPickerState.SanitizeRelPath("a\\b"));
    }

    // ── Construction ────────────────────────────────────────────────────

    [Fact]
    public void Constructor_KnownFolders_AreImported()
    {
        var state = new FolderPickerState(new[] { "combat", "combat/patrol", "idle" });

        Assert.True(state.ContainsFolder("combat"));
        Assert.True(state.ContainsFolder("combat/patrol"));
        Assert.True(state.ContainsFolder("idle"));
        Assert.True(state.ContainsFolder("")); // Root always present.

        Assert.False(state.ContainsFolder("nonexistent"));
    }

    [Fact]
    public void Constructor_UnsafeInputPaths_AreFiltered()
    {
        var state = new FolderPickerState(new[] { "combat", "../escape", "C:\\abs" });

        Assert.True(state.ContainsFolder("combat"));
        Assert.False(state.ContainsFolder("../escape"));
        Assert.False(state.ContainsFolder("C:\\abs"));
    }

    [Fact]
    public void FolderPaths_ReturnsAllFoldersIncludingRoot()
    {
        var state = new FolderPickerState(new[] { "b", "a" });

        // root "" comes first, then "a", then "b" (sorted).
        var paths = state.FolderPaths;
        Assert.Equal(3, paths.Count);
        Assert.Equal("", paths.First());
        Assert.Contains("a", paths);
        Assert.Contains("b", paths);
    }
}
