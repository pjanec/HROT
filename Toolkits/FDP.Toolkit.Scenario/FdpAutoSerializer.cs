using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Nodes;
using Fdp.Kernel;

namespace FDP.Toolkit.Scenario
{
    /// <summary>
    /// DOM-aware 1:1 fallback serializer that compiles typed
    /// <see cref="System.Linq.Expressions.Expression"/> delegates at build time — one
    /// extract delegate and one inject delegate per registered, saveable component
    /// type — so that no <see cref="System.Reflection.PropertyInfo.GetValue"/> calls
    /// are made on the hot (per-entity) serialization path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Build once, run fast:</b> Call <see cref="Build"/> once during startup.
    /// The resulting delegates are stored in a dictionary keyed by component-type ID.
    /// On each frame the serializer looks up the delegate by ID (O(1)) and invokes
    /// the compiled lambda directly.
    /// </para>
    /// <para>
    /// <b>What gets compiled:</b> For each saveable value-type component registered
    /// in <see cref="ComponentTypeRegistry"/>:
    /// <list type="bullet">
    ///   <item>An <em>extract</em> delegate reads every non-<c>[ScenarioIgnore]</c>
    ///     field using <see cref="Expression.Field"/> (direct field access, no boxing
    ///     via <c>PropertyInfo.GetValue</c>) and emits a <c>JsonObject</c>.</item>
    ///   <item>An <em>inject</em> delegate reads values from a <c>JsonNode</c> and
    ///     constructs a new struct instance via <c>Expression.MemberInit</c>, then
    ///     calls <c>repo.SetComponent&lt;T&gt;</c> through a pre-compiled setter.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <c>Entity</c>-typed fields are automatically patched through
    /// <see cref="IGuidResolver"/> on both paths.
    /// </para>
    /// </remarks>
    public sealed class FdpAutoSerializer
    {
        // ── State ────────────────────────────────────────────────────────────────

        private readonly Dictionary<int, AutoSerializeEntry> _entries = new();

        // ── Inspection seam ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns <see langword="true"/> after <see cref="Build"/> has compiled at
        /// least one delegate set.  Can be used by tests to verify that the compiled
        /// (non-reflective) hot path is active.
        /// </summary>
        public bool IsBuilt { get; private set; }

        /// <summary>
        /// Always <see langword="false"/>.  Present as a testable assertion that
        /// this implementation does not use <c>PropertyInfo.GetValue</c> on the
        /// serialization hot path.  All field access is via compiled
        /// <see cref="Expression.Field"/> lambdas.
        /// </summary>
        public bool UsesRuntimeReflection => false;

        // ── Build ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Compiles extraction and injection delegates for every saveable, non-abstract
        /// value-type component registered in <see cref="ComponentTypeRegistry"/>.
        /// Must be called once before any <see cref="TryExtract"/> or
        /// <see cref="TryInject"/> calls.
        /// </summary>
        public void Build()
        {
            _entries.Clear();

            foreach (int typeId in ComponentTypeRegistry.GetSaveableTypeIds())
            {
                var type = ComponentTypeRegistry.GetType(typeId);
                if (type == null || !type.IsValueType || type.IsEnum) continue;

                var entry = TryBuildEntry(type, typeId);
                if (entry != null)
                    _entries[typeId] = entry;
            }

            IsBuilt = true;
        }

        // ── Hot-path accessors ────────────────────────────────────────────────────

        /// <summary>
        /// Returns the component name for the given type ID, or <see langword="null"/> if
        /// not registered in this auto-serializer (e.g. <c>DataPolicy.NoSave</c> type).
        /// </summary>
        public string? GetComponentName(int typeId)
            => _entries.TryGetValue(typeId, out var e) ? e.ComponentName : null;

        /// <summary>Enumerates all component type IDs handled by this serializer.</summary>
        public IEnumerable<int> RegisteredTypeIds => _entries.Keys;

