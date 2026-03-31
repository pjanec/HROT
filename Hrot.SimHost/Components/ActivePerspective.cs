using System.Runtime.InteropServices;
using Hrot.Map.Definitions;
using Fdp.Kernel;

namespace Hrot.SimHost.Components
{
    /// <summary>
    /// Designates which high-level presentation perspective is currently active.
    /// </summary>
    public enum PerspectiveType : byte
    {
        /// <summary>The Image Generator (IG) 3-D window is the primary view.</summary>
        IG  = 0,

        /// <summary>The Sim Map 2-D tactical overlay is the primary view.</summary>
        Sim = 1,
    }

    /// <summary>
    /// Singleton ECS component that tracks which presentation perspective is active.
    ///
    /// <para>
    /// Seeded in <c>SimHostApp.OnLoad</c> (or <see cref="Hrot.SimHost.Infrastructure.SimHostInstance"/>)
    /// via <c>world.SetSingletonUnmanaged(new ActivePerspective { Current = PerspectiveType.Sim })</c>.
    /// </para>
    ///
    /// <para>
    /// Read-only for render systems (<see cref="IgMapRenderSystem"/>, <see cref="SimMapRenderSystem"/>)
    /// which gate their <c>Draw</c> calls on <see cref="Current"/>.
    /// Written by <c>PerspectiveCoordinatorSystem</c> upon receiving a
    /// <c>TogglePerspectiveEvent</c>.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(HrotComponentIds.ActivePerspective)]
    public struct ActivePerspective
    {
        /// <summary>The currently active presentation tier.</summary>
        public PerspectiveType Current;
    }
}
