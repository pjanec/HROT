using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Fdp.Core.Serialization.Converters;

namespace Fdp.Core.Serialization
{
    /// <summary>
    /// Central registry of canonical <see cref="JsonSerializerOptions"/> singletons for FDP/HROT.
    ///
    /// <para>
    /// All JSON serialisation in the platform must use one of these two singletons instead
    /// of constructing ad-hoc <see cref="JsonSerializerOptions"/> instances.  Both instances
    /// are frozen (<see cref="JsonSerializerOptions.MakeReadOnly"/>) after construction to
    /// prevent accidental mutation at runtime.
    /// </para>
    ///
    /// <list type="bullet">
    ///   <item><see cref="DefaultRelaxed"/> — field-aware, case-insensitive, null-omitting,
    ///         comment/trailing-comma tolerant.  Used for deserialization, scenario save/load,
    ///         and DDS payload round-trips.</item>
    ///   <item><see cref="Indented"/> — same as <see cref="DefaultRelaxed"/> plus
    ///         <c>WriteIndented = true</c>.  Used for clipboard JSON, diagnostic dump output,
    ///         and any human-readable serialization path.</item>
    /// </list>
    /// </summary>
    public static class FdpJsonOptionsRegistry
    {
        /// <summary>
        /// Relaxed, field-aware options suitable for most serialization and deserialization paths.
        ///
        /// <para>Settings:</para>
        /// <list type="bullet">
        ///   <item><c>IncludeFields = true</c> — required for <c>System.Numerics</c> types
        ///         (Vector3, Quaternion) which expose public fields rather than properties.</item>
        ///   <item><c>PropertyNameCaseInsensitive = true</c> — tolerates both camelCase and
        ///         PascalCase keys in incoming JSON.</item>
        ///   <item><c>AllowTrailingCommas = true</c> — tolerates hand-edited JSON files.</item>
        ///   <item><c>ReadCommentHandling = Skip</c> — tolerates commented-out lines in JSON
        ///         files.</item>
        ///   <item><c>DefaultIgnoreCondition = WhenWritingNull</c> — keeps output concise.</item>
        ///   <item>Custom converters: <see cref="FixedString32Converter"/>,
        ///         <see cref="FixedString64Converter"/>, <see cref="Vector2ArrayConverter"/>,
        ///         <see cref="Vector3ArrayConverter"/>, <see cref="Vector4ArrayConverter"/>,
        ///         <see cref="QuaternionArrayConverter"/>, <see cref="StrictStringEnumConverter"/>.
        ///         <see cref="StrictStringEnumConverter"/> is used instead of the standard
        ///         <see cref="JsonStringEnumConverter"/> to reject silent integer-as-enum
        ///         parsing across all serialization paths.</item>
        /// </list>
        /// </summary>
        public static readonly JsonSerializerOptions DefaultRelaxed;

        /// <summary>
        /// Same as <see cref="DefaultRelaxed"/> with <c>WriteIndented = true</c>.
        /// Used for clipboard JSON, diagnostic dump output, and other human-readable paths.
        /// </summary>
        public static readonly JsonSerializerOptions Indented;

        static FdpJsonOptionsRegistry()
        {
            var relaxed = new JsonSerializerOptions
            {
                IncludeFields                = true,
                PropertyNameCaseInsensitive  = true,
                AllowTrailingCommas          = true,
                ReadCommentHandling          = JsonCommentHandling.Skip,
                DefaultIgnoreCondition       = JsonIgnoreCondition.WhenWritingNull,
            };
            relaxed.Converters.Add(new FixedString32Converter());
            relaxed.Converters.Add(new FixedString64Converter());
            relaxed.Converters.Add(new Vector2ArrayConverter());
            relaxed.Converters.Add(new Vector3ArrayConverter());
            relaxed.Converters.Add(new Vector4ArrayConverter());
            relaxed.Converters.Add(new QuaternionArrayConverter());
            relaxed.Converters.Add(new StrictStringEnumConverter());
            // FC-3b (Q#21-C3/C1): fixed-list wrapper structs author as plain arrays; the
            // factory recurses per-element through THESE options, so element support tracks
            // the converter list above automatically.
            relaxed.Converters.Add(new FixedListJsonConverterFactory());
            // TypeInfoResolver must be set before MakeReadOnly() in .NET 8+.
            relaxed.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
            relaxed.MakeReadOnly();
            DefaultRelaxed = relaxed;

            var indented = new JsonSerializerOptions(DefaultRelaxed)
            {
                WriteIndented = true,
            };
            // TypeInfoResolver is inherited from DefaultRelaxed copy constructor but
            // MakeReadOnly() still requires it to be present on the new instance.
            indented.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
            indented.MakeReadOnly();
            Indented = indented;
        }
    }
}
