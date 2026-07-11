using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer.Windows {
  internal partial class ScreenSettingsWindow {
    private void DrawTransformTab() {
      if (!_enabled) {
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Loc.T("Screen.EnableHint"));
        return;
      }

      DrawPositionSection();
      ImGui.Spacing();
      ImGui.Separator();
      DrawRotationSection();
      ImGui.Spacing();
      ImGui.Separator();
      DrawSizeSection();
    }

    private void DrawPositionSection() {
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
        InvokeSave();
      }

      float nudge = 0.25f;
      if (ImGui.Button("\u2190##posX")) { _position.X -= nudge; _transform.Position = _position; InvokeSave(); }
      ImGui.SameLine();
      if (ImGui.Button("\u2192##posX")) { _position.X += nudge; _transform.Position = _position; InvokeSave(); }
      ImGui.SameLine();
      if (ImGui.Button("\u2193##posY")) { _position.Y -= nudge; _transform.Position = _position; InvokeSave(); }
      ImGui.SameLine();
      if (ImGui.Button("\u2191##posY")) { _position.Y += nudge; _transform.Position = _position; InvokeSave(); }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.Near"))) { _position.Z -= nudge; _transform.Position = _position; InvokeSave(); }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.Far"))) { _position.Z += nudge; _transform.Position = _position; InvokeSave(); }
    }

    private void DrawRotationSection() {
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
        InvokeSave();
      }

      if (ImGui.Button(Loc.T("Screen.FaceNorth"))) { _rotation.X = 0; _transform.RotationDegrees = new Vector3(_rotation.Y, 0, 0); InvokeSave(); }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.FaceEast"))) { _rotation.X = 90; _transform.RotationDegrees = new Vector3(_rotation.Y, 90, 0); InvokeSave(); }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.FaceSouth"))) { _rotation.X = 180; _transform.RotationDegrees = new Vector3(_rotation.Y, 180, 0); InvokeSave(); }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.FaceWest"))) { _rotation.X = -90; _transform.RotationDegrees = new Vector3(_rotation.Y, -90, 0); InvokeSave(); }
    }

    private void DrawSizeSection() {
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
        InvokeSave();
      }

      float ratioForPreset = _aspectRatio == 1 ? (3f / 4f) : (9f / 16f);
      if (ImGui.Button(Loc.T("Screen.SizeSmall"))) { _scale.X = 2f; _scale.Y = _scale.X * ratioForPreset; _transform.Scale = _scale; InvokeSave(); }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.SizeMedium"))) { _scale.X = 4f; _scale.Y = _scale.X * ratioForPreset; _transform.Scale = _scale; InvokeSave(); }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.SizeLarge"))) { _scale.X = 8f; _scale.Y = _scale.X * ratioForPreset; _transform.Scale = _scale; InvokeSave(); }
      ImGui.SameLine();
      if (ImGui.Button(Loc.T("Screen.SizeCinema"))) { _scale.X = 12f; _scale.Y = _scale.X * ratioForPreset; _transform.Scale = _scale; InvokeSave(); }
    }
  }
}
