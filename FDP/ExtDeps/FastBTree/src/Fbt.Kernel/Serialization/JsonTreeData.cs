using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Fbt.Serialization
{
    /// <summary>
    /// Represents the raw JSON structure of a behavior tree file.
    /// </summary>
    public class JsonTreeData
    {
        public string? TreeName { get; set; }
        public int Version { get; set; } = 1;
        public JsonNode? Root { get; set; }
    }

    public class JsonNode
    {
        public string? Type { get; set; }
        public string? Name { get; set; }
        public string? Action { get; set; }
        public string? Method { get; set; }
        public float WaitTime { get; set; }
        public int RepeatCount { get; set; }
        public float CooldownTime { get; set; }
        public int Policy { get; set; }
        public JsonNode[]? Children { get; set; }
        
        [JsonExtensionData]
        public Dictionary<string, object> Params { get; set; } = new Dictionary<string, object>();
    }
}
