namespace Hrot.Utility.Editor.Model;

// Reference to a test fixture used by the live-preview strip and CI tests.
public sealed class FixtureRef
{
    // Human-readable fixture name (also the CI fixture file stem).
    public string Name        = string.Empty;
    // Path to the fixture file relative to the project root.
    public string FilePath    = string.Empty;
}
