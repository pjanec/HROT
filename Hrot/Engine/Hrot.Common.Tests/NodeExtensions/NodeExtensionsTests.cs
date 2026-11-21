using System.Reflection;
using System.Text;
using Fdp.Core;
using Hrot.Common.Infrastructure;
using Hrot.Tests.ExtensionStaging;
using NX = Hrot.Common.Infrastructure.NodeExtensions;

namespace Hrot.Common.Tests.Extensions;

/// <summary>NodeExtensions has process-wide state: these tests run alone (and reset it).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NodeExtensionsCollection { public const string Name = "NodeExtensionsStatics"; }

/// <summary>
/// ⭐ CE-4293 (R-4158) — node extensions: loaded by path from <c>extensions/&lt;name&gt;/</c>, appended LAST, every refusal one report line.
/// 📄 docs/DESIGN_Node_Extensions.md §5 rails 1-3 and 6.
/// </summary>
[Collection(NodeExtensionsCollection.Name)]
public sealed class NodeExtensionsTests : IDisposable
{
    public NodeExtensionsTests() => NX.ResetForTests();
    public void Dispose() => NX.ResetForTests();

    private static readonly NodeExtensionContext Ctx = new(NodeRole.None, Offline: true, HostName: "UnitTest");

    private sealed class Cap : INodeCapability
    {
        public Cap(string key, IReadOnlyList<string>? needs = null, IReadOnlyList<FeatureDeclaration>? features = null)
        { Key = key; Needs = needs ?? Array.Empty<string>(); Features = features ?? Array.Empty<FeatureDeclaration>(); }
        public string Key { get; }
        public IReadOnlyList<string> Needs { get; }
        public IReadOnlyList<FeatureDeclaration> Features { get; }
    }

    public sealed class NullExt : INodeExtension { public string Name => "Null"; public INodeCapability? CreateCapability(NodeExtensionContext c) => null; }
    public sealed class ThrowingExt : INodeExtension { public string Name => "Throwing"; public INodeCapability? CreateCapability(NodeExtensionContext c) => throw new InvalidOperationException("boom"); }
    public sealed class NoCtorExt : INodeExtension { public NoCtorExt(int x) { } public string Name => "NoCtor"; public INodeCapability? CreateCapability(NodeExtensionContext c) => null; }
    public sealed class CtorThrowsExt : INodeExtension { public CtorThrowsExt() => throw new InvalidOperationException("ctor-boom"); public string Name => "CtorThrows"; public INodeCapability? CreateCapability(NodeExtensionContext c) => null; }
    public sealed class KeyedExt : INodeExtension { public string Name => "Keyed"; public INodeCapability? CreateCapability(NodeExtensionContext c) => new Cap("dup"); }
    public sealed class KeyedExt2 : INodeExtension { public string Name => "Keyed2"; public INodeCapability? CreateCapability(NodeExtensionContext c) => new Cap("dup"); }
    public sealed class NeedyExt : INodeExtension { public string Name => "Needy"; public INodeCapability? CreateCapability(NodeExtensionContext c) => new Cap("needy", new[] { "undeclared.resource" }); }
    public sealed class FeatureExt : INodeExtension
    {
        public string Name => "Featured";
        public INodeCapability? CreateCapability(NodeExtensionContext c)
            => new Cap("featured", features: new[] { FeatureDeclaration.Switch("clash.id", "x", FeatureChange.Live) });
    }

    private static IReadOnlyList<INodeCapability> Host(params string[] keys) => keys.Select(k => (INodeCapability)new Cap(k)).ToArray();

    // ── ① loaded by path, discovered, appended LAST ───────────────────────────────────────────────

