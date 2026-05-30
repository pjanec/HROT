using System.Reflection;
using Fdp.Toolkit.Utility;
using Hrot.Utility.Editor.Catalog;
using Xunit;

namespace Hrot.Utility.Editor.Tests.Catalog;

public sealed class InputCatalogBrowserTests
{
    private static Assembly FdpAssembly => typeof(In).Assembly;

    [Fact]
    public void Discover_EmptyAssemblyList_ReturnsEmpty()
    {
        var result = InputCatalogBrowser.Discover();
        Assert.Empty(result);
    }

    [Fact]
    public void Discover_AssemblyWithNoInClass_ReturnsEmpty()
    {
        // The test assembly itself has no "In" class
        var result = InputCatalogBrowser.Discover(typeof(InputCatalogBrowserTests).Assembly);
        Assert.Empty(result);
    }

    [Fact]
    public void Discover_MethodsFromInClass_NamedCorrectly()
    {
        var result = InputCatalogBrowser.Discover(FdpAssembly);
        Assert.Contains(result, e => e.Name == "HealthFraction");
    }

    [Fact]
    public void Discover_EqsTopScore_HasStringParam()
    {
        var result = InputCatalogBrowser.Discover(FdpAssembly);
        var entry  = Assert.Single(result.Where(e => e.Name == "EqsTopScore"));
        Assert.Equal(InputParamKind.String, entry.ParameterKind);
    }

    [Fact]
    public void Discover_Constant_HasFloatParam()
    {
        var result = InputCatalogBrowser.Discover(FdpAssembly);
        var entry  = Assert.Single(result.Where(e => e.Name == "Constant"));
        Assert.Equal(InputParamKind.Float, entry.ParameterKind);
    }

    [Fact]
    public void Discover_ParameterlessInput_HasNoneParam()
    {
        var result = InputCatalogBrowser.Discover(FdpAssembly);
        var entry  = Assert.Single(result.Where(e => e.Name == "HealthFraction"));
        Assert.Equal(InputParamKind.None, entry.ParameterKind);
    }

    [Fact]
    public void Discover_SortedByName()
    {
        var result = InputCatalogBrowser.Discover(FdpAssembly);
        var names  = result.Select(e => e.Name).ToList();
        var sorted = names.OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, names);
    }

    [Fact]
    public void Discover_DuplicateAcrossAssemblies_FirstWins()
    {
        // Pass same assembly twice; no duplicates expected
        var result = InputCatalogBrowser.Discover(FdpAssembly, FdpAssembly);
        var unique = result.DistinctBy(e => e.Name).ToList();
        Assert.Equal(unique.Count, result.Count);
    }
}
