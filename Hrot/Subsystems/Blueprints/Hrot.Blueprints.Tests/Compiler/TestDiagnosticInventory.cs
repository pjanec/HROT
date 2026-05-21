using System.Reflection;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Scans the test assembly for methods decorated with <see cref="CoversDiagnosticCodeAttribute"/>
/// and returns the set of covered diagnostic codes.
/// </summary>
internal static class TestDiagnosticInventory
{
    public static HashSet<string> GetCoveredCodes()
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        var asm = typeof(TestDiagnosticInventory).Assembly;

        foreach (var type in asm.GetTypes())
        {
            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static))
            {
                foreach (var attr in method.GetCustomAttributes<CoversDiagnosticCodeAttribute>())
                    covered.Add(attr.Code);
            }
        }
        return covered;
    }
}
