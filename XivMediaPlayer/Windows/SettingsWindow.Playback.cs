using Dalamud.Bindings.ImGui;
using System.Numerics;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer.Windows {
  internal partial class SettingsWindow {
    private void DrawPlaybackSettings() {
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
    }
  }
}
