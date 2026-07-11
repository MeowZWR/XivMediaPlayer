using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
using XivMediaPlayer.Localization;
using XivMediaPlayer.Networking.Models;

namespace XivMediaPlayer.Windows {
  internal partial class ScreenSettingsWindow {
    private void DrawSyncTab() {
      if (!_enabled) {
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Loc.T("Screen.EnableHint"));
        return;
      }

      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), Loc.T("Screen.Section.RoomSync"));
      ImGui.TextWrapped(Loc.T("Screen.RoomSyncBody"));

      string locationKey = _plugin.LocationKey;
      bool isOutdoorsSync = !string.IsNullOrEmpty(locationKey) && locationKey.StartsWith("zone_");
      bool isIslandSync = !string.IsNullOrEmpty(locationKey) && locationKey.StartsWith("island_");

      if (string.IsNullOrEmpty(locationKey) ||
          (!locationKey.StartsWith("house_") && !locationKey.StartsWith("zone_") && !locationKey.StartsWith("island_"))) {
        ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), Loc.T("Screen.MustBeInHousing"));
      } else {
        unsafe {
          var housingMgr = FFXIVClientStructs.FFXIV.Client.Game.HousingManager.Instance();
          if (housingMgr != null && !housingMgr->IsInside() && housingMgr->GetCurrentPlot() >= 0 && housingMgr->GetCurrentWard() >= 0) {
            ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), Loc.T("Settings.StandingInPlot", housingMgr->GetCurrentPlot() + 1));
          }
        }

        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), Loc.T("Settings.PlacementKey"));
        ImGui.SameLine();
        ImGui.Text(locationKey);

        if (_plugin.CurrentTvPlacement != null) {
          ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), Loc.T("Settings.SyncedTvKey"));
          ImGui.SameLine();
          ImGui.Text(_plugin.CurrentTvPlacement.LocationKey);
        }

        if (_plugin.CurrentTvPlacement == null || _plugin.CurrentTvPlacement.OwnerId == _plugin.Config.OwnerId) {
          bool isLocked = _plugin.CurrentTvPlacement?.IsLocked ?? !isOutdoorsSync;
          if (!isOutdoorsSync) {
            if (ImGui.Checkbox(Loc.T("Screen.LockTv"), ref isLocked)) {
              if (_plugin.CurrentTvPlacement != null) {
                _plugin.CurrentTvPlacement.IsLocked = isLocked;
              } else {
                _plugin.CurrentTvPlacement = new TvPlacement {
                  OwnerId = _plugin.Config.OwnerId,
                  IsLocked = isLocked
                };
              }
              RegisterTvAsync(locationKey);
            }
          }

          ImGui.Spacing();
          if (ImGui.Button(Loc.T("Screen.SyncPlacements"))) {
            RegisterTvAsync(locationKey);
          }
          ImGui.SameLine();
          if (ImGui.Button(Loc.T("Screen.RemoveTv"))) {
            _ = DeleteTvAsync(locationKey);
          }
        } else {
          if (_plugin.IsHousingMenuOpen || isOutdoorsSync || isIslandSync) {
            if (ImGui.Button(Loc.T("Screen.TakeOwnership"))) {
              RegisterTvAsync(locationKey);
            }
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), Loc.T("Screen.CanOverrideLocked"));
          } else if (_plugin.CurrentTvPlacement.IsLocked) {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), Loc.T("Screen.LockedByOwner"));
          }
        }

        if (!string.IsNullOrEmpty(_statusMessage)) {
          ImGui.TextColored(_statusColor, _statusMessage);
        }
      }
    }

    public async System.Threading.Tasks.Task<bool> DeleteTvAsync(string locationKey, bool restoreOnFailure = false) {
      if (_plugin.CurrentTvPlacement == null) return false;
      var currentPlacement = _plugin.CurrentTvPlacement;
      var serverLocationKey = string.IsNullOrEmpty(currentPlacement.LocationKey) ? locationKey : currentPlacement.LocationKey;

      _statusMessage = Loc.T("Screen.DeletingTv");
      _statusColor = new Vector4(1, 1, 1, 1);

      try {
        bool isOutdoorsSync = !string.IsNullOrEmpty(serverLocationKey) && serverLocationKey.StartsWith("zone_");
        bool isIslandSync = !string.IsNullOrEmpty(serverLocationKey) && serverLocationKey.StartsWith("island_");
        bool success = await _plugin.ServerClient.DeleteTvAsync(
          serverLocationKey,
          currentPlacement.Id,
          _plugin.Config.OwnerId,
          _plugin.IsHousingMenuOpen || isOutdoorsSync || isIslandSync);
        if (success) {
          _plugin.CurrentTvPlacement = null;
          _plugin.Config.ScreenPlacements.Remove(locationKey);
          _plugin.Config.ScreenPlacements.Remove(serverLocationKey);
          _transform.Enabled = false;
          _enabled = false;
          _plugin.Config.Save();
          _statusMessage = Loc.T("Screen.RemovedTv");
          _statusColor = new Vector4(0.3f, 1f, 0.3f, 1);
          _plugin.Chat.Print(Loc.T("Chat.Prefix") + Loc.T("Screen.RemovedTv"));
          return true;
        }

        RestoreEnabledAfterDeleteFailure(restoreOnFailure);
        _statusMessage = Loc.T("Screen.RemoveFailed");
        _statusColor = new Vector4(1, 0.3f, 0.3f, 1);
        _plugin.Chat.PrintError(Loc.T("Chat.Prefix") + Loc.T("Screen.RemoveFailed"));
        return false;
      } catch (UnauthorizedAccessException) {
        RestoreEnabledAfterDeleteFailure(restoreOnFailure);
        _statusMessage = Loc.T("Screen.CannotDeleteLocked");
        _statusColor = new Vector4(1, 0.3f, 0.3f, 1);
        _plugin.Chat.PrintError(Loc.T("Chat.Prefix") + Loc.T("Screen.CannotDeleteLocked"));
      } catch (Exception) {
        RestoreEnabledAfterDeleteFailure(restoreOnFailure);
        _statusMessage = Loc.T("Screen.DeleteNetworkError");
        _statusColor = new Vector4(1, 0.3f, 0.3f, 1);
        _plugin.Chat.PrintError(Loc.T("Chat.Prefix") + Loc.T("Screen.DeleteNetworkError"));
      }

      return false;
    }

    private void RestoreEnabledAfterDeleteFailure(bool restoreOnFailure) {
      if (!restoreOnFailure) return;
      _enabled = true;
      _transform.Enabled = true;
    }

    private DateTime _lastRegistrationTime = DateTime.MinValue;

    public async void RegisterTvAsync(string locationKey) {
      if (!_enabled) {
        _statusMessage = Loc.T("Screen.NotEnabled");
        _statusColor = new Vector4(1, 0.3f, 0.3f, 1);
        return;
      }

      if ((DateTime.UtcNow - _lastRegistrationTime).TotalSeconds < 2) {
        return;
      }
      _lastRegistrationTime = DateTime.UtcNow;

      _statusMessage = Loc.T("Screen.RegisteringTv");
      _statusColor = new Vector4(1, 1, 1, 1);

      var placement = new TvPlacement {
        LocationKey = locationKey,
        PositionX = _position.X,
        PositionY = _position.Y,
        PositionZ = _position.Z,
        RotationX = _transform.RotationDegrees.X,
        RotationY = _transform.RotationDegrees.Y,
        RotationZ = _transform.RotationDegrees.Z,
        ScaleX = _scale.X,
        ScaleY = _scale.Y,
        Opacity = _opacity,
        IsProjectorMode = _isProjectorMode,
        ScreensaverColorR = _screensaverColor.X,
        ScreensaverColorG = _screensaverColor.Y,
        ScreensaverColorB = _screensaverColor.Z,
        ScreensaverStyle = _screensaverStyle,
        OwnerId = _plugin.Config.OwnerId,
        IsLocked = _plugin.CurrentTvPlacement?.IsLocked ?? (!locationKey.StartsWith("zone_") && !locationKey.StartsWith("island_")),
        BypassLock = _plugin.IsHousingMenuOpen || locationKey.StartsWith("zone_") || locationKey.StartsWith("island_")
      };

      SyncToTransform();
      InvokeSave();

      try {
        var result = await _plugin.ServerClient.RegisterTvAsync(locationKey, placement);
        if (result != null) {
          _plugin.CurrentTvPlacement = result;
          _statusMessage = Loc.T("Screen.RegisteredTv");
          _statusColor = new Vector4(0.3f, 1f, 0.3f, 1);
          _plugin.Chat.Print(Loc.T("Chat.Prefix") + Loc.T("Screen.RegisteredTv"));
        } else {
          _statusMessage = Loc.T("Screen.RegisterFailed");
          _statusColor = new Vector4(1, 0.6f, 0.2f, 1);
          _plugin.Chat.PrintError(Loc.T("Chat.Prefix") + Loc.T("Screen.RegisterFailed"));
        }
      } catch (UnauthorizedAccessException) {
        _statusMessage = Loc.T("Screen.CannotMoveLocked");
        _statusColor = new Vector4(1, 0.3f, 0.3f, 1);
        _plugin.Chat.PrintError(Loc.T("Chat.Prefix") + Loc.T("Screen.CannotMoveLocked"));
      } catch (Exception) {
        _statusMessage = Loc.T("Screen.SyncNetworkError");
        _statusColor = new Vector4(1, 0.3f, 0.3f, 1);
        _plugin.Chat.PrintError(Loc.T("Chat.Prefix") + Loc.T("Screen.SyncNetworkError"));
      }
    }
  }
}
