using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer.Windows {
  internal partial class SettingsWindow {
    private void DrawYtdlpSettings() {
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

      ImGui.Spacing();
      ImGui.Spacing();
    }
  }
}
