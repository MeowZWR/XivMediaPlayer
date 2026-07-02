using Dalamud.Bindings.ImGui;
using System.Numerics;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer.Windows {
  internal partial class SettingsWindow {
    private void DrawDebugSettings() {
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
    }
  }
}
