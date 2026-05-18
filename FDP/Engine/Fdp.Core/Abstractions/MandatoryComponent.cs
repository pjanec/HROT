using Fdp.Core;

namespace Fdp.Interfaces
{
    /// <summary>
    /// Defines an ECS-native requirement for a <see cref="TkbTemplate"/>.
    ///
    /// Each entry specifies a component type (by its global type ID) that the ghost entity
    /// must have received before the <c>GhostPromotionSystem</c> can promote it.
    /// This replaces the network-coupled <see cref="MandatoryDescriptor"/> approach,
    /// which was tied to DDS descriptor ordinals.
    ///
    /// <para><b>Hard requirements</b> block promotion until the component is physically
    /// present in the entity's <c>ComponentMask</c>.</para>
    ///
    /// <para><b>Soft requirements</b> also block promotion, but the system gives up waiting
    /// after <see cref="SoftTimeoutFrames"/> frames since the ghost was created, allowing
    /// the entity to proceed without the optional data (e.g. decorative style info that
    /// may have been lost over UDP).</para>
    ///
    /// <para>Note: <c>TkbIdentity</c> is always implicitly a hard requirement regardless of
    /// whether it appears in <see cref="TkbTemplate.MandatoryComponents"/>.</para>
    /// </summary>
    public struct MandatoryComponent
    {
        /// <summary>
        /// The globally unique ECS component type ID.
        /// Obtain via <c>ComponentTypeRegistry.GetId(typeof(T))</c>.
        /// </summary>
        public int ComponentTypeId;

        /// <summary>
        /// When <c>true</c>, promotion is blocked indefinitely until the component arrives.
        /// When <c>false</c>, promotion proceeds after <see cref="SoftTimeoutFrames"/> frames.
        /// </summary>
        public bool IsHard;

        /// <summary>
        /// For soft requirements: number of frames to wait after ghost creation before
        /// giving up and promoting anyway.
        /// Ignored when <see cref="IsHard"/> is <c>true</c>.
        /// </summary>
        public uint SoftTimeoutFrames;
    }
}
