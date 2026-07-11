using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Controllers;
using KamiToolKit.Enums;
using KamiToolKit.Extensions;
using KamiToolKit.Nodes;
using Lumina.Text.ReadOnly;
using XivMediaPlayer.Localization;

namespace XivMediaPlayer.NativeUI;

/// <summary>
/// Adds a native icon button after the rug/carpet icon in the HousingGoods furnishing toolbar.
/// </summary>
internal unsafe class HousingScreenPlacementButton : IDisposable {
  private readonly Plugin _plugin;
  private AddonController? _housingGoodsController;
  private AddonController? _mjiHousingGoodsController;
  private IconButtonNode? _button;

  private const uint ScreenPlacementIconId = 35;
  private const float ToolbarButtonMinSize = 28f;
  private const float ToolbarButtonMaxSize = 52f;
  private const float CategoryRowMaxY = 100f;
  private const int RugCategoryIndex = 5;

  // Known HousingGoods / MJIHousingGoods rug-category node ids (validated by size before use).
  private static readonly uint[] KnownRugNodeIds = new uint[] { 36, 35, 34, 33, 32, 31, 30, 29, 57, 58, 59, 60, 61, 62 };

  public HousingScreenPlacementButton(Plugin plugin) {
    _plugin = plugin;
  }

  public void Enable() {
    if (_housingGoodsController != null) return;

    _housingGoodsController = new AddonController {
      AddonName = "HousingGoods",
      OnSetup = OnAddonSetup,
      OnRefresh = OnAddonRefresh,
      OnFinalize = _ => OnAddonFinalize(),
    };
    _housingGoodsController.Enable();

    _mjiHousingGoodsController = new AddonController {
      AddonName = "MJIHousingGoods",
      OnSetup = OnAddonSetup,
      OnRefresh = OnAddonRefresh,
      OnFinalize = _ => OnAddonFinalize(),
    };
    _mjiHousingGoodsController.Enable();
  }

  public void Disable() {
    _housingGoodsController?.Dispose();
    _housingGoodsController = null;
    _mjiHousingGoodsController?.Dispose();
    _mjiHousingGoodsController = null;
    OnAddonFinalize();
  }

  public void SyncButtonState() {
    if (_button != null) {
      _button.IsChecked = _plugin.ScreenSettingsWindow.IsOpen;
    }
  }

  public void RefreshForConfigChange() {
    if (!_plugin.Config.ShowScreenPlacementNativeButton) {
      OnAddonFinalize();
      return;
    }

    unsafe {
      var housingGoodsPtr = _plugin.GameGui.GetAddonByName("HousingGoods", 1);
      if (housingGoodsPtr.Address != nint.Zero) {
        AttachButton((AtkUnitBase*)housingGoodsPtr.Address);
        return;
      }

      var mjiHousingGoodsPtr = _plugin.GameGui.GetAddonByName("MJIHousingGoods", 1);
      if (mjiHousingGoodsPtr.Address != nint.Zero) {
        AttachButton((AtkUnitBase*)mjiHousingGoodsPtr.Address);
      }
    }
  }

  private void OnAddonSetup(AtkUnitBase* addon) => TryAttachButton(addon);

  private void OnAddonRefresh(AtkUnitBase* addon) {
    if (_plugin.Config.ShowScreenPlacementNativeButton && _button == null) {
      TryAttachButton(addon);
    }
  }

  private void OnAddonFinalize() {
    _button?.Dispose();
    _button = null;
  }

  private void TryAttachButton(AtkUnitBase* addon) {
    if (!_plugin.Config.ShowScreenPlacementNativeButton) return;
    AttachButton(addon);
  }

