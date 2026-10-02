using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core.Serialization;
using Fdp.Core.FlightRecorder;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.Diagnostics
{
    /// <summary>
    /// Convenience helper that serializes any runtime object to a JSON string
    /// using the DTO diagnostic mapper pipeline.
    /// </summary>
    public static class EventSerializationHelper
    {
        /// <summary>
        /// Maps <paramref name="value"/> through <see cref="DtoDiagnosticMapper.MapObject"/> then
        /// serializes the result to a JSON string.
        /// </summary>
        /// <param name="value">The value to serialize.</param>
        /// <param name="resolver">
        ///   Optional GUID resolver for entity-ref resolution.
        ///   Accepted for API compatibility; entity-ref resolution is deferred to a later task
        ///   when <c>NetworkEntityMap</c> is wired in.
        /// </param>
        public static string SerializeToJson(object? value, IGuidResolver? resolver = null)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var mapped  = DtoDiagnosticMapper.MapObject(value, value?.GetType() ?? typeof(object), visited);
            return JsonSerializer.Serialize(mapped, FdpJsonOptionsRegistry.Indented);
        }
    }
}
