using System.Runtime.InteropServices;

namespace Fdp.Kernel
{
    public enum LifecycleEventType : byte
    {
        Created = 0,   // Freshly allocated (TKB or Empty)
        Destroyed = 1, // About to be deallocated
        Restored = 2,   // Loaded from snapshot/save (Single entity)
        BatchRestored = 3 // Bulk restore event (optional optimization)
    }

    [StructLayout(LayoutKind.Sequential)]
    [EventId(1001)] // System Reserved ID range
    public struct EntityLifecycleEvent
    {
        public Entity Entity;
        public LifecycleEventType Type;
        public int TemplateId; // 0 if none
        public int Generation; // Useful for logging/debugging
    }
}
