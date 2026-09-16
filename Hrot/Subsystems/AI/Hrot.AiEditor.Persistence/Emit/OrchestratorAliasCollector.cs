using System;
using System.Collections.Generic;
using System.Text;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⭐⭐ <b>Batch 92 (<c>92a</c>) — the Approach-A alias arm, owned ONCE.</b>
///
/// <para>📐 <c>BTreeOrchestratorEmitter</c> and <c>HsmOrchestratorEmitter</c> carried this loop
/// character-for-character *(the dedupe key, the unit-separator, the namespace filter, the
/// sanitiser)*. ⛔ Two copies of one algorithm — 📌 ruling 9. ⭐ Both emit cores now call this.</para>
///
/// <para>⚠ <b>The two hosts differ in exactly two places</b>, and both are parameters here rather than
/// forks: the <b>identifier fallback</b> *(<c>"BTreeAsset"</c> vs <c>"HsmAsset"</c>)* and the set of
/// baseline usings the caller seeds.</para>
/// </summary>
public static class OrchestratorAliasCollector
{
    /// <summary>One emitted orchestrator method's inputs.</summary>
    public sealed class AliasMethod
    {
        public AliasMethod(string varName, string subTreeName, string dtoTypeName, string? dtoTypeNs)
        {
            VarName     = varName;
            SubTreeName = subTreeName;
            DtoTypeName = dtoTypeName;
            DtoTypeNs   = dtoTypeNs;
        }

        /// <summary>The MASTER blackboard field the sub-tree's blackboard is projected onto.</summary>
        public string VarName { get; }
        /// <summary>Identifier-safe sub-asset name; the method-name suffix.</summary>
        public string SubTreeName { get; }
        /// <summary>Short name of the sub-tree's blackboard struct.</summary>
        public string DtoTypeName { get; }
        /// <summary>Its namespace, or null when the persisted id carried none.</summary>
        public string? DtoTypeNs { get; }
    }

    /// <summary>
    /// Collects one method per unique <c>(variable, sub-tree)</c> pair, in blackboard-declaration
    /// order.
    /// </summary>
    /// <param name="aliases">The persisted alias map, keyed by variable name. Null ⇒ no methods.</param>
    /// <param name="variableNames">
    /// ⭐ The blackboard's variables, IN ORDER — ⛔ not the alias dictionary's keys, whose enumeration
    /// order is not a contract.
    /// </param>
    /// <param name="identifierFallback">
    /// Name used when a sub-asset's name sanitises to nothing (see <see cref="SanitizeIdentifier"/>).
    /// </param>
    public static IReadOnlyList<AliasMethod> Collect(
        IReadOnlyDictionary<string, List<BlackboardAliasBindingDto>>? aliases,
        IEnumerable<string> variableNames,
        string identifierFallback = "Asset")
    {
        var methods = new List<AliasMethod>();
        if (aliases is null || aliases.Count == 0) return methods;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var varName in variableNames)
        {
            if (!aliases.TryGetValue(varName, out var bindings) || bindings is null) continue;

            foreach (var binding in bindings)
            {
                if (binding is null) continue;

                string subTreeName = SanitizeIdentifier(binding.RequiringAssetName, identifierFallback);
                // Unit-separator (0x1F) is not a legal C# identifier char so it cannot appear in
                // either part, making this key unambiguous.
                string key = varName + "\x1F" + subTreeName;
                if (!seen.Add(key)) continue;

                SplitTypeId(binding.DtoTypeId, out string dtoTypeName, out string? dtoTypeNs);
                methods.Add(new AliasMethod(varName, subTreeName, dtoTypeName, dtoTypeNs));
            }
        }

        return methods;
    }

    /// <summary>
    /// ⭐⭐⭐ Splits a persisted <c>Type.FullName</c> into short name + namespace — ⛔ <b>never resolves
    /// a <c>System.Type</c></b>: a source generator cannot load the behavior assembly the struct lives
    /// in. 📌 This is the whole reason <c>91b</c> persisted the id as a string.
    /// </summary>
    /// <remarks>
    /// ⚠ A nested type's <c>FullName</c> spells the nesting with <c>'+'</c> (<c>Ns.Outer+Inner</c>),
    /// which is not legal C#. ⭐ It is rewritten to <c>Outer.Inner</c> — ⛔ <b>not</b> reduced to
    /// <c>Inner</c> the way <c>Type.Name</c> would, which is unreferable. ⚠ For every NON-nested type
    /// — which is every case the corpus has — this is character-identical to what the editor emitter
    /// produced from <c>Type.Name</c> / <c>Type.Namespace</c>.
    /// </remarks>
    public static void SplitTypeId(string? typeId, out string typeName, out string? typeNamespace)
    {
        if (string.IsNullOrEmpty(typeId))
        {
            typeName      = "object";
            typeNamespace = null;
            return;
        }

        int last = typeId!.LastIndexOf('.');
        if (last < 0)
        {
            typeName      = typeId.Replace('+', '.');
            typeNamespace = null;
            return;
        }

        typeName      = typeId.Substring(last + 1).Replace('+', '.');
        typeNamespace = typeId.Substring(0, last);
    }

    /// <summary>
    /// Adds each method's DTO namespace to <paramref name="usings"/>, skipping the asset's own
    /// namespace. ⚠ Compared against the asset's RAW <c>TargetNamespace</c> — ⛔ not the defaulted
    /// one — because that is what the editor emitters compared.
    /// </summary>
    public static void AddDtoNamespaces(
        HashSet<string> usings, IReadOnlyList<AliasMethod> methods, string rawTargetNamespace)
    {
        foreach (var m in methods)
        {
            if (!string.IsNullOrEmpty(m.DtoTypeNs)
                && !string.Equals(m.DtoTypeNs, rawTargetNamespace, StringComparison.Ordinal))
            {
                usings.Add(m.DtoTypeNs!);
            }
        }
    }

    /// <summary>Strips every character a C# identifier may not contain; prefixes a leading digit.</summary>
    public static string SanitizeIdentifier(string? name, string fallback)
    {
        var sb = new StringBuilder();
        if (name != null)
            foreach (char c in name)
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
        if (sb.Length == 0) return fallback;
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }

    /// <summary>The segment after the last <c>'.'</c>, or the whole string when there is none.</summary>
    public static string ShortTypeName(string? fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return string.Empty;
        int last = fqn!.LastIndexOf('.');
        return last >= 0 ? fqn.Substring(last + 1) : fqn;
    }
}
