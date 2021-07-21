using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EcoRecipeExtractor
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, ea) =>
            {
                ea.Cancel = true;
                try
                {
                    cts.Cancel();
                }
                catch
                {

                }
            };
            AppDomain.CurrentDomain.ProcessExit += (_, __) =>
            {
                try
                {
                    cts.Cancel();
                }
                catch
                {

                }
            };
            var cancellationToken = cts.Token;

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
                ItemPrices = new Dictionary<string, decimal>()
                {
                    //{ "Agave Leaves", 1 },
                    { "Birch Log", 1 },
                },
            };
            var sheetManager = new SheetManager(@"D:\Harrison\Downloads\eco-price-calculator-sheet-b5d2460d7eae.json", "1l0PgkpQgo_4F188z6vZR4AtCKHO3n1VYUTBFu9RgGpo");
            settings = await sheetManager.GetAvailabilitySettingsAsync(cancellationToken);

            var graph = new RecipeGraph(recipeData, itemData, settings);
            var prices = graph.GetPrices();
            foreach (var (item, price) in prices.OrderBy(kvp => kvp.Key))
            {
                Console.WriteLine($"{item} {price}");
            }

            await sheetManager.CommitPricesAsync(prices, cancellationToken);
        }
    }
}
