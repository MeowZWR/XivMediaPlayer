using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Threading.Tasks;

namespace XivMediaPlayer.Networking
{
    public class SyncClient : IDisposable
    {
        private HubConnection _connection;
        private MediaPlayerCore.MediaManager _mediaManager;
        private string _currentRoom;
        private bool _isHost = false;

        public bool IsConnected => _connection?.State == HubConnectionState.Connected;

        public SyncClient(MediaPlayerCore.MediaManager mediaManager)
        {
            _mediaManager = mediaManager;
        }

        public async Task ConnectAsync(string serverUrl, string roomCode, bool isHost)
        {
            _currentRoom = roomCode;
            _isHost = isHost;

            _connection = new HubConnectionBuilder()
                .WithUrl(serverUrl)
                .WithAutomaticReconnect()
                .Build();

            _connection.On<string, long, bool, DateTime>("ReceiveState", (mediaUrl, time, isPlaying, lastUpdateUtc) =>
            {
                if (_isHost) return; // Host dictates state, doesn't receive it (unless transferring host)

                var activeStream = _mediaManager.ActiveStream;
                if (activeStream == null) return;

                // Sync play/pause
                bool localPlaying = activeStream.PlaybackState == NAudio.Wave.PlaybackState.Playing;
                if (isPlaying && !localPlaying)
                {
                    // Assuming LibVLC's Play() is exposed or Pause() toggles
                    activeStream.Pause(); // Simple toggle if it was paused
                }
                else if (!isPlaying && localPlaying)
                {
                    activeStream.Pause();
                }

                // Sync time if drifted more than 1.5 seconds
                // Calculate projected time based on server's last update
                long projectedTime = time;
                if (isPlaying)
                {
                    projectedTime += (long)(DateTime.UtcNow - lastUpdateUtc).TotalMilliseconds;
                }

                if (Math.Abs(activeStream.Time - projectedTime) > 1500)
                {
                    activeStream.Time = projectedTime;
                }
            });

            await _connection.StartAsync();
            await _connection.InvokeAsync("JoinRoom", roomCode);
        }

        public async Task BroadcastStateAsync()
        {
            if (!IsConnected || !_isHost || _currentRoom == null) return;

            var activeStream = _mediaManager.ActiveStream;
            if (activeStream != null)
            {
                bool isPlaying = activeStream.PlaybackState == NAudio.Wave.PlaybackState.Playing;
                await _connection.InvokeAsync("UpdateState", _currentRoom, "", activeStream.Time, isPlaying);
            }
        }

        public void Dispose()
        {
            _connection?.DisposeAsync();
        }
    }
}
