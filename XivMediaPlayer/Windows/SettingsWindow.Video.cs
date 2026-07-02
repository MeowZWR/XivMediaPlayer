using Dalamud.Bindings.ImGui;
using System.Numerics;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer.Windows {
  internal partial class SettingsWindow {
    private void DrawVideoSettings() {
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
    }
  }
}
