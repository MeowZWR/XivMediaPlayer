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
        public bool RestoreGameBgmOnStop { get; set; }
        public bool ShowEdgeIconWhenScreenDetected { get; set; } = true;
        public bool ShowEdgePlaybackTitle { get; set; } = true;
        public bool HideEdgeTitleBackground { get; set; } = true;
        public float EdgeTitleScrollSpeed { get; set; } = 1f;
        public float EdgeTitleMarqueeLines { get; set; } = 8f;
        public bool KeepEdgeIconWithPanel { get; set; } = true;

        public static readonly JsonSerializerSettings JsonSettings = new()
        {
            Formatting = Formatting.Indented,
        };
    }
}
