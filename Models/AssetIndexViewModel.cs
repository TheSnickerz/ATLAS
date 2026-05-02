namespace ATLAS.Models;

public class AssetIndexViewModel
{
    public List<Asset>  Assets             { get; set; } = new();
    public int          Page               { get; set; }
    public int          PageSize           { get; set; }
    public int          TotalFilteredCount { get; set; }
    public int          TotalPages         => (int)Math.Ceiling((double)TotalFilteredCount / PageSize);
    public string?      Search             { get; set; }
    public string?      VlanFilter         { get; set; }
    public List<string> AvailableVlans     { get; set; } = new();
}
