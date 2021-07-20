using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EcoRecipeExtractor.Models
{
    public class Recipe
    {
        public string DispCraftStn { get; set; }
        public string CheckImage { get; set; }
        public string Untranslated { get; set; }
        public List<(string name1, string name2)> CraftStn { get; set; } = new List<(string name1, string name2)>();
        public List<(string name1, int level, string name2)> SkillNeeds { get; set; } = new List<(string name1, int level, string name2)>();
        public List<string> ModuleNeeds { get; set; } = new List<string>();
        public double BaseCraftTime { get; set; }
        public double BaseLaborCost { get; set; }
        public double BaseXpGain { get; set; }
        public string DefaultVariant { get; set; }
        public string DefaultVariantUntranslated { get; set; }
        public long NumberOfVariants { get; set; }
        public Dictionary<string, RecipeVariant> Variants { get; set; } = new Dictionary<string, RecipeVariant>();

        public override string ToString()
        {
            return Untranslated;
        }
    }

    public class RecipeVariant
    {
        public string Untranslated { get; set; }
        public List<(string type, string name1, long quantity, bool unknown, string name2)> Ingredients { get; set; } = new List<(string type, string name1, long quantity, bool cannotBeReducedViaModules, string name2)>();
        public List<(string name1, long quantity, string name2)> Products { get; set; } = new List<(string name1, long quantity, string name2)>();

        public override string ToString()
        {
            return $"{Untranslated} ({string.Join(" + ", Ingredients.Select(i => $"{i.quantity} {i.name1}"))} -> {string.Join(" + ", Products.Select(i => $"{i.quantity} {i.name1}"))})";
        }
    }
}
