using System;
using System.Collections.Generic;
using System.Text;

namespace EcoRecipeExtractor
{
    public class AvailabilitySettings
    {
        public Dictionary<string, decimal> MaterialPrices { get; set; } = new Dictionary<string, decimal>();
        public Dictionary<string, decimal> ByproductHandlingPrices { get; set; } = new Dictionary<string, decimal>();

        public List<string> AvailableTables { get; set; } = new List<string>();
        public Dictionary<string, int> AvailableSkills { get; set; } = new Dictionary<string, int>();

        public decimal CostPerLaborPoint { get; set; } = 1 / 1000.0m;
        public decimal CostPerMinute { get; set; } = 0.1m;
        public decimal NormalMargin { get; set; } = 0.1m;
        public decimal DemandMargin { get; set; } = 0.5m;
    }
}
