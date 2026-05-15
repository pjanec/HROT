using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.ReplayBrowser
{
    /// <summary>
    /// Maintains a back/forward navigation stack of playback frame indices.
    /// While GoBack/GoForward is executing, PushFrame calls are suppressed
    /// to prevent history pollution.
    /// </summary>
    public sealed class PlaybackHistoryTracker
    {
        private readonly List<int> _history = new();
        private int _currentIndex = -1;
        private bool _isNavigating;

        public event Action<int>? OnSeekRequested;

        public bool CanGoBack => _currentIndex > 0;
        public bool CanGoForward => _currentIndex < _history.Count - 1;

        public void PushFrame(int frameIndex)
        {
            if (_isNavigating) return;

            // Prevent duplicate consecutive frames
            if (_currentIndex >= 0 && _history[_currentIndex] == frameIndex) return;

            // Truncate forward history if diverging
            if (_currentIndex < _history.Count - 1)
            {
                _history.RemoveRange(_currentIndex + 1, _history.Count - (_currentIndex + 1));
            }

            _history.Add(frameIndex);
            _currentIndex++;
        }

        public void GoBack()
        {
            if (!CanGoBack) return;
            _isNavigating = true;
            _currentIndex--;
            OnSeekRequested?.Invoke(_history[_currentIndex]);
            _isNavigating = false;
        }

        public void GoForward()
        {
            if (!CanGoForward) return;
            _isNavigating = true;
            _currentIndex++;
            OnSeekRequested?.Invoke(_history[_currentIndex]);
            _isNavigating = false;
        }
    }
}
