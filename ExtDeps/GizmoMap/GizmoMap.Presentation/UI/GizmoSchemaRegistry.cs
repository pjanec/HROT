using System.Collections.Generic;
using StructEdit.Core;

namespace GizmoMap.Presentation
{
    /// <summary>
    /// Maps a schema hash (FNV-1a of the component type name) to a pre-built
    /// <see cref="EditDocument"/> used as the StructEdit side-channel for
    /// <see cref="ImGuiPropertyTreeAdapter"/>.
    /// </summary>
    public sealed class GizmoSchemaRegistry
    {
        private readonly Dictionary<uint, EditDocument> _docs = new();

        /// <summary>Registers <paramref name="doc"/> for the given <paramref name="schemaHash"/>.</summary>
        public void Register(uint schemaHash, EditDocument doc)
        {
            _docs[schemaHash] = doc;
        }

        /// <summary>
        /// Returns <c>true</c> and sets <paramref name="doc"/> when a document is registered
        /// for <paramref name="schemaHash"/>; otherwise returns <c>false</c>.
        /// </summary>
        public bool TryGet(uint schemaHash, out EditDocument? doc)
            => _docs.TryGetValue(schemaHash, out doc);
    }
}
