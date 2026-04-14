using Fdp.Kernel;

namespace Fdp.ModuleHost.Time
{
    public interface ISteppableTimeController : ITimeController
    {
        GlobalTime Step(float deltaTime);
    }
}
