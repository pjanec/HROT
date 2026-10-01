using System;
using StructEdit.Core;
using StructEdit.Core.Plugins;
using StructEdit.Reflection;

namespace Hrot.Editor.AiShared.Inspector.ActionBinding;

/// <summary>
/// ⭐ <c>CE-417</c> slice 4b — makes a <see cref="BehaviorActionBindingFacet"/> field ONE StructEdit leaf, so
/// <see cref="ActionBindingDrawer"/> draws the whole binding. ⛔ Without it the struct is a container, and StructEdit draws
/// a container read-only (<c>ComponentEditDrawer.DrawContainerNode</c>). Precedent: <c>PredicateValueFieldEditor</c>.
/// </summary>
public sealed class BehaviorActionBindingFieldEditor : ICustomFieldEditor
{
    public Type TargetType => typeof(BehaviorActionBindingFacet);

    public EditNode? CreateNode(EditNodeId id, string name, string jsonPath, IValueBinding binding, EditNodeMetadata metadata)
        => new(id, name, jsonPath, EditNodeKind.Custom, typeof(BehaviorActionBindingFacet), binding, null, metadata);
}

/// <summary>
/// ⭐⭐ <c>CE-417</c> slice 4b — THE edit service the BTree/HSM facets are drawn with, on BOTH hosts.
/// ⛔ Each host built it inline (<c>new ComponentEditServiceBuilder().Build()</c>, once in <c>EditorSubsystem</c>, once in
/// <c>CgfSubsystem</c>); a field editor registered on one only would have left the other drawing every binding read-only.
/// </summary>
public static class AiFacetEditService
{
    /// <summary>A builder with every AI-facet field editor registered — for a host that adds its own registrations.</summary>
    public static ComponentEditServiceBuilder CreateBuilder()
        => new ComponentEditServiceBuilder()
               .RegisterFieldEditor<BehaviorActionBindingFacet>(new BehaviorActionBindingFieldEditor());

    /// <summary>The facet edit service.</summary>
    public static IComponentEditService Build() => CreateBuilder().Build();
}
