using System.Collections.Generic;

namespace Hrot.Blueprints.Core.Compiler.Catalogs;

/// <summary>
/// Resolves the signature of a <c>FunctionCall</c> target method (a curated C# helper) so Stage0 can
/// rehydrate its data pins WITHOUT relying on runtime CLR reflection.
/// <para>
/// The default in-process path (compiler unit tests, the editor host) uses reflection over loaded
/// assemblies — see <c>Stage0_Rehydrate.EnrichClrFunctionCallPins</c>. But the Roslyn incremental
/// generator runs inside a netstandard2.0 analyzer host that can never load the game assemblies it is
/// compiling (<c>Hrot.AI.Behaviors.dll</c> et al. do not exist yet), so reflection returns null and
/// Stage0 has to fall back to typeless placeholder pins — which forces every blueprint that calls a
/// same-assembly curated helper to persist EXPLICIT pins. The editor strips pins on save (assets are
/// stored projection-only, <c>"Pins": []</c>), so that round-trip is unsafe (BP1602 unknown pin id).
/// </para>
/// <para>
/// A generator-supplied implementation backed by the Roslyn <c>Compilation</c>'s semantic model
/// (<c>GetTypeByMetadataName</c> → <c>IMethodSymbol</c>) resolves same-assembly signatures at generate
/// time, so the blueprints no longer need explicit pins and the editor save round-trip becomes safe.
/// The abstraction lives here (pure, no Roslyn dependency) so the netstandard2.0 compiler core stays
/// reflection-/Roslyn-free; the implementation lives in the generator project which already references
/// Microsoft.CodeAnalysis.
/// </para>
/// </summary>
public interface IClrSignatureResolver
{
    /// <summary>
    /// Attempts to resolve <paramref name="targetTypeId"/>.<paramref name="methodName"/> to a
    /// <see cref="ClrMethodSig"/>. Returns <c>false</c> (with <c>sig = null</c>) when the type or a
    /// method of that name cannot be found — the caller then falls back to reflection / placeholder
    /// pins exactly as before.
    /// </summary>
    bool TryResolve(string targetTypeId, string methodName, out ClrMethodSig? sig);
}

/// <summary>Reflection-free description of a resolved method signature (parameters + return type).</summary>
public sealed class ClrMethodSig
{
    public ClrMethodSig(IReadOnlyList<ClrParamInfo> parameters, string? returnTypeFullName)
    {
        Parameters = parameters;
        ReturnTypeFullName = returnTypeFullName;
    }

    /// <summary>Declared parameters in order (trailing engine-context params — <c>Entity self</c> /
    /// <c>ISimulationView</c> — are INCLUDED here; the caller omits them from the pin list per the
    /// node's <c>TrailingContext</c>, mirroring the reflection path).</summary>
    public IReadOnlyList<ClrParamInfo> Parameters { get; }

    /// <summary>Return type FQN in the same convention as <c>System.Type.FullName</c>
    /// (e.g. <c>"System.Int64"</c>, <c>"Hrot.AI.Behaviors.Brains.HillAttackSharedState"</c>), or
    /// <c>null</c> for <c>void</c> (no Return pin emitted).</summary>
    public string? ReturnTypeFullName { get; }
}

/// <summary>One resolved parameter: name + type FQN (by-ref already unwrapped to the element type).</summary>
public sealed class ClrParamInfo
{
    public ClrParamInfo(string name, string typeFullName)
    {
        Name = name;
        TypeFullName = typeFullName;
    }

    public string Name { get; }

    /// <summary>Element-type FQN in <c>System.Type.FullName</c> convention (no <c>global::</c> prefix,
    /// no by-ref <c>&amp;</c>).</summary>
    public string TypeFullName { get; }
}
