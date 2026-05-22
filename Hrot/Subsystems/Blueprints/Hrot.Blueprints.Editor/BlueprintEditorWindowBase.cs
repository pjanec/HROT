namespace Hrot.Blueprints.Editor;

public abstract class BlueprintEditorWindowBase : IBlueprintEditorWindow
{
    public abstract string Title { get; }
    public bool IsVisible { get; set; }
    public void ToggleVisible() => IsVisible = !IsVisible;
    public abstract void DrawUI();
    public virtual void OnActivated()   { }
    public virtual void OnDeactivated() { }
}
