using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐ rules ② (R4, 📄 docs/DESIGN_Scenario_Rules.md §00.11a) — marks a static <c>void Keep(ref TBlackboard fresh, in TBlackboard old)</c> as the STATE KEEPER of one
    /// behaviour: on a LIVE edit of the behaviour's params (<c>BehaviorIngressSystem.Reparameterise</c>) the new params are parsed into <c>fresh</c> exactly as a load
    /// would, then this copies the RUNTIME fields (the run, the clocks, the counters, the served sets) from the running blackboard <c>old</c> — a few lines, the
    /// field names written out. ⛔ Never a reflection-by-name copy. A behaviour WITHOUT a keeper refuses a live edit (L3).
    /// <para>Discovered beside the validators (<c>BehaviorSchemaDiscovery.BindKeepers</c>, called wherever <c>BindValidators</c> is): the NAME is the behaviour's registry name.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class BehaviorStateKeeperAttribute : Attribute
    {
        public string BehaviorName { get; }
        public BehaviorStateKeeperAttribute(string behaviorName) { BehaviorName = behaviorName; }
    }

    /// <summary>The keeper as the ingress calls it: over the raw bytes of the fresh (shadow) and the live params block.</summary>
    public delegate void StateKeeper(Span<byte> fresh, ReadOnlySpan<byte> old);

    /// <summary>The typed keeper a <see cref="BehaviorStateKeeperAttribute"/> method is.</summary>
    public delegate void KeepState<T>(ref T fresh, in T old) where T : unmanaged;

    /// <summary>⭐ R4 — binds a keeper method into a <see cref="StateKeeper"/> thunk (once, at discovery; ⛔ no reflection per call).</summary>
    public static class StateKeeperBinder
    {
        /// <summary>The thunk for <paramref name="m"/>, or throws when it is not <c>static void Keep(ref T fresh, in T old)</c> with an unmanaged <c>T</c>.</summary>
        public static StateKeeper Bind(MethodInfo m, string behaviorName)
        {
            var ps = m.GetParameters();
            bool shaped = m.IsStatic && m.ReturnType == typeof(void) && ps.Length == 2
                          && ps[0].ParameterType.IsByRef && ps[1].ParameterType.IsByRef && ps[1].IsIn
                          && ps[0].ParameterType == ps[1].ParameterType;
            if (!shaped)
                throw new InvalidOperationException($"[BehaviorStateKeeper(\"{behaviorName}\")] {m.DeclaringType?.FullName}.{m.Name} must be 'static void {m.Name}(ref TBlackboard fresh, in TBlackboard old)'.");
            var t = ps[0].ParameterType.GetElementType()!;
            return (StateKeeper)typeof(StateKeeperBinder).GetMethod(nameof(Make), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(t).Invoke(null, new object[] { m })!;
        }

        private static StateKeeper Make<T>(MethodInfo m) where T : unmanaged
        {
            var keep = (KeepState<T>)Delegate.CreateDelegate(typeof(KeepState<T>), m);
            return (fresh, old) =>
            {
                if (fresh.Length < Marshal.SizeOf<T>() || old.Length < Marshal.SizeOf<T>())
                    throw new InvalidOperationException($"the params block is smaller than {typeof(T).Name}");
                keep(ref MemoryMarshal.AsRef<T>(fresh), in MemoryMarshal.AsRef<T>(old));
            };
        }
    }
}
