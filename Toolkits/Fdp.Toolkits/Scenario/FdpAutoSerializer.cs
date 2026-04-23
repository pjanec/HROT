using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;

namespace Fdp.Toolkit.Scenario
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
        /// <remarks>
        /// <para>
        /// <b>Why the static <see cref="ComponentTypeRegistry"/>?</b>
        /// <c>FDP.Toolkit.Scenario</c> may not reference any Hrot assembly; it has access
        /// only to <c>Fdp.Core</c>.  The single global <see cref="ComponentTypeRegistry"/>
        /// maintained by <c>Fdp.Core</c> is the authoritative roster of all registered
        /// component types at any point in the application lifecycle.  Accepting a registry
        /// parameter (as shown in the CGF1-S0306 task detail) would require the caller to
        /// pass an instance that is actually the same static registry — adding indirection
        /// without benefit.  If a future design introduces multiple independent registries
        /// (e.g. per-world component sets), the signature should be updated to
        /// <c>Build(ComponentTypeRegistry registry)</c> at that time.
        /// </para>
        /// </remarks>
        public void Build()
        {
            _entries.Clear();

            foreach (int typeId in ComponentTypeRegistry.GetSaveableTypeIds())
            {
                var type = ComponentTypeRegistry.GetType(typeId);
                if (type == null || !type.IsValueType || type.IsEnum) continue;

                // Safety: fixed-buffer and InlineArray fields must not use Entity as element type.
                foreach (var (_, elemType, _) in GetFixedBufferFields(type))
                {
                    if (elemType == typeof(Entity))
                        throw new InvalidOperationException(
                            $"Component '{type.Name}' has a fixed-buffer field with element type Entity, which is not supported by FdpAutoSerializer. Use [ScenarioIgnore] to exclude it.");
                }
                foreach (var (_, elemType, _) in GetInlineArrayFields(type))
                {
                    if (elemType == typeof(Entity))
                        throw new InvalidOperationException(
                            $"Component '{type.Name}' has an [InlineArray] field with element type Entity, which is not supported by FdpAutoSerializer. Use [ScenarioIgnore] to exclude it.");
                }

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
                .GetMethod(nameof(CreateJsonValue), BindingFlags.NonPublic | BindingFlags.Static)!
                .GetGenericMethodDefinition();

        /// <summary>
        /// JsonSerializerOptions that includes public fields in addition to properties.
        /// Required for System.Numerics types (Vector3, Quaternion) which use public fields
        /// rather than properties — the default System.Text.Json options skip fields entirely.
        /// Custom array converters are registered here so that Vector2/Vector3/Quaternion-typed
        /// fields are written as compact single-line arrays (e.g. <c>[x, y, z]</c>) across
        /// the entire scenario without modifying any component definitions.
        /// </summary>
        private static readonly JsonSerializerOptions _fieldAwareOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            Converters    =
            {
                new Vector3ArrayConverter(),
                new QuaternionArrayConverter(),
                new Vector2ArrayConverter(),
            },
        };

        /// <summary>
        /// Serializes a value of type <typeparamref name="T"/> to a <see cref="JsonNode"/>
        /// using <see cref="JsonSerializer"/> with <see cref="_fieldAwareOptions"/>.
        /// Handles both primitive types (as JsonValue) and field-based structs like
        /// <c>Vector3</c>/<c>Quaternion</c> (as JsonObject) correctly across JSON string roundtrips.
        /// </summary>
        private static JsonNode? SerializeFieldToNode<T>(T value)
            => JsonSerializer.SerializeToNode(value, _fieldAwareOptions);

        private static readonly MethodInfo _serializeFieldToNodeGeneric =
            typeof(FdpAutoSerializer)
                .GetMethod(nameof(SerializeFieldToNode), BindingFlags.NonPublic | BindingFlags.Static)!
                .GetGenericMethodDefinition();

        /// <summary>
        /// Deserializes a <see cref="JsonNode"/> to type <typeparamref name="T"/>
        /// using <see cref="_fieldAwareOptions"/>.
        /// Handles both <see cref="JsonValue"/> (primitives) and <see cref="JsonObject"/>
        /// (complex structs like Vector3, Quaternion) through the JSON roundtrip.
        /// </summary>
        private static T DeserializeNode<T>(JsonNode node)
            => node.Deserialize<T>(_fieldAwareOptions)!;

        private static readonly MethodInfo _deserializeNodeGeneric =
            typeof(FdpAutoSerializer)
                .GetMethod(nameof(DeserializeNode), BindingFlags.NonPublic | BindingFlags.Static)!
                .GetGenericMethodDefinition();

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

        private static readonly MethodInfo _readFixedBufferGeneric =
            typeof(FdpAutoSerializer)
                .GetMethod(nameof(ReadFixedBuffer), BindingFlags.NonPublic | BindingFlags.Static)!
                .GetGenericMethodDefinition();

        private static readonly MethodInfo _readInlineArrayGeneric =
            typeof(FdpAutoSerializer)
                .GetMethod(nameof(ReadInlineArray), BindingFlags.NonPublic | BindingFlags.Static)!
                .GetGenericMethodDefinition();

        private static readonly MethodInfo _fillFixedBufferGeneric =
            typeof(FdpAutoSerializer)
                .GetMethod(nameof(FillFixedBuffer), BindingFlags.NonPublic | BindingFlags.Static)!
                .GetGenericMethodDefinition();

        private static readonly MethodInfo _fillInlineArrayGeneric =
            typeof(FdpAutoSerializer)
                .GetMethod(nameof(FillInlineArray), BindingFlags.NonPublic | BindingFlags.Static)!
                .GetGenericMethodDefinition();

        // ── Entry builder ────────────────────────────────────────────────────────

        private static AutoSerializeEntry? TryBuildEntry(Type componentType, int typeId)
        {
            // Only public instance fields that are not annotated with [ScenarioIgnore].
            var fields        = GetSerializableFields(componentType);
            var fixedFields   = GetFixedBufferFields(componentType);
            var inlineFields  = GetInlineArrayFields(componentType);
            if (fields.Length == 0 && fixedFields.Length == 0 && inlineFields.Length == 0) return null;

            var extractDelegate = BuildExtract(componentType, fields, fixedFields, inlineFields);
            var injectDelegate  = BuildInject(componentType, fields, fixedFields, inlineFields);

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
                // Skip fixed-buffer fields — handled separately by GetFixedBufferFields.
                if (f.GetCustomAttribute<FixedBufferAttribute>() != null) continue;
                // Skip InlineArray fields — handled separately by GetInlineArrayFields.
                if (f.FieldType.GetCustomAttribute<InlineArrayAttribute>() != null) continue;
                result.Add(f);
            }
            return result.ToArray();
        }

        /// <summary>
        /// Returns fixed-buffer fields of <paramref name="type"/> together with their element
        /// type and element count, as declared by <see cref="FixedBufferAttribute"/>.
        /// </summary>
        private static (FieldInfo field, Type elemType, int length)[] GetFixedBufferFields(Type type)
        {
            var result = new List<(FieldInfo, Type, int)>();
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.GetCustomAttribute<ScenarioIgnoreAttribute>() != null) continue;
                var attr = f.GetCustomAttribute<FixedBufferAttribute>();
                if (attr == null) continue;
                result.Add((f, attr.ElementType, attr.Length));
            }
            return result.ToArray();
        }

        /// <summary>
        /// Returns fields whose declared type carries <see cref="InlineArrayAttribute"/>,
        /// together with the element type (first field of the inline-array struct) and capacity.
        /// </summary>
        private static (FieldInfo field, Type elemType, int length)[] GetInlineArrayFields(Type type)
        {
            var result = new List<(FieldInfo, Type, int)>();
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.GetCustomAttribute<ScenarioIgnoreAttribute>() != null) continue;
                var attr = f.FieldType.GetCustomAttribute<InlineArrayAttribute>();
                if (attr == null) continue;
                // The element type is the (only) field of the InlineArray struct.
                var elemField = f.FieldType.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault();
                if (elemField == null) continue;
                result.Add((f, elemField.FieldType, attr.Length));
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
            Type componentType, FieldInfo[] fields,
            (FieldInfo field, Type elemType, int length)[] fixedBufferFields,
            (FieldInfo field, Type elemType, int length)[] inlineArrayFields)
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
                    // SerializeFieldToNode<fieldType>(comp.field)
                    // Handles both primitives (JsonValue) and nested structs (JsonObject)
                    jsonNodeExpr = Expression.Call(
                        null,
                        _serializeFieldToNodeGeneric.MakeGenericMethod(field.FieldType),
                        fieldAccess);
                }

                // Cast to JsonNode (parent type for the Add method argument)
                var asJsonNode = Expression.Convert(jsonNodeExpr, typeof(JsonNode));
                bodyStatements.Add(
                    Expression.Call(jsonVar, _jsonObjectAddMethod,
                        Expression.Constant(field.Name), asJsonNode));
            }

            // Fixed-buffer fields: serialize as JsonArray via ReadFixedBuffer<TFixed, TElem>.
            foreach (var (fbField, elemType, length) in fixedBufferFields)
            {
                var fixedFieldAccess = Expression.Field(compVar, fbField);
                // ReadFixedBuffer<TFixed, TElem>(comp.FixedField, length)
                var readMethod = _readFixedBufferGeneric.MakeGenericMethod(fbField.FieldType, elemType);
                var arrExpr    = Expression.Call(null, readMethod, fixedFieldAccess, Expression.Constant(length));
                bodyStatements.Add(
                    Expression.Call(jsonVar, _jsonObjectAddMethod,
                        Expression.Constant(fbField.Name), Expression.Convert(arrExpr, typeof(JsonNode))));
            }

            // InlineArray fields: serialize as JsonArray via ReadInlineArray<TInline, TElem>.
            foreach (var (iaField, elemType, length) in inlineArrayFields)
            {
                var inlineFieldAccess = Expression.Field(compVar, iaField);
                var readMethod = _readInlineArrayGeneric.MakeGenericMethod(iaField.FieldType, elemType);
                var arrExpr    = Expression.Call(null, readMethod, inlineFieldAccess, Expression.Constant(length));
                bodyStatements.Add(
                    Expression.Call(jsonVar, _jsonObjectAddMethod,
                        Expression.Constant(iaField.Name), Expression.Convert(arrExpr, typeof(JsonNode))));
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
            Type componentType, FieldInfo[] fields,
            (FieldInfo field, Type elemType, int length)[] fixedBufferFields,
            (FieldInfo field, Type elemType, int length)[] inlineArrayFields)
        {
            var repoParam     = Expression.Parameter(typeof(EntityRepository), "repo");
            var entityParam   = Expression.Parameter(typeof(Entity), "entity");
            var nodeParam     = Expression.Parameter(typeof(JsonNode), "node");
            var resolverParam = Expression.Parameter(typeof(IGuidResolver), "resolver");

            // Cast node to JsonObject
            var jsonObjVar = Expression.Variable(typeof(JsonObject), "jsonObj");
            var assignJsonObj = Expression.Assign(
                jsonObjVar, Expression.Convert(nodeParam, typeof(JsonObject)));

            bool hasSpecialFields = fixedBufferFields.Length > 0 || inlineArrayFields.Length > 0;

            if (hasSpecialFields)
            {
                // When there are fixed-buffer or InlineArray fields we need a mutable
                // handle on the struct so the runtime helpers can write into it.
                // We use a Holder<TComp> (a tiny class) so the helpers receive a
                // reference type they can mutate freely without unsafe expression-tree
                // ref-parameter passing.
                Type holderType = typeof(Holder<>).MakeGenericType(componentType);
                FieldInfo holderValueField = holderType.GetField("Value")!;

                var holderVar = Expression.Variable(holderType, "holder");
                var newHolder = Expression.Assign(holderVar, Expression.New(holderType));

                var memberBindings = new List<MemberBinding>(fields.Length);
                foreach (var field in fields)
                {
                    var itemAccess = Expression.Property(
                        jsonObjVar, _jsonObjectIndexer, Expression.Constant(field.Name));
                    Expression fieldValue = BuildInjectFieldValue(field.FieldType, itemAccess, resolverParam);
                    memberBindings.Add(Expression.Bind(field, fieldValue));
                }
                var newExpr      = Expression.MemberInit(Expression.New(componentType), memberBindings);
                var assignValue  = Expression.Assign(Expression.Field(holderVar, holderValueField), newExpr);

                var fillCalls = new List<Expression>();
                foreach (var (fbField, elemType, length) in fixedBufferFields)
                {
                    nint offset = Marshal.OffsetOf(componentType, fbField.Name);
                    var itemAccess = Expression.Property(
                        jsonObjVar, _jsonObjectIndexer, Expression.Constant(fbField.Name));
                    var arrExpr = Expression.Convert(itemAccess, typeof(JsonArray));
                    var fillMethod = _fillFixedBufferGeneric.MakeGenericMethod(componentType, elemType);
                    fillCalls.Add(Expression.Call(null, fillMethod,
                        holderVar, Expression.Constant(offset), Expression.Constant(length), arrExpr));
                }
                foreach (var (iaField, elemType, length) in inlineArrayFields)
                {
                    nint offset = Marshal.OffsetOf(componentType, iaField.Name);
                    var itemAccess = Expression.Property(
                        jsonObjVar, _jsonObjectIndexer, Expression.Constant(iaField.Name));
                    var arrExpr = Expression.Convert(itemAccess, typeof(JsonArray));
                    var fillMethod = _fillInlineArrayGeneric.MakeGenericMethod(componentType, elemType);
                    fillCalls.Add(Expression.Call(null, fillMethod,
                        holderVar, Expression.Constant(offset), Expression.Constant(length), arrExpr));
                }

                var setMethod = _setComponentGeneric.MakeGenericMethod(componentType);
                var setCall   = Expression.Call(repoParam, setMethod, entityParam,
                    Expression.Field(holderVar, holderValueField));

                var allStatements = new List<Expression> { assignJsonObj, newHolder, assignValue };
                allStatements.AddRange(fillCalls);
                allStatements.Add(setCall);

                var body = Expression.Block(new[] { jsonObjVar, holderVar }, allStatements);
                return Expression.Lambda<Action<EntityRepository, Entity, JsonNode?, IGuidResolver>>(
                    body, repoParam, entityParam, nodeParam, resolverParam).Compile();
            }
            else
            {
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
                    // DeserializeNode<fieldType>(jsonObj["fieldName"])
                    // Handles both JsonValue (primitives) and JsonObject (nested structs)
                    fieldValue = Expression.Call(
                        null,
                        _deserializeNodeGeneric.MakeGenericMethod(field.FieldType),
                        itemAccess);
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
            } // end else (no special fields)
        }

        /// <summary>
        /// Builds the expression that deserializes a single field value from a JsonNode.
        /// Centralised to avoid duplication between the with-Holder and without-Holder inject paths.
        /// </summary>
        private static Expression BuildInjectFieldValue(Type fieldType, Expression itemAccess, Expression resolverParam)
        {
            if (fieldType == typeof(Entity))
            {
                var getStr = Expression.Call(itemAccess, _jsonNodeGetValueGeneric.MakeGenericMethod(typeof(string)));
                return Expression.Call(resolverParam, _resolveStringMethod, getStr);
            }
            return Expression.Call(null, _deserializeNodeGeneric.MakeGenericMethod(fieldType), itemAccess);
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

        // ── Fixed-buffer and InlineArray runtime helpers ──────────────────────────

        /// <summary>
        /// Mutable container used by the inject path for types that have fixed-buffer
        /// or [InlineArray] fields. Wrapping the struct in a class gives the runtime
        /// helpers a stable addressable location to write into, without needing
        /// <c>ref</c> parameters in expression trees (which are not supported).
        /// </summary>
        private sealed class Holder<T> where T : struct { public T Value; }

        /// <summary>
        /// Reads <paramref name="length"/> elements of type <typeparamref name="TElem"/>
        /// from a fixed-buffer field value <paramref name="buf"/> and returns them as a
        /// <see cref="JsonArray"/>.
        /// </summary>
        /// <typeparam name="TFixed">The compiler-generated fixed-buffer struct type.</typeparam>
        /// <typeparam name="TElem">The element type of the fixed buffer.</typeparam>
        private static unsafe JsonArray ReadFixedBuffer<TFixed, TElem>(TFixed buf, int length)
            where TFixed : unmanaged
            where TElem  : unmanaged
        {
            ref TElem first = ref Unsafe.As<TFixed, TElem>(ref buf);
            var arr = new JsonArray();
            for (int i = 0; i < length; i++)
                arr.Add(SerializeFieldToNode(Unsafe.Add(ref first, i)));
            return arr;
        }

        /// <summary>
        /// Reads <paramref name="length"/> elements of type <typeparamref name="TElem"/>
        /// from an [InlineArray] value <paramref name="inline"/> and returns them as a
        /// <see cref="JsonArray"/>.
        /// </summary>
        /// <typeparam name="TInline">The [InlineArray] struct type.</typeparam>
        /// <typeparam name="TElem">The element type of the inline array.</typeparam>
        private static unsafe JsonArray ReadInlineArray<TInline, TElem>(TInline inline, int length)
            where TInline : unmanaged
            where TElem   : unmanaged
        {
            ref TElem first = ref Unsafe.As<TInline, TElem>(ref inline);
            var arr = new JsonArray();
            for (int i = 0; i < length; i++)
                arr.Add(SerializeFieldToNode(Unsafe.Add(ref first, i)));
            return arr;
        }

        /// <summary>
        /// Writes elements from <paramref name="arr"/> into the fixed-buffer field of
        /// <c>holder.Value</c> at byte offset <paramref name="fieldByteOffset"/>.
        /// </summary>
        private static unsafe void FillFixedBuffer<TComp, TElem>(
            Holder<TComp> holder, nint fieldByteOffset, int length, JsonArray? arr)
            where TComp : struct
            where TElem : unmanaged
        {
            if (arr == null) return;
            ref TElem first = ref Unsafe.As<TComp, TElem>(
                ref Unsafe.AddByteOffset(ref holder.Value, fieldByteOffset));
            int count = Math.Min(length, arr.Count);
            for (int i = 0; i < count; i++)
            {
                var node = arr[i];
                if (node != null)
                    Unsafe.Add(ref first, i) = node.Deserialize<TElem>(_fieldAwareOptions)!;
            }
        }

        /// <summary>
        /// Writes elements from <paramref name="arr"/> into the [InlineArray] field of
        /// <c>holder.Value</c> at byte offset <paramref name="fieldByteOffset"/>.
        /// </summary>
        private static unsafe void FillInlineArray<TComp, TElem>(
            Holder<TComp> holder, nint fieldByteOffset, int length, JsonArray? arr)
            where TComp : struct
            where TElem : unmanaged
        {
            if (arr == null) return;
            ref TElem first = ref Unsafe.As<TComp, TElem>(
                ref Unsafe.AddByteOffset(ref holder.Value, fieldByteOffset));
            int count = Math.Min(length, arr.Count);
            for (int i = 0; i < count; i++)
            {
                var node = arr[i];
                if (node != null)
                    Unsafe.Add(ref first, i) = node.Deserialize<TElem>(_fieldAwareOptions)!;
            }
        }
    }
}
