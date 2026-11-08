using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game;
using XivMediaPlayer.LocalUi;

namespace XivMediaPlayer
{
    public sealed partial class Plugin
    {
        private PlaybackEdgeTabWindow? _playbackEdgeWindow;
        private readonly PlaybackDockMotion _dockMotion = new();
        private Vector2 _playbackPanelRest;
        private Vector2 _playbackPanelSize = new(480f, 180f);
        private bool _playbackPanelPlaced;
        private bool _plotDockActive;
        private bool _screenEdgeFallback;
        private bool _ignoreNextPanelClose;
        private bool _plotPlaceTracked;
        private int _keepPlaybackPanelTicks;
        private uint _plotDockWorldId;
        private HousingDistrict _plotDockDistrict;
        private int _plotDockWard;
        private int _plotDockPlot;
        private bool _plotDockIndoor;

        internal List<RegisteredPlot> RegisteredPlots
        {
            get
            {
                _config.RegisteredPlots ??= new List<RegisteredPlot>();
                return _config.RegisteredPlots;
            }
        }

        internal static string FormatHousePlot(string worldName, HousingDistrict district, bool subdivision, int ward, int plot, bool? indoor)
        {
            string text = DistrictLabel(district);
            if (subdivision)
                text += "分区";
            text += $" 第 {ward + 1} 区 {plot + 1} 号";
            if (!string.IsNullOrWhiteSpace(worldName))
                text = worldName.Trim() + " · " + text;
            if (indoor == true)
                text += " · 室内";
            else if (indoor == false)
                text += " · 室外";
            return text;
        }

        internal HousePlotRead ReadHousePlot()
        {
            var read = new HousePlotRead { Kind = HousePlotReadKind.NotOnPlot, WorldName = "" };
            try
            {
                uint territoryId = _clientState.TerritoryType;
                if (territoryId == 0 || !TryClassifyHousingTerritory(territoryId, out HousingDistrict district, out bool indoorMap))
                    return read;

                unsafe
                {
                    var housingMgr = HousingManager.Instance();
                    if (housingMgr == null)
                        return Unknown();

                    if (indoorMap)
                        return ReadIndoorHouse(housingMgr, district);

                    int ward = housingMgr->GetCurrentWard();
                    int plot = housingMgr->GetCurrentPlot();
                    if (housingMgr->IsInside())
                        return Unknown();
                    if (ward < 0)
                        return Unknown();
                    if (plot < 0)
                        return read;
                    bool subdivision = IsHousingSubdivision(housingMgr->GetCurrentDivision(), territoryId);

                    uint worldId = _cachedLocalPlayerWorldId;
                    if (worldId == 0)
                        return Unknown();

                    read.Kind = HousePlotReadKind.OnPlot;
                    read.WorldId = worldId;
                    read.WorldName = ReadCurrentWorldName();
                    read.District = district;
                    read.Subdivision = subdivision;
                    read.Ward = ward;
                    read.Plot = plot;
                    read.Indoor = false;
                    return read;
                }
            }
            catch
            {
                return Unknown();
            }
        }

        internal bool IsPlotRegistered(HousePlotRead read)
            => read.Kind == HousePlotReadKind.OnPlot && FindRegisteredPlot(read) != null;

        internal bool RegisterCurrentPlot()
        {
            HousePlotRead read = ReadHousePlot();
            if (read.Kind != HousePlotReadKind.OnPlot || FindRegisteredPlot(read) != null)
                return false;

            RegisteredPlots.Add(new RegisteredPlot
            {
                WorldId = read.WorldId,
                WorldName = read.WorldName ?? "",
                District = read.District,
                Subdivision = read.Subdivision,
                Ward = read.Ward,
                Plot = read.Plot,
            });
            _config.SaveLocal();
            return true;
        }

        internal void RemoveRegisteredPlotAt(int index)
        {
            var plots = RegisteredPlots;
            if ((uint)index >= (uint)plots.Count)
                return;
            KeepPlaybackPanelOpen();
            plots.RemoveAt(index);
            _config.SaveLocal();
        }

        internal void KeepPlaybackPanelOpen()
        {
            if (_localMediaWindow?.IsOpen == true)
                _keepPlaybackPanelTicks = 8;
        }

