using NAudio.Wave;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;

namespace MediaPlayerCore {
  public class MediaManager : IDisposable {
    byte[] _lastFrame = Array.Empty<byte>();
    private bool _invalidated = false;
    private ConcurrentDictionary<string, MediaObject> _playbackStreams = new ConcurrentDictionary<string, MediaObject>();
    private List<MediaObject> _deadStreams = new List<MediaObject>();
    private FFmpegMediaObject _ffmpegStream;

    public event EventHandler<MediaError> OnErrorReceived;
    public event EventHandler OnCleanupTime;
    public event EventHandler<string> OnPlaybackFinished;
    private IMediaGameObject _mainPlayer = null;
    private IMediaGameObject _camera;
    private string _libVLCPath;
    private Task _updateLoop;
    private bool notDisposed = true;
    private float _livestreamVolume = 1;
    private float _cameraAndPlayerPositionSlider;
    // Serialize all LibVLC Play/Stop/Dispose to avoid concurrent Quit/event_detach crashes.
    private readonly SemaphoreSlim _vlcGate = new SemaphoreSlim(1, 1);

    public float LiveStreamVolume { get => _livestreamVolume; set => _livestreamVolume = value; }
    public byte[] LastFrame { get => _lastFrame; set => _lastFrame = value; }
    public object FrameLock { get; } = new object();
    public ulong LastFrameCount { get; set; } = 0;
    public int LastFrameWidth { get; set; } = 0;
    public int LastFrameHeight { get; set; } = 0;
    public int LastFrameTrueWidth { get; set; } = 0;
    public int LastFrameTrueHeight { get; set; } = 0;
    public bool Invalidated { get => _invalidated; set => _invalidated = value; }
    
    public MediaObject? ActiveStream {
      get {
        var stream = _playbackStreams.Values.FirstOrDefault();
        return stream;
      }
    }

    public event EventHandler OnNewMediaTriggered;

    public MediaManager(IMediaGameObject playerObject, IMediaGameObject camera, string libVLCPath) {
      _mainPlayer = playerObject;
      _camera = camera;
      _libVLCPath = libVLCPath;
      _updateLoop = Task.Run(() => Update());
    }

    public void PlayStream(IMediaGameObject playerObject, string audioPath, bool spatialAllowed, int startTimeMs = 0, Dictionary<string, string>? httpHeaders = null, bool audioOnly = false, string? slaveAudioPath = null) {
      Task.Run(async () => {
        await _vlcGate.WaitAsync().ConfigureAwait(false);
        try {
          if (!notDisposed) return;
          if (!audioOnly) {
              StopFFmpegStream();
          }
          OnNewMediaTriggered?.Invoke(this, EventArgs.Empty);
          if (!string.IsNullOrEmpty(audioPath)) {
            await ConfigureStreamAsync(playerObject, audioPath, spatialAllowed, startTimeMs, httpHeaders, audioOnly, slaveAudioPath).ConfigureAwait(false);
          }
        } catch (Exception e) {
          OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
        } finally {
          try { _vlcGate.Release(); } catch { }
        }
      });
    }

    public long Length {
      get {
        if (ActiveStream != null) {
          return ActiveStream.Length;
        }
        return 0;
      }
    }

    public bool IsFFmpegPlaying => _ffmpegStream != null && _ffmpegStream.IsPlaying;

    public void PlayFFmpegStream(string url, IMediaGameObject characterObject = null, bool spatialAllowed = false) {
        Task.Run(async () => {
            await _vlcGate.WaitAsync().ConfigureAwait(false);
            try {
                if (!notDisposed) return;
                StopFFmpegStream();

                // Stop all VLC streams synchronously to prevent them from playing concurrently
                MediaObject[] streams;
                lock (_playbackStreams) {
                    streams = _playbackStreams.Values.ToArray();
                    _playbackStreams.Clear();
                    streams = streams.Concat(_deadStreams).ToArray();
                    _deadStreams.Clear();
                }
                foreach (var stream in streams) {
                    try { stream?.Dispose(); } catch { }
                }

                OnNewMediaTriggered?.Invoke(this, EventArgs.Empty);
                
                // _libVLCPath is e.g. ConfigDir/Dependencies
                string ffmpegPath = Path.Combine(_libVLCPath, "ffmpeg.exe");

                _ffmpegStream = new FFmpegMediaObject(this, ffmpegPath);
                _ffmpegStream.OnErrorReceived += MediaManager_OnErrorReceived;
                _ffmpegStream.PlaybackStopped += FFmpegStream_PlaybackStopped;
                _ffmpegStream.Play(url, characterObject, spatialAllowed);
            } catch (Exception e) {
                OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
            } finally {
                try { _vlcGate.Release(); } catch { }
            }
        });
    }

