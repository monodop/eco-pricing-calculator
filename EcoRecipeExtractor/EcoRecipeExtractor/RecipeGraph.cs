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
            public PriceInfo PriceInfo { get; set; }
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
            public List<(string ingredient, long quantity, bool cannotBeReducedViaModules, PriceInfo pricePerUnit)> IngredientCosts { get; set; } = new List<(string ingredient, long quantity, bool cannotBeReducedViaModules, PriceInfo pricePerUnit)>();

            public decimal LaborCost
                => ((decimal?)RecipeUsed?.BaseLaborCost ?? 0m) * _settings.CostPerLaborPoint * 0.2m; // lvl 7 80% reduction

            public decimal TimeCost
                    => ((decimal?)RecipeUsed?.BaseCraftTime ?? 0m) * _settings.CostPerMinute * _getModifier();

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
                        var result = IngredientCosts.Sum(c => c.pricePerUnit.TotalCost * c.quantity * (c.cannotBeReducedViaModules ? 1 : _getModifier()));

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
                        var result = IngredientCosts.Sum(c => c.pricePerUnit.SuggestedPrice * c.quantity * (c.cannotBeReducedViaModules ? 1 : _getModifier()));

                        if (VariantUsed == null)
                        {
                            return result;
                        }

                        _suggestedIngredientsPrice = result;
                    }
                    return (decimal)_suggestedIngredientsPrice;
                }
            }

            private decimal _getModifier()
            {
                if (RecipeUsed == null)
                    return 1;
                return RecipeUsed.CraftStn
                    .Select(s => _settings.TableUpgradeTypes[s.name1])
                    .Min(upgradeType => 1 - upgradeType switch
                    {
                        AvailabilitySettings.UpgradeType.Basic => _settings.BasicUpgradePct,
                        AvailabilitySettings.UpgradeType.Advanced => _settings.AdvancedUpgradePct,
                        AvailabilitySettings.UpgradeType.Modern => _settings.ModernUpgradePct,
                        _ => 0,
                    });
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
                        .Where(r => r.SkillNeeds == null || r.SkillNeeds.All(need => _settings.AvailableSkills.ContainsKey(need.name1) && _settings.AvailableSkills[need.name1] >= need.level))
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

            // Iteration helper
            IEnumerable<TOutput> processItem<TOutput>(string type, string name, Func<string, TOutput> func)
            {
                if (type == "ITEM")
                {
                    yield return func(name);
                }
                else if (type == "TAG")
                {
                    foreach (var item in _itemData.Tags[name])
                    {
                        yield return func(item);
                    }
                }
                else
                {
                    throw new NotImplementedException();
                }
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
                            var options = processItem(ingredient.type, ingredient.name1, item => progressData.ContainsKey(item));
                            if (!options.Any(o => o))
                            {
                                progress.RemainingRecipesToCompute.Remove((recipe, variant));
                                dirty = true;
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
                        processItem(ingredient.type, ingredient.name1, item =>
                        {
                            if (!progressData.ContainsKey(item))
                                return 0;

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
                            return 0;
                        });
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
                        return processItem(ingredient.type, ingredient.name1, item =>
                            progressData.ContainsKey(item) && progressData[item].RemainingRecipesToCompute.Count(ingredientRv => !loop.Contains(ingredientRv)) > 0
                        ).Any();
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
                    processItem(ingredient.type, ingredient.name1, item =>
                    {
                        if (!progressData.ContainsKey(item))
                            return 0;

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
                        return 0;
                    });
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

                    decimal bestRecipeCost = decimal.MaxValue;
                    var bestPriceInfo = progress.PriceInfo;

                    if (!itemPrices.ContainsKey(item))
                    {
                        foreach (var (recipe, variant) in progress.RemainingRecipesToCompute)
                        {
                            var currentPriceInfo = new PriceInfo(item, _settings)
                            {
                                MaterialCost = 0,
                                WasteProductHandlingCost = 0,
                                RecipeUsed = recipe,
                                VariantUsed = variant,
                            };
                            var currentBlockedByLoop = false;

                            foreach (var ingredient in variant.Ingredients)
                            {
                                var ingredientOptions = processItem(ingredient.type, ingredient.name1, item => item)
                                    .Where(item => progressData.ContainsKey(item))
                                    .ToList();
                                if (ingredientOptions.Count == 0 || ingredientOptions.Any(option => !progressData[option].Completed))
                                {
                                    if (!remainingLoops.Any(loop => loop.Contains((recipe, variant))))
                                    {
                                        Console.WriteLine($"{variant} waiting on {ingredient.type.ToLower()} {ingredient.name1}");
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

                                var bestOption = ingredientOptions.OrderBy(option => progressData[option].PriceInfo.TotalCost).First();
                                currentPriceInfo.IngredientCosts.Add((bestOption, ingredient.quantity, ingredient.cannotBeReducedViaModules, progressData[bestOption].PriceInfo));
                            }

                            if (!completed)
                                break;

                            if (currentBlockedByLoop)
                                continue;

                            if (currentPriceInfo.TotalCost < bestRecipeCost)
                            {
                                bestPriceInfo = currentPriceInfo;
                                bestRecipeCost = currentPriceInfo.TotalCost;
                            }
                        }
                    }

                    if (completed && (!blockedByLoop || bestPriceInfo != progress.PriceInfo))
                    {
                        progress.Completed = true;
                        progress.PriceInfo = bestPriceInfo;
                        remaining.Remove(item);
                        Console.WriteLine($"Processed {item}");
                    }
                }
            }

            return progressData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.PriceInfo);
        }
    }
}
