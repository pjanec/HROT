namespace Hrot.Blueprints.Editor;

public interface IBlueprintEditorWindow
{
    string Title { get; }
    bool IsVisible { get; set; }
    void ToggleVisible();
    void DrawUI();
    void OnActivated();
    void OnDeactivated();
}
