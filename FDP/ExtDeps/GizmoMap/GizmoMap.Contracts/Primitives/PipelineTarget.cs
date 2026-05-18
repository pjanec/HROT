using System;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    [Flags]
    public enum PipelineTarget : byte
    {
        None       = 0,
        Map2D      = 1,
        Viewport3D = 2,
        NodeGraph  = 4,
        All        = 7
    }
}
