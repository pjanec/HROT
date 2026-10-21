namespace Hrot.UI.Common.Facades;

/// <summary>
/// Abstraction that decouples the time-transport status-bar UI from the concrete time
/// controller (editor-local <see cref="Fdp.Toolkit.Time.Controllers.MasterSyncController"/>
/// vs. cluster bus intents).
///
/// <para>Implementors supply the current transport state as readable properties and
/// handle user actions via the mutating methods.</para>
/// </summary>
public interface ITimeTransportFacade
{
    /// <summary><c>true</c> when the Play/Pause button should be interactable.</summary>
    bool IsPlayPauseEnabled { get; }

    /// <summary><c>true</c> when the Step button should be interactable.</summary>
    bool IsStepEnabled { get; }

    /// <summary><c>true</c> when the Stop button should be interactable.</summary>
    bool IsStopEnabled { get; }

    /// <summary><c>true</c> when simulation time is currently frozen (paused).</summary>
    bool IsPaused { get; }

    /// <summary>Current simulation time in seconds.</summary>
    double TotalTime { get; }

    /// <summary>Current time-scale multiplier (e.g. 1.0 = real-time, 2.0 = 2x).</summary>
    float TimeScale { get; }

    /// <summary>Toggles between play and pause.</summary>
    void TogglePlayPause();

    /// <summary>Advances simulation by one fixed-size step (valid only when paused).</summary>
    void Step();

    /// <summary>
    /// Advances simulation by one step of <paramref name="seconds"/> (valid only when paused) — what
    /// <see cref="Step"/> does with 1/60 s. The film director's coarser rates (1/20, 1/15 s; `DESIGN_Film_Director.md` D-1).
    /// </summary>
    void StepBy(float seconds);

    /// <summary>Stops the simulation and returns to Idle.</summary>
    void Stop();

    /// <summary>Changes the time-scale multiplier.</summary>
    void SetTimeScale(float scale);
}
