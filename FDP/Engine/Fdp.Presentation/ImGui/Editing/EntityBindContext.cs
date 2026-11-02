using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Scenario;

namespace Fdp.Presentation.Editing
{
    /// <summary>
    /// ⭐⭐ CE-4210 P2 (MA-2 third home, D) — the <see cref="IParamBindContext"/> of ONE ENTITY: reads and writes its <see cref="ParamBindings"/> under
    /// <c>"&lt;prefix&gt;.&lt;path&gt;"</c> (<c>"EntityInfo.ForceId"</c>, <c>"Behavior.Params.Speed"</c>), every edit a NEW immutable record set through the host's
    /// <see cref="IInspectableSession"/> (the inspector's one component-write seam). Params in scope = the resident header's <c>Params</c> (<see cref="HeaderParams"/>).
    /// </summary>
    public sealed class EntityBindContext : IParamBindContext
    {
        private readonly IInspectableSession _session;
        private readonly Entity _entity;
        private readonly Func<IReadOnlyList<ParamInScope>> _params;
        private readonly Func<Type, string, object?, ParamInScope?>? _declare;

        /// <summary>The prefix every path gets (a component name, or <c>"Behavior.Params"</c> for the params struct).</summary>
        public string Prefix { get; }
        public Entity Entity => _entity;

        public EntityBindContext(IInspectableSession session, Entity entity, string prefix, Func<IReadOnlyList<ParamInScope>> paramsInScope,
                                 Func<Type, string, object?, ParamInScope?>? declare = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _entity = entity;
            Prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
            _params = paramsInScope ?? throw new ArgumentNullException(nameof(paramsInScope));
            _declare = declare;
        }

        /// <summary>The same entity and host, another prefix (the params form's <c>"Behavior.Params"</c>).</summary>
        public EntityBindContext WithPrefix(string prefix) => new(_session, _entity, prefix, _params, _declare);

        public IReadOnlyList<ParamInScope> Params => _params();

        private string Key(string path) => path.Length == 0 ? Prefix : Prefix + "." + path;

        private IReadOnlyDictionary<string, string> Current()
            => _session.IsAlive(_entity) && _session.HasComponent(_entity, typeof(ParamBindings))
               && _session.GetComponent(_entity, typeof(ParamBindings)) is ParamBindings pb ? pb.Bindings : new Dictionary<string, string>();

        public string? BoundTo(string path) => Current().TryGetValue(Key(path), out var n) ? n : null;

        private void Write(Dictionary<string, string> next)
        {
            if (!_session.IsAlive(_entity)) return;
            _session.SetComponent(_entity, typeof(ParamBindings), new ParamBindings { Bindings = next });
        }

        public void Bind(string path, string param)
        {
            var next = new Dictionary<string, string>(Current(), StringComparer.Ordinal) { [Key(path)] = param };
            Write(next);
        }

        public void Unbind(string path)
        {
            var next = new Dictionary<string, string>(Current(), StringComparer.Ordinal);
            if (next.Remove(Key(path))) Write(next);
        }

        /// <summary>SR-161: an index in a path is refused — the only validator reason the third home adds (declared / type are the drawer's checks).</summary>
        public string? Problem(string path) => path.IndexOf('[') >= 0 ? "A binding cannot name an array element; bind a whole value." : null;

        public bool CanDeclareParams => _declare is not null;

        public ParamInScope? NewParam(Type fieldType, string suggestedName, object? currentValue) => _declare?.Invoke(fieldType, suggestedName, currentValue);
    }

    /// <summary>The parameters a world's resident header declares, as the bind menu lists them.</summary>
    public static class HeaderParams
    {
        public static IReadOnlyList<ParamInScope> Of(EntityRepository? world)
            => ScenarioDescription.HeaderOf(world)?.Params is { } ps
                ? ps.Select(p => new ParamInScope(p.Name, p.TypeId)).ToArray()
                : Array.Empty<ParamInScope>();
    }
}
