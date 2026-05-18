using Fdp.Core;

namespace Fdp.ModuleHost.Time
{
    public interface ISteppableTimeController : ITimeController
    {
        GlobalTime Step(float deltaTime);
    }
}
