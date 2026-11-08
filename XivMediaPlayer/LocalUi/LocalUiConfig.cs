using Newtonsoft.Json;
using System.Collections.Generic;

namespace XivMediaPlayer.LocalUi
{
    internal sealed class LocalUiConfig
    {
        public const string FileName = "local-ui.json";

        public List<string> LocalPlaylist { get; set; } = new();
        public bool LocalPlaybackMode { get; set; }
        public string LastPlayedUrl { get; set; } = "";
        public string LastPlayedTitle { get; set; } = "";
        public long LastPlayedTimecodeMs { get; set; }
        public int LastPlayedPlaylistIndex { get; set; } = -1;
        public bool LastPlayedIsLive { get; set; }
        public string LastSyncLocationKey { get; set; } = "";
        public List<RegisteredPlot> RegisteredPlots { get; set; } = new();
        public PlaybackScreenEdge PlaybackEdgeSide { get; set; } = PlaybackScreenEdge.Right;
        public float PlaybackEdgeAnchor { get; set; } = 0.42f;
        public bool PlaybackLightlessStyle { get; set; } = true;
        public bool ShowEdgeIconWhenScreenDetected { get; set; }

        public static readonly JsonSerializerSettings JsonSettings = new()
        {
            Formatting = Formatting.Indented,
        };
    }
}
