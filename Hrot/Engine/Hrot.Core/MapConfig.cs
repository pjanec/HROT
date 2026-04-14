namespace Hrot.Map.Common
{
    public static class MapConfig
    {
        /// <summary>
        /// Default map group ID.
        /// </summary>
        public const int DefaultMapGroupId = 0;

        /// <summary>
        /// Default map instance ID.
        /// </summary>
        public const int DefaultMapId = 1;
    }

    public static class ContextKeys
    {
        /// <summary>
        /// Context for placing tank entities on map.
        /// </summary>
        public const string PlaceTank = "place_tank";
        
        /// <summary>
        /// Context for drawing route waypoints.
        /// </summary>
        public const string DrawRoute = "draw_route";
        
        /// <summary>
        /// Context for drawing fire lines.
        /// </summary>
        public const string DrawFireLine = "draw_fire_line";
        
        /// <summary>
        /// Context for measuring distances.
        /// </summary>
        public const string Measure = "measure";
        
        /// <summary>
        /// Context for selecting entities.
        /// </summary>
        public const string Select = "select";
        
        /// <summary>
        /// Context for entity deletion.
        /// </summary>
        public const string Delete = "delete";
    }
}