    private void FFmpegStream_PlaybackStopped(object? sender, string e) {
        OnPlaybackFinished?.Invoke(this, "Emulation");
    }

    private void StopFFmpegStream() {
        if (_ffmpegStream != null) {
            _ffmpegStream.OnErrorReceived -= MediaManager_OnErrorReceived;
            _ffmpegStream.PlaybackStopped -= FFmpegStream_PlaybackStopped;
            try { _ffmpegStream.Dispose(); } catch { }
            _ffmpegStream = null;
        }
    }

    public void ChangeStream(IMediaGameObject playerObject, string audioPath, float width, string? slaveAudioPath = null) {
      bool spatial = ActiveStream?.SpatialAllowed ?? true;
      PlayStream(playerObject, audioPath, spatial, startTimeMs: 0, httpHeaders: null, audioOnly: false, slaveAudioPath: slaveAudioPath);
    }

    public void StopStream() {
      Task.Run(async () => {
        await _vlcGate.WaitAsync().ConfigureAwait(false);
        try {
          MediaObject[] streams;
          lock (_playbackStreams) {
              streams = _playbackStreams.Values.ToArray();
              _playbackStreams.Clear();
              streams = streams.Concat(_deadStreams).ToArray();
              _deadStreams.Clear();
          }
          StopFFmpegStream();
          foreach (var stream in streams) {
            try {
              stream?.Dispose();
            } catch { }
          }
        } catch (Exception e) {
          OnErrorReceived?.Invoke(this, new MediaError() { Exception = e });
        } finally {
          try { _vlcGate.Release(); } catch { }
        }
      });
    }

    public bool IsAllowedToStartStream(IMediaGameObject playerObject) {
      if (_playbackStreams.ContainsKey(playerObject.Name)) {
        return true;
      } else {
        if (_playbackStreams.Count == 0) {
          return true;
        } else {
          foreach (string key in _playbackStreams.Keys) {
            bool noStream = _playbackStreams[key].PlaybackState == PlaybackState.Stopped;
            return noStream;
          }
        }
      }
      return false;
    }

    private async Task ConfigureStreamAsync(IMediaGameObject playerObject, string audioPath, bool spatialAllowed, int startTimeMs, Dictionary<string, string>? httpHeaders = null, bool audioOnly = false, string? slaveAudioPath = null) {
      if (playerObject == null) return;

      MediaObject[] toDispose;
      lock (_playbackStreams) {
          toDispose = _playbackStreams.Values.ToArray();
          _playbackStreams.Clear();
          toDispose = toDispose.Concat(_deadStreams).ToArray();
          _deadStreams.Clear();
      }

      foreach (var old in toDispose) {
          try { old?.Dispose(); } catch { }
      }

      var stream = new MediaObject(this, playerObject, _camera, SoundType.Livestream, audioPath, _libVLCPath, spatialAllowed, audioOnly);
      lock (_playbackStreams) {
          if (!notDisposed) {
              try { stream.Dispose(); } catch { }
              return;
          }
          _playbackStreams[playerObject.Name] = stream;
      }

      stream.OnErrorReceived += MediaManager_OnErrorReceived;
      stream.PlaybackFinished += (s, e) => {
         OnPlaybackFinished?.Invoke(this, e);
      };
      await stream.PlayAsync(audioPath, _livestreamVolume, startTimeMs, httpHeaders, slaveAudioPath).ConfigureAwait(false);
    }

    private void Update() {
      while (notDisposed) {
        try {
          UpdateVolumes(_playbackStreams);
        } catch { }
        Thread.Sleep(100);
      }
    }

