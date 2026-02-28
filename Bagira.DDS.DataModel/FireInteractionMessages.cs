using CycloneDDS.Schema;
using Bagira.DDS.DM;

namespace Bagira.BDC.SSTM
{
    /// <summary>
    /// Transient combat interaction event published by SimHost and consumed by IG.
    /// </summary>
    [DdsTopic("FireInteractionEvent")]
    [DdsIdlFile("bdc-sst-sim-msgs")]
    public partial struct FireInteractionEvent
    {
        public float ShooterX;
        public float ShooterY;
        public float TargetX;
        public float TargetY;
    }
}
