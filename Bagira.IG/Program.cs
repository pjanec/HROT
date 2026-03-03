using Bagira.IG;

// ── CLI: --domain / -d ────────────────────────────────────────────────────
// Priority: CLI arg > config.json (if present) > IgNetworkConstants.DdsDomain (0)
int domainId = 0;
for (int i = 0; i < args.Length - 1; i++)
{
    if ((args[i] == "--domain" || args[i] == "-d") && int.TryParse(args[i + 1], out int d))
    {
        domainId = d;
        break;
    }
}

Console.WriteLine($"[IG] Starting — domain={domainId}");

var app = new IgApplication();
app.Initialize(domainId != 0 ? domainId : null);
try
{
    app.Run();
}
finally
{
    app.Shutdown();
}
