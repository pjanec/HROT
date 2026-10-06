namespace Fdp.Toolkit.Squad.DangerArea.Topics
{
    /// <summary>The wire ⇄ descriptor mapping, one place for both translators.</summary>
    public static class DangerAreaWireMap
    {
        /// <summary>A descriptor as it goes on the wire (the threat stays behind).</summary>
        public static DangerAreaWire ToWire(in DangerAreaDescriptor d) => new()
        {
            FeatureId = d.FeatureId, Kind = (byte)d.Kind,
            CenterX = d.Center.X, CenterY = d.Center.Y, CenterZ = d.Center.Z,
            ExtentX = d.ExtentsXY.X, ExtentY = d.ExtentsXY.Y, AngleRad = d.AngleRad, ZFloor = d.ZFloor, ZCeiling = d.ZCeiling,
            NearX = d.NearSideHandle.X, NearY = d.NearSideHandle.Y, NearZ = d.NearSideHandle.Z,
            FarX = d.FarSideHandle.X, FarY = d.FarSideHandle.Y, FarZ = d.FarSideHandle.Z,
            DistanceAlongRoute = d.DistanceAlongRoute,
        };

        /// <summary>A wire area back as a descriptor, threat 0.</summary>
        public static DangerAreaDescriptor FromWire(in DangerAreaWire w) => new()
        {
            FeatureId = w.FeatureId, Kind = (DangerAreaKind)w.Kind, ThreatRating = 0f,
            Center = new System.Numerics.Vector3(w.CenterX, w.CenterY, w.CenterZ),
            ExtentsXY = new System.Numerics.Vector2(w.ExtentX, w.ExtentY), AngleRad = w.AngleRad, ZFloor = w.ZFloor, ZCeiling = w.ZCeiling,
            NearSideHandle = new System.Numerics.Vector3(w.NearX, w.NearY, w.NearZ),
            FarSideHandle = new System.Numerics.Vector3(w.FarX, w.FarY, w.FarZ),
            DistanceAlongRoute = w.DistanceAlongRoute,
        };
    }
}
