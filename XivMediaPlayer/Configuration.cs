using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using XivMediaPlayer.LocalUi;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer {
  public enum ConfigSaveResult {
    Saved,
    SkippedInProgress,
    Failed,
  }

  [Serializable]
  public class RoomMediaState {
      public string CurrentUrl { get; set; } = "";
      public long TimecodeMs { get; set; } = 0;
      public List<string> Playlist { get; set; } = new List<string>();
  }

  [Serializable]
  public class MediaHistoryEntry {
      public string Url { get; set; } = "";
      public string Title { get; set; } = "";
      public long TimecodeMs { get; set; } = 0;
      public DateTime LastPlayed { get; set; } = DateTime.UtcNow;
  }

  [Serializable]
  public class Configuration : IPluginConfiguration {
    public event EventHandler OnConfigurationChanged;

    private float _livestreamVolume = 0.5f;
    private int _defaultVideoOpen = 1; // 0 = open, 1 = closed
    private bool _enableOutdoorPublicScreens = false; // Opt-in
    private bool _onlySafeDomainsPublicScreens = true;
    private bool _spatialAudioEnabled = true;
    private bool _desktopAudioVisualsEnabled = true;

    public int Version { get; set; }

    #region Saved configuration values

    public float LivestreamVolume { get => _livestreamVolume; set => _livestreamVolume = value; }
    public int DefaultVideoOpen { get => _defaultVideoOpen; set => _defaultVideoOpen = value; }
    
    public bool EnableOutdoorPublicScreens { get => _enableOutdoorPublicScreens; set => _enableOutdoorPublicScreens = value; }
    public bool OnlySafeDomainsPublicScreens { get => _onlySafeDomainsPublicScreens; set => _onlySafeDomainsPublicScreens = value; }
    public bool SpatialAudioEnabled { get => _spatialAudioEnabled; set => _spatialAudioEnabled = value; }
    public string DiscordSessionToken { get; set; } = string.Empty;
    public string DiscordUsername { get; set; } = string.Empty;
    public string DiscordUserId { get; set; } = string.Empty;
    public bool DesktopAudioVisualsEnabled { get => _desktopAudioVisualsEnabled; set => _desktopAudioVisualsEnabled = value; }
    public bool ShowOutdoorGridDebug { get; set; } = false;
    public bool HasAutoDetectedAMD_v2 { get; set; } = false;
    public bool DepthOcclusionEnabled { get; set; } = true;
    public bool TvGlowEnabled { get; set; } = true;
    public bool DisableUIBlockDetection { get; set; } = false;
    public bool AutoResumeMedia { get; set; } = true;
    public bool VerboseChatLogging { get; set; } = false;
    public bool EnableWanderersCampfireFix { get; set; } = false;
    public bool EnableUiCulling { get; set; } = true;
    public float UIBlendThreshold { get; set; } = 0.0f;

    // yt-dlp settings
    public int PreferredQuality { get; set; } = 720;
    public bool EnableSabrProxy { get; set; } = true;

    /// <summary>Automatically upload XivMediaPlayer warnings/errors from dalamud.log to the sync server.</summary>
    public bool AutoSendDiagnosticLogs { get; set; } = false;

    /// <summary>Show a chat hint when new plugin warnings/errors are detected in dalamud.log.</summary>
    public bool NotifyOnDiagnosticLogs { get; set; } = true;

    public const int CurrentConfigVersion = 7;

    public const string DefaultTranslationServerUrl = "http://ai.hubujubu.com:5681";

    /// <summary>UI language index matching <see cref="LanguageEnum"/>.</summary>
    public int UiLanguage { get; set; } = (int)LanguageEnum.English;

    /// <summary>Dev-only override for the translation proxy. Ignored unless <see cref="DevMode"/> is enabled.</summary>
    public string TranslationServerUrl { get; set; } = DefaultTranslationServerUrl;

    /// <summary>Unlocks developer settings such as the translation server URL override.</summary>
    public bool DevMode { get; set; } = false;

    public string GetEffectiveTranslationServerUrl()
    {
      if (DevMode && !string.IsNullOrWhiteSpace(TranslationServerUrl))
      {
        return TranslationServerUrl.Trim().TrimEnd('/');
      }

      return DefaultTranslationServerUrl;
    }

    // World screen compositing settings (legacy single placement)
    public MediaPlayerCore.Compositing.WorldScreenTransform WorldScreen { get; set; } = new MediaPlayerCore.Compositing.WorldScreenTransform();

    // Per-location screen placements: key = location string, value = transform
    public Dictionary<string, MediaPlayerCore.Compositing.WorldScreenTransform> ScreenPlacements { get; set; }
      = new Dictionary<string, MediaPlayerCore.Compositing.WorldScreenTransform>();

    // Per-TV screen placements: key = "{locationKey}#{tvId}"
    public Dictionary<string, MediaPlayerCore.Compositing.WorldScreenTransform> ScreenPlacementsByTvId { get; set; }
      = new Dictionary<string, MediaPlayerCore.Compositing.WorldScreenTransform>();

    public Dictionary<string, RoomMediaState> RoomMediaStates { get; set; } = new Dictionary<string, RoomMediaState>();
    public Dictionary<string, MediaHistoryEntry> WatchHistory { get; set; } = new Dictionary<string, MediaHistoryEntry>();
    public List<string> LocalPlaylist { get; set; } = new List<string>();
    public bool LocalPlaybackMode { get; set; }
    public string LastPlayedUrl { get; set; } = "";
    public string LastPlayedTitle { get; set; } = "";
    public long LastPlayedTimecodeMs { get; set; }
    public int LastPlayedPlaylistIndex { get; set; } = -1;
    public bool LastPlayedIsLive { get; set; }
    public string LastSyncLocationKey { get; set; } = "";
    public List<RegisteredPlot> RegisteredPlots { get; set; } = new List<RegisteredPlot>();
    public PlaybackScreenEdge PlaybackEdgeSide { get; set; } = PlaybackScreenEdge.Right;
    public float PlaybackEdgeAnchor { get; set; } = 0.42f;
    public bool PlaybackLightlessStyle { get; set; } = true;
    public bool RestoreGameBgmOnStop { get; set; }
    public bool ShowEdgeIconWhenScreenDetected { get; set; } = true;
    public bool ShowEdgePlaybackTitle { get; set; } = true;
    public bool HideEdgeTitleBackground { get; set; } = true;
    public float EdgeTitleScrollSpeed { get; set; } = LocalUiConfig.DefaultEdgeTitleScrollSpeed;
    public float EdgeTitleMarqueeLines { get; set; } = LocalUiConfig.DefaultEdgeTitleMarqueeLines;
    public float EdgeIconScreenMargin { get; set; } = LocalUiConfig.DefaultEdgeIconScreenMargin;
    public float EdgeTitleIconGap { get; set; } = LocalUiConfig.DefaultEdgeTitleIconGap;
    public bool KeepEdgeIconWithPanel { get; set; } = true;

    public bool ShouldSerializeLocalPlaylist() => false;
    public bool ShouldSerializeLocalPlaybackMode() => false;
    public bool ShouldSerializeLastPlayedUrl() => false;
    public bool ShouldSerializeLastPlayedTitle() => false;
    public bool ShouldSerializeLastPlayedTimecodeMs() => false;
    public bool ShouldSerializeLastPlayedPlaylistIndex() => false;
    public bool ShouldSerializeLastPlayedIsLive() => false;
    public bool ShouldSerializeLastSyncLocationKey() => false;
    public bool ShouldSerializeRegisteredPlots() => false;
    public bool ShouldSerializePlaybackEdgeSide() => false;
    public bool ShouldSerializePlaybackEdgeAnchor() => false;
    public bool ShouldSerializePlaybackLightlessStyle() => false;
    public bool ShouldSerializeRestoreGameBgmOnStop() => false;
    public bool ShouldSerializeShowEdgeIconWhenScreenDetected() => false;
    public bool ShouldSerializeShowEdgePlaybackTitle() => false;
    public bool ShouldSerializeHideEdgeTitleBackground() => false;
    public bool ShouldSerializeEdgeTitleScrollSpeed() => false;
    public bool ShouldSerializeEdgeTitleMarqueeLines() => false;
    public bool ShouldSerializeEdgeIconScreenMargin() => false;
    public bool ShouldSerializeEdgeTitleIconGap() => false;
    public bool ShouldSerializeKeepEdgeIconWithPanel() => false;

    public string ServerUrl
    {
      get
      {
        if (_serverUrl == "http://24.77.70.65:5000")
        {
          _serverUrl = "http://50.70.102.177:5000";
        }

        return _serverUrl;
      }
      set { _serverUrl = value; }
    }

    // Unique identity for the local user to establish TV ownership
    public string OwnerId { get; set; } = Guid.NewGuid().ToString();

    // Playback controls
    public int SeekIncrementSeconds { get; set; } = 10;
    public bool LoopEnabled { get; set; } = false;
    public bool ShuffleEnabled { get; set; } = false;

    #endregion

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;
    [NonSerialized]
    private Action? deferSave;
    [NonSerialized]
    private Action? deferLocalSave;

    private static readonly JsonSerializerSettings ConfigJsonSettings = new() {
      Formatting = Formatting.Indented,
      TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
      TypeNameHandling = TypeNameHandling.Objects,
    };

    /// <summary>
    /// Parameterless constructor required for Dalamud deserialization.
    /// </summary>
    public Configuration() { }

    /// <summary>
    /// Call after construction or deserialization to wire up the save interface.
    /// </summary>
    public void Initialize(IDalamudPluginInterface pi, Action deferSaveAction, Action deferLocalSaveAction) {
      this.pluginInterface = pi;
      this.deferSave = deferSaveAction;
      this.deferLocalSave = deferLocalSaveAction;
      CleanupStaleTempFiles(pi);
    }

    /// <summary>
    /// Queues a config write for the next framework tick. Safe to call from UI draw handlers.
    /// </summary>
    public void Save() {
      deferSave?.Invoke();
    }

    public void SaveLocal() {
      deferLocalSave?.Invoke();
    }

    /// <summary>
    /// Applies one-time config upgrades for existing installs.
    /// Returns true when the on-disk config should be rewritten (caller should defer Save until after load).
    /// </summary>
    public bool Migrate() {
      var versioned = (IPluginConfiguration)this;
      if (versioned.Version >= CurrentConfigVersion) {
        return false;
      }

      // v2: SABR for YouTube VOD + automatic live detection became the default path.
      if (versioned.Version < 2) {
        EnableSabrProxy = true;
      }

      if (versioned.Version < 3 && !Enum.IsDefined(typeof(LanguageEnum), UiLanguage)) {
        UiLanguage = (int)LanguageEnum.English;
      }

      if (versioned.Version < 4) {
        if (string.IsNullOrWhiteSpace(TranslationServerUrl)) {
          TranslationServerUrl = "http://ai.hubujubu.com:5681";
        }
      }

      // v5: revert mistaken localhost default from an earlier v4 build
      if (versioned.Version < 5) {
        if (string.Equals(TranslationServerUrl, "http://127.0.0.1:5681", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TranslationServerUrl, "http://localhost:5681", StringComparison.OrdinalIgnoreCase)) {
          TranslationServerUrl = "http://ai.hubujubu.com:5681";
        }
      }

      if (versioned.Version < 6) {
        AutoSendDiagnosticLogs = false;
        NotifyOnDiagnosticLogs = true;
      }

      versioned.Version = CurrentConfigVersion;
      return true;
    }

    private int _saveInProgress;
    private int _localSaveInProgress;
    private string _serverUrl = "http://50.70.102.177:5000";

    private static void CleanupStaleTempFiles(IDalamudPluginInterface pi)
    {
      try
      {
        var directory = pi.ConfigDirectory.FullName;
        if (!Directory.Exists(directory)) return;

        foreach (var pattern in new[] { ".XivMediaPlayer.json.*.new", ".local-ui.json.*.new" })
        {
          foreach (var tempFile in Directory.EnumerateFiles(directory, pattern))
          {
            try
            {
              if (File.GetLastWriteTimeUtc(tempFile) < DateTime.UtcNow.AddHours(-1))
              {
                File.Delete(tempFile);
              }
            }
            catch
            {
              // Best effort.
            }
          }
        }

        var legacyTemp = Path.Combine(directory, "XivMediaPlayer.json.tmp");
        if (File.Exists(legacyTemp))
        {
          try { File.Delete(legacyTemp); } catch { }
        }
      }
      catch
      {
        // Best effort.
      }
    }

    /// <summary>
    /// Writes config JSON atomically. Uses a unique temp name so we never fight Dalamud's ".json.tmp" writer.
    /// Dalamud loads plugin config via direct file read, so this stays compatible with GetPluginConfig().
    /// </summary>
    private void WriteConfigFileDirect(IDalamudPluginInterface pi)
    {
      var path = pi.ConfigFile.FullName;
      var directory = Path.GetDirectoryName(path) ?? pi.ConfigDirectory.FullName;
      Directory.CreateDirectory(directory);

      var json = JsonConvert.SerializeObject(this, ConfigJsonSettings);
      var tempPath = Path.Combine(directory, $".XivMediaPlayer.json.{Guid.NewGuid():N}.new");

      File.WriteAllText(tempPath, json, Encoding.UTF8);
      try
      {
        if (File.Exists(path))
        {
          File.Delete(path);
        }
        File.Move(tempPath, path);
      }
      catch
      {
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        throw;
      }
    }

    /// <summary>
    /// Writes config to disk immediately. Avoid calling from ImGui draw; use on shutdown or after FlushPendingConfigSave.
    /// </summary>
    public ConfigSaveResult SaveImmediate(out Exception? error) {
      error = null;
      if (Interlocked.CompareExchange(ref _saveInProgress, 1, 0) != 0)
      {
        return ConfigSaveResult.SkippedInProgress;
      }

      try
      {
        if (this.pluginInterface != null) {
          WriteConfigFileDirect(this.pluginInterface);
        }
        OnConfigurationChanged?.Invoke(this, EventArgs.Empty);
        return ConfigSaveResult.Saved;
      }
      catch (Exception ex)
      {
        error = ex;
        return ConfigSaveResult.Failed;
      }
      finally
      {
        Interlocked.Exchange(ref _saveInProgress, 0);
      }
    }

    public bool LoadLocalUi() {
      if (pluginInterface == null) return true;

      var path = Path.Combine(pluginInterface.ConfigDirectory.FullName, LocalUiConfig.FileName);
      if (File.Exists(path)) {
        try {
          var loaded = JsonConvert.DeserializeObject<LocalUiConfig>(File.ReadAllText(path));
          if (loaded != null) ApplyLocalUi(loaded);
        }
        catch {
          // Keep values already read from the main config for this session.
        }
        return true;
      }

      return SaveLocalImmediate() == ConfigSaveResult.Saved;
    }

    public ConfigSaveResult SaveLocalImmediate() {
      if (pluginInterface == null) return ConfigSaveResult.Failed;
      if (Interlocked.CompareExchange(ref _localSaveInProgress, 1, 0) != 0)
        return ConfigSaveResult.SkippedInProgress;

      try {
        WriteLocalUi(pluginInterface);
        return ConfigSaveResult.Saved;
      }
      catch {
        return ConfigSaveResult.Failed;
      }
      finally {
        Interlocked.Exchange(ref _localSaveInProgress, 0);
      }
    }

    private void ApplyLocalUi(LocalUiConfig loaded) {
      LocalPlaylist = loaded.LocalPlaylist ?? new List<string>();
      LocalPlaybackMode = loaded.LocalPlaybackMode;
      LastPlayedUrl = loaded.LastPlayedUrl ?? "";
      LastPlayedTitle = loaded.LastPlayedTitle ?? "";
      LastPlayedTimecodeMs = loaded.LastPlayedTimecodeMs;
      LastPlayedPlaylistIndex = loaded.LastPlayedPlaylistIndex;
      LastPlayedIsLive = loaded.LastPlayedIsLive;
      LastSyncLocationKey = loaded.LastSyncLocationKey ?? "";
      RegisteredPlots = loaded.RegisteredPlots ?? new List<RegisteredPlot>();
      PlaybackEdgeSide = loaded.PlaybackEdgeSide;
      PlaybackEdgeAnchor = loaded.PlaybackEdgeAnchor;
      PlaybackLightlessStyle = loaded.PlaybackLightlessStyle;
      RestoreGameBgmOnStop = loaded.RestoreGameBgmOnStop;
      ShowEdgeIconWhenScreenDetected = loaded.ShowEdgeIconWhenScreenDetected;
      ShowEdgePlaybackTitle = loaded.ShowEdgePlaybackTitle;
      HideEdgeTitleBackground = loaded.HideEdgeTitleBackground;
      EdgeTitleScrollSpeed = loaded.EdgeTitleScrollSpeed;
      EdgeTitleMarqueeLines = loaded.EdgeTitleMarqueeLines;
      EdgeIconScreenMargin = loaded.EdgeIconScreenMargin;
      EdgeTitleIconGap = loaded.EdgeTitleIconGap;
      KeepEdgeIconWithPanel = loaded.KeepEdgeIconWithPanel;
    }

    private static void WriteLocalUi(IDalamudPluginInterface pi, Configuration source) {
      var directory = pi.ConfigDirectory.FullName;
      Directory.CreateDirectory(directory);
      var path = Path.Combine(directory, LocalUiConfig.FileName);
      var snapshot = new LocalUiConfig {
        LocalPlaylist = source.LocalPlaylist ?? new List<string>(),
        LocalPlaybackMode = source.LocalPlaybackMode,
        LastPlayedUrl = source.LastPlayedUrl ?? "",
        LastPlayedTitle = source.LastPlayedTitle ?? "",
        LastPlayedTimecodeMs = source.LastPlayedTimecodeMs,
        LastPlayedPlaylistIndex = source.LastPlayedPlaylistIndex,
        LastPlayedIsLive = source.LastPlayedIsLive,
        LastSyncLocationKey = source.LastSyncLocationKey ?? "",
        RegisteredPlots = source.RegisteredPlots ?? new List<RegisteredPlot>(),
        PlaybackEdgeSide = source.PlaybackEdgeSide,
        PlaybackEdgeAnchor = source.PlaybackEdgeAnchor,
        PlaybackLightlessStyle = source.PlaybackLightlessStyle,
        RestoreGameBgmOnStop = source.RestoreGameBgmOnStop,
        ShowEdgeIconWhenScreenDetected = source.ShowEdgeIconWhenScreenDetected,
        ShowEdgePlaybackTitle = source.ShowEdgePlaybackTitle,
        HideEdgeTitleBackground = source.HideEdgeTitleBackground,
        EdgeTitleScrollSpeed = source.EdgeTitleScrollSpeed,
        EdgeTitleMarqueeLines = source.EdgeTitleMarqueeLines,
        EdgeIconScreenMargin = source.EdgeIconScreenMargin,
        EdgeTitleIconGap = source.EdgeTitleIconGap,
        KeepEdgeIconWithPanel = source.KeepEdgeIconWithPanel,
      };
      var json = JsonConvert.SerializeObject(snapshot, LocalUiConfig.JsonSettings);
      var tempPath = Path.Combine(directory, $".local-ui.json.{Guid.NewGuid():N}.new");
      File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
      try {
        if (File.Exists(path)) File.Delete(path);
        File.Move(tempPath, path);
      }
      catch {
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        throw;
      }
    }

    private void WriteLocalUi(IDalamudPluginInterface pi) => WriteLocalUi(pi, this);
  }
}
