using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer.Windows {
  // Tab shell only. Each upstream section lives in SettingsWindow.<Section>.cs for easier merges.
  internal partial class SettingsWindow : Window {
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

      if (ImGui.BeginTabBar("SettingsTabs")) {
        if (ImGui.BeginTabItem(Loc.T("Settings.Tab.General"))) {
          DrawAudioSettings();
          DrawTwitchSettings();
          DrawPlaybackSettings();
          ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("Settings.Tab.Video"))) {
          DrawVideoSettings();
          ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("Settings.Tab.OutdoorTvs"))) {
          DrawOutdoorTvsSettings();
          ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("Settings.Tab.Cookies"))) {
          DrawCookiesSettings();
          ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("Settings.Tab.Advanced"))) {
          DrawDebugSettings();
          DrawYtdlpSettings();
          DrawServerSyncSettings();
          ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
      }

      DrawSafeModePopup();
      DrawHelpFooter();
    }
  }
}
