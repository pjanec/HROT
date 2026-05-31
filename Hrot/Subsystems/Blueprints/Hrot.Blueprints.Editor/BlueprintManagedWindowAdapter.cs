using Fdp.Presentation.WindowManager;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// Bridges <see cref="IBlueprintEditorWindow"/> to the engine <see cref="ManagedWindow"/> contract.
/// The blueprint window instance is created lazily on first render so that DI-heavy windows are
/// only constructed when the window is actually opened.
/// </summary>
internal sealed class BlueprintManagedWindowAdapter : ManagedWindow
{
    private readonly Func<IBlueprintEditorWindow> _factory;
    private IBlueprintEditorWindow? _instance;

    internal BlueprintManagedWindowAdapter(string name, Func<IBlueprintEditorWindow> factory)
        : base(id: name, title: name, owningPerspective: "Blueprints", scope: WindowScope.PerspectiveBound)
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