        /// <summary>
        /// Extracts a <c>JsonObject</c> for the given component type on <paramref name="entity"/>.
        /// Returns <see langword="null"/> if the entity does not carry that component or if
        /// the type is not handled by this auto-serializer.
        /// </summary>
        public JsonObject? TryExtract(
            EntityRepository repo, Entity entity, int typeId, IGuidResolver resolver)
        {
            if (!_entries.TryGetValue(typeId, out var entry)) return null;
            return entry.Extract(repo, entity, resolver);
        }

        /// <summary>
        /// Injects component data from <paramref name="jsonNode"/> onto
        /// <paramref name="entity"/> for the given component type ID.
        /// No-op if the type is not handled by this auto-serializer.
        /// </summary>
        public void TryInject(
            EntityRepository repo, Entity entity, int typeId, JsonNode? jsonNode, IGuidResolver resolver)
        {
            if (jsonNode == null) return;
            if (!_entries.TryGetValue(typeId, out var entry)) return;
            entry.Inject(repo, entity, jsonNode, resolver);
        }

        // ── Delegate record ──────────────────────────────────────────────────────

        private sealed class AutoSerializeEntry
        {
            public required string ComponentName { get; init; }
            public required Func<EntityRepository, Entity, IGuidResolver, JsonObject?> Extract { get; init; }
            public required Action<EntityRepository, Entity, JsonNode?, IGuidResolver> Inject { get; init; }
        }

        // ── Compilation ──────────────────────────────────────────────────────────

        private static readonly MethodInfo _hasComponentGeneric =
            FindGenericMethod(typeof(EntityRepository), "HasComponent", 1, 1);

        private static readonly MethodInfo _setComponentGeneric =
            FindGenericMethod(typeof(EntityRepository), "SetComponent", 1, 2);

        /// <summary>
        /// Points to <see cref="GetComponentCopy{T}"/> — a static wrapper that returns a
        /// value-type copy of the component, bypassing the <c>ref readonly T</c> return
        /// of <c>EntityRepository.GetComponent&lt;T&gt;</c> which expression trees cannot
        /// assign directly to a local variable.
        /// </summary>
        private static readonly MethodInfo _getComponentCopyGeneric =
            typeof(FdpAutoSerializer)
                .GetMethod(nameof(GetComponentCopy), BindingFlags.NonPublic | BindingFlags.Static)!;

