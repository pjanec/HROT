using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace FDP.Toolkit.Behavior.Components
{
    public enum MissionTriggerType : byte
    {
        None = 0,
        TimeElapsed = 1,
        AreaEnter = 2,
        HealthBelow = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MissionTrigger
    {
        public MissionTriggerType Type;
        public float Param;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MissionPhase
    {
        public int DoctrineId;
        public MissionTrigger Trigger;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MissionPlanQueue
    {
        public int Count;
        // Storage for 8 phases. 
        // Size logic: sizeof(MissionPhase) * 8. 
        // We reserve 128 bytes to be safe and aligned.
        private fixed byte _buffer[128];
        
        public ref MissionPhase this[int index]
        {
            get
            {
                if ((uint)index >= 8) throw new IndexOutOfRangeException();
                fixed (byte* p = _buffer)
                {
                    return ref ((MissionPhase*)p)[index];
                }
            }
        }
    }
}
