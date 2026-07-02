using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer.Windows {
  internal class SettingsWindow : Window {
    private Plugin _plugin;
    private Action _onVolumeFix;

    public SettingsWindow(Plugin plugin, Action onVolumeFix = null) :
      base(Loc.T("SettingsWindow.Title") + "###SettingsWindow", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize, false) {
      _plugin = plugin;
      _onVolumeFix = onVolumeFix;
      Size = new Vector2(420, 0);
      SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw() {
      WindowName = Loc.T("SettingsWindow.Title") + "###SettingsWindow";

      // Volume 
      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), Loc.T("Settings.Section.Audio"));
      ImGui.Separator();

      float volume = _plugin.Config.LivestreamVolume;
      if (ImGui.SliderFloat(Loc.T("Settings.StreamVolume"), ref volume, 0f, 3f)) {
        _plugin.Config.LivestreamVolume = volume;
        if (_plugin.MediaManager != null) {
            _plugin.MediaManager.LiveStreamVolume = volume;
        }
        _plugin.Config.Save();
      }

      if (_onVolumeFix != null && ImGui.Button(Loc.T("Settings.FixGameVolume"))) {
        _onVolumeFix.Invoke();
      }

      ImGui.Spacing();
      ImGui.Spacing();

      // Twitch 
      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), Loc.T("Settings.Section.Twitch"));
      ImGui.Separator();

      bool tuneInto = _plugin.Config.TuneIntoTwitchStreams;
      if (ImGui.Checkbox(Loc.T("Settings.AutoTuneTwitch"), ref tuneInto)) {
        _plugin.Config.TuneIntoTwitchStreams = tuneInto;
        _plugin.Config.Save();
      }

      bool streamPrompt = _plugin.Config.TuneIntoTwitchStreamPrompt;
      if (ImGui.Checkbox(Loc.T("Settings.StreamPrompts"), ref streamPrompt)) {
        _plugin.Config.TuneIntoTwitchStreamPrompt = streamPrompt;
        _plugin.Config.Save();
      }

      ImGui.Spacing();
      ImGui.Spacing();

      // Video 
      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), Loc.T("Settings.Section.Video"));
      ImGui.Separator();

      bool defaultOpen = _plugin.Config.DefaultVideoOpen == 0;
      if (ImGui.Checkbox(Loc.T("Settings.DefaultVideoOpen"), ref defaultOpen)) {
        _plugin.Config.DefaultVideoOpen = defaultOpen ? 0 : 1;
        _plugin.Config.Save();
      }

      bool autoResume = _plugin.Config.AutoResumeMedia;
      if (ImGui.Checkbox(Loc.T("Settings.AutoResume"), ref autoResume)) {
        _plugin.Config.AutoResumeMedia = autoResume;
        _plugin.Config.Save();
      }
      bool tvGlow = _plugin.Config.TvGlowEnabled;
      if (ImGui.Checkbox(Loc.T("Settings.TvGlow"), ref tvGlow)) {
        _plugin.Config.TvGlowEnabled = tvGlow;
        _plugin.Config.Save();
      }
      if (ImGui.IsItemHovered()) {
        ImGui.SetTooltip(Loc.T("Settings.TvGlowTooltip"));
      }

      bool uiCulling = _plugin.Config.EnableUiCulling;
      if (ImGui.Checkbox(Loc.T("Settings.UiCulling"), ref uiCulling)) {
        _plugin.Config.EnableUiCulling = uiCulling;
        _plugin.Config.Save();
      }
      if (ImGui.IsItemHovered()) {
        ImGui.SetTooltip(Loc.T("Settings.UiCullingTooltip"));
      }


      bool strictMasking = _plugin.Config.UIBlendThreshold > 0.5f;
      if (ImGui.Checkbox(Loc.T("Settings.StrictUiMasking"), ref strictMasking)) {
        _plugin.Config.UIBlendThreshold = strictMasking ? (171.0f / 255.0f) : 0.0f;
        _plugin.Config.Save();
      }
      if (ImGui.IsItemHovered()) {
        ImGui.SetTooltip(Loc.T("Settings.StrictUiMaskingTooltip"));
      }

      bool disableUiBlock = _plugin.Config.DisableUIBlockDetection;
      if (ImGui.Checkbox(Loc.T("Settings.DisableUiBlock"), ref disableUiBlock)) {
        _plugin.Config.DisableUIBlockDetection = disableUiBlock;
        _plugin.Config.Save();
      }
      if (ImGui.IsItemHovered()) {
        ImGui.SetTooltip(Loc.T("Settings.DisableUiBlockTooltip"));
      }

      bool enableWanderersCampfireFix = _plugin.Config.EnableWanderersCampfireFix;
      if (ImGui.Checkbox(Loc.T("Settings.WanderersCampfireFix"), ref enableWanderersCampfireFix)) {
        _plugin.Config.EnableWanderersCampfireFix = enableWanderersCampfireFix;
        _plugin.Config.Save();
      }
      if (ImGui.IsItemHovered()) {
        ImGui.SetTooltip(Loc.T("Settings.WanderersCampfireFixTooltip"));
      }

      if (ImGui.Button(Loc.T("Settings.ClearWatchHistory"))) {
        _plugin.Config.WatchHistory.Clear();
        _plugin.Config.Save();
        _plugin.Chat.Print(Loc.Chat("WatchHistoryCleared"));
      }

      ImGui.Spacing();
      ImGui.Spacing();

      // Outdoor TVs
      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), Loc.T("Settings.Section.OutdoorTvs"));
      ImGui.Separator();

      bool enableOutdoor = _plugin.Config.EnableOutdoorPublicScreens;
      if (ImGui.Checkbox(Loc.T("Settings.EnableOutdoorScreens"), ref enableOutdoor)) {
        _plugin.Config.EnableOutdoorPublicScreens = enableOutdoor;
        _plugin.Config.Save();
        _plugin.HandleOutdoorSettingToggled();
      }

      bool safeMode = _plugin.Config.OnlySafeDomainsPublicScreens;
      if (ImGui.Checkbox(Loc.T("Settings.SafeMode"), ref safeMode)) {
        if (!safeMode) {
            ImGui.OpenPopup(Loc.T("Settings.SafeModePopupTitle"));
        } else {
            _plugin.Config.OnlySafeDomainsPublicScreens = true;
            _plugin.Config.Save();
        }
      }
      ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f),
        Loc.T("Settings.SafeModeHint"));

      var viewportCenter = ImGui.GetMainViewport().GetCenter();
      ImGui.SetNextWindowPos(viewportCenter, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
      if (ImGui.BeginPopupModal(Loc.T("Settings.SafeModePopupTitle"), ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings)) {
          ImGui.Text(Loc.T("Settings.SafeModeWarning1"));
          ImGui.Text(Loc.T("Settings.SafeModeWarning2"));
          ImGui.Spacing();
          ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), Loc.T("Settings.SafeModeAgree1"));
          ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), Loc.T("Settings.SafeModeAgree2"));
          ImGui.Separator();
          ImGui.Spacing();
          
          if (ImGui.Button(Loc.T("Settings.SafeModeAgreeButton"), new Vector2(250, 0))) {
              _plugin.Config.OnlySafeDomainsPublicScreens = false;
              _plugin.Config.Save();
              ImGui.CloseCurrentPopup();
          }
          ImGui.SameLine();
          if (ImGui.Button(Loc.T("Settings.Cancel"), new Vector2(120, 0))) {
              ImGui.CloseCurrentPopup();
          }
          ImGui.EndPopup();
      }

      ImGui.Separator();

      bool spatialAudio = _plugin.Config.SpatialAudioEnabled;
      if (ImGui.Checkbox(Loc.T("Settings.SpatialAudio"), ref spatialAudio)) {
        _plugin.Config.SpatialAudioEnabled = spatialAudio;
        _plugin.Config.Save();
        _plugin.DoRefreshCurrentMedia();
      }
      ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f),
        Loc.T("Settings.SpatialAudioHint"));

      ImGui.Separator();
      
      bool showGrid = _plugin.Config.ShowOutdoorGridDebug;
      if (ImGui.Checkbox(Loc.T("Settings.OutdoorGridDebug"), ref showGrid)) {
        _plugin.Config.ShowOutdoorGridDebug = showGrid;
        _plugin.Config.Save();
      }

      ImGui.Spacing();
      ImGui.Spacing();

      // Playback
      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), Loc.T("Settings.Section.Playback"));
      ImGui.Separator();

      int seekIncrement = _plugin.Config.SeekIncrementSeconds;
      if (ImGui.SliderInt(Loc.T("Settings.SeekIncrement"), ref seekIncrement, 1, 60)) {
        _plugin.Config.SeekIncrementSeconds = seekIncrement;
        _plugin.Config.Save();
      }
      ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f),
        Loc.T("Settings.SeekIncrementHint"));

      ImGui.Spacing();
      ImGui.Spacing();

      // Debug
      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), Loc.T("Settings.Section.Debug"));
      ImGui.Separator();

      unsafe
      {
          var housingMgr = FFXIVClientStructs.FFXIV.Client.Game.HousingManager.Instance();
          if (housingMgr != null && !housingMgr->IsInside() && housingMgr->GetCurrentPlot() >= 0 && housingMgr->GetCurrentWard() >= 0)
          {
              ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), Loc.T("Settings.StandingInPlot", housingMgr->GetCurrentPlot() + 1));
          }
      }

      string locationKey = _plugin.LocationKey;
      ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), Loc.T("Settings.PlacementKey"));
      ImGui.SameLine();
      ImGui.Text(locationKey ?? Loc.T("Settings.Unknown"));
      if (locationKey != null) {
          ImGui.SameLine();
          if (ImGui.Button(Loc.T("Settings.Copy"))) {
              ImGui.SetClipboardText(locationKey);
          }
      }

      if (_plugin.CurrentTvPlacement != null)
      {
          ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), Loc.T("Settings.SyncedTvKey"));
          ImGui.SameLine();
          ImGui.Text(_plugin.CurrentTvPlacement.LocationKey);
          ImGui.SameLine();
          if (ImGui.Button(Loc.T("Settings.CopySynced"))) {
              ImGui.SetClipboardText(_plugin.CurrentTvPlacement.LocationKey);
          }
      }
      ImGui.Spacing();

      bool verboseChat = _plugin.Config.VerboseChatLogging;
      if (ImGui.Checkbox(Loc.T("Settings.VerboseChat"), ref verboseChat)) {
        _plugin.Config.VerboseChatLogging = verboseChat;
        _plugin.Config.Save();
      }
      ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f),
        Loc.T("Settings.VerboseChatHint"));

      ImGui.Spacing();
      ImGui.Spacing();

      // yt-dlp quality
      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), Loc.T("Settings.Section.Ytdlp"));
      ImGui.Separator();

      string[] qualityLabels = new string[] { "360p", "480p", "720p", "1080p", "Best" };
      int[] qualityValues = new int[] { 360, 480, 720, 1080, 0 };
      int currentQualityIdx = Array.IndexOf(qualityValues, _plugin.Config.PreferredQuality);
      if (currentQualityIdx < 0) currentQualityIdx = 2; // default 720p
      if (ImGui.Combo(Loc.T("Settings.PreferredQuality"), ref currentQualityIdx, qualityLabels, qualityLabels.Length)) {
        _plugin.Config.PreferredQuality = qualityValues[currentQualityIdx];
        _plugin.Config.Save();
      }

      ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f),
        Loc.T("Settings.YtdlpHint"));

      if (_plugin.YtDlpManager != null && !_plugin.YtDlpManager.HasCookiesFile) {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), Loc.T("Settings.NoCookiesWarning"));
        ImGui.TextWrapped(Loc.T("Settings.NoCookiesBody"));
        
        if (ImGui.Button(Loc.T("Settings.ChromeExtension"))) {
            try {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName = "https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge",
                    UseShellExecute = true
                });
            } catch { }
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Settings.FirefoxExtension"))) {
            try {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName = "https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter/",
                    UseShellExecute = true
                });
            } catch { }
        }
      }

      ImGui.Spacing();
      ImGui.Spacing();

      // Server Sync
      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), Loc.T("Settings.Section.ServerSync"));
      ImGui.Separator();

      string serverUrl = _plugin.Config.ServerUrl;
      if (ImGui.InputText(Loc.T("Settings.ServerUrl"), ref serverUrl, 256)) {
        _plugin.Config.ServerUrl = serverUrl;
        _plugin.Config.Save();
      }
      ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f),
        Loc.T("Settings.ServerUrlHint"));

      ImGui.Spacing();
      ImGui.Spacing();

      // Help & Support
      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), Loc.T("Settings.Section.Help"));
      ImGui.Separator();

      if (ImGui.Button(Loc.T("Settings.TutorialVideo"))) {
          try {
              System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                  FileName = "https://www.youtube.com/watch?v=ZgLs2OJQ8ks",
                  UseShellExecute = true
              });
          } catch { }
      }

      ImGui.SameLine();

      if (ImGui.Button(Loc.T("Settings.JoinDiscord"))) {
          try {
              System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                  FileName = "https://discord.gg/rtGXwMn7pX",
                  UseShellExecute = true
              });
          } catch { }
      }

      ImGui.Spacing();

      if (ImGui.Button(Loc.T("Settings.SupportKofi"))) {
          try {
              System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                  FileName = "https://ko-fi.com/sebastina",
                  UseShellExecute = true
              });
          } catch { }
      }
    }
  }
}
