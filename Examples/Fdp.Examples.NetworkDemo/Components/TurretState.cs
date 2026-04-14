using System.ComponentModel.DataAnnotations;
using Fdp.Interfaces.Abstractions;
using CycloneDDS.Schema;
using Fdp.Core;

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
