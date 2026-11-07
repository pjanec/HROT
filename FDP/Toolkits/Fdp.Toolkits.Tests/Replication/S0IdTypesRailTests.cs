using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Xunit;

namespace Fdp.Toolkits.Tests.Replication
{
    /// <summary>
    /// ⭐⭐ CE-4200 SV-2 (<c>docs/DESIGN_Semantic_Value_Types.md</c>) — an ID says what it names by its TYPE. Every member of every component a
    /// scenario SAVES (<see cref="ComponentIdAttribute"/>, not <see cref="DataPolicy.NoScenario"/>) and of every params DTO fails on a bare
    /// <c>long</c> named like a TKB template id (<c>*Tkb*</c> / <c>*TemplateId*</c> — it is a <see cref="TkbTypeId"/>), unless
    /// <see cref="NotATemplateId"/> says what it is instead. ⇒ an entity id (now 32-bit) can never be swapped for a template id, and a tool shows the
    /// template NAME. 📌 Mirrors <see cref="S0ComponentTypesRailTests"/>' scan; the node-id rails (CE-4200 SV-3) live in this class too.
    /// </summary>
    public sealed class S0IdTypesRailTests
    {
        private static IEnumerable<Assembly> Assemblies() => new[]
        {
            typeof(SimTransform).Assembly,                                       // Fdp.Core
            typeof(EntityRef).Assembly,                                          // Fdp.Toolkits
            typeof(Hrot.Map.Definitions.Behavior.ParamsDtoRegistry).Assembly,   // Hrot.Core
            typeof(Hrot.AI.Behaviors.Brains.CgfNodes).Assembly,                 // Hrot.AI.Behaviors
        };

        /// <summary><c>Type.Member</c> → what it is instead of a template id. ⭐ A decision each; never a silencer.</summary>
        internal static readonly Dictionary<string, string> NotATemplateId = new(StringComparer.Ordinal)
        {
            // (filled from the first scan — every entry names the reason)
        };

        private static bool Saved(Type t)
            => t.GetCustomAttribute<ComponentIdAttribute>() != null
               && (t.GetCustomAttribute<DataPolicyAttribute>()?.Policy & DataPolicy.NoScenario) != DataPolicy.NoScenario;

        private static bool ParamsDto(Type t) => t.Name.EndsWith("Params", StringComparison.Ordinal) || t.Name.EndsWith("ParamsJsonDto", StringComparison.Ordinal);

