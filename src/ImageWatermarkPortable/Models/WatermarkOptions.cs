namespace ImageWatermarkPortable.Models;

public sealed class WatermarkOptions
{
    public string Text { get; set; } = string.Empty;
    public int OpacityPercent { get; set; } = 25;
    public int FontSizePx { get; set; } = 36;
    public Color Color { get; set; } = Color.White;
}
