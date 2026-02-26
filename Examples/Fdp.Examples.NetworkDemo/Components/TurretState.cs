using System.ComponentModel.DataAnnotations;
using FDP.Interfaces.Abstractions;
using CycloneDDS.Schema;
using Fdp.Kernel;

namespace Fdp.Examples.NetworkDemo.Components
{
    [FdpDescriptor(20, "TurretState")]
    [DdsTopic("TurretState")]
    [ComponentId(213)]
    public partial struct TurretState
    {
        [DdsKey]
        public long EntityId;
        public float Yaw;
        public float Pitch;
        public byte AmmoCount;
    }
}
