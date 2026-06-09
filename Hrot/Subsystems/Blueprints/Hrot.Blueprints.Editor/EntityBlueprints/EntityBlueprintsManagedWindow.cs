using System;
using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Editor.EntityBlueprints;

/// <summary>
/// <see cref="ManagedWindow"/> wrapper that lazily creates an <see cref="EntityBlueprintsPanel"/>
/// on first render. The optional <paramref name="entityResolver"/> is called each frame before
/// refreshing reality so the panel tracks the editor's entity selection.
/// </summary>
public sealed class EntityBlueprintsManagedWindow : ManagedWindow
{
    private readonly Func<EntityBlueprintsPanel> _factory;
    private EntityBlueprintsPanel? _instance;

    public EntityBlueprintsManagedWindow(
        Func<EntityBlueprintsPanel> factory)
        : base(id: "Entity Blueprints", title: "Entity Blueprints",
            owningPerspective: "Blueprint", scope: WindowScope.PerspectiveBound)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    protected override void DrawClientArea()
    {
        _instance ??= _factory();
        Title = _instance.Title;
        _instance.DrawUI();
    }
}
