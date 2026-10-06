using System;

namespace Hrot.Editor.AiShared.References;

/// <summary>
/// ⭐⭐ One blackboard variable, as a referenceable sub-element — <b>for every AI host</b>.
///
/// <para>⭐ <b>Key format is a CONTRACT, not a detail:</b> <c>{assetId:D}::{variableName}</c> (Guid with hyphens,
/// double-colon separator). A contributor that ENUMERATES variables and one that enumerates REFERENCES to them
/// must spell the key identically or <c>RefactorService</c> silently finds nothing to rewrite — a rename then
/// reports success and dangles the binding.</para>
///
/// <para>⛔⛔ <b>That is why this class is HERE and not beside a host's contributor</b> (<c>HSM-017</c>,
/// <c>2026-10-06</c>). It began as an <c>internal</c> class inside <c>Hrot.BTree.Editor</c>; when HSM needed the
/// same element the only alternatives were a second copy of the key format — the drift this comment describes —
/// or an <c>Hrot.Hsm.Editor</c> → <c>Hrot.BTree.Editor</c> reference, which the editor assemblies deliberately do
/// not have. ⭐ One statement of the format, in the assembly both hosts already depend on.</para>
/// </summary>
public sealed class BlackboardVariableSubElement : IAssetSubElement
{
    public string         Key           { get; }
    public SubElementKind Kind          => SubElementKind.BlackboardVariable;
    public string         DisplayName   { get; }
    public Guid?          SourceAssetId { get; }

    public BlackboardVariableSubElement(Guid assetId, string variableName)
    {
        SourceAssetId = assetId;
        DisplayName   = variableName;
        Key           = KeyFor(assetId, variableName);
    }

    /// <summary>⭐ The ONE spelling of the key. Reference-producing contributors call this rather than
    /// re-interpolating the format.</summary>
    public static string KeyFor(Guid assetId, string variableName) => $"{assetId:D}::{variableName}";
}