    public void UpdateVolumes(ConcurrentDictionary<string, MediaObject> sounds) {
      for (int i = 0; i < sounds.Count; i++) {
        lock (sounds) {
          try {
            string characterObjectName = sounds.Keys.ElementAt<string>(i);
            if (sounds.ContainsKey(characterObjectName)) {
              try {
                lock (sounds[characterObjectName]) {
                    if (sounds[characterObjectName].SpatialAllowed) {
                      if (sounds[characterObjectName].CharacterObject != null) {
                        Vector3 dir = new Vector3();
                        if (sounds[characterObjectName].CharacterObject.Position.Length() > 0) {
                          dir = Vector3.Normalize(sounds[characterObjectName].CharacterObject.Position - GetListeningPosition());
                        } else {
                          dir = Vector3.Normalize(_mainPlayer.Position - GetListeningPosition());
                        }
                        float direction = AngleDir(_camera.Forward, dir, _camera.Top);
                        try {
                          sounds[characterObjectName].Pan = direction;
                          sounds[characterObjectName].Volume = CalculateObjectVolume(characterObjectName, sounds[characterObjectName]);
                        } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
                      }
                    } else {
                      sounds[characterObjectName].Pan = 0f;
                      sounds[characterObjectName].Volume = _livestreamVolume;
                    }
                }
              } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
            }
          } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
        }
      }

      if (_ffmpegStream != null && _ffmpegStream.SpatialAllowed && _ffmpegStream.CharacterObject != null) {
          try {
              Vector3 dir = new Vector3();
              if (_ffmpegStream.CharacterObject.Position.Length() > 0) {
                dir = Vector3.Normalize(_ffmpegStream.CharacterObject.Position - GetListeningPosition());
              } else {
                dir = Vector3.Normalize(_mainPlayer.Position - GetListeningPosition());
              }
              float direction = AngleDir(_camera.Forward, dir, _camera.Top);
              _ffmpegStream.Pan = direction;
              
              // Copy of CalculateObjectVolume logic for FFmpeg stream
              float maxDistance = 100;
              float volume = _livestreamVolume;
              float distance = Vector3.Distance(GetListeningPosition(), _ffmpegStream.CharacterObject.Position);
              float attenuation = Math.Clamp((maxDistance - distance) / maxDistance, 0f, 1f);
              float exponentialAttenuation = (float)Math.Pow(attenuation, 2.0);
              _ffmpegStream.Volume = Math.Clamp(volume * exponentialAttenuation, 0f, 1f);
          } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
      }
    }

    Vector3 GetListeningPosition() {
      return Vector3.Lerp(new Vector3(_camera.Position.X, _mainPlayer.Position.Y, _camera.Position.Z), _mainPlayer.Position, _cameraAndPlayerPositionSlider);
    }

    public float CalculateObjectVolume(string playerName, MediaObject mediaObject) {
      float maxDistance = 100;
      float volume = _livestreamVolume;
      float distance = Vector3.Distance(GetListeningPosition(), mediaObject.CharacterObject.Position);
      
      // Calculate linear attenuation
      float attenuation = Math.Clamp((maxDistance - distance) / maxDistance, 0f, 1f);
      
      // Apply an exponential curve to make the volume drop off more naturally over distance
      float exponentialAttenuation = (float)Math.Pow(attenuation, 2.0);
      
      return Math.Clamp(volume * exponentialAttenuation, 0f, 1f);
    }

    public float AngleDir(Vector3 fwd, Vector3 targetDir, Vector3 up) {
      Vector3 perp = Vector3.Cross(fwd, targetDir);
      float dir = Vector3.Dot(perp, up);
      return dir;
    }

    private void MediaManager_OnErrorReceived(object? sender, MediaError e) {
      OnErrorReceived?.Invoke(this, new MediaError() { Exception = e.Exception });
    }

    public void CleanSounds() {
      try {
        _vlcGate.Wait();
        try {
            lock (_playbackStreams) {
                var allStreamsToDispose = _playbackStreams.Values.Concat(_deadStreams).ToArray();
                foreach (var sound in allStreamsToDispose) {
                  if (sound != null) {
                    sound.Invalidated = true;
                    sound.Dispose();
                    sound.OnErrorReceived -= MediaManager_OnErrorReceived;
                  }
                }
                _playbackStreams?.Clear();
                _deadStreams.Clear();
            }
            lock (FrameLock) {
              _lastFrame = Array.Empty<byte>();
              LastFrameWidth = 0;
              LastFrameHeight = 0;
              LastFrameTrueWidth = 0;
              LastFrameTrueHeight = 0;
              LastFrameCount++;
            }
            StopFFmpegStream();
            OnCleanupTime?.Invoke(this, EventArgs.Empty);
        } finally {
            try { _vlcGate.Release(); } catch { }
        }
      } catch (Exception e) { OnErrorReceived?.Invoke(this, new MediaError() { Exception = e }); }
    }

    public void Dispose() {
      notDisposed = false;
      CleanSounds();
      try {
        _updateLoop?.Wait(TimeSpan.FromSeconds(2));
      } catch { }
      try { _vlcGate.Dispose(); } catch { }
    }
  }
}
