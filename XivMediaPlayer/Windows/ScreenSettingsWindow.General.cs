using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer.Windows {
  internal partial class ScreenSettingsWindow {
    private void DrawGeneralTab() {
      bool hasPrivileges = HasPlacementPrivileges();

      if (ImGui.Checkbox(Loc.T("Screen.RenderInWorld"), ref _enabled)) {
        _transform.Enabled = _enabled;

        if (!_enabled && !string.IsNullOrEmpty(_plugin.LocationKey) &&
            _plugin.CurrentTvPlacement != null &&
            (_plugin.CurrentTvPlacement.OwnerId == _plugin.Config.OwnerId || hasPrivileges)) {
          _ = DeleteTvAsync(_plugin.LocationKey, restoreOnFailure: true);
        } else {
          InvokeSave();
        }
      }

      ImGui.SameLine();
      ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 110);
      if (ImGui.Button(Loc.T("Screen.TutorialVideo"))) {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
          FileName = "https://www.youtube.com/watch?v=ZgLs2OJQ8ks",
          UseShellExecute = true
        });
      }

      if (!_enabled) {
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Loc.T("Screen.EnableHint"));
        return;
      }

      ImGui.Separator();

      unsafe {
        bool isSnapKeyPressed = ImGui.GetIO().KeyShift && ImGui.GetIO().KeyCtrl;
        if (isSnapKeyPressed && !_wasShiftPressed) {
          var hm = FFXIVClientStructs.FFXIV.Client.Game.HousingManager.Instance();
          if (hm != null && hm->IndoorTerritory != null) {
            var hover = hm->IndoorTerritory->HoveredHousingObject;
            var target = hm->IndoorTerritory->TargetedHousingObject;
            var objToSnap = hover != null ? hover : target;

            if (objToSnap != null) {
              _position = objToSnap->Position;
              _rotation.X = objToSnap->Rotation * (180f / (float)Math.PI);
              _rotation.Y = 0f;
              SyncToTransform();
              InvokeSave();
            }
          }
        }
        _wasShiftPressed = isSnapKeyPressed;
      }

      if (ImGui.Button(Loc.T("Screen.PlaceAtCamera"))) {
        InvokePlaceAtCamera();
      }

      ImGui.Spacing();
      ImGui.TextColored(new Vector4(0.7f, 1f, 0.7f, 1f), Loc.T("Screen.QuickSnap"));
      ImGui.TextWrapped(Loc.T("Screen.QuickSnapBody"));
      ImGui.Spacing();

      if (ImGui.Button(Loc.T("Screen.Save"))) {
        SyncToTransform();
        InvokeSave();
      }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.Reset"))) {
        _transform.Enabled = false;
        _enabled = false;
        SyncFromTransform();

        string locKey = _plugin.LocationKey;
        if (!string.IsNullOrEmpty(locKey) && _plugin.CurrentTvPlacement != null &&
            (_plugin.CurrentTvPlacement.OwnerId == _plugin.Config.OwnerId || hasPrivileges)) {
          _ = DeleteTvAsync(locKey, restoreOnFailure: true);
        } else {
          InvokeSave();
        }
      }

      ImGui.Spacing();
      ImGui.Separator();
      ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f),
        Loc.T("Screen.InfoFormat", _scale.X, _scale.Y, _position.X, _position.Y, _position.Z));

      var depthDebug = _renderer.DepthDebugInfo;
      if (!string.IsNullOrEmpty(depthDebug)) {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), Loc.T("Screen.DepthDebug"));
        ImGui.TextWrapped(depthDebug);
      }

      var rendererError = _renderer.DepthRendererError;
      if (!string.IsNullOrEmpty(rendererError)) {
        ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), Loc.T("Screen.GpuError", rendererError));
      }
    }
  }
}
