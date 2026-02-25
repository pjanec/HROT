using Bagira.IG;

var app = new IgApplication();
app.Initialize();
try
{
    app.Run();
}
finally
{
    app.Shutdown();
}