        internal bool PlaybackDockMoving => _dockMotion.Active;
        internal float PlaybackPanelReveal => _dockMotion.Active ? _dockMotion.PanelAlpha : 1f;
        internal float PlaybackEdgeReveal =>
            _config.KeepEdgeIconWithPanel || !_dockMotion.Active ? 1f : 1f - _dockMotion.PanelAlpha;
        internal Vector2 PlaybackPanelMotionPos => _dockMotion.PanelPos;

        internal void NotePlaybackPanelBounds(Vector2 pos, Vector2 size)
        {
            if (size.X < 8f || size.Y < 8f)
                return;
            _playbackPanelRest = pos;
            _playbackPanelSize = size;
            _playbackPanelPlaced = true;
        }

        internal bool IsPlaybackPanelOpen => _localMediaWindow?.IsOpen == true;

        internal void TogglePlaybackPanelFromEdge()
        {
            if (_localMediaWindow == null)
                return;

            if (_config.KeepEdgeIconWithPanel && _localMediaWindow.IsOpen)
            {
                BeginClosePlaybackPanel();
                return;
            }

            OpenPlaybackPanel();
        }

        internal void OpenPlaybackPanel()
        {
            if (_localMediaWindow == null)
                return;

            bool fromEdge = _playbackEdgeWindow?.IsOpen == true || _dockMotion.Active;
            _localMediaWindow.IsOpen = true;
            if (!fromEdge || _playbackEdgeWindow == null)
            {
                _dockMotion.Stop();
                if (_playbackEdgeWindow != null)
                    _playbackEdgeWindow.IsOpen = _plotDockActive && _config.KeepEdgeIconWithPanel;
                return;
            }

            _dockMotion.Begin(true, CollapsedPanelPos(), PanelRestPos());
            _playbackEdgeWindow.IsOpen = true;
        }

        internal void OnPlaybackPanelClosed(bool hostNotify = false)
        {
            if (hostNotify && _ignoreNextPanelClose)
            {
                _ignoreNextPanelClose = false;
                return;
            }

            BeginClosePlaybackPanel();
        }

        private void BeginClosePlaybackPanel()
        {
            if (_localMediaWindow == null)
                return;
            if (!_plotDockActive || _playbackEdgeWindow == null)
            {
                _dockMotion.Stop();
                _localMediaWindow.IsOpen = false;
                return;
            }

            _localMediaWindow.IsOpen = true;
            _playbackEdgeWindow.IsOpen = true;
            if (_dockMotion.Active && !_dockMotion.Opening)
                return;

            _dockMotion.Begin(false, CollapsedPanelPos(), PanelRestPos());
        }

        private void UpdatePlotPlaybackDock()
        {
            if (_keepPlaybackPanelTicks > 0)
                _keepPlaybackPanelTicks--;

            bool motionDone = _dockMotion.Advance();

            HousePlotRead read = ReadHousePlot();
            if (read.Kind == HousePlotReadKind.Unknown)
            {
                if (motionDone && _dockMotion.Active)
                    FinishDockMotion();
                return;
            }

            bool placeChanged = read.Kind != HousePlotReadKind.OnPlot || !_plotPlaceTracked || !SameTrackedPlace(read);
            if (read.Kind == HousePlotReadKind.OnPlot)
            {
                RememberPlace(read);
                _plotPlaceTracked = true;
            }
            else
            {
                _plotPlaceTracked = false;
            }

            RegisteredPlot? match = read.Kind == HousePlotReadKind.OnPlot ? FindRegisteredPlot(read) : null;
            if (match == null || !ScopeAllows(match, read.Indoor))
            {
                if (ShouldShowScreenEdgeFallback())
                {
                    bool leavingPlotDock = _plotDockActive && !_screenEdgeFallback;
                    _screenEdgeFallback = true;
                    _plotDockActive = true;
                    if (leavingPlotDock && placeChanged && !_dockMotion.Active && _localMediaWindow?.IsOpen == true && _keepPlaybackPanelTicks == 0)
                        _localMediaWindow.IsOpen = false;
                    EnsurePlotDockVisible();
                    if (motionDone && _dockMotion.Active)
                        FinishDockMotion();
                    return;
                }

                _screenEdgeFallback = false;
                if (_plotDockActive)
                    DismissPlotPlaybackDock(closePanel: placeChanged && _keepPlaybackPanelTicks == 0);
                return;
            }

            _screenEdgeFallback = false;
            _plotDockActive = true;
            if (placeChanged)
                ApplyPlotArrival(match);
            EnsurePlotDockVisible();
            if (motionDone && _dockMotion.Active)
                FinishDockMotion();
        }

