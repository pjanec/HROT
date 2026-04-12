using System;
using CycloneDDS.Core;

namespace ModuleHost.Network.Cyclone.Topics
{
    public partial struct WeaponStateTopic
    {
        public long EntityId { get; set; }
        public long InstanceId { get; set; }
        public float Azimuth { get; set; }
        public float Elevation { get; set; }
        public int Ammo { get; set; }
        public byte Status { get; set; }
    }
}
