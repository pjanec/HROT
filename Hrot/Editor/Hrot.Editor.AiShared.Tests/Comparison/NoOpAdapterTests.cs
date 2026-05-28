using Microsoft.Extensions.DependencyInjection;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Di;

namespace Hrot.Editor.AiShared.Tests.Comparison;

public sealed class NoOpAdapterTests
{
    [Fact]
    public void NoOpAdapter_Adapt_ReturnsSameJson_DidMigrateFalse()
    {
        var adapter = new NoOpComparisonMigrationAdapter();
        const string json = "{\"Header\":{\"SchemaVersion\":\"1.0\"}}";

        string result = adapter.Adapt(json, out bool didMigrate);

        Assert.Equal(json, result);
        Assert.False(didMigrate);
    }

    [Fact]
    public void NoOpMetaSanitizer_Sanitize_ReturnsSameEnvelope()
    {
        var sanitizer = new NoOpMetaEnvelopeSanitizer();
        const string envelope = "{\"docType\":\"Blueprint\",\"schemaVersion\":\"1.0\",\"createdBy\":\"editor\"}";

        string result = sanitizer.Sanitize(envelope);

        Assert.Equal(envelope, result);
    }

    [Fact]
    public void DI_DefaultContainer_ResolvesNoOpAdapter()
    {
        var services = new ServiceCollection();
        services.AddSharedAiEditor();
        using var sp = services.BuildServiceProvider();

        var adapter = sp.GetRequiredService<IComparisonMigrationAdapter>();

        Assert.IsType<NoOpComparisonMigrationAdapter>(adapter);
    }
}
