using System;
using System.Collections.Generic;
using StructEdit.Core;

namespace Fdp.Presentation.Editing.Types;

/// <summary>An editor that draws through an <see cref="IImGuiFieldDrawer"/> chosen per node (a field rule whose drawer depends on the attribute instance — <c>[AiAssetPicker(kind)]</c>) — so rails can ask which one.</summary>
public interface IDrawerSource
{
    /// <summary>The drawer that would draw <paramref name="node"/>, or null.</summary>
    IImGuiFieldDrawer? DrawerFor(EditNode node);
}

/// <summary>What a host's catalogue resolves a node to (rails and the forwarding assertions; the table itself calls <see cref="TypeCatalogue.TryGetFor"/>).</summary>
public static class TypeCatalogueExtensions
{
    /// <summary>The <see cref="IImGuiFieldDrawer"/> behind the editor <paramref name="catalogue"/> picks for <paramref name="node"/> (a field rule first, then the node's type), or null.</summary>
    public static IImGuiFieldDrawer? DrawerFor(this TypeCatalogue catalogue, EditNode node)
        => catalogue.TryGetFor(node, out var e) && e is IDrawerSource s ? s.DrawerFor(node) : null;

    /// <summary>The <see cref="IImGuiFieldDrawer"/> registered for <paramref name="type"/> (a TYPE entry, not a field rule), or null.</summary>
    public static IImGuiFieldDrawer? DrawerOf(this TypeCatalogue catalogue, Type type)
        => catalogue.TryGet(type, out var e) && e is FieldTypeEditor f ? f.Drawer : null;

    /// <summary>
    /// ⭐ The forwarding rail's question (CE-3378 §7 step 1, "proves it"): does a host's catalogue draw the types every table must — <c>Quaternion</c> (yaw · pitch · roll), <c>Guid</c> and <c>Type</c> (the filtered combo)?
    /// Returns the name of the first one it cannot draw, or null when all three draw. Asked of the catalogue a CONSTRUCTED <see cref="ComponentEditDrawer"/> reports, never of the registrar's source.
    /// </summary>
    public static string? MissingStandardLeaf(this TypeCatalogue catalogue)
    {
        if (!(catalogue.TryGet(typeof(System.Numerics.Quaternion), out var q) && q.CanDraw)) return nameof(System.Numerics.Quaternion);
        if (!(catalogue.TryGet(typeof(Guid), out var g) && g.CanDraw)) return nameof(Guid);
        if (!(catalogue.TryGet(typeof(Type), out var t) && t.CanDraw)) return nameof(Type);
        return null;
    }
}

/// <summary>
/// A tiny <see cref="IServiceProvider"/> — what a host passes as <c>services:</c> to <see cref="ComponentEditDrawer"/> so a domain type's editor can read its registry / asset through
/// <see cref="TypeDrawContext.Get{T}"/>. Built ONCE by the host (never per frame): <c>new ServiceBag().With(registry)</c>.
/// </summary>
public sealed class ServiceBag : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();

    /// <summary>Adds <paramref name="service"/> under <typeparamref name="T"/> (null is ignored).</summary>
    public ServiceBag With<T>(T? service) where T : class
    {
        if (service != null) _services[typeof(T)] = service;
        return this;
    }

    public object? GetService(Type serviceType) => _services.TryGetValue(serviceType, out var s) ? s : null;
}
