namespace Bagira.SimHost.Configuration
{
    public static class SimHostConfig
    {
        // Reference origin for WGS84 projection (e.g. near Tel Aviv for testing)
        // These are dummy values as required by the task, but using real geographic coordinates improves testing realism.
        public const double OriginLatitude = 32.0853;
        public const double OriginLongitude = 34.7818;
        public const double OriginAltitude = 10.0;
        
        public const string DomainIdEnvVar = "CYCLONEDDS_URI"; // Or whatever config env var is used
    }
}
