namespace XivMediaPlayer
{
    public enum HousingDistrict : byte
    {
        Mist = 1,
        LavenderBeds = 2,
        Goblet = 3,
        Shirogane = 4,
        Empyreum = 5,
    }

    public enum PlotTriggerScope : byte
    {
        Both = 0,
        Outdoor = 1,
        Indoor = 2,
    }

    public enum PlotArrivalAction : byte
    {
        EdgeTab = 0,
        OpenPanel = 1,
    }

    public enum PlaybackScreenEdge : byte
    {
        Left = 0,
        Right = 1,
        Top = 2,
        Bottom = 3,
    }

    internal enum HousePlotReadKind
    {
        Unknown,
        NotOnPlot,
        OnPlot,
    }

    internal struct HousePlotRead
    {
        public HousePlotReadKind Kind;
        public uint WorldId;
        public string WorldName;
        public HousingDistrict District;
        public bool Subdivision;
        public int Ward;
        public int Plot;
        public bool Indoor;
    }

    public sealed class RegisteredPlot
    {
        public uint WorldId { get; set; }
        public string WorldName { get; set; } = "";
        public HousingDistrict District { get; set; }
        public bool Subdivision { get; set; }
        public int Ward { get; set; }
        public int Plot { get; set; }
        public PlotTriggerScope Scope { get; set; } = PlotTriggerScope.Both;
        public PlotArrivalAction Arrival { get; set; } = PlotArrivalAction.EdgeTab;
    }
}
