using System;
using System.Collections.Generic;
using Fdp.Interfaces;

namespace Fdp.Toolkit.Tkb
{
    /// <summary>
    /// Parses a JSON sub-element into a descriptor DTO and stores it on the template.
    /// </summary>
    public delegate void TkbDescriptorParserThunk(
        TkbTemplate template, int partId, System.Text.Json.JsonElement jsonElement);

    /// <summary>
    /// Static registry mapping descriptor hierarchical names to parser thunks.
    /// Populated once at startup by source-generated [ModuleInitializer] code (Phase 5)
    /// and then read-only for the lifetime of the process.
    /// </summary>
    public static class TkbDescriptorRegistry
    {
        private static readonly Dictionary<string, TkbDescriptorParserThunk> _parsers
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registers a parser for the given hierarchical name.
        /// Last registration wins (typically called once per type from ModuleInitializer).
        /// </summary>
        public static void RegisterParser(
            string hierarchicalName, TkbDescriptorParserThunk parser)
        {
            _parsers[hierarchicalName] = parser;
        }

        /// <summary>
        /// Looks up the parser thunk for the given hierarchical name.
        /// Returns true and sets <paramref name="thunk"/> if found, false otherwise.
        /// </summary>
        /// <remarks>
        /// NOTE: Dictionary.GetAlternateLookup&lt;ReadOnlySpan&lt;char&gt;&gt;() requires .NET 9+.
        /// This project targets net8.0, so the key must be materialized to a string for the
        /// dictionary lookup. The allocation is acceptable since parsing is a startup-time
        /// operation. When this project is upgraded to .NET 9+, switch to GetAlternateLookup
        /// and update TkbDeserializer accordingly.
        /// </remarks>
        public static bool TryGetParser(
            ReadOnlySpan<char> hierarchicalName,
            out TkbDescriptorParserThunk? thunk)
        {
            // net8.0: must allocate a string for the lookup key.
            return _parsers.TryGetValue(hierarchicalName.ToString(), out thunk);
        }

        /// <summary>
        /// Clears all registered parsers. For testing only.
        /// </summary>
        internal static void Clear() => _parsers.Clear();
    }
}
