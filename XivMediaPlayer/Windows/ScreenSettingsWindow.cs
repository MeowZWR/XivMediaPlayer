using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using MediaPlayerCore.Compositing;
using XivMediaPlayer.Compositing;
using System;
using System.Numerics;
using Dalamud.Plugin.Services;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer.Windows {
  /// <summary>
  /// ImGui settings window for interactively positioning the world-space video screen.
  /// </summary>
  internal partial class ScreenSettingsWindow : Window {
    private readonly WorldScreenTransform _transform;
    private readonly WorldVideoRenderer _renderer;
    private readonly Action _onSave;
    private readonly Action _onPlaceAtCamera;
    private readonly Plugin _plugin;
    private readonly IGameGui _gameGui;

    internal string _statusMessage = "";
    internal Vector4 _statusColor = new Vector4(1, 1, 1, 1);

    internal Vector3 _position;
    internal Vector2 _rotation; // yaw, pitch
    internal Vector2 _scale;
    internal bool _enabled;
    private bool _wasShiftPressed;
    internal int _aspectRatio = 0; // 0 = 16:9, 1 = 4:3

    internal float _opacity = 1.0f;
    internal bool _isProjectorMode = false;
    internal Vector3 _screensaverColor = new Vector3(0.0f, 0.0f, 0.0f);
    internal int _screensaverStyle = 0;

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

      Size = new Vector2(360, 0);
      SizeCondition = ImGuiCond.FirstUseEver;

      SyncFromTransform();
    }

    public void SyncFromTransform() {
      _position = _transform.Position;
      _rotation = new Vector2(_transform.RotationDegrees.Y, _transform.RotationDegrees.X);
      _scale = _transform.Scale;
      _enabled = _transform.Enabled;
      _opacity = _transform.Opacity;
      _isProjectorMode = _transform.IsProjectorMode;
      _screensaverColor = _transform.ScreensaverColor;
      _screensaverStyle = _transform.ScreensaverStyle;
    }

    internal void SyncToTransform() {
      _transform.Position = _position;
      _transform.RotationDegrees = new Vector3(_rotation.Y, _rotation.X, 0);
      _transform.Scale = _scale;
      _transform.Enabled = _enabled;
      _transform.Opacity = _opacity;
      _transform.IsProjectorMode = _isProjectorMode;
      _transform.ScreensaverColor = _screensaverColor;
      _transform.ScreensaverStyle = _screensaverStyle;
    }

    internal void InvokeSave() => _onSave?.Invoke();

    internal void InvokePlaceAtCamera() {
      _onPlaceAtCamera?.Invoke();
      SyncFromTransform();
      InvokeSave();
    }

    internal bool HasPlacementPrivileges() {
      string locKey = _plugin.LocationKey;
      bool isOutdoors = !string.IsNullOrEmpty(locKey) && locKey.StartsWith("zone_");
      bool isIsland = !string.IsNullOrEmpty(locKey) && locKey.StartsWith("island_");
      return isOutdoors || isIsland || _plugin.IsHousingMenuOpen;
    }

    public override void Draw() {
      WindowName = Loc.T("ScreenWindow.Title") + "###ScreenPlacement";

      if (!DrawPrivilegeGate()) {
        return;
      }

      if (ImGui.BeginTabBar("ScreenPlacementTabs")) {
        if (ImGui.BeginTabItem(Loc.T("Screen.Tab.General"))) {
          DrawGeneralTab();
          ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("Screen.Tab.Transform"))) {
          DrawTransformTab();
          ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("Screen.Tab.Appearance"))) {
          DrawAppearanceTab();
          ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("Screen.Tab.Sync"))) {
          DrawSyncTab();
          ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(Loc.T("Screen.Tab.Settings"))) {
          DrawSettingsTab();
          ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
      }
    }

    private bool DrawPrivilegeGate() {
      string locKey = _plugin.LocationKey;
      bool isOutdoors = !string.IsNullOrEmpty(locKey) && locKey.StartsWith("zone_");
      bool hasPrivileges = HasPlacementPrivileges();

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
        return false;
      }

      if (isOutdoors && !_plugin.Config.EnableOutdoorPublicScreens) {
        ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), Loc.T("Screen.OutdoorDisabled"));
        ImGui.TextWrapped(Loc.T("Screen.OutdoorDisabledBody"));
        return false;
      }

      return true;
    }

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
          float sensitivity = 0.01f;
          _transform.Position = _dragStartPosition + new Vector3(
            delta.X * sensitivity,
            -delta.Y * sensitivity,
            0);
          SyncFromTransform();
          return true;
        }

        if (_isDragging) {
          InvokeSave();
        }
        _isDragging = false;
      }

      return false;
    }
  }
}
