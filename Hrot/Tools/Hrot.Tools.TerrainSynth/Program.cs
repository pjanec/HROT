namespace Hrot.Tools.TerrainSynth;

public static class Program
{
    public static int Main(string[] args)
    {
        SynthOptions options;
        try
        {
            options = SynthOptions.Parse(args);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine("usage: hrot-terrain-synth [--preset tiny|full] [--seed N] [--out DIR] [--size M] [--height-spacing M] [--buildings N] [--trees N]");
            return 2;
        }
        // TerrainSynthesizer.Run(options) arrives with the orchestration task.
        _ = options.Resolve();
        return 0;
    }
}
