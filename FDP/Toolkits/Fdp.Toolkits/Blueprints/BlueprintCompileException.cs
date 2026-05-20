namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// Thrown by BlueprintTestFixture.CompileAndLoad when the compiler emits errors.
/// </summary>
public sealed class BlueprintCompileException : Exception
{
    public string Diagnostics { get; }

    public BlueprintCompileException(string message, string diagnostics)
        : base(message)
    {
        Diagnostics = diagnostics;
    }
}
