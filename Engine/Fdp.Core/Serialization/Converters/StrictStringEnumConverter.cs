using System.Text.Json.Serialization;

namespace Fdp.Core.Serialization.Converters
{
    /// <summary>
    /// A <see cref="JsonStringEnumConverter"/> variant that rejects numeric enum values,
    /// throwing <see cref="System.Text.Json.JsonException"/> when an integer is encountered.
    /// This prevents the silent integer-as-enum parsing bug in the wire protocol and
    /// diagnostic dump handlers.
    /// </summary>
    public class StrictStringEnumConverter : JsonStringEnumConverter
    {
        /// <summary>Initialises the converter with <c>allowIntegerValues = false</c>.</summary>
        public StrictStringEnumConverter() : base(allowIntegerValues: false) { }
    }
}
