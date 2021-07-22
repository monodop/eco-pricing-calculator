using EcoRecipeExtractor.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EcoRecipeExtractor
{

    public class RecipeGraph
    {
        class ComputationProgress
        {
            public ComputationProgress(string itemName, AvailabilitySettings settings)
            {
                PriceInfo = new PriceInfo(itemName, settings);
            }

            public List<(Recipe recipe, RecipeVariant variant)> RemainingRecipesToCompute { get; set; } = new List<(Recipe recipe, RecipeVariant variant)>();
            public bool Completed { get; set; } = false;
            public PriceInfo PriceInfo { get; }
        }

        public class PriceInfo
        {
            private readonly AvailabilitySettings _settings;

            public PriceInfo(string itemName, AvailabilitySettings settings)
            {
                ItemName = itemName;
                _settings = settings;
            }

            public string ItemName { get; }
            public Recipe RecipeUsed { get; set; }
            public RecipeVariant VariantUsed { get; set; }
            public List<(string ingredient, long quantity, PriceInfo pricePerUnit)> IngredientCosts { get; set; } = new List<(string ingredient, long quantity, PriceInfo pricePerUnit)>();
            public decimal LaborCost { get; set; }
            public decimal TimeCost { get; set; }
            public decimal EnergyCost { get; set; }
            public decimal MaterialCost { get; set; }
            public decimal WasteProductHandlingCost { get; set; }

            private decimal? _ingredientCost;
            public decimal IngredientsCost
            {
                get
                {
                    if (_ingredientCost == null)
                    {
                        var result = IngredientCosts.Sum(c => c.pricePerUnit.TotalCost * c.quantity);

                        if (VariantUsed == null)
                        {
                            return result;
                        }

                        _ingredientCost = result;
                    }
                    return (decimal)_ingredientCost;
                }
            }

            private decimal? _suggestedIngredientsPrice;
            public decimal SuggestedIngredientsPrice
            {
                get
                {
                    if (_suggestedIngredientsPrice == null)
                    {
                        var result = IngredientCosts.Sum(c => c.pricePerUnit.SuggestedPrice * c.quantity);

                        if (VariantUsed == null)
                        {
                            return result;
                        }

                        _suggestedIngredientsPrice = result;
                    }
                    return (decimal)_suggestedIngredientsPrice;
                }
            }

            private decimal? _totalCost;
            public decimal TotalCost
            {
                get
                {
                    if (_totalCost == null)
                    {
                        // TODO: factor in other products?
                        var result = (LaborCost + TimeCost + EnergyCost + MaterialCost + IngredientsCost + WasteProductHandlingCost) / ((VariantUsed?.Products.Single(p => p.name1 == ItemName).quantity) ?? 1);

                        if (VariantUsed == null)
                        {
                            return result;
                        }

                        _totalCost = result;
                    }
                    return (decimal)_totalCost;
                }
            }

            private decimal? _suggestedPrice;
            public decimal SuggestedPrice
            {
                get
                {
                    if (_suggestedPrice == null)
                    {
                        var result = (LaborCost + TimeCost + EnergyCost + MaterialCost + SuggestedIngredientsPrice + WasteProductHandlingCost) / ((VariantUsed?.Products.Single(p => p.name1 == ItemName).quantity) ?? 1);
                        result = -(result / (_settings.NormalDemandMargin - 1));

                        if (VariantUsed == null)
                        {
                            return result;
                        }

                        _suggestedPrice = result;
                    }
                    return (decimal)_suggestedPrice;
                }
            }

            public decimal SuggestedMarkup => SuggestedPrice * _settings.NormalDemandMargin;

            public string FormatCurrency(decimal value)
            {
                return value.ToString("0.00");
            }

            public override string ToString()
            {
                var totalCost = TotalCost;
                var suggestedPrice = SuggestedPrice;

                if (IngredientCosts.Count == 0)
                {
                    return $"(${FormatCurrency(totalCost)} - ${FormatCurrency(suggestedPrice)})";
                }

                var breakdown = string.Join(" + ", IngredientCosts.Select(c => $"{c.ingredient} (${FormatCurrency(c.pricePerUnit.TotalCost * c.quantity)} - ${FormatCurrency(c.pricePerUnit.SuggestedPrice * c.quantity)})"));
                var addtBreakdown = $"Labor (${FormatCurrency(LaborCost)}) + Time (${FormatCurrency(TimeCost)}) + Energy (${FormatCurrency(EnergyCost)}) + Waste (${FormatCurrency(WasteProductHandlingCost)}) + Markup ($0.00 - ${FormatCurrency(suggestedPrice * _settings.NormalDemandMargin)})";
                return $"(${FormatCurrency(totalCost)} - ${FormatCurrency(suggestedPrice)}) - {VariantUsed}: {breakdown} + {addtBreakdown}";
            }
        }

        private readonly RecipeResponse _recipesData;
        private readonly ItemDataResponse _itemData;
        private readonly AvailabilitySettings _settings;

        public RecipeGraph(RecipeResponse recipesData, ItemDataResponse itemData, AvailabilitySettings settings)
        {
            _recipesData = recipesData;
            _itemData = itemData;
            _settings = settings;
        }

        public Dictionary<string, PriceInfo> GetPrices()
        {
            var progressData = new Dictionary<string, ComputationProgress>();

            // Get all preset item prices
            var itemPrices = new Dictionary<string, decimal>();
            foreach (var tagPrice in _settings.TagPrices)
            {
                foreach (var item in _itemData.Tags[tagPrice.Key])
                {
                    if (!itemPrices.ContainsKey(item) || itemPrices[item] > tagPrice.Value)
                        itemPrices[item] = tagPrice.Value;
                }
            }
            foreach (var itemPrice in _settings.ItemPrices)
            {
                itemPrices[itemPrice.Key] = itemPrice.Value;
            }

            // Setup
            foreach (var (item, data) in _itemData.Items)
            {
                var computationProgress = new ComputationProgress(item, _settings);
                if (_recipesData.Products.ContainsKey(item))
                {
                    var recipeNames = _recipesData.Products[item];
                    var variants = _recipesData.Recipes.Values
                        .SelectMany(r => r.Variants, (r, v) => (r, v.Value))
                        .Where(rv => recipeNames.Contains(rv.Value.Untranslated));
                    computationProgress.RemainingRecipesToCompute.AddRange(variants);
                }

                if (itemPrices.ContainsKey(item))
                {
                    computationProgress.PriceInfo.MaterialCost = itemPrices[item];
                }

                progressData[item] = computationProgress;
            }

            // Compute accessible recipes
            var dirty = true;
            while (dirty)
            {
                dirty = false;

                foreach (var (item, progress) in progressData.ToList())
                {
                    // Eliminate items that we can't produce that have no remaining recipes
                    if (progress.RemainingRecipesToCompute.Count == 0 && !itemPrices.ContainsKey(item))
                    {
                        progressData.Remove(item);
                        dirty = true;
                    }

                    // Eliminate recipes with ingredients that we can't produce
                    foreach (var (recipe, variant) in progress.RemainingRecipesToCompute.ToList())
                    {
                        foreach (var ingredient in variant.Ingredients)
                        {
                            if (ingredient.type == "ITEM" && !progressData.ContainsKey(ingredient.name1))
                            {
                                progress.RemainingRecipesToCompute.Remove((recipe, variant));
                                dirty = true;
                            }
                            else if (ingredient.type == "TAG")
                            {
                                if (_itemData.Tags[ingredient.name1].Count(option => progressData.ContainsKey(option)) == 0)
                                {
                                    progress.RemainingRecipesToCompute.Remove((recipe, variant));
                                    dirty = true;
                                }
                            }
                            else
                            {

                            }
                        }
                    }
                }

                if (dirty)
                    continue;

                // Remove inaccessible loops
                var loops = new List<HashSet<(Recipe recipe, RecipeVariant variant)>>();
                void findLoops((Recipe recipe, RecipeVariant variant) currentVariant, HashSet<(Recipe recipe, RecipeVariant variant)> currentLoop, (Recipe recipe, RecipeVariant variant) startingPoint)
                {
                    foreach (var ingredient in currentVariant.variant.Ingredients)
                    {
                        if (ingredient.type == "ITEM" && progressData.ContainsKey(ingredient.name1))
                        {
                            var ingredientProcessData = progressData[ingredient.name1];
                            foreach (var ingredientRv in ingredientProcessData.RemainingRecipesToCompute)
                            {
                                if (currentLoop.Contains(ingredientRv))
                                {
                                    if (startingPoint == ingredientRv && !loops.Any(loop => loop.SetEquals(currentLoop)))
                                        loops.Add(currentLoop);
                                    continue;
                                }

                                var updatedSet = new HashSet<(Recipe recipe, RecipeVariant variant)>(currentLoop);
                                updatedSet.Add(ingredientRv);
                                findLoops(ingredientRv, updatedSet, startingPoint);
                            }
                        }
                        else if (ingredient.type == "TAG")
                        {
                            foreach (var item in _itemData.Tags[ingredient.name1])
                            {
                                if (!progressData.ContainsKey(item))
                                    continue;
                                var ingredientProcessData = progressData[item];
                                foreach (var ingredientRv in ingredientProcessData.RemainingRecipesToCompute)
                                {
                                    if (currentLoop.Contains(ingredientRv))
                                    {
                                        if (startingPoint == ingredientRv && !loops.Any(loop => loop.SetEquals(currentLoop)))
                                            loops.Add(currentLoop);
                                        continue;
                                    }

                                    var updatedSet = new HashSet<(Recipe recipe, RecipeVariant variant)>(currentLoop);
                                    updatedSet.Add(ingredientRv);
                                    findLoops(ingredientRv, updatedSet, startingPoint);
                                }
                            }
                        }
                    }
                }
                foreach (var (item, progress) in progressData.ToList())
                {
                    foreach (var (recipe, variant) in progress.RemainingRecipesToCompute.ToList())
                    {
                        findLoops((recipe, variant), new HashSet<(Recipe recipe, RecipeVariant variant)>() { (recipe, variant) }, (recipe, variant));
                    }
                }
                foreach (var loop in loops)
                {
                    var shouldKeep = loop.Any(rv => rv.variant.Ingredients.All(ingredient =>
                    {
                        if (ingredient.type == "ITEM")
                        {
                            if (progressData.ContainsKey(ingredient.name1) && progressData[ingredient.name1].RemainingRecipesToCompute.Count(ingredientRv => !loop.Contains(ingredientRv)) > 0)
                            {
                                return true;
                            }
                        }
                        else if (ingredient.type == "TAG")
                        {
                            foreach (var item in _itemData.Tags[ingredient.name1])
                            {
                                if (progressData.ContainsKey(item))
                                {
                                    return true;
                                }
                            }
                        }
                        return false;
                    }));

                    if (shouldKeep)
                        continue;

                    foreach (var rv in loop)
                    {
                        if (loops.Sum(l => l.Contains(rv) ? 1 : 0) == 1)
                        {
                            foreach (var (item, progress) in progressData.ToList())
                            {
                                if (progress.RemainingRecipesToCompute.Remove(rv))
                                {
                                    dirty = true;
                                }
                            }
                        }
                    }
                }
            }

            // Find remaining loops
            var remainingLoops = new List<HashSet<(Recipe recipe, RecipeVariant variant)>>();
            void findRemainingLoops((Recipe recipe, RecipeVariant variant) currentVariant, HashSet<(Recipe recipe, RecipeVariant variant)> currentLoop, (Recipe recipe, RecipeVariant variant) startingPoint)
            {
                foreach (var ingredient in currentVariant.variant.Ingredients)
                {
                    if (ingredient.type == "ITEM" && progressData.ContainsKey(ingredient.name1))
                    {
                        var ingredientProcessData = progressData[ingredient.name1];
                        foreach (var ingredientRv in ingredientProcessData.RemainingRecipesToCompute)
                        {
                            if (currentLoop.Contains(ingredientRv))
                            {
                                if (startingPoint == ingredientRv && !remainingLoops.Any(loop => loop.SetEquals(currentLoop)))
                                    remainingLoops.Add(currentLoop);
                                continue;
                            }

                            var updatedSet = new HashSet<(Recipe recipe, RecipeVariant variant)>(currentLoop);
                            updatedSet.Add(ingredientRv);
                            findRemainingLoops(ingredientRv, updatedSet, startingPoint);
                        }
                    }
                    else if (ingredient.type == "TAG")
                    {
                        foreach (var item in _itemData.Tags[ingredient.name1])
                        {
                            if (!progressData.ContainsKey(item))
                                continue;
                            var ingredientProcessData = progressData[item];
                            foreach (var ingredientRv in ingredientProcessData.RemainingRecipesToCompute)
                            {
                                if (currentLoop.Contains(ingredientRv))
                                {
                                    if (startingPoint == ingredientRv && !remainingLoops.Any(loop => loop.SetEquals(currentLoop)))
                                        remainingLoops.Add(currentLoop);
                                    continue;
                                }

                                var updatedSet = new HashSet<(Recipe recipe, RecipeVariant variant)>(currentLoop);
                                updatedSet.Add(ingredientRv);
                                findRemainingLoops(ingredientRv, updatedSet, startingPoint);
                            }
                        }
                    }
                }
            }
            foreach (var (item, progress) in progressData.ToList())
            {
                foreach (var (recipe, variant) in progress.RemainingRecipesToCompute.ToList())
                {
                    findRemainingLoops((recipe, variant), new HashSet<(Recipe recipe, RecipeVariant variant)>() { (recipe, variant) }, (recipe, variant));
                }
            }

            // Compute pricing
            var remaining = progressData.Keys.ToList();
            var lastRemainingCount = 0;
            while (remaining.Count > 0)
            {
                if (lastRemainingCount == remaining.Count)
                {
                    var variantsRemaining = progressData
                        .Where(p => !p.Value.Completed)
                        .Select(p => (p.Key, p.Value.RemainingRecipesToCompute.Select(r => r.variant)))
                        .ToDictionary(p => p.Key, p => p.Item2);
                    var json = JsonConvert.SerializeObject(variantsRemaining, Formatting.Indented);
                    Console.WriteLine(json);

                    throw new Exception("Stuck");
                }
                lastRemainingCount = remaining.Count;
                foreach (var item in remaining.ToList())
                {
                    var progress = progressData[item];
                    var completed = true;
                    var blockedByLoop = false;

                    (Recipe recipe, RecipeVariant variant) bestRecipe = (null, null);
                    decimal bestRecipeCost = decimal.MaxValue;
                    var bestIngredientCosts = new List<(string ingredient, long quantity, PriceInfo pricePerUnit)>();

                    if (!itemPrices.ContainsKey(item))
                    {
                        foreach (var (recipe, variant) in progress.RemainingRecipesToCompute)
                        {
                            decimal currentRecipeCost = 0;
                            var currentBlockedByLoop = false;
                            var currentIngredientCosts = new List<(string ingredient, long quantity, PriceInfo pricePerUnit)>();

                            foreach (var ingredient in variant.Ingredients)
                            {
                                if (ingredient.type == "ITEM")
                                {
                                    if (!progressData.ContainsKey(ingredient.name1) || !progressData[ingredient.name1].Completed)
                                    {
                                        if (!remainingLoops.Any(loop => loop.Contains((recipe, variant))))
                                        {
                                            Console.WriteLine($"{variant} waiting on item {ingredient.name1}");
                                            completed = false;
                                            break;
                                        }
                                        else
                                        {
                                            blockedByLoop = true;
                                            currentBlockedByLoop = true;
                                            continue;
                                        }
                                    }
                                    currentRecipeCost += progressData[ingredient.name1].PriceInfo.TotalCost * ingredient.quantity;
                                    currentIngredientCosts.Add((ingredient.name1, ingredient.quantity, progressData[ingredient.name1].PriceInfo));
                                }
                                else if (ingredient.type == "TAG")
                                {
                                    var availableOptions = _itemData.Tags[ingredient.name1].Where(i => progressData.ContainsKey(i)).ToList();
                                    if (availableOptions.Count == 0 || availableOptions.Any(option => !progressData[option].Completed))
                                    {
                                        if (!remainingLoops.Any(loop => loop.Contains((recipe, variant))))
                                        {
                                            Console.WriteLine($"{variant} waiting on tag {ingredient.name1}");
                                            completed = false;
                                            break;
                                        }
                                        else
                                        {
                                            blockedByLoop = true;
                                            currentBlockedByLoop = true;
                                            continue;
                                        }
                                    }

                                    var orderedOptions = availableOptions.OrderBy(option => progressData[option].PriceInfo.TotalCost);
                                    var bestOption = orderedOptions.First();
                                    currentRecipeCost += progressData[bestOption].PriceInfo.TotalCost * ingredient.quantity;
                                    currentIngredientCosts.Add((bestOption, ingredient.quantity, progressData[bestOption].PriceInfo));
                                }
                            }

                            if (!completed)
                                break;

                            if (currentBlockedByLoop)
                                continue;

                            if (currentRecipeCost < bestRecipeCost)
                            {
                                // TODO: factor in all costs, somehow
                                bestRecipe = (recipe, variant);
                                bestRecipeCost = currentRecipeCost;
                                bestIngredientCosts = currentIngredientCosts;
                            }
                        }
                    }

                    if (completed && (!blockedByLoop || bestRecipe.recipe != null))
                    {
                        progress.Completed = true;
                        progress.PriceInfo.RecipeUsed = bestRecipe.recipe;
                        progress.PriceInfo.VariantUsed = bestRecipe.variant;
                        if (progress.PriceInfo.VariantUsed != null)
                        {
                            progress.PriceInfo.IngredientCosts = bestIngredientCosts;
                            progress.PriceInfo.LaborCost = (decimal)progress.PriceInfo.RecipeUsed.BaseLaborCost * _settings.CostPerLaborPoint;
                            progress.PriceInfo.TimeCost = (decimal)progress.PriceInfo.RecipeUsed.BaseCraftTime * _settings.CostPerMinute;
                        }
                        remaining.Remove(item);
                        Console.WriteLine($"Processed {item}");
                    }
                }
            }

            return progressData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.PriceInfo);
        }
    }
}
