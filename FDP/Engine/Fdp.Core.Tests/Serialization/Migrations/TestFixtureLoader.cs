namespace Fdp.Core.Tests.Serialization.Migrations;

/// <summary>
/// Loads JSON fixture files from the <c>TestFixtures/</c> directory that is
/// copied alongside the test assembly. Paths are relative to the
/// <c>Serialization/Migrations/TestFixtures</c> output directory.
/// </summary>
internal static class TestFixtureLoader
{
    private static readonly string s_basePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Serialization",
        "Migrations",
        "TestFixtures");

    /// <summary>
    /// Returns the absolute path to a fixture file.
    /// </summary>
    /// <param name="relativePath">
    /// Path relative to <c>Serialization/Migrations/TestFixtures/</c>,
    /// e.g. <c>Envelopes/valid_basic.json</c>.
    /// </param>
    public static string GetPath(string relativePath)
        => Path.Combine(s_basePath, relativePath);

    /// <summary>
    /// Reads a fixture file as a UTF-8 string.
    /// </summary>
    public static string Load(string relativePath)
        => File.ReadAllText(GetPath(relativePath));

    /// <summary>
    /// Reads a fixture file as raw UTF-8 bytes.
    /// </summary>
    public static byte[] LoadBytes(string relativePath)
        => File.ReadAllBytes(GetPath(relativePath));

    /// <summary>
    /// Opens a fixture file as a readable <see cref="Stream"/>.
    /// Caller is responsible for disposal.
    /// </summary>
    public static Stream OpenStream(string relativePath)
        => File.OpenRead(GetPath(relativePath));
}
