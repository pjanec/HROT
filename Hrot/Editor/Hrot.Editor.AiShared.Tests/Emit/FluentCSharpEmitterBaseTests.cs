using Hrot.Editor.AiShared.Emit;

namespace Hrot.Editor.AiShared.Tests.Emit;

public sealed class FluentCSharpEmitterBaseTests
{
    private static readonly Guid TestAssetId =
        Guid.Parse("f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21");

    [Fact]
    public void BuildHeader_ContainsMarker()
    {
        string header = FluentCSharpEmitterBase.BuildHeader(TestAssetId);
        Assert.Contains(FluentCSharpEmitterBase.EditorGeneratedMarker, header);
    }

    [Fact]
    public void BuildHeader_ContainsAssetId()
    {
        string header = FluentCSharpEmitterBase.BuildHeader(TestAssetId);
        Assert.Contains(TestAssetId.ToString("D"), header);
    }

    [Fact]
    public void BuildHeader_AssetIdFormat_IsD()
    {
        // "D" format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx (no braces)
        string header = FluentCSharpEmitterBase.BuildHeader(TestAssetId);
        Assert.Contains("f7c0a1b2-1188-4c5d-9e3a-7b6c5d4e3f21", header);
        Assert.DoesNotContain("{", header);
    }

    [Fact]
    public void WriteAtomic_WritesFile_WhenNew()
    {
        string path = Path.GetTempFileName();
        File.Delete(path); // start with no file
        try
        {
            bool written = FluentCSharpEmitterBase.WriteAtomic(path, "hello");
            Assert.True(written);
            Assert.Equal("hello", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WriteAtomic_WritesFile_WhenContentDiffers()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "old content");
            bool written = FluentCSharpEmitterBase.WriteAtomic(path, "new content");
            Assert.True(written);
            Assert.Equal("new content", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WriteAtomic_NoOp_WhenContentIdentical()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "same content");
            bool written = FluentCSharpEmitterBase.WriteAtomic(path, "same content");
            Assert.False(written);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void EditorGeneratedMarker_IsAsciiOnly()
    {
        foreach (char c in FluentCSharpEmitterBase.EditorGeneratedMarker)
            Assert.True(c < 128, $"Non-ASCII character found: U+{(int)c:X4}");
    }
}
