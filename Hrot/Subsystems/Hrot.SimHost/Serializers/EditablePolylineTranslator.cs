using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Scenario;
using Hrot.IG.Components;
using Hrot.Map.Common;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// Custom scenario translator for <see cref="EditablePolyline"/> managed component.
    /// </summary>
    public sealed class EditablePolylineTranslator : IEntityScenarioTranslator
    {
        private const string Key = "EditablePolyline";

        // EditablePolyline contains value data only (List<Vector2> + version).
        public bool IsExtractionSafe => true;

        public BitMask512 GetConsumedComponentsMask()
        {
            var mask = new BitMask512();
            int id = ComponentTypeRegistry.GetId(typeof(EditablePolyline));
            if (id >= 0)
                mask.SetBit(id);
            else
                mask.SetBit(GlobalComponentIds.EditablePolyline);
            return mask;
        }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasManagedComponent<EditablePolyline>(entity);

        public Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver guidResolver)
        {
            var polyline = ((ISimulationView)repo).GetManagedComponentRO<EditablePolyline>(entity);
            var points = new JsonArray();

            if (polyline?.Points != null)
            {
                foreach (var pt in polyline.Points)
                {
                    points.Add(new JsonObject
                    {
                        ["X"] = pt.X,
                        ["Y"] = pt.Y,
                    });
                }
            }

            var obj = new JsonObject
            {
                ["Points"]  = points,
                ["Version"] = polyline?.Version ?? 0,
            };

            return new Dictionary<string, object> { [Key] = obj };
        }

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver guidResolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw) || raw is not JsonObject obj)
                return;

            var polyline = new EditablePolyline
            {
                Points = new List<Vector2>(),
                Version = obj["Version"]?.GetValue<int>() ?? 0,
            };

            if (obj["Points"] is JsonArray pointsArray)
            {
                foreach (var node in pointsArray)
                {
                    if (node is not JsonObject ptObj) continue;
                    float x = ptObj["X"]?.GetValue<float>() ?? 0f;
                    float y = ptObj["Y"]?.GetValue<float>() ?? 0f;
                    polyline.Points.Add(new Vector2(x, y));
                }
            }

            repo.SetManagedComponent(entity, polyline);
        }

        public IEnumerable<string> GetOutputDomKeys()
        {
            yield return Key;
        }
    }
}

