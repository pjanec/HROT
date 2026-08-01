using Fdp.Core;
using Hrot.Blueprints.Editor.NodeDrawers;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// CA-04 (Slice W1) — headless tests for <see cref="ReflectionWritableComponentTypeProvider"/>:
/// proves the discovery predicate is <c>[ComponentId]</c> AND
/// <see cref="Fdp.Core.BlueprintWritableAttribute"/> presence -- the Set palette/picker offers ONLY
/// this writable subset, while <see cref="ReflectionComponentTypeProvider"/> (the read side,
/// unchanged by this batch) stays all-components. Mirrors <c>ComponentTypeProviderTests</c>.
/// </summary>
public sealed class WritableComponentTypeProviderTests
{
    [ComponentId(1001)]
    [BlueprintWritable]
    private struct WritableMarkedTestComponent
    {
        public int Value;
    }

    // [ComponentId] but NOT [BlueprintWritable] -- must be EXCLUDED from the writable provider
    // even though it's a perfectly valid (readable) component.
    [ComponentId(1002)]
    private struct ReadOnlyMarkedTestComponent
    {
        public int Value;
    }

    // Neither attribute -- must be excluded from both providers.
    private struct UnmarkedTestComponent
    {
        public int Value;
    }

    [Fact]
    public void GetComponentTypeFqns_FindsWritableAttributeMarkedTestType()
    {
        var provider = new ReflectionWritableComponentTypeProvider();

        var fqns = provider.GetComponentTypeFqns();

        Assert.Contains(typeof(WritableMarkedTestComponent).FullName, fqns);
    }

    [Fact]
    public void GetComponentTypeFqns_ExcludesComponentIdOnlyType_NotBlueprintWritable()
    {
        var provider = new ReflectionWritableComponentTypeProvider();

        var fqns = provider.GetComponentTypeFqns();

        // Readable (has [ComponentId]) but not writable -- must NOT appear in the write picker.
        Assert.DoesNotContain(typeof(ReadOnlyMarkedTestComponent).FullName, fqns);
    }

    [Fact]
    public void GetComponentTypeFqns_ExcludesUnmarkedType()
    {
        var provider = new ReflectionWritableComponentTypeProvider();

        var fqns = provider.GetComponentTypeFqns();

        Assert.DoesNotContain(typeof(UnmarkedTestComponent).FullName, fqns);
    }

    [Fact]
    public void GetComponentTypeFqns_ResultIsSortedAndDistinct()
    {
        var provider = new ReflectionWritableComponentTypeProvider();

        var fqns = provider.GetComponentTypeFqns();

        Assert.Equal(fqns.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal), fqns);
    }

    // ── Cross-check: the READ provider stays all-components (unaffected by this batch) ────────

    [Fact]
    public void ReadProvider_StillIncludesComponentIdOnlyType_NotGatedByBlueprintWritable()
    {
        var readProvider = new ReflectionComponentTypeProvider();

        var fqns = readProvider.GetComponentTypeFqns();

        Assert.Contains(typeof(ReadOnlyMarkedTestComponent).FullName, fqns);
    }

    [Fact]
    public void ReadProvider_AlsoIncludesWritableType()
    {
        var readProvider = new ReflectionComponentTypeProvider();

        var fqns = readProvider.GetComponentTypeFqns();

        Assert.Contains(typeof(WritableMarkedTestComponent).FullName, fqns);
    }
}
