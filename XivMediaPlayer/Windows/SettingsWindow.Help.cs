using Dalamud.Bindings.ImGui;
using System.Numerics;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer.Windows {
  internal partial class SettingsWindow {
    private void DrawHelpFooter() {
      ImGui.Spacing();
      ImGui.Separator();
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