    [Fact]
    public void AnExtensionFolder_IsLoaded_Discovered_AndItsCapabilityAppendedLast()
    {
        string baseDir = SampleExtensionStaging.SharedBaseDirectory;
        NX.LoadAll(baseDir);

        Assert.Empty(NX.Report);
        var found = Assert.Single(NX.Discovered);
        Assert.Equal("Sample", found.Name);
        Assert.Equal(Path.Combine(baseDir, "extensions", "sample"), Path.GetDirectoryName(found.GetType().Assembly.Location));   // by path, from ITS folder

        var host = Host("a", "b");
        var composed = NX.CapabilitiesFor(Ctx, host);

        Assert.Equal(3, composed.Count);
        Assert.Same(host[0], composed[0]);
        Assert.Same(host[1], composed[1]);
        Assert.Equal("sample.extension", composed[2].Key);   // LAST

        // The context the extension was asked with: role, offline, host name, and its own folder.
        var last = (NodeExtensionContext)found.GetType().GetField("LastContext")!.GetValue(null)!;
        Assert.Equal(Ctx.Role, last.Role);
        Assert.True(last.Offline);
        Assert.Equal("UnitTest", last.HostName);
        Assert.Equal(Path.Combine(baseDir, "extensions", "sample"), last.Folder);
    }

    [Fact]
    public void LoadAll_IsIdempotentAndThreadSafe_ForTheSameDirectory()
    {
        string baseDir = SampleExtensionStaging.SharedBaseDirectory;
        Parallel.For(0, 16, _ => NX.LoadAll(baseDir));
        NX.LoadAll(baseDir);

        Assert.Single(NX.Discovered);
        Assert.Empty(NX.Report);
    }

    [Fact]
    public void DeploymentAssembliesLoadAll_LoadsTheExtensionsToo()
    {
        DeploymentAssemblies.LoadAll(SampleExtensionStaging.SharedBaseDirectory);   // the process pre-load every runner calls
        Assert.Single(NX.Discovered);
    }