        private bool ShouldShowScreenEdgeFallback()
        {
            if (!_config.ShowEdgeIconWhenScreenDetected)
                return false;

            uint territoryId = _clientState.TerritoryType;
            if (territoryId == 0 || !TryClassifyHousingTerritory(territoryId, out _, out _))
                return false;

            return HasRoomTvsForCurrentLocation();
        }

        private void FinishDockMotion()
        {
            bool opening = _dockMotion.Opening;
            _dockMotion.Stop();
            if (opening)
            {
                if (_playbackEdgeWindow != null && !_config.KeepEdgeIconWithPanel)
                    _playbackEdgeWindow.IsOpen = false;
                return;
            }

            if (_localMediaWindow != null)
            {
                _ignoreNextPanelClose = true;
                _localMediaWindow.IsOpen = false;
            }
        }

        private Vector2 PanelRestPos()
        {
            if (_playbackPanelPlaced)
                return _playbackPanelRest;

            Vector2 collapsed = CollapsedPanelPos();
            var viewport = ImGui.GetMainViewport();
            Vector2 center = viewport.Pos + viewport.Size * 0.5f;
            Vector2 dir = center - collapsed;
            float len = dir.Length();
            if (len < 1f)
                return collapsed + new Vector2(-120f, 40f);
            return collapsed + dir / len * 160f;
        }

        private Vector2 CollapsedPanelPos()
            => PlaybackEdgeTabWindow.CollapsedWindowPos(_playbackPanelSize, _config.PlaybackEdgeSide, _config.PlaybackEdgeAnchor);

        private bool SameTrackedPlace(HousePlotRead read)
            => _plotDockWorldId == read.WorldId
            && _plotDockDistrict == read.District
            && _plotDockWard == read.Ward
            && _plotDockPlot == read.Plot
            && _plotDockIndoor == read.Indoor;

        private void RememberPlace(HousePlotRead read)
        {
            _plotDockWorldId = read.WorldId;
            _plotDockDistrict = read.District;
            _plotDockWard = read.Ward;
            _plotDockPlot = read.Plot;
            _plotDockIndoor = read.Indoor;
        }

        private void DismissPlotPlaybackDock(bool closePanel = true)
        {
            _plotDockActive = false;
            _dockMotion.Stop();
            if (_playbackEdgeWindow != null)
                _playbackEdgeWindow.IsOpen = false;
            if (closePanel && _localMediaWindow != null)
                _localMediaWindow.IsOpen = false;
        }

        private void ApplyPlotArrival(RegisteredPlot plot)
        {
            if (plot.Arrival == PlotArrivalAction.OpenPanel)
                OpenPlaybackPanel();
            else if (_localMediaWindow != null && _keepPlaybackPanelTicks == 0)
                _localMediaWindow.IsOpen = false;
        }

        private void EnsurePlotDockVisible()
        {
            if (_dockMotion.Active)
            {
                if (_localMediaWindow != null)
                    _localMediaWindow.IsOpen = true;
                if (_playbackEdgeWindow != null)
                    _playbackEdgeWindow.IsOpen = true;
                return;
            }

            if (_localMediaWindow?.IsOpen == true)
            {
                if (_playbackEdgeWindow != null)
                    _playbackEdgeWindow.IsOpen = _config.KeepEdgeIconWithPanel;
                return;
            }

            if (_playbackEdgeWindow != null)
                _playbackEdgeWindow.IsOpen = true;
        }

        private RegisteredPlot? FindRegisteredPlot(HousePlotRead read)
        {
            foreach (RegisteredPlot plot in RegisteredPlots)
            {
                if (plot != null
                    && plot.WorldId == read.WorldId
                    && plot.District == read.District
                    && plot.Ward == read.Ward
                    && plot.Plot == read.Plot)
                    return plot;
            }

            return null;
        }

        private static bool ScopeAllows(RegisteredPlot plot, bool indoor)
        {
            if (plot.Scope == PlotTriggerScope.Outdoor)
                return !indoor;
            if (plot.Scope == PlotTriggerScope.Indoor)
                return indoor;
            return true;
        }

