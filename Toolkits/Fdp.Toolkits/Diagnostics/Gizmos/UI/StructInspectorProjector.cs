using System;
using GizmoMap.Network;
using StructEdit.Core;
using StructEdit.Json;

namespace Fdp.Toolkit.Diagnostics.Gizmos.UI
{
    // Per-gizmo helper that hides the dual-channel (visual + UI-state) architecture.
    // The gizmo calls EmitAndSync once per frame; this class handles:
    //   - Emitting the StructInspector debug primitive to the draw builder (always).
    //   - Serialising the DTO and publishing to the UI-state channel only when the
    //     content has changed (change-detection via cached JSON string).
    //
    // Design: DESIGN.md §2.1
    public sealed class StructInspectorProjector<T> where T : class
    {
        private readonly IComponentEditService _editService;
        private readonly IGizmoUiStatePublisher? _uiPublisher;
        private string? _lastPublishedJson;

        public StructInspectorProjector(
            IComponentEditService editService,
            IGizmoUiStatePublisher? uiPublisher)
        {
            _editService = editService ?? throw new ArgumentNullException(nameof(editService));
            _uiPublisher = uiPublisher;
        }

        // Called every frame. Always emits the MakeStructInspector primitive.
        // Only calls uiPublisher.Publish when the serialised JSON differs from the cache.
        // When uiPublisher is null, no JSON is allocated.
        public void EmitAndSync(
            IDebugDrawBuilder draw,
            long networkId,
            uint schemaHash,
            T dto,
            ScreenAnchor anchor = ScreenAnchor.TopLeft,
            SizeMode sizeMode = SizeMode.ScreenPixels)
        {
            var prim = DebugPrimitive.MakeStructInspector(
                networkId:  networkId,
                schemaHash: schemaHash,
                anchor:     anchor,
                sizeMode:   sizeMode);
            draw.EmitRaw(in prim);

            if (_uiPublisher == null) return;

            using var session = _editService.Open(dto, typeof(T));
            var json = session.ToJson();
            if (json == _lastPublishedJson) return;

            _lastPublishedJson = json;
            _uiPublisher.Publish(new GizmoUiState
            {
                GizmoInstanceId  = (uint)networkId,
                EditDocumentJson = json,
            });
        }

        // Called from OnStructUpdate. Deserialises the incoming JSON into dto and updates
        // the cache so the next EmitAndSync with the same DTO state does not echo back.
        public void ApplyUpdate(string payloadJson, ref T dto)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return;
            try
            {
                using var session = _editService.Open(dto, typeof(T));
                session.LoadJson(payloadJson);
                dto = (T)session.Commit();
                // Re-serialise to canonical form so cache matches what EmitAndSync would produce.
                using var canonSession = _editService.Open(dto, typeof(T));
                _lastPublishedJson = canonSession.ToJson();
            }
            catch (Exception)
            {
                // Silently drop malformed updates — same pattern as LayerControlGizmo.
            }
        }
    }
}
