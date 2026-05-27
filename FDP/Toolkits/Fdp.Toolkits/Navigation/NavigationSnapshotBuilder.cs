using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Navigation.EngineBacked;
using Fdp.Toolkit.Navigation.Fake;

namespace Fdp.Toolkit.Navigation
{
    /// <summary>
    /// Builds a JSON diagnostic snapshot of the current navigation provider state.
    /// NAV-P7-T2: used by FakeNavigationInspectorWindow's "Snapshot JSON" button.
    /// Schema matches DD-Fake-Nav paragraph 9.
    /// </summary>
    public static class NavigationSnapshotBuilder
    {
        private static readonly JsonSerializerOptions s_indented =
            new JsonSerializerOptions { WriteIndented = true };

        /// <summary>
        /// Serialises the current navigation singleton state held in <paramref name="repo"/>
        /// to an indented JSON string.
        /// Top-level keys: captured_at_tick, loaded_map, navmesh, crowd, volumetric, path_registry.
        /// </summary>
        public static string Build(EntityRepository repo)
        {
            var navmesh = repo.HasSingletonManaged<INavmeshProvider>()
                ? repo.GetSingletonManaged<INavmeshProvider>()
                : null;
            var pathReg = repo.HasSingletonManaged<IPathRegistry>()
                ? repo.GetSingletonManaged<IPathRegistry>()
                : null;

            string loadedMap = navmesh switch
            {
                FakeNavmeshProvider _         => "fake-navmesh",
                EngineBackedNavmeshProvider _ => "engine-backed",
                null                          => "none",
                _                             => navmesh.GetType().Name,
            };

            JsonNode? navmeshNode = navmesh switch
            {
                FakeNavmeshProvider _         => new JsonObject { ["type"] = "FakeNavmeshProvider" },
                EngineBackedNavmeshProvider _ => new JsonObject { ["type"] = "EngineBackedNavmeshProvider" },
                _                             => null,
            };

            JsonNode? pathRegNode = null;
            if (pathReg is SharedPathRegistry shared)
            {
                var snap = ((IFakeMusclePathRegistryTestApi)shared.Muscle).Snapshot();
                pathRegNode = new JsonObject
                {
                    ["type"]         = "SharedPathRegistry",
                    ["handle_count"] = snap.Count,
                };
            }
            else if (pathReg != null)
            {
                pathRegNode = new JsonObject { ["type"] = pathReg.GetType().Name };
            }

            var root = new JsonObject
            {
                ["captured_at_tick"] = 0,
                ["loaded_map"]       = loadedMap,
                ["navmesh"]          = navmeshNode,
                ["crowd"]            = null,
                ["volumetric"]       = null,
                ["path_registry"]    = pathRegNode,
            };

            return root.ToJsonString(s_indented);
        }
    }
}
