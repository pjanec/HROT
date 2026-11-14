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
        // ⭐ Every DEFINED layer — not 0xFFFFFFFF: an IDL enumerator must fit a signed 32-bit value, and Cyclone DDS 11's idlc rejects
        //   @value(4294967295) (CycloneDDS.NET 0.4 fails the build). Only compared by identity (FakeNavmeshProvider), never masked.
        All      = Infantry | Vehicle | Naval | Air,
    }
}