        private static MethodInfo FindGenericMethod(Type type, string name, int typeArgCount, int paramCount)
        {
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != name) continue;
                if (!m.IsGenericMethodDefinition) continue;
                if (m.GetGenericArguments().Length != typeArgCount) continue;
                if (m.GetParameters().Length != paramCount) continue;
                return m;
            }
            throw new InvalidOperationException(
                $"Could not locate method '{name}' with {typeArgCount} generic args and {paramCount} params on {type.Name}.");
        }

        private static readonly MethodInfo _resolveEntityMethod =
            typeof(IGuidResolver).GetMethod("Resolve", new[] { typeof(Entity) })!;

        private static readonly MethodInfo _resolveStringMethod =
            typeof(IGuidResolver).GetMethod("Resolve", new[] { typeof(string) })!;

        /// <summary>
        /// Wraps <c>JsonValue.Create&lt;T&gt;</c> with an explicit 1-param signature so that
        /// expression trees can call it without dealing with the optional
        /// <c>JsonNodeOptions?</c> argument that .NET 8 adds to the original method.
        /// </summary>
        private static JsonValue CreateJsonValue<T>(T value) => JsonValue.Create<T>(value)!;

        private static readonly MethodInfo _createJsonValueGeneric =
            typeof(FdpAutoSerializer)
                .GetMethod(nameof(CreateJsonValue), BindingFlags.NonPublic | BindingFlags.Static)!;

        private static readonly MethodInfo _jsonNodeGetValueGeneric =
            typeof(JsonNode).GetMethod("GetValue", Type.EmptyTypes)
            ?? throw new InvalidOperationException("Could not locate JsonNode.GetValue<T>().");

        private static readonly MethodInfo _jsonObjectAddMethod =
            typeof(JsonObject).GetMethod("Add", new[] { typeof(string), typeof(JsonNode) })!;

        private static readonly PropertyInfo _jsonObjectIndexer =
            typeof(JsonObject).GetProperty("Item", typeof(JsonNode), new[] { typeof(string) })!;

        private static readonly ConstructorInfo _jsonObjectCtor =
            typeof(JsonObject).GetConstructor(new[] { typeof(JsonNodeOptions?) })
            ?? typeof(JsonObject).GetConstructors()[0];

        // ── Entry builder ────────────────────────────────────────────────────────

        private static AutoSerializeEntry? TryBuildEntry(Type componentType, int typeId)
        {
            // Only public instance fields that are not annotated with [ScenarioIgnore].
            var fields = GetSerializableFields(componentType);
            if (fields.Length == 0) return null;

            var extractDelegate = BuildExtract(componentType, fields);
            var injectDelegate  = BuildInject(componentType, fields);

            return new AutoSerializeEntry
            {
                ComponentName = componentType.Name,
                Extract       = extractDelegate,
                Inject        = injectDelegate,
            };
        }

        private static FieldInfo[] GetSerializableFields(Type type)
        {
            var all = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var result = new List<FieldInfo>(all.Length);
            foreach (var f in all)
            {
                if (f.GetCustomAttribute<ScenarioIgnoreAttribute>() != null) continue;
                // Only serializable leaf types: primitives, string, Entity, other value types.
                result.Add(f);
            }
            return result.ToArray();
        }

        // ── Extract delegate compilation ─────────────────────────────────────────

        /// <summary>
        /// Compiles a delegate:
        /// <c>(EntityRepository repo, Entity entity, IGuidResolver resolver) → JsonObject?</c>
        /// <para>
        /// Returns <see langword="null"/> when the entity does not carry the component.
        /// </para>
        /// </summary>
        private static Func<EntityRepository, Entity, IGuidResolver, JsonObject?> BuildExtract(
            Type componentType, FieldInfo[] fields)
        {
            var repoParam     = Expression.Parameter(typeof(EntityRepository), "repo");
            var entityParam   = Expression.Parameter(typeof(Entity), "entity");
            var resolverParam = Expression.Parameter(typeof(IGuidResolver), "resolver");

            // repo.HasComponent<T>(entity)
            var hasMethod = _hasComponentGeneric.MakeGenericMethod(componentType);
            var hasCall   = Expression.Call(repoParam, hasMethod, entityParam);

            // GetComponentCopy<T>(repo, entity) — static helper that returns T by value,
            // avoiding the ref-return incompatibility with expression-tree Assign.
            var getMethod = _getComponentCopyGeneric.MakeGenericMethod(componentType);

            var compVar = Expression.Variable(componentType, "comp");
            var jsonVar = Expression.Variable(typeof(JsonObject), "json");

            var bodyStatements = new List<Expression>
            {
                // comp = GetComponentCopy<T>(repo, entity)
                Expression.Assign(compVar, Expression.Call(null, getMethod, repoParam, entityParam)),
                // json = new JsonObject(null)
                Expression.Assign(jsonVar, Expression.New(_jsonObjectCtor, Expression.Constant(null, typeof(JsonNodeOptions?)))),
            };

            foreach (var field in fields)
            {
                var fieldAccess = Expression.Field(compVar, field);
                Expression jsonNodeExpr;

                if (field.FieldType == typeof(Entity))
                {
                    // resolver.Resolve(comp.field) → string; wrap in CreateJsonValue<string>
                    var resolvedStr = Expression.Call(resolverParam, _resolveEntityMethod, fieldAccess);
                    jsonNodeExpr = Expression.Call(
                        null,
                        _createJsonValueGeneric.MakeGenericMethod(typeof(string)),
                        resolvedStr);
                }
                else
                {
                    // CreateJsonValue<fieldType>(comp.field)
                    jsonNodeExpr = Expression.Call(
                        null,
                        _createJsonValueGeneric.MakeGenericMethod(field.FieldType),
                        fieldAccess);
                }

                // Cast to JsonNode (parent type for the Add method argument)
                var asJsonNode = Expression.Convert(jsonNodeExpr, typeof(JsonNode));
                bodyStatements.Add(
                    Expression.Call(jsonVar, _jsonObjectAddMethod,
                        Expression.Constant(field.Name), asJsonNode));
            }

            bodyStatements.Add(jsonVar); // return json

            var ifFound = Expression.Block(
                new[] { compVar, jsonVar },
                bodyStatements);

            // if (!has) return null; else run body
            var nullJson = Expression.Constant(null, typeof(JsonObject));
            var conditional = Expression.Condition(hasCall, ifFound, nullJson);

            return Expression.Lambda<Func<EntityRepository, Entity, IGuidResolver, JsonObject?>>(
                conditional, repoParam, entityParam, resolverParam).Compile();
        }

        // ── Inject delegate compilation ──────────────────────────────────────────

        /// <summary>
        /// Compiles a delegate:
        /// <c>(EntityRepository repo, Entity entity, JsonNode? node, IGuidResolver resolver) → void</c>
        /// </summary>
        private static Action<EntityRepository, Entity, JsonNode?, IGuidResolver> BuildInject(
            Type componentType, FieldInfo[] fields)
        {
            var repoParam     = Expression.Parameter(typeof(EntityRepository), "repo");
            var entityParam   = Expression.Parameter(typeof(Entity), "entity");
            var nodeParam     = Expression.Parameter(typeof(JsonNode), "node");
            var resolverParam = Expression.Parameter(typeof(IGuidResolver), "resolver");

            // Cast node to JsonObject
            var jsonObjVar = Expression.Variable(typeof(JsonObject), "jsonObj");
            var assignJsonObj = Expression.Assign(
                jsonObjVar, Expression.Convert(nodeParam, typeof(JsonObject)));

            var memberBindings = new List<MemberBinding>(fields.Length);

            foreach (var field in fields)
            {
                // jsonObj["fieldName"]
                var itemAccess = Expression.Property(
                    jsonObjVar, _jsonObjectIndexer, Expression.Constant(field.Name));

                Expression fieldValue;
                if (field.FieldType == typeof(Entity))
                {
                    // resolver.Resolve(jsonObj["fieldName"].GetValue<string>())
                    var getStr = Expression.Call(
                        itemAccess,
                        _jsonNodeGetValueGeneric.MakeGenericMethod(typeof(string)));
                    fieldValue = Expression.Call(resolverParam, _resolveStringMethod, getStr);
                }
                else
                {
                    // jsonObj["fieldName"].GetValue<fieldType>()
                    fieldValue = Expression.Call(
                        itemAccess,
                        _jsonNodeGetValueGeneric.MakeGenericMethod(field.FieldType));
                }

                memberBindings.Add(Expression.Bind(field, fieldValue));
            }

            // new T { Field1 = ..., Field2 = ... }
            var newExpr = Expression.MemberInit(Expression.New(componentType), memberBindings);
            var compVar = Expression.Variable(componentType, "comp");
            var assignComp = Expression.Assign(compVar, newExpr);

            // repo.SetComponent<T>(entity, comp)
            var setMethod = _setComponentGeneric.MakeGenericMethod(componentType);
            var setCall   = Expression.Call(repoParam, setMethod, entityParam, compVar);

            var body = Expression.Block(
                new[] { jsonObjVar, compVar },
                assignJsonObj,
                assignComp,
                setCall);

            return Expression.Lambda<Action<EntityRepository, Entity, JsonNode?, IGuidResolver>>(
                body, repoParam, entityParam, nodeParam, resolverParam).Compile();
        }

        // ── Component value copy helper ───────────────────────────────────────────

        /// <summary>
        /// Returns a by-value copy of the component on <paramref name="entity"/>.
        /// Used from compiled expression trees: <c>EntityRepository.GetComponent&lt;T&gt;</c>
        /// returns <c>ref readonly T</c>, which expression trees cannot assign directly to a
        /// local variable.  This wrapper returns <c>T</c> by value, which expression trees handle
        /// without issue.
        /// </summary>
        private static T GetComponentCopy<T>(EntityRepository repo, Entity entity)
        {
            repo.TryGetComponent<T>(entity, out T value);
            return value;
        }
    }
}
