using System;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// Bit-flag enum identifying which agent-traversal layers are active.
    /// Used by <see cref="Fake.FakeNavLayer.Layer"/> and navmesh layer-mask queries.
    /// </summary>
    [Flags]
    public enum NavLayerMask : uint
    {
        None     = 0u,
        Infantry = 1u,
        Vehicle  = 2u,
        Naval    = 4u,
        Air      = 8u,
        All      = 0xFFFFFFFFu,
    }
}
