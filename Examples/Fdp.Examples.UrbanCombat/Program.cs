using Fdp.Examples.UrbanCombat;

// Entry point for the Urban Ambush headless demo application.
// Constructs the app, runs the 600-frame (10-second at 60 Hz) simulation, then exits.
var app = new HeadlessDemoApp();
app.Initialize();
app.Run();
