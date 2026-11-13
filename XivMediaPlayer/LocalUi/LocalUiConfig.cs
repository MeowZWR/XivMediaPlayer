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
        public float PlaybackEdgeAnchor { get; set; } = 0.6f;
        public bool PlaybackLightlessStyle { get; set; } = true;
        public bool RestoreGameBgmOnStop { get; set; }
        public bool ShowEdgeIconWhenScreenDetected { get; set; } = true;
        public bool ShowEdgePlaybackTitle { get; set; } = true;
        public bool HideEdgeTitleBackground { get; set; } = true;
        public const float DefaultEdgeTitleScrollSpeed = 1f;
        public const float DefaultEdgeTitleMarqueeLines = 10f;
        public const float DefaultEdgeIconScreenMargin = 6f;
        public const float DefaultEdgeTitleIconGap = 6f;
        public const float MaxEdgeIconScreenMargin = 64f;
        public const float MaxEdgeTitleIconGap = 48f;
        public float EdgeTitleScrollSpeed { get; set; } = DefaultEdgeTitleScrollSpeed;
        public float EdgeTitleMarqueeLines { get; set; } = DefaultEdgeTitleMarqueeLines;
        public float EdgeIconScreenMargin { get; set; } = DefaultEdgeIconScreenMargin;
        public float EdgeTitleIconGap { get; set; } = DefaultEdgeTitleIconGap;
        public bool KeepEdgeIconWithPanel { get; set; } = true;
        public bool ShowEdgeTransportPlayPause { get; set; } = true;
        public bool ShowEdgeTransportPrevious { get; set; }
        public bool ShowEdgeTransportNext { get; set; }
        public bool ShowEdgeTransportPaste { get; set; } = true;
        public bool ShowEdgeTransportLoop { get; set; }
        public bool ShowEdgeTransportLock { get; set; }
        public bool ShowEdgeTransportCrop { get; set; }
        public bool ShowEdgeTransportPlaybackSettings { get; set; }
        public bool ShowEdgeTransportHousing { get; set; }
        public bool ShowEdgeTransportPluginSettings { get; set; }
        public List<int> EdgeTransportOrder { get; set; } = EdgeTransportCatalog.DefaultOrder();
        public bool? CropToFill { get; set; }

        public static readonly JsonSerializerSettings JsonSettings = new()
        {
            Formatting = Formatting.Indented,
        };
    }
}
