using System.Text.Json;

namespace PictureQs.Models
{
    public class PointOfInterest
    {
        public string Text { get; set; } = string.Empty;
        public double XPercent { get; set; }
        public double YPercent { get; set; }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }

        public static PointOfInterest? FromJson(string json)
        {
            return JsonSerializer.Deserialize<PointOfInterest>(json);
        }
    }
}