namespace Hrot.Tools.TerrainOsm;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        return await OsmCli.RunAsync(args, Console.Out, Console.Error, cts.Token).ConfigureAwait(false);
    }
}
