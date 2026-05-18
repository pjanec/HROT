using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.ReplayBrowser
{
    public readonly record struct NavigationWaypoint(int FrameIndex, Entity SelectedEntity);

    /// <summary>
    /// Maintains a back/forward navigation stack of playback waypoints.
    /// While GoBack/GoForward is executing, push calls are suppressed
    /// to prevent history pollution.
    /// </summary>
    public sealed class PlaybackHistoryTracker
    {
        private readonly List<NavigationWaypoint> _history = new();
        private int _currentIndex = -1;
        private bool _isNavigating;

        public event Action<NavigationWaypoint>? OnWaypointRequested;

        public bool CanGoBack => _currentIndex > 0;
        public bool CanGoForward => _currentIndex < _history.Count - 1;

        public void PushWaypoint(int frameIndex, Entity selectedEntity)
            => PushWaypoint(new NavigationWaypoint(frameIndex, selectedEntity));

        public void PushWaypoint(NavigationWaypoint waypoint)
        {
            if (_isNavigating) return;

            // Prevent duplicate consecutive waypoints
            if (_currentIndex >= 0 && _history[_currentIndex].Equals(waypoint)) return;

            // Truncate forward history if diverging
            if (_currentIndex < _history.Count - 1)
            {
                _history.RemoveRange(_currentIndex + 1, _history.Count - (_currentIndex + 1));
            }

            _history.Add(waypoint);
            _currentIndex++;
        }

        public void GoBack()
        {
            if (!CanGoBack) return;
            _isNavigating = true;
            _currentIndex--;
            var waypoint = _history[_currentIndex];
            OnWaypointRequested?.Invoke(waypoint);
            _isNavigating = false;
        }

        public void GoForward()
        {
            if (!CanGoForward) return;
            _isNavigating = true;
            _currentIndex++;
            var waypoint = _history[_currentIndex];
            OnWaypointRequested?.Invoke(waypoint);
            _isNavigating = false;
        }

        public void Clear()
        {
            _history.Clear();
            _currentIndex = -1;
            _isNavigating = false;
        }
    }
}
