using System;
using System.Collections.Generic;
using System.Text;

namespace EcoRecipeExtractor.Models
{
    public class Item
    {
        public string Untranslated { get; set; }
        public string Category { get; set; }
        public string Group { get; set; }
        public string Description { get; set; }
        public List<string> TagGroups { get; set; } = new List<string>();
        public int MaxStack { get; set; }
        public string Carried { get; set; }
        public double? Weight { get; set; }
        public double? Calories { get; set; }
        public double? Carbs { get; set; }
        public double? Protein { get; set; }
        public double? Fat { get; set; }
        public double? Vitamins { get; set; }
        public double? Density { get; set; }
        public int? Fuel { get; set; }
        public string? Yield { get; set; }
        public bool? Currency { get; set; }
        public double? SkillValue { get; set; }
        public string? RoomCategory { get; set; }
        public string? FurnitureType { get; set; }
        public double? RepeatsDepreciation { get; set; }
        public int? MaterialTier { get; set; }
        public string? FuelsUsed { get; set; }
        public int? GridRadius { get; set; }
        public int? EnergyUsed { get; set; }
        public int? EnergyProduced { get; set; }
        public string? EnergyType { get; set; }
        public List<(string fluid, double amount)> FluidsUsed { get; set; } = new List<(string fluid, double amount)>();
        public List<(string fluid, double amount)> FluidsProduced { get; set; } = new List<(string fluid, double amount)>();
        public List<string> ValidTalents { get; set; } = new List<string>();
        public string? Footprint { get; set; }
        public bool? Mobile { get; set; }
        public int? RoomSizeReq { get; set; }
        public string? RoomMatReq { get; set; }
        public bool? RoomContainReq { get; set; }
        public long? InventorySlots { get; set; }
        public long? InventoryMaxWeight { get; set; }
        public List<(string error, string message)> InventoryRestrictions { get; set; } = new List<(string error, string message)>();
        public List<(string fertilizer, double amount)> FertilizerNutrients { get; set; } = new List<(string fertilizer, double amount)>();
        public string Type { get; set; }
        public int TypeId { get; set; }
    }
}
