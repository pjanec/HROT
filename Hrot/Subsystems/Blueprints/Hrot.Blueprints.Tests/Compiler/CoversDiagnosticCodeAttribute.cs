namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Marks a test method as providing positive coverage for a specific diagnostic code.
/// Applied to tests that deliberately trigger a diagnostic (the "fires when expected" case).
/// Used by <see cref="TestDiagnosticInventory"/> to build the covered-codes set for
/// <c>V_AllValidatorsCoverageTests</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CoversDiagnosticCodeAttribute : Attribute
{
    public string Code { get; }
    public CoversDiagnosticCodeAttribute(string code) => Code = code;
}
