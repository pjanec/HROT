namespace Hrot.Blueprints.Tests;

public sealed class BlueprintTestFixtureOptions
{
    public static BlueprintTestFixtureOptions Default { get; } = new();
    public bool VerifyAlcUnloadOnDispose { get; init; } = true;
    public int GcReclaimRetries { get; init; } = 30;  // was 20; bumped for BATCH-17 GC pressure
    public int GcReclaimDelayMs { get; init; } = 50;
    public bool VerboseLeakDiagnostics { get; init; } = false;
}
