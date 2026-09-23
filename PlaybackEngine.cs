using Android.OS;
using System;

namespace BimmerReader
{
    public class PlaybackEngine
    {
        private readonly ParsedLog _log;
        private readonly Handler _handler;
        private int _frame = 0;
        private bool _playing = false;
        private float _speed = 1f;

        public event Action<int, float[]>? FrameChanged;
        public event Action<bool>? PlayStateChanged;

        public int CurrentFrame => _frame;
        public int TotalFrames => _log.Frames.Count;
        public float Duration => _log.Duration;
        public float CurrentTime => _log.Frames.Count > 0 ? _log.Frames[_frame].Time : 0f;
        public bool IsPlaying => _playing;

        public PlaybackEngine(ParsedLog log)
        {
            _log = log;
            _handler = new Handler(Looper.MainLooper!);
        }

        public void Play()
        {
            if (_playing) return;
            if (_frame >= _log.Frames.Count - 1) _frame = 0;
            _playing = true;
            PlayStateChanged?.Invoke(true);
            ScheduleNext();
        }

        public void Pause()
        {
            if (!_playing) return;
            _playing = false;
            _handler.RemoveCallbacksAndMessages(null);
            PlayStateChanged?.Invoke(false);
        }

        public void SetSpeed(float speed)
        {
            _speed = Math.Max(0.25f, speed);
        }

        public void SeekToFrame(int frame)
        {
            frame = Math.Clamp(frame, 0, Math.Max(0, _log.Frames.Count - 1));
            _frame = frame;
            if (_log.Frames.Count > 0)
                FrameChanged?.Invoke(_frame, _log.Frames[_frame].Values);
        }

        public void Reset()
        {
            Pause();
            SeekToFrame(0);
        }

        public void Destroy()
        {
            _playing = false;
            _handler.RemoveCallbacksAndMessages(null);
        }

        private void ScheduleNext()
        {
            if (!_playing || _frame >= _log.Frames.Count - 1)
            {
                if (_frame >= _log.Frames.Count - 1)
                {
                    _playing = false;
                    PlayStateChanged?.Invoke(false);
                }
                return;
            }

            float timeDelta = _log.Frames[_frame + 1].Time - _log.Frames[_frame].Time;
            long delayMs = (long)(timeDelta * 1000f / _speed);
            if (delayMs < 0) delayMs = 0;

            _handler.PostDelayed(() =>
            {
                if (!_playing) return;
                _frame++;
                FrameChanged?.Invoke(_frame, _log.Frames[_frame].Values);
                ScheduleNext();
            }, delayMs);
        }
    }
}
