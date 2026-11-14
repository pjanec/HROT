using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// ⭐⭐⭐ <b><c>HSM-020</c> — "what output lane does this action write to?", answered from the Roslyn compilation.</b>
///
/// <para>📐 <b>Why it has to be answered HERE.</b> The lane lives on <c>[HsmAction(Lane = …)]</c>, on the method.
/// <c>Fhsm.Compiler</c> takes actions as <b>names</b> and does <b>no reflection at all</b> (it references only
/// <c>Fhsm.Kernel</c>), so it cannot read the attribute — which is exactly why
/// <c>HSM_Editor_NodeEditor_Host_Design.md</c> §10.3 step 5's *"the kernel computes it at compile time"* was false
/// and every machine reached <c>HsmKernelCore.ArbitrateOutputLanes</c> with an all-zero mask. ⭐ The generator CAN
/// see the attribute, because it has the <see cref="Compilation"/>.</para>
///
/// <para>⭐ <b>Derived at build time, never persisted.</b> The alternative — storing the mask in the
/// <c>.hsm.json</c> when the editor infers it — would go stale the moment someone edits a <c>Lane</c> in C# and
/// rebuilds without reopening the asset, and a stale mask is worse than none: it makes the kernel suppress the
/// WRONG region. Reading it from the compilation being built cannot be stale by construction.</para>
///
/// <para>⭐ Shaped like <see cref="SharedAiMethodResolver"/> deliberately — same FQN → symbol walk, same
/// per-compilation cache, because an asset binds one action at many sites.</para>
/// </summary>
internal static class HsmActionLaneResolver
{
    private const string HsmActionAttr = "Fhsm.Kernel.Attributes.HsmActionAttribute";

    /// <summary>The <c>CommandLane</c> value declared on an action FQN, or null when the method is not found,
    /// carries no <c>[HsmAction]</c>, or leaves <c>Lane</c> at its <c>None</c> default.</summary>
    public static Func<string, byte?> Make(Compilation compilation)
    {
        var cache = new Dictionary<string, byte?>(StringComparer.Ordinal);
        return fqn =>
        {
            if (string.IsNullOrEmpty(fqn)) return null;
            if (cache.TryGetValue(fqn, out var hit)) return hit;

            byte? result = null;
            int dot = fqn.LastIndexOf('.');
            if (dot > 0)
            {
                var type = compilation.GetTypeByMetadataName(fqn.Substring(0, dot));
                if (type != null)
                {
                    foreach (var member in type.GetMembers(fqn.Substring(dot + 1)))
                    {
                        if (member is not IMethodSymbol method) continue;
                        foreach (var a in method.GetAttributes())
                        {
                            if (a.AttributeClass?.ToDisplayString() != HsmActionAttr) continue;
                            // Lane is a NAMED property, not a constructor argument.
                            foreach (var named in a.NamedArguments)
                            {
                                if (named.Key != "Lane") continue;
                                if (named.Value.Value is byte b)   result = b;
                                else if (named.Value.Value is int i && i >= 0 && i <= 255) result = (byte)i;
                            }
                        }
                        if (result != null) break;
                    }
                }
            }

            cache[fqn] = result;
            return result;
        };
    }
}
