using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EcoRecipeExtractor
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var loader = new WikiExtractor();
            var recipeData = await loader.GetRecipesAsync();
            var recipes = recipeData.Recipes;

            var itemData = await loader.GetItemDataAsync();
            var items = itemData.Items;
            var tags = itemData.Tags;

            var settings = new AvailabilitySettings()
            {
                AvailableSkills = new Dictionary<string, int>()
                {
                    { "Campfire Cooking", 0 },
                },
                AvailableTables = new List<string>()
                {
                    "Campfire",
                },
                MaterialPrices = new Dictionary<string, decimal>()
                {
                    //{ "Agave Leaves", 1 },
                    { "Birch Log", 1 },
                },
            };
            var graph = new RecipeGraph(recipeData, itemData, settings);
            var prices = graph.GetPrices();
            foreach (var (item, price) in prices.OrderBy(kvp => kvp.Key))
            {
                Console.WriteLine($"{item} {price.ToString(settings)}");
            }
        }
    }
}