        private unsafe HousePlotRead ReadIndoorHouse(HousingManager* housingMgr, HousingDistrict district)
        {
            int ward = housingMgr->GetCurrentWard();
            int plot = housingMgr->GetCurrentPlot();
            HouseId house = ReadBoundHouse(housingMgr);
            if (house.Id != 0 && (house.IsApartment || house.IsWorkshop) && plot < 0)
                return new HousePlotRead { Kind = HousePlotReadKind.NotOnPlot, WorldName = "" };

            if (ward < 0 && house.Id != 0)
                ward = house.WardIndex;
            if (plot < 0 && house.Id != 0)
                plot = house.PlotIndex;
            if (ward < 0 || plot < 0)
                return Unknown();

            uint worldId = _cachedLocalPlayerWorldId;
            if (worldId == 0)
                worldId = house.WorldId;
            if (worldId == 0)
                return Unknown();

            bool subdivision = IsHousingSubdivision(housingMgr->GetCurrentDivision(), house.TerritoryTypeId);

            return new HousePlotRead
            {
                Kind = HousePlotReadKind.OnPlot,
                WorldId = worldId,
                WorldName = ReadCurrentWorldName(),
                District = district,
                Subdivision = subdivision,
                Ward = ward,
                Plot = plot,
                Indoor = true,
            };
        }

        private unsafe HouseId ReadBoundHouse(HousingManager* housingMgr)
        {
            if (housingMgr->IndoorTerritory != null && housingMgr->IndoorTerritory->HouseId.Id != 0)
                return housingMgr->IndoorTerritory->HouseId;
            HouseId house = housingMgr->GetCurrentIndoorHouseId();
            if (house.Id != 0)
                return house;
            return housingMgr->GetCurrentHouseId();
        }

        private static bool IsHousingSubdivision(int division, uint territoryId)
            => division == 2 || territoryId is 344 or 345 or 346 or 650 or 980;

        private static bool TryClassifyHousingTerritory(uint territoryId, out HousingDistrict district, out bool indoor)
        {
            switch (territoryId)
            {
                case 339:
                case 344:
                    district = HousingDistrict.Mist;
                    indoor = false;
                    return true;
                case 282:
                case 283:
                case 284:
                    district = HousingDistrict.Mist;
                    indoor = true;
                    return true;
                case 340:
                case 345:
                    district = HousingDistrict.LavenderBeds;
                    indoor = false;
                    return true;
                case 285:
                case 286:
                case 287:
                    district = HousingDistrict.LavenderBeds;
                    indoor = true;
                    return true;
                case 341:
                case 346:
                    district = HousingDistrict.Goblet;
                    indoor = false;
                    return true;
                case 288:
                case 289:
                case 290:
                    district = HousingDistrict.Goblet;
                    indoor = true;
                    return true;
                case 641:
                case 650:
                    district = HousingDistrict.Shirogane;
                    indoor = false;
                    return true;
                case 649:
                case 651:
                case 652:
                    district = HousingDistrict.Shirogane;
                    indoor = true;
                    return true;
                case 979:
                case 980:
                    district = HousingDistrict.Empyreum;
                    indoor = false;
                    return true;
                case 981:
                case 982:
                case 983:
                    district = HousingDistrict.Empyreum;
                    indoor = true;
                    return true;
                default:
                    district = default;
                    indoor = false;
                    return false;
            }
        }

        private static string DistrictLabel(HousingDistrict district)
        {
            switch (district)
            {
                case HousingDistrict.Mist: return "海雾村";
                case HousingDistrict.LavenderBeds: return "薰衣草苗圃";
                case HousingDistrict.Goblet: return "高脚孤丘";
                case HousingDistrict.Shirogane: return "白银乡";
                case HousingDistrict.Empyreum: return "穹顶皓天";
                default: return "住宅区";
            }
        }

        private string ReadCurrentWorldName()
        {
            try
            {
                if (_objectTable[0] is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player
                    && player.CurrentWorld.IsValid)
                    return player.CurrentWorld.Value.Name.ExtractText();
            }
            catch
            {
                return "";
            }

            return "";
        }

        private static HousePlotRead Unknown()
            => new HousePlotRead { Kind = HousePlotReadKind.Unknown, WorldName = "" };
    }
}
