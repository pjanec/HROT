using System.Text;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⭐⭐⭐ <b><c>Q49</c> — THE SUBTREE-SYNC IDENTITY, DERIVED IN EXACTLY ONE PLACE.</b>
/// 📄 <c>docs/blueprints/Architect_Question_49_Subtree_Sync_Identity_Survives_Reload.md</c>, approved by the
/// user <c>2026-08-22</c> as <b>option C + option D</b>.
///
/// <para>⛔⛔ <b>The defect this closes</b> *(<c>BP-342</c> gap ①)*: <c>BehaviorTreeAsset._syncNodeMeta</c>
/// carries the identity Approach-B emission needs, and its <b>only</b> writer was
/// <c>InspectorWindow:194</c> — <b>a UI draw</b>. ⇒ ⚠ after a reload the identity was gone and
/// <c>GetApproachBSyncGroups()</c> skipped every node, so <b>Approach-B emitted nothing until a designer
/// re-opened the panel on each node</b>.</para>
///
/// <para>⭐⭐⭐ <b>The insight the question turns on:</b> <c>SubDtoTypeName</c>/<c>Ns</c> describe the
/// <b>CALLEE</b> — the subtree being called — not the calling node. ⇒ the identity is <b>DERIVED</b>, not
/// authored, and the fix is *"recompute it from the authoritative source"*, ⛔ not *"persist a second
/// copy"*. 📌 That is why the DTO exclusion rail <c>BTreeDtoRuntimeFieldExclusionTests</c> was <b>RIGHT</b>
/// and stays untouched.</para>
///
/// <para>⭐⭐ <b>WHY IT LIVES HERE, measured rather than assumed.</b> 📐 <c>Hrot.AiEditor.Persistence</c> is
/// <c>netstandard2.0</c> and is referenced by <b>all three</b> arms that need this derivation:
/// <list type="bullet">
///   <item><c>Hrot.AiEditor.Generators</c> *(the source generator — option <b>D</b>)*;</item>
///   <item><c>Hrot.Editor.AiShared</c> *(the Inspector's sync panel, which authors the bindings)*;</item>
///   <item><c>Hrot.BTree.Editor</c> *(<c>BehaviorTreeAsset</c> — option <b>C</b>'s recompute)*.</item>
/// </list>
/// ⇒ ⭐ <b>there is no netstandard2.0/net8.0 wall on this path</b>, so 📌 ruling 9's <i>"one
/// implementation"</i> is achievable and the algorithm is <b>not</b> duplicated across the two arms — the
/// hazard <c>BATCH-03-REPORT.md:100</c> filed for that wall does not apply here.</para>
///
/// <para>⚠ <b>Extracted, not rewritten</b> — these three helpers were <c>private static</c> on
/// <c>InspectorWindow</c>; that copy is <b>deleted</b> in the same commit, so the identity a designer
/// authors and the identity a reload recomputes are byte-identical <b>by construction</b> rather than by
/// two functions agreeing.</para>
/// </summary>
public static class SubtreeSyncIdentity
{
    /// <summary>
    /// ⭐⭐⭐ <b>The whole derivation, from the two facts a resolved sub-asset carries.</b>
    /// ⛔ Both inputs are <b>persisted</b> and are plain strings — 📐 <c>BehaviorTreeAssetDto:342</c>
    /// (<c>BlackboardTypeName</c>) and the asset's own name ⇒ ⚠ <b>no type loading and no compilation</b>,
    /// which is what makes this runnable in a source generator as well as in the editor.
    /// </summary>
    /// <param name="subAssetName">The called subtree's asset name.</param>
    /// <param name="subBlackboardTypeName">Its blackboard type's fully-qualified name.</param>
    public static (string SubtreeName, string SubDtoTypeName, string? SubDtoTypeNs) Derive(
        string subAssetName, string subBlackboardTypeName)
        => (SanitizeIdentifier(subAssetName),
            ShortTypeName(subBlackboardTypeName),
            NsOf(subBlackboardTypeName));

    /// <summary>⭐ The type name without its namespace. ⚠ A nested type's <c>+</c> is left alone — the
    /// emit core already normalises that, and doing it twice differently is how the two copies would
    /// drift.</summary>
    public static string ShortTypeName(string fqn)
    {
        int last = fqn.LastIndexOf('.');
        return last >= 0 ? fqn.Substring(last + 1) : fqn;
    }

    /// <summary>⭐ The namespace, or <see langword="null"/> for a global-namespace type.</summary>
    public static string? NsOf(string fqn)
    {
        int last = fqn.LastIndexOf('.');
        return last > 0 ? fqn.Substring(0, last) : null;
    }

    /// <summary>
    /// ⭐ An asset name reduced to a legal C# identifier — it becomes half of the emitted field name
    /// <c>{SubtreeName}_{SubDtoTypeName}</c>.
    /// <para>⚠ <b>The two guards are load-bearing and are kept exactly as the Inspector had them:</b>
    /// an all-punctuation name would otherwise emit an <b>empty</b> identifier, and a leading digit is
    /// not a legal identifier start.</para>
    /// </summary>
    public static string SanitizeIdentifier(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name)
            if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
        if (sb.Length == 0) return "Asset";
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }
}