  private void AttachButton(AtkUnitBase* addon) {
    var anchor = FindCarpetAnchorNode(addon);
    if (anchor == null) {
      _plugin.PluginLog.Warning($"[ScreenPlacement] Rug category anchor not found in {addon->NameString}");
      return;
    }

    _button?.Dispose();
    var buttonSize = new Vector2(anchor->Width, anchor->Height);
    _button = new IconButtonNode {
      Size = buttonSize,
      IconId = ScreenPlacementIconId,
      IsChecked = _plugin.ScreenSettingsWindow.IsOpen,
    };
    _button.TextTooltip = (ReadOnlySeString)Loc.T("Screen.NativeButtonTooltip");
    _button.OnClick = () => {
      _plugin.ToggleScreenSettingsWindow();
      _button.IsChecked = _plugin.ScreenSettingsWindow.IsOpen;
    };
    _button.AttachNode(anchor, NodePosition.AfterTarget);
    _button.Position = new Vector2(anchor->X + anchor->Width, anchor->Y);
  }

  private static AtkResNode* FindCarpetAnchorNode(AtkUnitBase* addon) {
    foreach (var nodeId in KnownRugNodeIds) {
      var node = addon->UldManager.SearchNodeById(nodeId);
      if (IsCategoryToolbarButton(node)) {
        return node;
      }
    }

    return FindCarpetAnchorByHeuristic(addon);
  }

  private static bool IsCategoryToolbarButton(AtkResNode* node) {
    if (node == null) return false;
    if (node->GetNodeType() != NodeType.Component) return false;

    float width = node->Width;
    float height = node->Height;
    if (width < ToolbarButtonMinSize || width > ToolbarButtonMaxSize) return false;
    if (height < ToolbarButtonMinSize || height > ToolbarButtonMaxSize) return false;
    if (node->Y > CategoryRowMaxY) return false;

    return node->GetAsAtkComponentRadioButton() != null || node->GetAsAtkComponentButton() != null;
  }

  private const int MaxToolbarButtons = 32;

  private static AtkResNode* FindCarpetAnchorByHeuristic(AtkUnitBase* addon) {
    AtkResNode*[] candidates = new AtkResNode*[MaxToolbarButtons];
    int candidateCount = 0;
    foreach (var entry in addon->UldManager.Nodes) {
      var node = entry.Value;
      if (!IsCategoryToolbarButton(node)) continue;
      if (candidateCount >= MaxToolbarButtons) break;
      candidates[candidateCount++] = node;
    }

    if (candidateCount < 6) return null;

    SortByYThenX(candidates, candidateCount);

    AtkResNode* bestRug = null;
    int bestRowCount = 0;
    for (int i = 0; i < candidateCount; i++) {
      float baseY = candidates[i]->Y;
      AtkResNode*[] row = new AtkResNode*[MaxToolbarButtons];
      int rowCount = 0;
      for (int j = 0; j < candidateCount; j++) {
        if (Math.Abs(candidates[j]->Y - baseY) <= 3f) {
          row[rowCount++] = candidates[j];
        }
      }

      SortByX(row, rowCount);
      if (rowCount > bestRowCount && rowCount >= 6) {
        bestRowCount = rowCount;
        int rugIndex = rowCount >= 7 ? RugCategoryIndex : Math.Min(RugCategoryIndex, rowCount - 1);
        bestRug = row[rugIndex];
      }
    }

    return bestRug;
  }

  private static void SortByYThenX(AtkResNode*[] nodes, int count) {
    for (int i = 1; i < count; i++) {
      var key = nodes[i];
      int j = i - 1;
      while (j >= 0 && CompareByYThenX(nodes[j], key) > 0) {
        nodes[j + 1] = nodes[j];
        j--;
      }
      nodes[j + 1] = key;
    }
  }

  private static void SortByX(AtkResNode*[] nodes, int count) {
    for (int i = 1; i < count; i++) {
      var key = nodes[i];
      int j = i - 1;
      while (j >= 0 && nodes[j]->X > key->X) {
        nodes[j + 1] = nodes[j];
        j--;
      }
      nodes[j + 1] = key;
    }
  }

  private static int CompareByYThenX(AtkResNode* a, AtkResNode* b) {
    int yCompare = a->Y.CompareTo(b->Y);
    return yCompare != 0 ? yCompare : a->X.CompareTo(b->X);
  }

  public void Dispose() => Disable();
}
