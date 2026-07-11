using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using System;
using System.Numerics;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer.Windows {
  internal partial class SettingsWindow {
    private void DrawCookiesSettings() {
      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), Loc.T("Settings.Section.Cookies"));
      ImGui.Separator();

      bool autoImport = _plugin.Config.AutoImportCookiesFromClipboard;
      if (ImGui.Checkbox(Loc.T("Settings.AutoImportCookies"), ref autoImport)) {
        _plugin.Config.AutoImportCookiesFromClipboard = autoImport;
        _plugin.Config.Save();
      }
      if (ImGui.IsItemHovered()) {
        ImGui.SetTooltip(Loc.T("Settings.AutoImportCookiesTooltip"));
      }

      ImGui.Spacing();

      bool hasCookies = _plugin.YtDlpManager?.HasCookiesFile == true;
      if (hasCookies) {
        ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), Loc.T("Settings.CookiesFound"));
      } else {
        ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), Loc.T("Settings.NoCookiesWarning"));
      }

      if (_plugin.YtDlpManager != null) {
        ImGui.TextWrapped(Loc.T("Settings.CookiesPath", _plugin.YtDlpManager.CookiesSavePath));
      }

      ImGui.Spacing();
      ImGui.TextWrapped(Loc.T("Settings.CookiesManualImportHint"));

      if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Paste, Loc.T("Settings.ImportCookiesFromClipboard"))) {
        _plugin.ImportCookiesFromClipboard("settings");
      }
      if (ImGui.IsItemHovered()) {
        ImGui.SetTooltip(Loc.T("Settings.ImportCookiesTooltip"));
      }

      ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), Loc.T("Settings.CookiesCommandHint"));

      ImGui.Spacing();
      ImGui.Separator();
      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), Loc.T("Settings.Section.VRCVideoCacher"));
      ImGui.TextWrapped(Loc.T("Settings.VRCVideoCacherBody"));

      bool listenerActive = _plugin.YtDlpManager?.IsCookieListenerActive == true;
      if (listenerActive) {
        ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), Loc.T("Settings.VRCVideoCacherListenerActive"));
      } else {
        ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), Loc.T("Settings.VRCVideoCacherListenerInactive"));
      }

      if (!hasCookies) {
        ImGui.TextWrapped(Loc.T("Settings.NoCookiesBody"));
      }

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
      if (ImGui.IsItemHovered()) {
        ImGui.SetTooltip(Loc.T("Settings.VRCVideoCacherExtensionTooltip"));
      }

      ImGui.Spacing();
      ImGui.Spacing();
    }
  }
}
