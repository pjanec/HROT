using System;
using System.Linq;
using Hrot.AiEditor.Persistence;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// ⭐⭐⭐ <b>THE ONE implementation of the editor-owned blackboard variable lifecycle — create, name,
/// compose for a picked blueprint, drop on unpick.</b> Shared by BOTH AI hosts.
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.6c · <c>DESIGN_Parameter_Model.md</c> §4.
///
/// <para>🔒 <b>User, <c>2026-09-28</c>:</b> <i>"If btree does something right, hsm should reuse it by
/// sharing wherever possible, not by duplication."</i> ⭐ This type is that reuse. Before it the same
/// four operations existed in <b>five</b> places — <c>BTreeCommandSink.ComposeAiPrimitiveAction</c> and
/// <c>…Condition</c> (near-identical to each other), <c>BTreeCommandSink.GenerateUniqueVariableName</c>,
/// <c>HsmFacetDispatcher.ComposeBlueprintParams</c>+<c>UniqueVariableName</c>, and the two picker
/// drawers' <c>Promote</c> (character-for-character the same body).</para>
///
/// <para>⭐⭐ <b>The seam that made sharing possible already existed:</b>
/// <see cref="IBlackboardManagedAsset"/>. Both <c>BehaviorTreeAsset</c> and <c>HsmAsset</c> implement it,
/// and every one of those five sites was already going through it — they just each re-spelled the rules
/// on top. ⇒ this is the prior-art case this repository keeps meeting: <i>"we need a shared X"</i> almost
/// always means <b>X exists and is under-adopted</b>.</para>
///
/// <para>⛔⛔ <b>What is deliberately NOT shared, so nobody "finishes" the unification wrongly:</b> the
/// BTree host binds a SECOND variable for a composed node's WORKING STATE
/// (<c>WorkingStateTargetField</c>, <c>Role=State</c>) so the designer can widen its <c>Scope</c> to
/// <c>Behavior</c> and make two nodes share a partition slot. ⚠ The HSM host must NOT: an HSM-hosted
/// occurrence's working state lives in the occurrence slot keyed
/// <c>(machine, region, state, childAsset)</c> — <c>HsmOccurrence.ResolveOrAttach&lt;Params,
/// WorkingState&gt;</c> — so there is no variable to bind and inventing one would be a second storage
/// story. ⭐ That is why <see cref="ComposeForAiPrimitive"/> takes the working-state base name as an
/// OPTION rather than always creating one.</para>
/// </summary>
public static class AutoManagedVariables
{
    /// <summary>
    /// ⭐ <paramref name="baseName"/> if free, else <c>baseName_2</c>, <c>baseName_3</c>, … — first
    /// unused wins.
    /// </summary>
    public static string UniqueName(IBlackboardManagedAsset asset, string baseName)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));

        if (asset.BlackboardVariables.All(v => v.Name != baseName)) return baseName;

        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{baseName}_{suffix}";
            if (asset.BlackboardVariables.All(v => v.Name != candidate)) return candidate;
        }
    }

    /// <summary>
    /// ⭐⭐ Adds an editor-owned variable under a unique name and returns that name.
    ///
    /// <para>⚠ <c>IsAutoManaged</c> is the whole contract: it is what marks the row un-renamable and
    /// un-deletable in the variables panel, and what lets <see cref="RemoveIfAutoManaged"/> tell a
    /// variable the EDITOR created from one the AUTHOR created.</para>
    /// </summary>
    public static string Create(
        IBlackboardManagedAsset asset,
        string baseName,
        Type fieldType,
        BlackboardVariableRole role = BlackboardVariableRole.Input,
        WorkingStateScope scope = WorkingStateScope.Node,
        string? comment = null)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));
        if (fieldType is null) throw new ArgumentNullException(nameof(fieldType));

        string name = UniqueName(asset, baseName);
        asset.AddVariable(new BlackboardVariableEntry(
            name, fieldType, comment, IsAutoManaged: true, DefaultValueJson: null,
            Role: role, Scope: scope));
        return name;
    }

    /// <summary>
    /// ⭐⭐ Drops the variable named by a hosting site — <b>and only if the editor owns it.</b>
    /// Returns whether anything was removed.
    ///
    /// <para>⛔ A variable the author declared and pointed a site at is never removed by a node
    /// operation. ⚠ Deleting authored data as a side effect of deleting a node is the failure this
    /// guard exists for, and it is why the test is <c>IsAutoManaged</c> rather than <i>"is this name
    /// still referenced"</i>.</para>
    /// </summary>
    public static bool RemoveIfAutoManaged(IBlackboardManagedAsset asset, string? name)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));
        if (string.IsNullOrEmpty(name)) return false;

        var entry = asset.BlackboardVariables.FirstOrDefault(v => v.Name == name);
        if (entry is not { IsAutoManaged: true }) return false;

        asset.RemoveVariable(name!);
        return true;
    }

    /// <summary>
    /// ⭐⭐ <b>"Promote to new variable"</b> — the per-SITE variable a picker offers when no compatible
    /// one exists. Named <c>_auto_{visualId:N}</c> so it is stable for that site across sessions.
    ///
    /// <para>⛔ Returns <see langword="null"/> when <paramref name="visualId"/> is not a GUID — the
    /// caller then offers nothing rather than creating a variable under a name that cannot be matched
    /// back to its site. ⭐ An existing variable of that name is returned as-is, never duplicated.</para>
    /// </summary>
    public static string? PromoteForSite(IBlackboardManagedAsset asset, string? visualId, Type fieldType)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));
        if (fieldType is null) throw new ArgumentNullException(nameof(fieldType));
        if (!Guid.TryParse(visualId, out var guid)) return null;

        string name = $"_auto_{guid:N}";
        if (asset.BlackboardVariables.Any(v => v.Name == name)) return name;

        asset.AddVariable(new BlackboardVariableEntry(
            Name: name, FieldType: fieldType, Comment: null, IsAutoManaged: true));
        return name;
    }

    /// <summary>The generated sibling <c>WorkingState</c> of an AiPrimitive's <c>Params</c>, or null.</summary>
    /// <remarks>
    /// The compiler emits <c>Params</c> and <c>WorkingState</c> as sibling nested types of one generated
    /// class, so the second is reached from the first's declaring type. ⛔ Null — never a throw — when the
    /// generated shape does not match: the caller still places the node, it just binds no working state.
    /// </remarks>
    public static Type? WorkingStateTypeOf(ActionSchemaEntry entry)
        => entry?.DtoType?.DeclaringType?.GetNestedType("WorkingState");

    /// <summary>
    /// ⭐⭐⭐ <b>THE COMPOSE: a picked AiPrimitive brings its own variables.</b>
    ///
    /// <para>⭐⭐ <b>The property that matters is the TYPE.</b> The params variable IS the blueprint's
    /// generated <c>Params</c> struct, so a hosting site's seed offset is that ONE variable's offset and
    /// every field offset inside it comes from the DTO. ⇒ there is no byte WINDOW spanning several
    /// variables, and therefore no declaration ORDER to get wrong — which is the whole of
    /// <c>CE-414</c>.</para>
    ///
    /// <para>⚠ <paramref name="workingStateBaseName"/> is <see langword="null"/> for the HSM host — see
    /// this type's own remarks for why that asymmetry is correct rather than unfinished.</para>
    /// </summary>
    public static ComposedBlueprintVariables ComposeForAiPrimitive(
        IBlackboardManagedAsset asset,
        ActionSchemaEntry entry,
        string paramsBaseName = "bpParams",
        string? workingStateBaseName = "bpWorkingState")
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));
        if (entry is null) throw new ArgumentNullException(nameof(entry));

        // ⭐⭐⭐ SHARED, and it was wrongly called BTree-specific in the first cut of this type.
        //   📐 Measured: BOTH emitters gate their whole params path on the managed flag —
        //   BTreeJsonGenerator raises BTREE0002 and skips the asset, and
        //   HsmBridgeEmitCore.PackParams:464 returns an EMPTY field list. ⛔ The HSM one is SILENT:
        //   no ParseParams, no binding table, and a hosted blueprint whose params are always zero,
        //   with no diagnostic. ⇒ composing on an unmanaged asset is never valid on either host, so
        //   the flip belongs here rather than in one caller.
        asset.SetBlackboardEditorManaged(true);

        string paramsVar = Create(asset, paramsBaseName, entry.DtoType);

        Type? wsType = WorkingStateTypeOf(entry);
        string? wsVar = wsType != null && workingStateBaseName != null
            // ⭐ Role=State, Node scope by default: a SEPARATE variable from the params one, so its Scope
            //   is independently authorable and two composed nodes can be pointed at one Behavior-scoped
            //   slot.
            ? Create(asset, workingStateBaseName, wsType,
                     BlackboardVariableRole.State, WorkingStateScope.Node)
            : null;

        return new ComposedBlueprintVariables(paramsVar, wsVar, wsType);
    }
}

/// <summary>What <see cref="AutoManagedVariables.ComposeForAiPrimitive"/> created.</summary>
/// <param name="ParamsVariable">The variable holding the blueprint's generated <c>Params</c>.</param>
/// <param name="WorkingStateVariable">The <c>Role=State</c> variable, or null when none was asked for.</param>
/// <param name="WorkingStateType">
/// The generated <c>WorkingState</c> type, or null when the generated shape has none. ⚠ Non-null even
/// when <see cref="WorkingStateVariable"/> is null, because the BTree payload records the TYPE NAME
/// separately from the variable binding.
/// </param>
public readonly record struct ComposedBlueprintVariables(
    string ParamsVariable,
    string? WorkingStateVariable,
    Type? WorkingStateType);
