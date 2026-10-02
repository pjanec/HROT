namespace Hrot.SystemTests.Goldens;

/// <summary>
/// ⭐⭐⭐ <b>A FRESH editor with the scenario loaded EXACTLY ONCE — the only state a golden may be captured
/// from.</b>
/// 📄 <c>docs/DESIGN_Regression_Net.md</c> §6 *(the capture contract)* · <c>HN-011</c>.
///
/// <para>⛔⛔ <b>WHY THE SHARED <see cref="EditorProcessFixture"/> CANNOT BE USED HERE, and it is a measured
/// reason, not a preference.</b> 📐 <c>HN-011</c>: loading <c>hill-attack</c> a SECOND time into one editor
/// leaves entity <c>1000</c> carrying <c>BlueprintAssignments</c> that a first load does not — the world is
/// not fully cleared *(and it is not a settle race: 5 ticks and 40 ticks agree)*. ⇒ ⭐⭐⭐ <b>a golden captured
/// after a reload BAKES THE DEFECT IN</b>, and the day the loader is fixed every golden reddens for a reason
/// that has nothing to do with the change under test. ⛔ That is how a net loses its authority.</para>
///
/// <para>⭐⭐ So: one process this class owns, <b>one</b> <c>POST /scenario/load</c>, and cases only switch
/// perspective afterwards. ⚠ <c>parallelizeTestCollections</c> is <c>false</c> in
/// <c>xunit.runner.json</c>, so this editor does not run alongside the collection's shared one.</para>
///
/// <para>⭐ The scenario is a constructor-time constant rather than a parameter: a fixture that could load
/// *different* scenarios would need to reload, which is the thing this exists to prevent.</para>
/// </summary>
public sealed class GoldenCaptureFixture : IAsyncLifetime
{
    /// <summary>The curated world every panel golden is captured from. 📌 <c>D7</c>'s worked example uses it too.</summary>
    public const string Scenario = "hill-attack";

    private EditorProcess? _editor;

    /// <summary>The driver. Valid after <see cref="InitializeAsync"/>.</summary>
    public McpClient Client { get; private set; } = null!;

    /// <summary>⚠ False when the host cannot run system tests — every case skips itself, as elsewhere in this suite.</summary>
    public bool Ready { get; private set; }

    public async Task InitializeAsync()
    {
        if (SystemTestEnvironment.SkipReason is { } reason)
        {
            Client = new McpClient(new Uri("http://localhost:0/"));
            Console.WriteLine($"[SystemTests] golden fixture idle: {reason}");
            return;
        }

        _editor = await EditorProcess.StartAsync("golden").ConfigureAwait(false);
        Client = _editor.Client;

        // ⭐ THE FIRST AND ONLY LOAD. See the class remarks — HN-011 makes this load-count part of the contract.
        (await Client.LoadScenarioEditAsync(Scenario).ConfigureAwait(false)).EnsureOk();
        Ready = true;
    }

    public async Task DisposeAsync()
    {
        if (_editor is not null)
        {
            await _editor.DisposeAsync().ConfigureAwait(false);
            _editor = null;
        }
        else Client?.Dispose();
    }
}
