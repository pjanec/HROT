using System.Collections.Generic;

namespace Hrot.Editor.AiShared.Inspector;

/// <summary>
/// Exposes a testable list of available pick items independent of ImGui rendering.
/// Implemented by attribute-specific StructEdit field drawers so their item lists
/// can be tested headlessly.
/// </summary>
public interface IPickerListSource
{
    /// <summary>Returns the current list of candidate strings for picking.</summary>
    IReadOnlyList<string> GetItems();
}
