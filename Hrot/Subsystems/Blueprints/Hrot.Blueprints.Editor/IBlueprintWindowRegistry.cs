namespace Hrot.Blueprints.Editor;

/// <summary>
/// Abstracts the window registration surface so blueprint windows can be registered
/// without depending on a concrete engine WindowManager type.
/// </summary>
public interface IBlueprintWindowRegistry
{
    void Register(string name, Func<IBlueprintEditorWindow> factory);
}
