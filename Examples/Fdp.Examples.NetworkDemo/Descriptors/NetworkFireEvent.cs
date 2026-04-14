using CycloneDDS.Schema;
using Fdp.Interfaces.Abstractions;

namespace Fdp.Examples.NetworkDemo.Descriptors
{
    [DdsTopic("FDP.Evt_FireInteraction")]
    [FdpDescriptor(300, "FDP.Evt_FireInteraction")]
    public partial struct NetworkFireEvent
    {
        public long AttackerNetId;
        
        public long TargetNetId;
        
        public int WeaponInstanceId;
        
        public float Damage;
    }
}
