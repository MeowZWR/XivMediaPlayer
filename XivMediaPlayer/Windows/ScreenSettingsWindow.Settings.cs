using Dalamud.Bindings.ImGui;
using System.Numerics;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer.Windows {
  internal partial class ScreenSettingsWindow {
    private void DrawSettingsTab() {
      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), Loc.T("Screen.Section.HousingUi"));
      ImGui.Separator();

      bool autoOpen = _plugin.Config.AutoOpenScreenPlacementOnHousingMenu;
      if (ImGui.Checkbox(Loc.T("Screen.AutoOpenOnHousingMenu"), ref autoOpen)) {
        _plugin.Config.AutoOpenScreenPlacementOnHousingMenu = autoOpen;
        _plugin.Config.Save();
      }
      ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), Loc.T("Screen.AutoOpenOnHousingMenuHint"));

      ImGui.Spacing();

      bool nativeButton = _plugin.Config.ShowScreenPlacementNativeButton;
      if (ImGui.Checkbox(Loc.T("Screen.ShowNativeButton"), ref nativeButton)) {
        _plugin.Config.ShowScreenPlacementNativeButton = nativeButton;
        _plugin.Config.Save();
        _plugin.OnScreenPlacementSettingsChanged();
      }
      ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), Loc.T("Screen.ShowNativeButtonHint"));
    }
  }
}
