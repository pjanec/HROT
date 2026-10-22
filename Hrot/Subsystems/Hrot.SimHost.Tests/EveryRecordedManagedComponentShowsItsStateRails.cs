using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Fdp.Core;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐ <c>CE-3305</c> — <b>a managed component the flight recorder writes keeps no state the recorder cannot see.</b> The recorder's
/// serializer (<c>FdpAutoSerializer</c>) writes public fields, public settable properties and <see cref="RecordedMemberAttribute"/>
/// fields. 📐 Measured with a dry-run keyframe on <c>uc-urban-battle</c>: a route behind a read-only view (<c>RoutePlan</c>) came back
/// EMPTY, and the same held for every get-only collection (<c>ChildMap</c>, <c>PendingAuthorityGrants</c>, <c>SopStartRecord.HostedKeys</c>)
/// — lost from every replay, checkpoint and dry run, silently.
/// <para>A type the recorder never writes (<c>NoReplay</c>) is not checked. A managed SINGLETON is never recorded (it carries no table
/// version — restore design D7), so it is exempt with that reason; anything else is exempt only with its own.</para>
/// </summary>
public sealed class EveryRecordedManagedComponentShowsItsStateRails
{
    private static readonly Dictionary<string, string> Exempt = new()
    {
        ["Fdp.Toolkit.Perception.Signatures.SoundEmissionRing"] = "a managed singleton — never recorded",
        ["Fdp.Toolkit.Replication.ConstructionResults"]        = "a managed singleton — never recorded",
        ["Fdp.Toolkit.Replication.Services.BlockIdManager"]    = "a managed singleton — never recorded",
        ["Hrot.Common.Orders.OrderDesignations"]               = "a managed singleton — never recorded",
    };

    [Fact]
    public void NoRecordedManagedComponent_HidesStateFromTheRecorder()
    {
        foreach (var f in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
        {
            var n = Path.GetFileName(f);
            if (n.StartsWith("Fdp.") || n.StartsWith("Hrot.")) { try { Assembly.LoadFrom(f); } catch { } }
        }

        var offenders = new List<string>();
        var seen = new HashSet<string>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name!.StartsWith("Fdp.") || a.GetName().Name!.StartsWith("Hrot.")))
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
            foreach (var t in types)
            {
                if (!t.IsClass || t.GetCustomAttribute<ComponentIdAttribute>() == null) continue;
                if (t.GetCustomAttribute<DataPolicyAttribute>() is { } p && p.Policy.HasFlag(DataPolicy.NoReplay)) continue;
                var hidden = t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(fi => fi.GetCustomAttribute<RecordedMemberAttribute>() == null)
                    .Where(fi => !fi.Name.Contains("k__BackingField") || !IsSettableAutoProperty(t, fi))
                    .Select(fi => fi.Name).ToList();
                if (hidden.Count == 0) continue;
                seen.Add(t.FullName!);
                if (!Exempt.ContainsKey(t.FullName!))
                    offenders.Add($"{t.FullName}: {string.Join(", ", hidden)} — the recorder cannot see it; make it a public settable member, " +
                                  "a `{ get; private set; }` property or a [RecordedMember] field — or exempt it with its reason");
            }
        }
        foreach (var stale in Exempt.Keys.Where(k => !seen.Contains(k)))
            offenders.Add($"{stale}: exempt but hides nothing (or is gone) — remove the exemption");
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    private static bool IsSettableAutoProperty(Type t, FieldInfo backing)
    {
        var name = backing.Name.TrimStart('<').Split('>')[0];
        return t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { CanRead: true, CanWrite: true };
    }
}
