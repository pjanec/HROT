using Fdp.Core;

namespace Fdp.Examples.NetworkDemo.Events
{
    /// <summary>
    /// Internal ECS event for combat interactions.
    /// Changed to Struct for Zero-Alloc performance.
    /// </summary>
    [EventId(11002)]
    public struct FireInteractionEvent
    {
        public Entity AttackerRoot;    // The shooting tank
        public Entity TargetRoot;      // The target tank
        public int WeaponInstanceId;   // Which weapon (1 = Main Gun, 2 = MG)
        public float Damage;
        public bool IsRemote;          // Loopback prevention
    }
}
