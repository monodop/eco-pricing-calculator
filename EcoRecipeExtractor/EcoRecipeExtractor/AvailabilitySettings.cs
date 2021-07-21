using System;
using System.Collections.Generic;
using System.Text;

namespace EcoRecipeExtractor
{
    public class AvailabilitySettings
    {
        public Dictionary<string, decimal> ItemPrices { get; set; } = new Dictionary<string, decimal>();
        public Dictionary<string, decimal> TagPrices { get; set; } = new Dictionary<string, decimal>();
        public Dictionary<string, decimal> ItemByproductHandlingPrices { get; set; } = new Dictionary<string, decimal>();
        public Dictionary<string, decimal> TagByproductHandlingPrices { get; set; } = new Dictionary<string, decimal>();

        public List<string> AvailableTables { get; set; } = new List<string>();
        public Dictionary<string, int> AvailableSkills { get; set; } = new Dictionary<string, int>();

        public decimal CostPerLaborPoint { get; set; } = 1 / 1000.0m;
        public decimal CostPerMinute { get; set; } = 0.1m;
        public decimal NormalDemandMargin { get; set; } = 0.1m;
        public decimal HighDemandMargin { get; set; } = 0.5m;
    }
}