        private static IEnumerable<Type> Scanned()
            => Assemblies().SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; } })
                           .Where(t => t != null && (Saved(t!) || ParamsDto(t!))).Select(t => t!).Distinct();

        private static IEnumerable<(string Owner, string Member, Type Type)> Members(Type t)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) yield return (t.Name, f.Name, f.FieldType);
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) yield return (t.Name, p.Name, p.PropertyType);
        }

        private static bool LooksLikeATemplateId(string name)
            => name.Contains("Tkb", StringComparison.OrdinalIgnoreCase) || name.Contains("TemplateId", StringComparison.OrdinalIgnoreCase);

        private static Type Strip(Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            return t.IsArray ? t.GetElementType()! : t;
        }

        [Fact]
        public void S0_EveryTemplateIdMember_IsATkbTypeId()
        {
            var types = Scanned().ToList();
            Assert.True(types.Count >= 150, $"only {types.Count} component / params types found — the scan lost its assemblies");
            var offences = new List<string>();
            foreach (var t in types)
                foreach (var (owner, member, type) in Members(t))
                    if (Strip(type) == typeof(long) && LooksLikeATemplateId(member) && !NotATemplateId.ContainsKey(owner + "." + member))
                        offences.Add($"{t.FullName}.{member} ({type.Name}) — a template id is a TkbTypeId (or say what it is)");
            var sb = new StringBuilder();
            foreach (var o in offences.OrderBy(s => s, StringComparer.Ordinal)) sb.AppendLine(o);
            Assert.True(offences.Count == 0, $"{offences.Count} saved / params members look like a template id but are a bare long:\n{sb}");
        }

        [Fact]
        public void S0_EveryNotATemplateIdEntry_StillNamesAMember()
        {
            var live = new HashSet<string>(Scanned().SelectMany(t => Members(t).Select(m => m.Owner + "." + m.Member)));
            var stale = NotATemplateId.Keys.Where(k => !live.Contains(k)).ToList();
            Assert.True(stale.Count == 0, "NotATemplateId entries name no member any more: " + string.Join(", ", stale));
        }

        /// <summary>The type itself — a long's memory, a plain JSON number, implicit OUT to long, explicit IN (⛔ never from an int entity id).</summary>
        [Fact]
        public void TkbTypeId_IsALongsMemory_AndAPlainJsonNumber_AndExplicitIn()
        {
            Assert.Equal(8, System.Runtime.CompilerServices.Unsafe.SizeOf<TkbTypeId>());
            Assert.Equal("101", System.Text.Json.JsonSerializer.Serialize(new TkbTypeId(101)));
            Assert.Equal(new TkbTypeId(7202), System.Text.Json.JsonSerializer.Deserialize<TkbTypeId>("7202"));
            long asLong = new TkbTypeId(42);                      // implicit out
            Assert.Equal(42L, asLong);
            Assert.Equal(new TkbTypeId(9), (TkbTypeId)9L);        // explicit in
            Assert.Equal("{\"TkbType\":3}", System.Text.Json.JsonSerializer.Serialize(new { TkbType = new TkbTypeId(3) }));
            // ⛔ no implicit conversion from long and none from int: asserted by reflection (a compile-time property, pinned so an added operator is caught)
            var ops = typeof(TkbTypeId).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "op_Implicit").ToList();
            Assert.Single(ops);
            Assert.Equal(typeof(long), ops[0].ReturnType);
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
        // ⭐⭐ CE-4200 SV-3 — a NODE id says what it names by its TYPE too: <see cref="NodeId"/>, an int's memory and JSON.
        // The scan is wider than the template rail's (a node id travels in events, commands and config as well as in saved components): every
        // public type of the four assemblies except the DDS WIRE types (their IDL has no wrapper — the id is a plain int there, and the type stops
        // at the topic boundary).
        // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary><c>Type.Member</c> → why it is NOT a cluster node id. ⭐ A decision each; never a silencer.</summary>
        internal static readonly Dictionary<string, string> NotANodeId = new(StringComparer.Ordinal)
        {
            ["RoadSegmentJson.StartNodeId"] = "a ROAD-GRAPH node (a junction in the car-kinematics network), not a cluster node",
            ["RoadSegmentJson.EndNodeId"] = "a ROAD-GRAPH node (a junction in the car-kinematics network), not a cluster node",
            ["FollowRoadGraphParams.TargetNodeId"] = "a ROAD-GRAPH node the follower heads for, not a cluster node",
            ["RunnerConfiguration.NodeId"] = "a COMMAND-LINE option (`-n`, Default = -1): CommandLineParser sets the int default by reflection and cannot put it in a struct; the value becomes a NodeId in RunnerOptions / HrotNodeConfig",
            ["NavigationIntent.TargetNodeId"] = "a NAVIGATION-GRAPH node the unit heads for, not a cluster node",
        };

        /// <summary>A DDS wire type: marked <c>[DdsTopic]</c> / <c>[DdsStruct]</c>, or the codegen's <c>*_Native</c> / <c>*View</c> twin of one.</summary>
        private static bool Wire(Type t)
        {
            static bool Marked(Type x) => x.GetCustomAttributes(false).Any(a => a.GetType().Name is "DdsTopicAttribute" or "DdsStructAttribute");
            if (Marked(t)) return true;
            foreach (var suffix in new[] { "_Native", "View" })
                if (t.Name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    var twin = t.Assembly.GetType((t.Namespace is null ? "" : t.Namespace + ".") + t.Name[..^suffix.Length]);
                    if (twin != null && Marked(twin)) return true;
                }
            return false;
        }

        private static IEnumerable<Type> AllPublicNonWire()
            => Assemblies().SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; } })
                           .Where(t => t != null && (t.IsPublic || t.IsNestedPublic) && !Wire(t!) && t!.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() == null)
                           .Select(t => t!).Distinct();

        private static bool LooksLikeANodeId(string name) => name.EndsWith("NodeId", StringComparison.OrdinalIgnoreCase);

        [Fact]
        public void S0_EveryNodeIdMember_IsANodeId()
        {
            var types = AllPublicNonWire().ToList();
            Assert.True(types.Count >= 1000, $"only {types.Count} public types found — the scan lost its assemblies");
            var offences = new List<string>();
            foreach (var t in types)
                foreach (var (owner, member, type) in Members(t))
                {
                    var u = Strip(type);
                    if ((u == typeof(int) || u == typeof(long)) && LooksLikeANodeId(member) && !NotANodeId.ContainsKey(owner + "." + member))
                        offences.Add($"{t.FullName}.{member} ({type.Name}) — a node id is a NodeId (or say what it is)");
                }
            var sb = new StringBuilder();
            foreach (var o in offences.OrderBy(s => s, StringComparer.Ordinal)) sb.AppendLine(o);
            Assert.True(offences.Count == 0, $"{offences.Count} members look like a cluster node id but are a bare int / long:\n{sb}");
        }

        [Fact]
        public void S0_EveryNotANodeIdEntry_StillNamesAMember()
        {
            var live = new HashSet<string>(AllPublicNonWire().SelectMany(t => Members(t).Select(m => m.Owner + "." + m.Member)));
            var stale = NotANodeId.Keys.Where(k => !live.Contains(k)).ToList();
            Assert.True(stale.Count == 0, "NotANodeId entries name no member any more: " + string.Join(", ", stale));
        }

        /// <summary>The type itself — an int's memory, a plain JSON number, implicit OUT to int, explicit IN.</summary>
        [Fact]
        public void NodeId_IsAnIntsMemory_AndAPlainJsonNumber_AndExplicitIn()
        {
            Assert.Equal(4, System.Runtime.CompilerServices.Unsafe.SizeOf<NodeId>());
            Assert.Equal("300", System.Text.Json.JsonSerializer.Serialize(new NodeId(300)));
            Assert.Equal(new NodeId(7), System.Text.Json.JsonSerializer.Deserialize<NodeId>("7"));
            int asInt = new NodeId(42);                           // implicit out
            Assert.Equal(42, asInt);
            Assert.Equal(new NodeId(9), (NodeId)9);               // explicit in
            Assert.Equal("{\"NodeId\":3}", System.Text.Json.JsonSerializer.Serialize(new { NodeId = new NodeId(3) }));
            var ops = typeof(NodeId).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "op_Implicit").ToList();
            Assert.Single(ops);
            Assert.Equal(typeof(int), ops[0].ReturnType);
            // the binder / parser seam: a string ("-n 3", a config value) and an int convert in, a string out
            var tc = System.ComponentModel.TypeDescriptor.GetConverter(typeof(NodeId));
            Assert.Equal(new NodeId(3), tc.ConvertFromInvariantString("3"));
            Assert.Equal("3", tc.ConvertToInvariantString(new NodeId(3)));
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
        // ⭐⭐ CE-4204 SV-6 — an EXERCISE / EPISODE id is a type a tool can OPEN: <see cref="ExerciseId"/> / <see cref="EpisodeId"/>, a Guid's memory
        // and JSON. Correlation tokens (RequestId, TransactionId, SessionId) stay Guid — the rail keys on the name containing Exercise / Episode.
        // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary><c>Type.Member</c> → why it is NOT an exercise / episode id. ⭐ A decision each; never a silencer. (The DDS wire types are skipped by <see cref="Wire"/>.)</summary>
        internal static readonly Dictionary<string, string> NotAnExerciseOrEpisodeId = new(StringComparer.Ordinal)
        {
        };

        private static bool LooksLikeAnExerciseOrEpisodeId(string name)
            => name.EndsWith("Id", StringComparison.Ordinal)
               && (name.Contains("Exercise", StringComparison.OrdinalIgnoreCase) || name.Contains("Episode", StringComparison.OrdinalIgnoreCase));

        [Fact]
        public void S0_EveryExerciseOrEpisodeIdMember_IsTyped()
        {
            var types = AllPublicNonWire().ToList();
            Assert.True(types.Count >= 1000, $"only {types.Count} public types found — the scan lost its assemblies");
            var offences = new List<string>();
            foreach (var t in types)
                foreach (var (owner, member, type) in Members(t))
                    if (Strip(type) == typeof(Guid) && LooksLikeAnExerciseOrEpisodeId(member) && !NotAnExerciseOrEpisodeId.ContainsKey(owner + "." + member))
                        offences.Add($"{t.FullName}.{member} ({type.Name}) — an exercise id is an ExerciseId, an episode id an EpisodeId (or say what it is)");
            var sb = new StringBuilder();
            foreach (var o in offences.OrderBy(x => x, StringComparer.Ordinal)) sb.AppendLine(o);
            Assert.True(offences.Count == 0, $"{offences.Count} members look like an exercise / episode id but are a bare Guid:\n{sb}");
        }

        [Fact]
        public void S0_EveryNotAnExerciseOrEpisodeIdEntry_StillNamesAMember()
        {
            var live = new HashSet<string>(AllPublicNonWire().SelectMany(t => Members(t).Select(m => m.Owner + "." + m.Member)));
            var stale = NotAnExerciseOrEpisodeId.Keys.Where(k => !live.Contains(k)).ToList();
            Assert.True(stale.Count == 0, "NotAnExerciseOrEpisodeId entries name no member any more: " + string.Join(", ", stale));
        }

        /// <summary>The types — a Guid's memory, a plain JSON GUID string, implicit OUT, explicit IN; and the serialised recordings keep their JSON.</summary>
        [Fact]
        public void ExerciseId_And_EpisodeId_AreAGuidsMemory_AndAPlainJsonGuid_AndExplicitIn()
        {
            var g = Guid.Parse("11111111-2222-3333-4444-555555555555");
            Assert.Equal(16, System.Runtime.CompilerServices.Unsafe.SizeOf<ExerciseId>());
            Assert.Equal(16, System.Runtime.CompilerServices.Unsafe.SizeOf<EpisodeId>());
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(g), System.Text.Json.JsonSerializer.Serialize(new ExerciseId(g)));
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(g), System.Text.Json.JsonSerializer.Serialize(new EpisodeId(g)));
            Assert.Equal(new ExerciseId(g), System.Text.Json.JsonSerializer.Deserialize<ExerciseId>(System.Text.Json.JsonSerializer.Serialize(g)));
            Assert.Equal(new EpisodeId(g), System.Text.Json.JsonSerializer.Deserialize<EpisodeId>(System.Text.Json.JsonSerializer.Serialize(g)));
            Guid asGuid = new ExerciseId(g);                      // implicit out
            Assert.Equal(g, asGuid);
            Assert.Equal(new EpisodeId(g), (EpisodeId)g);         // explicit in
            Assert.Equal(g.ToString(), new ExerciseId(g).ToString());
            foreach (var t in new[] { typeof(ExerciseId), typeof(EpisodeId) })
            {
                var ops = t.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "op_Implicit").ToList();
                Assert.Single(ops);
                Assert.Equal(typeof(Guid), ops[0].ReturnType);
                var tc = System.ComponentModel.TypeDescriptor.GetConverter(t);
                Assert.Equal(g.ToString(), tc.ConvertToInvariantString(Activator.CreateInstance(t, g)));
            }
            // ⚠ recordings: RecordingMetadata's JSON is byte-identical whether its ids are typed or a bare Guid
            var meta = new Fdp.Core.FlightRecorder.Metadata.RecordingMetadata { ExerciseId = new ExerciseId(g), ParentExerciseId = new ExerciseId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")) };
            var json = System.Text.Json.JsonSerializer.Serialize(meta);
            Assert.Contains("\"" + g + "\"", json);
            Assert.Equal(g, (Guid)System.Text.Json.JsonSerializer.Deserialize<Fdp.Core.FlightRecorder.Metadata.RecordingMetadata>(json)!.ExerciseId);
            Assert.Equal("{\"EpisodeId\":\"" + g + "\"}", System.Text.Json.JsonSerializer.Serialize(new { EpisodeId = new EpisodeId(g) }));
            // the recorded component keeps its bytes: EpisodeTag is 16 bytes, the Guid's
            Assert.Equal(16, System.Runtime.CompilerServices.Unsafe.SizeOf<EpisodeTag>());
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
        // ⭐⭐ CE-4202 SV-4 — a RELATIVE angle says its unit by its TYPE: <see cref="Angle"/>, a float's memory and JSON, RADIANS. The world compass stays <see cref="Bearing"/>.
        // The scan keys on the NAME (a camel-case word Yaw / Pitch / Elevation / Azimuth / Roll, or a last word Angle / Rad) on a bare float, outside the DDS wire types.
        // A field whose last word is Deg / Degrees names its own unit and is skipped.
        // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary><c>Type.Member</c> → why it is NOT a relative angle in radians. ⭐ A decision each; never a silencer.</summary>
        internal static readonly Dictionary<string, string> NotARelativeAngle = new(StringComparer.Ordinal)
        {
            ["ManualDrive.Yaw"] = "an ABSOLUTE world yaw (radians) — a world direction, not a relative angle; the world-direction-in-radians type is a later decision",
            ["OperatorInput.Yaw"] = "the same absolute world yaw, as the control frame carries it to ManualDrive.Yaw",
            ["OperatorControlFrameEvent.Yaw"] = "the same absolute world yaw, on the control-frame event",
            ["AirFollower.OrbitAngle"] = "the polar angle of a plane's circle about OrbitCenter in the WORLD frame (atan2 of world offsets), not an offset from anything",
            ["DangerAreaDescriptor.AngleRad"] = "the OBB's WORLD yaw (radians) — an orientation of the footprint in the world",
            ["Solution.Elevation"] = "DEGREES above the horizontal (its doc says so) — a unit of its own, like the *Deg fields",
            ["VehicleKit.RestElevation"] = "radians relative, but a record parameter filled by the GENERATED VehicleBody.WeaponKits.g.cs with float literals — it changes with its generator",
            ["SoundDto.PitchJitter"] = "the pitch of a SOUND (a ratio), not an angle",
        };

        private static string[] Words(string name)
            => System.Text.RegularExpressions.Regex.Matches(name, "[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+").Select(m => m.Value).ToArray();

        private static bool LooksLikeARelativeAngle(string name)
        {
            var w = Words(name);
            if (w.Length == 0) return false;
            string last = w[^1];
            if (last is "Deg" or "Degrees" or "Sec" || w.Contains("Rate")) return false;   // a unit of its own, or an angular RATE
            return last is "Angle" or "Rad" || w.Any(x => x is "Yaw" or "Pitch" or "Elevation" or "Azimuth" or "Roll");
        }

        [Fact]
        public void S0_EveryRelativeAngleMember_IsAnAngle()
        {
            var types = AllPublicNonWire().ToList();
            Assert.True(types.Count >= 1000, $"only {types.Count} public types found — the scan lost its assemblies");
            var offences = new List<string>();
            foreach (var t in types)
                foreach (var (owner, member, type) in Members(t))
                    if (Strip(type) == typeof(float) && LooksLikeARelativeAngle(member) && !NotARelativeAngle.ContainsKey(owner + "." + member))
                        offences.Add($"{t.FullName}.{member} ({type.Name}) — a relative angle in radians is an Angle (or say what it is)");
            var sb = new StringBuilder();
            foreach (var o in offences.OrderBy(x => x, StringComparer.Ordinal)) sb.AppendLine(o);
            Assert.True(offences.Count == 0, $"{offences.Count} members look like a relative angle but are a bare float:\n{sb}");
        }

        [Fact]
        public void S0_EveryNotARelativeAngleEntry_StillNamesAMember()
        {
            var live = new HashSet<string>(AllPublicNonWire().SelectMany(t => Members(t).Select(m => m.Owner + "." + m.Member)));
            var stale = NotARelativeAngle.Keys.Where(k => !live.Contains(k)).ToList();
            Assert.True(stale.Count == 0, "NotARelativeAngle entries name no member any more: " + string.Join(", ", stale));
        }

        /// <summary>The type — a float's memory, a plain JSON number (radians), implicit OUT, explicit IN, shown in degrees.</summary>
        [Fact]
        public void Angle_IsAFloatsMemory_AndAPlainJsonNumber_AndExplicitIn_AndShownInDegrees()
        {
            Assert.Equal(4, System.Runtime.CompilerServices.Unsafe.SizeOf<Angle>());
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(0.5f), System.Text.Json.JsonSerializer.Serialize(new Angle(0.5f)));
            Assert.Equal(new Angle(0.25f), System.Text.Json.JsonSerializer.Deserialize<Angle>("0.25"));
            Assert.Equal("{\"Steer\":0.5}", System.Text.Json.JsonSerializer.Serialize(new { Steer = new Angle(0.5f) }));
            float asFloat = new Angle(1.5f);                      // implicit out
            Assert.Equal(1.5f, asFloat);
            Assert.Equal(new Angle(2f), (Angle)2f);               // explicit in
            Assert.Equal("90.0°", Angle.FromDegrees(90f).ToString());
            Assert.Equal(180f, new Angle(MathF.PI).Degrees, 3);
            Assert.Equal(new Angle(1f), new Angle(3f) - new Angle(2f));
            var ops = typeof(Angle).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "op_Implicit").ToList();
            Assert.Single(ops);
            Assert.Equal(typeof(float), ops[0].ReturnType);
            Assert.Equal("0.5", System.ComponentModel.TypeDescriptor.GetConverter(typeof(Angle)).ConvertToInvariantString(new Angle(0.5f)));
        }

        // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
        // ⭐⭐ CE-4201 SV-1 (first slice) — a POINT in sim time is a <see cref="SimInstant"/> (a double: a float resolves ~4 ms at 10 h), a length a <see cref="SimDuration"/>.
        // The scan: a bare FLOAT whose last camel-case word is At / Since / Until. (The double instants and the bare-float durations are the rest of SV-1.)
        // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary><c>Type.Member</c> → why it is NOT an instant in sim time. ⭐ A decision each; never a silencer.</summary>
        internal static readonly Dictionary<string, string> NotAnInstant = new(StringComparer.Ordinal)
        {
            ["TerrainOpening.At"] = "metres ALONG a wall panel (its doc says so), not a time",
            ["CivilianParams.AlarmAt"] = "a stress / attitude LEVEL (0..1) at which a civilian is alarmed — a threshold named *At, not a time",
            ["CivilianParams.PanicAt"] = "the stress level at which a civilian panics — a threshold, not a time",
            ["CivilianParams.RiotAt"] = "the attitude level at which a civilian riots — a threshold, not a time",
            ["CivilianParamsJsonDto.AlarmAt"] = "the authored form of CivilianParams.AlarmAt (a level)",
            ["CivilianParamsJsonDto.PanicAt"] = "the authored form of CivilianParams.PanicAt (a level)",
            ["CivilianParamsJsonDto.RiotAt"] = "the authored form of CivilianParams.RiotAt (a level)",
            ["Vars.StartedAt"] = "a variable of a GENERATED demo blueprint (SubordinateForEachDemo, authored with the blueprint scalar Flt); blueprint variables are the compiler's scalar set, not a component field",
            ["GlobalTime.TotalTime"] = "the SOURCE clock / a recording or clock-control value: stays double; a SimInstant is taken FROM it",
            ["GlobalTime.UnscaledTotalTime"] = "the SOURCE clock / a recording or clock-control value: stays double; a SimInstant is taken FROM it",
            ["FrameOuterHeader.SimTime"] = "the SOURCE clock / a recording or clock-control value: stays double; a SimInstant is taken FROM it",
            ["FrameMetadata.SimTime"] = "the SOURCE clock / a recording or clock-control value: stays double; a SimInstant is taken FROM it",
            ["CheckpointMark.TotalTime"] = "the SOURCE clock / a recording or clock-control value: stays double; a SimInstant is taken FROM it",
            ["CheckpointMark.UnscaledTotalTime"] = "the SOURCE clock / a recording or clock-control value: stays double; a SimInstant is taken FROM it",
            ["ClusterTimeObservation.ResumeSimTime"] = "the SOURCE clock / a recording or clock-control value: stays double; a SimInstant is taken FROM it",
            ["ClusterTimeObservation.SnapshotSimTime"] = "the SOURCE clock / a recording or clock-control value: stays double; a SimInstant is taken FROM it",
            ["ISimClock.TotalTime"] = "the SOURCE clock / a recording or clock-control value: stays double; a SimInstant is taken FROM it",
            ["WorldSimClock.TotalTime"] = "the SOURCE clock / a recording or clock-control value: stays double; a SimInstant is taken FROM it",
            ["AdvanceFrameIntent.TargetSimTime"] = "the SOURCE clock / a recording or clock-control value: stays double; a SimInstant is taken FROM it",
            ["JumpTimeIntent.TotalTime"] = "the SOURCE clock / a recording or clock-control value: stays double; a SimInstant is taken FROM it",
            ["TestStep.Time"] = "a step of an authored TEST SCRIPT (JSON), not runtime state",
            ["NetworkTransform.SimStamp"] = "WIRE stamp: the replicated sample's sim time, copied from the DDS double by the translator",
            ["FireAtPointParamsJsonDto.FireAt"] = "the authored (JSON) form of a params struct: kept a plain double until S-d proves the JSON path round-trips",
        };

        private static bool LooksLikeAnInstant(string name)
        {
            var w = Words(name);
            return w.Length > 0 && w[^1] is "At" or "Since" or "Until";
        }

        [Fact]
        public void S0_EveryAtSinceUntilMember_IsNotABareFloat()
        {
            var types = AllPublicNonWire().ToList();
            Assert.True(types.Count >= 1000, $"only {types.Count} public types found — the scan lost its assemblies");
            var offences = new List<string>();
            foreach (var t in types)
                foreach (var (owner, member, type) in Members(t))
                    if (Strip(type) == typeof(float) && LooksLikeAnInstant(member) && !NotAnInstant.ContainsKey(owner + "." + member))
                        offences.Add($"{t.FullName}.{member} ({type.Name}) — a point in sim time is a SimInstant (a float resolves ~4 ms at 10 h); say what it is if it is not one");
            var sb = new StringBuilder();
            foreach (var o in offences.OrderBy(x => x, StringComparer.Ordinal)) sb.AppendLine(o);
            Assert.True(offences.Count == 0, $"{offences.Count} members look like an instant but are a bare float:\n{sb}");
        }

        private static bool LooksLikeADoubleInstant(string name)
        {
            var w = Words(name);
            return w.Length > 0 && w[^1] is "At" or "Since" or "Until" or "Time";
        }

        /// <summary>⭐ CE-4201 SV-1 S-b — the DOUBLE half of the rail above: a point in sim time that is a bare <c>double</c> (outside the wire types and the allow-list) is a <see cref="SimInstant"/>.</summary>
        [Fact]
        public void S0_EveryAtSinceUntilTimeMember_IsNotABareDouble()
        {
            var types = AllPublicNonWire().ToList();
            Assert.True(types.Count >= 1000, $"only {types.Count} public types found — the scan lost its assemblies");
            var offences = new List<string>();
            foreach (var t in types)
                foreach (var (owner, member, type) in Members(t))
                    if (Strip(type) == typeof(double) && LooksLikeADoubleInstant(member) && !NotAnInstant.ContainsKey(owner + "." + member))
                        offences.Add($"{t.FullName}.{member} ({type.Name}) — a point in sim time is a SimInstant; say what it is if it is not one");
            var sb = new StringBuilder();
            foreach (var o in offences.OrderBy(x => x, StringComparer.Ordinal)) sb.AppendLine(o);
            Assert.True(offences.Count == 0, $"{offences.Count} members look like an instant but are a bare double:\n{sb}");
        }

        [Fact]
        public void S0_EveryNotAnInstantEntry_StillNamesAMember()
        {
            var live = new HashSet<string>(AllPublicNonWire().SelectMany(t => Members(t).Select(m => m.Owner + "." + m.Member)));
            var stale = NotAnInstant.Keys.Where(k => !live.Contains(k)).ToList();
            Assert.True(stale.Count == 0, "NotAnInstant entries name no member any more: " + string.Join(", ", stale));
        }

        /// <summary>SimInstant — a double's memory and plain JSON number, implicit OUT, explicit IN; instant − instant = duration; instant ± duration = instant.
        /// SimDuration — a float's memory and plain JSON number, implicit both ways.</summary>
        [Fact]
        public void SimInstant_AndSimDuration_AreTheirPrimitivesMemoryAndJson_WithTheRightArithmetic()
        {
            Assert.Equal(8, System.Runtime.CompilerServices.Unsafe.SizeOf<SimInstant>());
            Assert.Equal(4, System.Runtime.CompilerServices.Unsafe.SizeOf<SimDuration>());
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(36000.125d), System.Text.Json.JsonSerializer.Serialize(new SimInstant(36000.125d)));
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(1.5f), System.Text.Json.JsonSerializer.Serialize(new SimDuration(1.5f)));
            Assert.Equal(new SimInstant(36000.125d), System.Text.Json.JsonSerializer.Deserialize<SimInstant>("36000.125"));
            Assert.Equal(new SimDuration(2.5f), System.Text.Json.JsonSerializer.Deserialize<SimDuration>("2.5"));
            Assert.Equal("{\"T\":12.5,\"D\":3}", System.Text.Json.JsonSerializer.Serialize(new { T = new SimInstant(12.5), D = new SimDuration(3f) }));
            double asDouble = new SimInstant(7.25); Assert.Equal(7.25, asDouble);             // implicit out
            float asFloat = new SimDuration(0.5f); Assert.Equal(0.5f, asFloat);               // implicit out
            SimDuration fromFloat = 2f; Assert.Equal(new SimDuration(2f), fromFloat);          // implicit in (a duration is just seconds)
            Assert.Equal(new SimInstant(1d), (SimInstant)1d);                                  // explicit in
            SimDuration d = new SimInstant(100.5) - new SimInstant(100.0);
            Assert.Equal(new SimDuration(0.5f), d);
            Assert.Equal(new SimInstant(103d), new SimInstant(100d) + new SimDuration(3f));
            Assert.Equal(new SimInstant(97d), new SimInstant(100d) - new SimDuration(3f));
            Assert.True(new SimInstant(2d) > new SimInstant(1d));
            Assert.Equal("1 min 12 s", new SimDuration(72f).ToString());
            Assert.Equal("t=12.5 s", new SimInstant(12.5).ToString());
            // ⛔ instant + instant is an [Obsolete(error)] operator, not a quiet trip through double
            var add = typeof(SimInstant).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "op_Addition" && m.GetParameters()[1].ParameterType == typeof(SimInstant));
            Assert.True(add.GetCustomAttribute<ObsoleteAttribute>()!.IsError);
            // ⭐ no implicit conversion INTO an instant
            Assert.DoesNotContain(typeof(SimInstant).GetMethods(BindingFlags.Public | BindingFlags.Static), m => m.Name == "op_Implicit" && m.ReturnType == typeof(SimInstant));
            Assert.Equal("36000.5", System.ComponentModel.TypeDescriptor.GetConverter(typeof(SimInstant)).ConvertToInvariantString(new SimInstant(36000.5)));
            Assert.Equal("0.5", System.ComponentModel.TypeDescriptor.GetConverter(typeof(SimDuration)).ConvertToInvariantString(new SimDuration(0.5f)));
        }
    }
}
