namespace Hrot.Blueprints.Tests;

public sealed class BlueprintTestFixtureOptions
{
    public static BlueprintTestFixtureOptions Default { get; } = new();
    public bool VerifyAlcUnloadOnDispose { get; init; } = true;
    public int GcReclaimRetries { get; init; } = 50;  // was 20->30->50; each batch of new tests adds heap pressure
    public int GcReclaimDelayMs { get; init; } = 50;
    public bool VerboseLeakDiagnostics { get; init; } = false;
}
