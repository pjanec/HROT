using Fdp.Presentation.Raylib;
using Hrot.FakeStrideApp;

// Default values; override with --domain <id> --node <id>
int domainId = 0;
int nodeId   = 700;

for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--domain") int.TryParse(args[i + 1], out domainId);
    if (args[i] == "--node")   int.TryParse(args[i + 1], out nodeId);
}

var appConfig = new ApplicationConfig
{
    WindowTitle = "FakeStrideApp -- HROT Stride Mock",
    Width       = 1280,
    Height      = 720,
    TargetFPS   = 60,
};

using var app = new FakeStrideApp(appConfig, domainId, nodeId);
app.Run();
