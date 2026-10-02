using Fdp.Core;

namespace Hrot.Common.Events;

/// <summary>
/// Command requesting the map canvas to pan and zoom to the specified entity.
///
/// <para>⭐ <b><c>CE-051</c> (Axis-C E3) — MOVED here from <c>Hrot.Editor.Commands</c></b>, beside
/// <see cref="SelectEntityCommand"/> and <see cref="OpenRenameDialogCommand"/>, which were already
/// shared. 📄 <c>docs/DESIGN_Cgf_Tool_Selection_Camera_Slice.md</c> §2/§3 ①.</para>
/// </summary>
[EventId(8104)]
public struct CenterOnEntityCommand
{
    /// <summary>Network entity ID to centre the view on.</summary>
    public long NetworkId;
}
