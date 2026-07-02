using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using MediaPlayerCore.Compositing;
using XivMediaPlayer.Compositing;
using System;
using System.Numerics;
using Dalamud.Plugin.Services;
using XivMediaPlayer.Networking.Models;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer.Windows {
  /// <summary>
  /// ImGui settings window for interactively positioning the world-space video screen.
  /// Provides drag controls for position, rotation, and scale, plus quick-action buttons.
  /// </summary>
  internal class ScreenSettingsWindow : Window {
    private readonly WorldScreenTransform _transform;
    private readonly WorldVideoRenderer _renderer;
    private readonly Action _onSave;
    private readonly Action _onPlaceAtCamera;
    private readonly Plugin _plugin;
    private readonly IGameGui _gameGui;

    private string _statusMessage = "";
    private Vector4 _statusColor = new Vector4(1, 1, 1, 1);

    private Vector3 _position;
    private Vector2 _rotation; // yaw, pitch
    private Vector2 _scale;
    private bool _enabled;
    private bool _wasShiftPressed;
    private int _aspectRatio = 0; // 0 = 16:9, 1 = 4:3
    
    private float _opacity = 1.0f;
    private bool _isProjectorMode = false;
    private Vector3 _screensaverColor = new Vector3(0.0f, 0.0f, 0.0f);
    private int _screensaverStyle = 0;

    // Drag state for world-space interaction
    private bool _isDragging;
    private Vector2 _dragStartMouse;
    private Vector3 _dragStartPosition;

    public ScreenSettingsWindow(
        Plugin plugin,
        IGameGui gameGui,
        WorldScreenTransform transform,
        WorldVideoRenderer renderer,
        Action onSave,
        Action onPlaceAtCamera) :
      base(Loc.T("ScreenWindow.Title") + "###ScreenPlacement",
        ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize,
        false) {
      _plugin = plugin;
      _gameGui = gameGui;
      _transform = transform;
      _renderer = renderer;
      _onSave = onSave;
      _onPlaceAtCamera = onPlaceAtCamera;

      Size = new Vector2(340, 0);
      SizeCondition = ImGuiCond.FirstUseEver;

      SyncFromTransform();
    }

    public void SyncFromTransform() {
      _position = _transform.Position;
      _rotation = new Vector2(_transform.RotationDegrees.Y, _transform.RotationDegrees.X); // yaw, pitch
      _scale = _transform.Scale;
      _enabled = _transform.Enabled;
      _opacity = _transform.Opacity;
      _isProjectorMode = _transform.IsProjectorMode;
      _screensaverColor = _transform.ScreensaverColor;
      _screensaverStyle = _transform.ScreensaverStyle;
    }

    private void SyncToTransform() {
      _transform.Position = _position;
      _transform.RotationDegrees = new Vector3(_rotation.Y, _rotation.X, 0); // pitch, yaw, roll
      _transform.Scale = _scale;
      _transform.Enabled = _enabled;
      _transform.Opacity = _opacity;
      _transform.IsProjectorMode = _isProjectorMode;
      _transform.ScreensaverColor = _screensaverColor;
      _transform.ScreensaverStyle = _screensaverStyle;
    }

    public override void Draw() {
      WindowName = Loc.T("ScreenWindow.Title") + "###ScreenPlacement";

      string locKey = _plugin.LocationKey;
      bool isOutdoors = !string.IsNullOrEmpty(locKey) && locKey.StartsWith("zone_");
      bool isIsland = !string.IsNullOrEmpty(locKey) && locKey.StartsWith("island_");
      bool hasHousingMenuOpen = _plugin.IsHousingMenuOpen;
      bool hasPrivileges = isOutdoors || isIsland || hasHousingMenuOpen;

      if (!hasPrivileges) {
          ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), Loc.T("Screen.HousingRequired"));
          ImGui.TextWrapped(Loc.T("Screen.HousingRequiredBody"));
          ImGui.Spacing();
          if (ImGui.Button(Loc.T("Screen.TutorialVideo"))) {
              System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                  FileName = "https://www.youtube.com/watch?v=ZgLs2OJQ8ks",
                  UseShellExecute = true
              });
          }
          return;
      }

      if (isOutdoors && !_plugin.Config.EnableOutdoorPublicScreens) {
          ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), Loc.T("Screen.OutdoorDisabled"));
          ImGui.TextWrapped(Loc.T("Screen.OutdoorDisabledBody"));
          return;
      }

      // Enable toggle 
      if (ImGui.Checkbox(Loc.T("Screen.RenderInWorld"), ref _enabled)) {
        _transform.Enabled = _enabled;
        
        // Auto-delete from server if turning off and we own it or have privileges
        if (!_enabled && !string.IsNullOrEmpty(locKey) &&
            _plugin.CurrentTvPlacement != null && (_plugin.CurrentTvPlacement.OwnerId == _plugin.Config.OwnerId || hasPrivileges)) {
            _ = DeleteTvAsync(locKey, restoreOnFailure: true);
        } else {
            _onSave?.Invoke();
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
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f),
          Loc.T("Screen.EnableHint"));
        return;
      }

      ImGui.Separator();

      // Ctrl+Shift quick-snap logic
      bool isSnapKeyPressed = ImGui.GetIO().KeyShift && ImGui.GetIO().KeyCtrl;
      if (isSnapKeyPressed && !_wasShiftPressed) {
          unsafe {
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
                      _onSave?.Invoke();
                  }
              }
          }
      }
      _wasShiftPressed = isSnapKeyPressed;

      // Quick actions 
      if (ImGui.Button(Loc.T("Screen.PlaceAtCamera"))) {
        _onPlaceAtCamera?.Invoke();
        SyncFromTransform();
        _onSave?.Invoke();
      }
      
      ImGui.Spacing();
      ImGui.TextColored(new Vector4(0.7f, 1f, 0.7f, 1f), Loc.T("Screen.QuickSnap"));
      ImGui.TextWrapped(Loc.T("Screen.QuickSnapBody"));
      ImGui.Spacing();
      
      if (ImGui.Button(Loc.T("Screen.Save"))) {
        SyncToTransform();
        _onSave?.Invoke();
      }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.Reset"))) {
        _transform.Enabled = false;
        _enabled = false;
        SyncFromTransform();
        
        string locKey2 = _plugin.LocationKey;
        if (!string.IsNullOrEmpty(locKey2) && _plugin.CurrentTvPlacement != null && (_plugin.CurrentTvPlacement.OwnerId == _plugin.Config.OwnerId || hasPrivileges)) {
            _ = DeleteTvAsync(locKey2, restoreOnFailure: true);
        } else {
            _onSave?.Invoke();
        }
      }

      ImGui.Spacing();
      ImGui.Separator();

      // Position 
      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), Loc.T("Screen.Section.Position"));

      bool posChanged = false;
      posChanged |= ImGui.DragFloat("X##pos", ref _position.X, 0.05f, -1000f, 1000f, "%.2f");
      bool savePos = ImGui.IsItemDeactivatedAfterEdit();
      posChanged |= ImGui.DragFloat("Y##pos", ref _position.Y, 0.05f, -1000f, 1000f, "%.2f");
      savePos |= ImGui.IsItemDeactivatedAfterEdit();
      posChanged |= ImGui.DragFloat("Z##pos", ref _position.Z, 0.05f, -1000f, 1000f, "%.2f");
      savePos |= ImGui.IsItemDeactivatedAfterEdit();
      
      if (posChanged) {
        _transform.Position = _position;
      }
      if (savePos) {
        _onSave?.Invoke();
      }

      // Nudge buttons
      float nudge = 0.25f;
      if (ImGui.Button("\u2190##posX")) { _position.X -= nudge; _transform.Position = _position; _onSave?.Invoke(); }
      ImGui.SameLine();
      if (ImGui.Button("\u2192##posX")) { _position.X += nudge; _transform.Position = _position; _onSave?.Invoke(); }
      ImGui.SameLine();
      if (ImGui.Button("\u2193##posY")) { _position.Y -= nudge; _transform.Position = _position; _onSave?.Invoke(); }
      ImGui.SameLine();
      if (ImGui.Button("\u2191##posY")) { _position.Y += nudge; _transform.Position = _position; _onSave?.Invoke(); }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.Near"))) { _position.Z -= nudge; _transform.Position = _position; _onSave?.Invoke(); }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.Far"))) { _position.Z += nudge; _transform.Position = _position; _onSave?.Invoke(); }

      ImGui.Spacing();
      ImGui.Separator();

      // Rotation 
      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), Loc.T("Screen.Section.Rotation"));

      bool rotChanged = false;
      rotChanged |= ImGui.SliderFloat("Yaw##rot", ref _rotation.X, -180f, 180f, "%.1f\u00b0");
      bool saveRot = ImGui.IsItemDeactivatedAfterEdit();
      rotChanged |= ImGui.SliderFloat("Pitch##rot", ref _rotation.Y, -90f, 90f, "%.1f\u00b0");
      saveRot |= ImGui.IsItemDeactivatedAfterEdit();
      if (rotChanged) {
        _transform.RotationDegrees = new Vector3(_rotation.Y, _rotation.X, 0);
      }
      if (saveRot) {
        _onSave?.Invoke();
      }

      // Quick rotation presets
      if (ImGui.Button(Loc.T("Screen.FaceNorth"))) { _rotation.X = 0; _transform.RotationDegrees = new Vector3(_rotation.Y, 0, 0); _onSave?.Invoke(); }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.FaceEast"))) { _rotation.X = 90; _transform.RotationDegrees = new Vector3(_rotation.Y, 90, 0); _onSave?.Invoke(); }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.FaceSouth"))) { _rotation.X = 180; _transform.RotationDegrees = new Vector3(_rotation.Y, 180, 0); _onSave?.Invoke(); }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.FaceWest"))) { _rotation.X = -90; _transform.RotationDegrees = new Vector3(_rotation.Y, -90, 0); _onSave?.Invoke(); }

      ImGui.Spacing();
      ImGui.Separator();

      // Scale 
      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), Loc.T("Screen.Section.Size"));

      bool aspectChanged = false;
      aspectChanged |= ImGui.RadioButton(Loc.T("Screen.Aspect169"), ref _aspectRatio, 0);
        ImGui.SameLine();
        aspectChanged |= ImGui.RadioButton(Loc.T("Screen.Aspect43"), ref _aspectRatio, 1);
        ImGui.SameLine();
        aspectChanged |= ImGui.RadioButton(Loc.T("Screen.AspectCustom"), ref _aspectRatio, 2);
      
      bool scaleChanged = false;
        if (_aspectRatio != 2) {
            scaleChanged |= ImGui.DragFloat(Loc.T("Screen.DiagonalSize"), ref _scale.X, 0.1f, 0.5f, 200f, "%.1f");
        } else {
            scaleChanged |= ImGui.DragFloat(Loc.T("Screen.Width"), ref _scale.X, 0.1f, 0.5f, 200f, "%.1f");
            scaleChanged |= ImGui.DragFloat(Loc.T("Screen.Height"), ref _scale.Y, 0.1f, 0.5f, 200f, "%.1f");
        }
      bool saveScale = ImGui.IsItemDeactivatedAfterEdit();

      if (aspectChanged || scaleChanged) {
          if (_aspectRatio != 2) {
              float ratio = _aspectRatio == 0 ? (9f / 16f) : (3f / 4f);
              _scale.Y = _scale.X * ratio;
          }
          _transform.Scale = _scale;
        }
      if (saveScale || aspectChanged) {
        _onSave?.Invoke();
      }

      // Preset sizes
      if (ImGui.Button(Loc.T("Screen.SizeSmall"))) { _scale.X = 2f; _scale.Y = _scale.X * (_aspectRatio == 1 ? (3f/4f) : (9f/16f)); _transform.Scale = _scale; _onSave?.Invoke(); }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.SizeMedium"))) { _scale.X = 4f; _scale.Y = _scale.X * (_aspectRatio == 1 ? (3f/4f) : (9f/16f)); _transform.Scale = _scale; _onSave?.Invoke(); }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.SizeLarge"))) { _scale.X = 8f; _scale.Y = _scale.X * (_aspectRatio == 1 ? (3f/4f) : (9f/16f)); _transform.Scale = _scale; _onSave?.Invoke(); }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.SizeCinema"))) { _scale.X = 12f; _scale.Y = _scale.X * (_aspectRatio == 1 ? (3f/4f) : (9f/16f)); _transform.Scale = _scale; _onSave?.Invoke(); }

      ImGui.Spacing();
      ImGui.Separator();

      // Projector & Transparency
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
        _onSave?.Invoke();
      }

      ImGui.Spacing();
      ImGui.Separator();

      // Info 
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

      ImGui.Spacing();
      ImGui.Separator();

      ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), Loc.T("Screen.Section.RoomSync"));
      ImGui.TextWrapped(Loc.T("Screen.RoomSyncBody"));
      
      string locationKey = _plugin.LocationKey;
      bool isOutdoorsSync = !string.IsNullOrEmpty(locationKey) && locationKey.StartsWith("zone_");
      bool isIslandSync = !string.IsNullOrEmpty(locationKey) && locationKey.StartsWith("island_");
      
      if (string.IsNullOrEmpty(locationKey) || (!locationKey.StartsWith("house_") && !locationKey.StartsWith("zone_") && !locationKey.StartsWith("island_"))) {
          ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), Loc.T("Screen.MustBeInHousing"));
      } else {
          unsafe
          {
              var housingMgr = FFXIVClientStructs.FFXIV.Client.Game.HousingManager.Instance();
              if (housingMgr != null && !housingMgr->IsInside() && housingMgr->GetCurrentPlot() >= 0 && housingMgr->GetCurrentWard() >= 0)
              {
                  ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), Loc.T("Settings.StandingInPlot", housingMgr->GetCurrentPlot() + 1));
              }
          }
          
          ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), Loc.T("Settings.PlacementKey"));
          ImGui.SameLine();
          ImGui.Text(locationKey);

          if (_plugin.CurrentTvPlacement != null)
          {
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
                          _plugin.CurrentTvPlacement = new Networking.Models.TvPlacement {
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
              } else {
                  if (_plugin.CurrentTvPlacement.IsLocked) {
                      ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), Loc.T("Screen.LockedByOwner"));
                  }
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
            bool success = await _plugin.ServerClient.DeleteTvAsync(serverLocationKey, currentPlacement.Id, _plugin.Config.OwnerId, _plugin.IsHousingMenuOpen || isOutdoorsSync || isIslandSync);
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
            } else {
                RestoreEnabledAfterDeleteFailure(restoreOnFailure);
                _statusMessage = Loc.T("Screen.RemoveFailed");
                _statusColor = new Vector4(1, 0.3f, 0.3f, 1);
                _plugin.Chat.PrintError(Loc.T("Chat.Prefix") + Loc.T("Screen.RemoveFailed"));
                return false;
            }
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

    /// <summary>
    /// Handles world-space click-drag interaction on the video quad.
    /// Call this from the main draw loop with the projected screen coordinates
    /// of the quad center. Returns true if drag is active.
    /// </summary>
    public bool HandleWorldDrag(Vector2 screenCenter, float screenRadius) {
      if (!_enabled) return false;

      var mousePos = ImGui.GetMousePos();
      float dist = Vector2.Distance(mousePos, screenCenter);

      if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && dist < screenRadius) {
        _isDragging = true;
        _dragStartMouse = mousePos;
        _dragStartPosition = _transform.Position;
      }

      if (_isDragging) {
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
          var delta = ImGui.GetMousePos() - _dragStartMouse;
          // Convert screen delta to world delta (approximate: 0.01 world units per pixel)
          float sensitivity = 0.01f;
          _transform.Position = _dragStartPosition + new Vector3(
            delta.X * sensitivity,
            -delta.Y * sensitivity,
            0);
          SyncFromTransform();
          return true;
        } else {
          if (_isDragging) {
             _onSave?.Invoke();
          }
          _isDragging = false;
        }
      }

      return false;
    }

    private DateTime _lastRegistrationTime = DateTime.MinValue;

    public async void RegisterTvAsync(string locationKey) {
      if (!_enabled) {
        _statusMessage = Loc.T("Screen.NotEnabled");
        _statusColor = new Vector4(1, 0.3f, 0.3f, 1);
        return;
      }

      if ((DateTime.UtcNow - _lastRegistrationTime).TotalSeconds < 2) {
          return; // Debounce to prevent double-logs from FFXIV UI flickering
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
      _onSave?.Invoke();

      try 
      {
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
      } 
      catch (UnauthorizedAccessException) 
      {
        _statusMessage = Loc.T("Screen.CannotMoveLocked");
        _statusColor = new Vector4(1, 0.3f, 0.3f, 1);
        _plugin.Chat.PrintError(Loc.T("Chat.Prefix") + Loc.T("Screen.CannotMoveLocked"));
      }
      catch (Exception)
      {
        _statusMessage = Loc.T("Screen.SyncNetworkError");
        _statusColor = new Vector4(1, 0.3f, 0.3f, 1);
        _plugin.Chat.PrintError(Loc.T("Chat.Prefix") + Loc.T("Screen.SyncNetworkError"));
      }
    }

  }
}


