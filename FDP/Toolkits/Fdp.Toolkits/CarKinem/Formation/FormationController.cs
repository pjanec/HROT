using System.Runtime.InteropServices;
using Fdp.Core;

namespace CarKinem.Formation
{
    /// <summary>
    /// Formation controller component (attached to the formation leader entity).
    /// Holds formation type, template, and parameters.
    /// Member tracking has moved to UnitRoster (Hrot.Core layer).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.FormationController)]
    public struct FormationController
    {
        public int TemplateId;            // Index into formation template blob
        public FormationType Type;        // Formation type
        public FormationParams Params;    // Formation parameters
    }
}