    [Fact]
    public void NoExtensionsFolder_IsNothing_NoError_AndTheHostListItself()
    {
        string empty = Path.Combine(Path.GetTempPath(), "hrot-ext-none-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        NX.LoadAll(empty);

        var host = Host("a", "b");
        Assert.Empty(NX.Report);
        Assert.Empty(NX.Discovered);
        Assert.Same(host, NX.CapabilitiesFor(Ctx, host));   // the SAME instance, not a copy
    }

    // ── ② null composes nothing ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ANullCapability_LeavesTheHostListIdentical()
    {
        NX.Add(typeof(NullExt), "");
        var host = Host("a", "b");

        var composed = NX.CapabilitiesFor(Ctx, host);

        Assert.Same(host, composed);
        Assert.Empty(NX.Report);
    }

    // ── ③ every refusal is one report line ────────────────────────────────────────────────────────

    [Fact]
    public void ABuildStampThatIsNotTheRunningHrotCommon_RefusesThatExtensionOnly_NamingBothVersions()
    {
        string running = NX.RunningVersion;
        Assert.Contains("+", running);   // the informational version carries the commit

        string baseDir = SampleExtensionStaging.Stage("stale");
        string dll = Path.Combine(baseDir, "extensions", "stale", "SampleNodeExtension.dll");
        byte[] bytes = File.ReadAllBytes(dll);
        byte[] from = Encoding.UTF8.GetBytes(running);
        byte[] to = Encoding.UTF8.GetBytes(running.Substring(0, running.Length - 1) + (running[^1] == '0' ? '1' : '0'));
        int replaced = 0;
        for (int i = 0; i + from.Length <= bytes.Length; i++)
            if (bytes.AsSpan(i, from.Length).SequenceEqual(from)) { to.CopyTo(bytes, i); replaced++; }
        Assert.True(replaced > 0, "the stamp must be in the staged dll");
        File.WriteAllBytes(dll, bytes);

        NX.LoadAll(baseDir);

        string line = Assert.Single(NX.Report);
        Assert.Contains("stale", line);
        Assert.Contains(Encoding.UTF8.GetString(to), line);
        Assert.Contains(running, line);
        Assert.Empty(NX.Discovered);
    }

    [Theory]
    [InlineData("Hrot.Common.dll")]
    [InlineData("Fdp.Core.dll")]
    [InlineData("CycloneDDS.Runtime.dll")]
    public void AShippedSharedAssembly_RefusesTheFolder_NamingTheFile(string fileName)
    {
        string dummy = Path.Combine(Path.GetTempPath(), "hrot-ext-dummy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dummy);
        string shipped = Path.Combine(dummy, fileName);
        File.Copy(typeof(NX).Assembly.Location, shipped);   // content is irrelevant: the NAME is refused before anything is read
        string baseDir = SampleExtensionStaging.Stage("shipper", shipped);

        NX.LoadAll(baseDir);

        string line = Assert.Single(NX.Report);
        Assert.Contains(fileName, line);
        Assert.Contains("shipper", line);
        Assert.Empty(NX.Discovered);
    }

    [Fact]
    public void AFolderWithoutAStampedAssembly_IsRefused()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "hrot-ext-unstamped-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(baseDir, "extensions", "plain");
        Directory.CreateDirectory(folder);
        File.Copy(typeof(NodeExtensionsTests).Assembly.Location, Path.Combine(folder, "SomethingElse.dll"));   // managed, but no BuiltAgainstHrot

        NX.LoadAll(baseDir);

        Assert.Contains("BuiltAgainstHrot", Assert.Single(NX.Report));
    }

    [Fact]
    public void ATypeWithoutAPublicParameterlessConstructor_IsReported()
    {
        NX.Add(typeof(NoCtorExt), "");
        Assert.Contains(nameof(NoCtorExt), Assert.Single(NX.Report));
        Assert.Empty(NX.Discovered);
    }

    [Fact]
    public void AThrowingConstructor_IsReported()
    {
        NX.Add(typeof(CtorThrowsExt), "");
        string line = Assert.Single(NX.Report);
        Assert.Contains("ctor-boom", line);
    }

    [Fact]
    public void AThrowingCreateCapability_IsReported_NotSwallowed_AndTheListIsUnchanged()
    {
        NX.Add(typeof(ThrowingExt), "");
        var host = Host("a");

        var composed = NX.CapabilitiesFor(Ctx, host);

        Assert.Same(host, composed);
        string line = Assert.Single(NX.Report);
        Assert.Contains("Throwing", line);
        Assert.Contains("boom", line);
    }

    [Fact]
    public void ACapabilityKeyAlreadyOnTheNode_IsRefused_AgainstTheHostAndAgainstAnotherExtension()
    {
        NX.Add(typeof(KeyedExt), "");
        NX.Add(typeof(KeyedExt2), "");

        var againstHost = NX.CapabilitiesFor(Ctx, Host("dup"));
        Assert.Single(againstHost);   // neither is added
        Assert.Equal(2, NX.Report.Count);

        NX.ResetForTests();
        NX.Add(typeof(KeyedExt), "");
        NX.Add(typeof(KeyedExt2), "");
        var composed = NX.CapabilitiesFor(Ctx, Host("a"));
        Assert.Equal(2, composed.Count);                      // the first extension's, not the second's
        Assert.Contains("Keyed2", Assert.Single(NX.Report));
    }

    [Fact]
    public void ANeedTheRoleDoesNotDeclare_AndAFeatureIdAlreadyDeclared_AreRefused()
    {
        NX.Add(typeof(NeedyExt), "");
        NX.Add(typeof(FeatureExt), "");
        var host = new INodeCapability[] { new Cap("h", features: new[] { FeatureDeclaration.Switch("clash.id", "x", FeatureChange.Live) }) };

        var composed = NX.CapabilitiesFor(Ctx, host, declaredResources: Array.Empty<string>());

        Assert.Same(host, composed);
        Assert.Equal(2, NX.Report.Count);
        Assert.Contains(NX.Report, l => l.Contains("undeclared.resource"));
        Assert.Contains(NX.Report, l => l.Contains("clash.id"));
    }

    // ⑥ (design §5) — "a dll copied beside the binaries is not found by name" is a fact of the plain app host (measured: a framework-dependent console
    //   app answers FileNotFoundException for Assembly.Load("SampleNodeExtension") with the dll next to it). It cannot be a rail HERE: the test host installs a
    //   resolver for the test folder (the same probe finds the file), so the rail would assert the test host, not HROT. See the design's as-built notes.
}
