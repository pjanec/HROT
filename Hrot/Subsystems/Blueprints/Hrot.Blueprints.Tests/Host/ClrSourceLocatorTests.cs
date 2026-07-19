using Hrot.Blueprints.Editor.Host;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// Editor punch-list #6: <see cref="ClrSourceLocator"/> resolves a reflected method's source
/// file + line from the portable PDB. Exercised end-to-end against this test assembly (built with
/// a portable PDB on disk), which validates the method-token → MethodDebugInformation math.
/// </summary>
public sealed class ClrSourceLocatorTests
{
    // A stable target whose source location the test asserts against. Keep this method here.
    public static int SampleTargetMethod(int a, int b) => a + b;

    [Fact]
    public void Resolve_ReturnsSourceFileAndLine_ForMethodInThisAssembly()
    {
        var method = typeof(ClrSourceLocatorTests).GetMethod(nameof(SampleTargetMethod))!;

        var loc = ClrSourceLocator.Resolve(method);

        Assert.NotNull(loc);
        Assert.EndsWith("ClrSourceLocatorTests.cs", loc!.Value.File.Replace('\\', '/'));
        Assert.True(loc.Value.Line > 0, "expected a positive 1-based source line");
    }
}
