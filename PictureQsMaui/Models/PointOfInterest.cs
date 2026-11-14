namespace PictureQsMaui.Models;

public class PointOfInterest
{
    public string Text { get; set; } = string.Empty;
    public double XPercent { get; set; }
    public double YPercent { get; set; }
    public List<PoiShape> Shapes { get; set; } = new();
}

public class PoiShape
{
    public List<PoiShapePoint> Points { get; set; } = new();
    public bool IsArea { get; set; }
}

public class PoiShapePoint
{
    public double XPercent { get; set; }
    public double YPercent { get; set; }
}
