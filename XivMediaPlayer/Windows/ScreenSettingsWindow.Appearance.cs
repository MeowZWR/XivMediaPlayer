using Dalamud.Bindings.ImGui;
using System.Numerics;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer.Windows {
  internal partial class ScreenSettingsWindow {
    private void DrawAppearanceTab() {
      if (!_enabled) {
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Loc.T("Screen.EnableHint"));
        return;
      }

      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), Loc.T("Screen.Section.Projector"));

      bool appearanceChanged = false;
      appearanceChanged |= ImGui.Checkbox(Loc.T("Screen.ProjectorMode"), ref _isProjectorMode);
      appearanceChanged |= ImGui.SliderFloat(Loc.T("Screen.Opacity"), ref _opacity, 0.05f, 1.0f, "%.2f");
      appearanceChanged |= ImGui.ColorEdit3(Loc.T("Screen.ScreensaverColor"), ref _screensaverColor);

      string[] screensaverStyles = new string[] {
        Loc.T("Screen.Screensaver.BouncingLogo"),
        Loc.T("Screen.Screensaver.Vcr"),
        Loc.T("Screen.Screensaver.NoSignal"),
        Loc.T("Screen.Screensaver.Static"),
        Loc.T("Screen.Screensaver.TestPattern"),
        Loc.T("Screen.Screensaver.MatrixRain")
      };
      appearanceChanged |= ImGui.Combo(Loc.T("Screen.ScreensaverStyle"), ref _screensaverStyle, screensaverStyles, screensaverStyles.Length);

      bool saveAppearance = ImGui.IsItemDeactivatedAfterEdit() || ImGui.IsItemDeactivated();

      if (appearanceChanged) {
        _transform.Opacity = _opacity;
        _transform.IsProjectorMode = _isProjectorMode;
        _transform.ScreensaverColor = _screensaverColor;
        _transform.ScreensaverStyle = _screensaverStyle;
      }
      if (saveAppearance || appearanceChanged) {
        InvokeSave();
      }
    }
  }
}
