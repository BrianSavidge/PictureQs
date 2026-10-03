using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PictureQs.Models;

namespace PictureQs.Services
{
    public class PoiStorageService
    {
        private const string FileName = "poi.json";

        public List<PointOfInterest> LoadPointsOfInterest()
        {
            if (!File.Exists(FileName))
            {
                return new List<PointOfInterest>();
            }

            var json = File.ReadAllText(FileName);
            return JsonSerializer.Deserialize<List<PointOfInterest>>(json) ?? new List<PointOfInterest>();
        }

        public void SavePointOfInterest(PointOfInterest poi)
        {
            var pois = LoadPointsOfInterest();
            pois.Add(poi);
            File.WriteAllText(FileName, JsonSerializer.Serialize(pois));
        }
    }
}