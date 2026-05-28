namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// No-op implementation of <see cref="IComparisonMigrationAdapter"/> for use until the
/// Migration System lands. Returns the input JSON unchanged and always reports no migration.
/// See design §3.5 step 0 and §8.1.
/// </summary>
public sealed class NoOpComparisonMigrationAdapter : IComparisonMigrationAdapter
{
    /// <inheritdoc/>
    public string Adapt(string rawJson, out bool didMigrate)
    {
        didMigrate = false;
        return rawJson;
    }
}
