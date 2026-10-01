using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiShared.Inspector.ActionBinding;

/// <summary>
/// ⭐⭐ <b><c>CE-417</c> slice 4b — what the ONE binding drawer lists, for either host.</b>
/// 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c> §5.4.
///
/// <para>The hosts differ in ONE place only — which methods a slot offers — so that is the one thing a host supplies
/// (<c>methods</c>): BTree offers its behavior registry, HSM offers the exporter's activities/guards unioned with the
/// names the asset already binds (<c>CE-386</c>). Everything else — the blueprint list, which variables fit a binding,
/// promote — is the same rule on both hosts and lives here once. ⛔ It replaces <c>BehaviorHashPickerDrawer</c>,
/// <c>HsmActionPickerDrawer</c>, <c>HsmGuardPickerDrawer</c>, <c>BlackboardFieldPickerDrawer</c> and
/// <c>HsmBlackboardFieldPickerDrawer</c>, whose variable rules were two copies of one rule.</para>
///
/// <para>Headless: every list is testable without an ImGui context.</para>
/// </summary>
public sealed class ActionBindingSources
{
    private readonly IBlackboardManagedAsset                       _asset;
    private readonly Func<BindingSlotKind, IEnumerable<string>>    _methods;
    private readonly IActionSchemaExporter?                        _exporter;
    private readonly IAssetCatalog?                                _catalog;

    /// <param name="asset">The open asset whose blackboard the variables come from.</param>
    /// <param name="methods">The host's one difference: the method names a slot of this kind may bind.</param>
    /// <param name="exporter">Resolves a method / blueprint to its parameter type. ⚠ Optional for headless fixtures — without
    /// it every variable is listed and nothing can be promoted. ⛔ A production host HAS one and passes it.</param>
    /// <param name="catalog">Lists the blueprints. ⚠ Optional — without it a blueprint site offers none.</param>
    public ActionBindingSources(
        IBlackboardManagedAsset asset,
        Func<BindingSlotKind, IEnumerable<string>> methods,
        IActionSchemaExporter? exporter = null,
        IAssetCatalog? catalog = null)
    {
        _asset    = asset   ?? throw new ArgumentNullException(nameof(asset));
        _methods  = methods ?? throw new ArgumentNullException(nameof(methods));
        _exporter = exporter;
        _catalog  = catalog;
    }

    /// <summary>The exporter this source resolves through (null in a headless fixture).</summary>
    public IActionSchemaExporter? Exporter => _exporter;

    /// <summary>The catalogue this source lists blueprints from (null in a headless fixture).</summary>
    public IAssetCatalog? Catalog => _catalog;

    /// <summary>The methods a slot of <paramref name="kind"/> offers, distinct and sorted.</summary>
    public IReadOnlyList<string> GetMethods(BindingSlotKind kind)
        => _methods(kind)
               .Where(n => !string.IsNullOrEmpty(n))
               .Distinct(StringComparer.Ordinal)
               .OrderBy(n => n, StringComparer.Ordinal)
               .ToList();

    /// <summary>⭐ <c>CE-462</c> — the SHOWN text of a method (name + technology); the stored value stays the FQN.</summary>
    public string MethodLabel(string fqn) => AiPrimitiveNaming.PickerLabel(fqn, _exporter);

    /// <summary>The catalogue's blueprints, sorted — the same list the <c>[AiAssetPicker(Blueprint)]</c> drawer shows.</summary>
    public IReadOnlyList<string> GetBlueprints()
        => _catalog is null
            ? Array.Empty<string>()
            : new AiAssetPickerDrawer(_catalog, AssetKind.Blueprint).GetItems();

    /// <summary>The variables that fit <paramref name="binding"/> (<see cref="ActionBindingCompatibility"/>).</summary>
    public IReadOnlyList<string> GetVariables(in BehaviorActionBindingFacet binding)
        => ActionBindingCompatibility.CompatibleVariables(
               ActionBindingCompatibility.ParameterType(_exporter, binding.MethodFqn, binding.BlueprintName),
               _asset.BlackboardVariables);

    /// <summary>
    /// True when the binding's parameter type is known and no variable has it — the state in which the drawer offers
    /// "Promote to new variable". ⛔ Never for a whole-blackboard binding: it has no per-binding variable.
    /// </summary>
    public bool HasNoCompatibleVariables(in BehaviorActionBindingFacet binding)
        => !binding.TargetsWholeBlackboard
        && ActionBindingCompatibility.ParameterType(_exporter, binding.MethodFqn, binding.BlueprintName) is not null
        && GetVariables(binding).Count == 0;

    /// <summary>
    /// ⭐ Creates this binding's own variable, typed by its parameter type, and returns its name — or null when the type
    /// is unknown, the binding targets the whole blackboard, or the site has no Guid id.
    /// </summary>
    public string? Promote(in BehaviorActionBindingFacet binding)
    {
        if (binding.TargetsWholeBlackboard) return null;
        var type = ActionBindingCompatibility.ParameterType(_exporter, binding.MethodFqn, binding.BlueprintName);
        return type is null ? null : AutoManagedVariables.PromoteForSite(_asset, binding.SiteId, binding.SiteSlot, type);
    }
}

/// <summary>
/// ⭐⭐ <c>CE-417</c> slice 4b — THE rule for which blackboard variables a binding may target, both hosts.
///
/// <para>A binding's parameter type is its method's <c>DtoType</c>, or — for a blueprint binding — the blueprint's
/// generated <c>Params</c> (the <c>CE-414</c> compose type). A variable fits when its type IS that type. ⚠ When the type
/// is unknown (no method yet, an FQN the exporter does not know, no exporter) every variable is offered: an unknown is
/// not a reason to hide what the author may legitimately pick. ⛔ Was two copies — BTree's
/// <c>BlackboardFieldPickerAttribute.GetCompatibleVariables</c> and HSM's inline drawer rule — which agreed only by care.</para>
/// </summary>
public static class ActionBindingCompatibility
{
    /// <summary>Display string shown when a binding's type is known and no variable has it.</summary>
    public const string NoCompatibleVariablesDisplay = "(no compatible variables)";

    /// <summary>The binding's parameter type, or null when it cannot be resolved.</summary>
    public static Type? ParameterType(IActionSchemaExporter? exporter, string? methodFqn, string? blueprintName)
    {
        if (exporter is null) return null;
        if (!string.IsNullOrEmpty(methodFqn))
            return exporter.Lookup(methodFqn!)?.DtoType;
        return AiPrimitiveNaming.TryFindAiPrimitiveByName(exporter, blueprintName, out var entry) ? entry.DtoType : null;
    }

    /// <summary>The names of the variables of <paramref name="parameterType"/>, sorted; every name when it is null.</summary>
    public static IReadOnlyList<string> CompatibleVariables(
        Type? parameterType, IEnumerable<BlackboardVariableEntry> variables)
        => variables
               .Where(v => parameterType is null || v.FieldType == parameterType)
               .Select(v => v.Name)
               .OrderBy(n => n, StringComparer.Ordinal)
               .ToList();
}
