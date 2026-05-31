using ImGuiNET;
using NodeEditor.Core.Interfaces;
using System.Numerics;

namespace NodeEditor.UI.Panels;

/// <summary>
/// Helpers for registering ImGui drag-drop sources on My Blueprint item rows.
/// Stores the currently dragged item's ID in a thread-static so that drop
/// targets can read it without unsafe code.
/// </summary>
public static class MyBlueprintDragSource
{
    /// <summary>Payload type strings per item kind.</summary>
    public const string Variable        = "MyBP.Variable";
    public const string Function        = "MyBP.Function";
    public const string Macro           = "MyBP.Macro";
    public const string CustomEvent     = "MyBP.CustomEvent";
    public const string EventDispatcher = "MyBP.Dispatcher";
    public const string GraphEntry      = "MyBP.GraphEntry";

    [ThreadStatic]
    private static string? _currentPayloadType;

    [ThreadStatic]
    private static string? _currentItemId;

    [ThreadStatic]
    private static string? _currentDisplayName;

    /// <summary>The payload type of the item currently being dragged, or null.</summary>
    public static string? CurrentPayloadType => _currentPayloadType;

    /// <summary>The item id of the item currently being dragged, or null.</summary>
    public static string? CurrentItemId => _currentItemId;

    /// <summary>The display name of the item currently being dragged, or null.</summary>
    public static string? CurrentDisplayName => _currentDisplayName;

    /// <summary>
    /// Begin a drag-drop source for the given item if the user starts dragging.
    /// Returns true if dragging is active; caller must call EndSource() if so.
    /// </summary>
    public static bool BeginSource(string itemId, string sectionId, string displayName)
    {
        string payloadType = GetPayloadType(sectionId);

        if (!ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
            return false;

        _currentPayloadType = payloadType;
        _currentItemId      = itemId;
        _currentDisplayName = displayName;

        // Pass a zero-length payload; drop targets read CurrentItemId from the static.
        ImGui.SetDragDropPayload(payloadType, IntPtr.Zero, 0);
        ImGui.Text($"\ud83d\udce6 {displayName}");  // preview label

        return true;
    }

    /// <summary>End the drag-drop source (mirrors BeginSource's ImGui.BeginDragDropSource).</summary>
    public static void EndSource()
    {
        ImGui.EndDragDropSource();
    }

    /// <summary>Clear drag state when drag ends (call from drop handler or on cancel).</summary>
    public static void ClearState()
    {
        _currentPayloadType = null;
        _currentItemId      = null;
        _currentDisplayName = null;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string GetPayloadType(string sectionId) => sectionId.ToLowerInvariant() switch
    {
        "functions"         => Function,
        "macros"            => Macro,
        "customevents"      => CustomEvent,
        "eventdispatchers"  => EventDispatcher,
        "graphs"            => GraphEntry,
        _                   => Variable,
    };
}
