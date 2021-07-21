using EcoRecipeExtractor.Models;
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
            public decimal IngredientsCost
            {
                get
                {
                    return IngredientCosts.Sum(c => c.pricePerUnit.TotalCost * c.quantity);
                }
            }
            public decimal GetSuggestedIngredientsPrice()
            {
                return IngredientCosts.Sum(c => c.pricePerUnit.GetSuggestedPrice() * c.quantity);
            }
            public decimal TotalCost
            {
                get
                {
                    // TODO: factor in other products?
                    return (LaborCost + TimeCost + EnergyCost + MaterialCost + IngredientsCost + WasteProductHandlingCost) / (VariantUsed?.Products.Single(p => p.name1 == ItemName).quantity) ?? 1;
                }
            }

            public decimal GetSuggestedPrice()
            {
                var suggestedCost = (LaborCost + TimeCost + EnergyCost + MaterialCost + GetSuggestedIngredientsPrice() + WasteProductHandlingCost) / (VariantUsed?.Products.Single(p => p.name1 == ItemName).quantity) ?? 1;
                return -(suggestedCost / (_settings.NormalDemandMargin - 1));
            }

            public decimal GetSuggestedMarkup()
            {
                return GetSuggestedPrice() * _settings.NormalDemandMargin;
            }

            public string FormatCurrency(decimal value)
            {
                return value.ToString("0.00");
            }

            public override string ToString()
            {
                var totalCost = TotalCost;
                var suggestedPrice = GetSuggestedPrice();

                if (IngredientCosts.Count == 0)
                {
                    return $"(${FormatCurrency(totalCost)} - ${FormatCurrency(suggestedPrice)})";
                }

                var breakdown = string.Join(" + ", IngredientCosts.Select(c => $"{c.ingredient} (${FormatCurrency(c.pricePerUnit.TotalCost * c.quantity)} - ${FormatCurrency(c.pricePerUnit.GetSuggestedPrice() * c.quantity)})"));
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

                if (_settings.ItemPrices.ContainsKey(item))
                {
                    computationProgress.PriceInfo.MaterialCost = _settings.ItemPrices[item];
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
                    if (progress.RemainingRecipesToCompute.Count == 0 && !_settings.ItemPrices.ContainsKey(item))
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
                                if (!_itemData.Tags[ingredient.name1].Any(option => progressData.ContainsKey(option)))
                                {
                                    progress.RemainingRecipesToCompute.Remove((recipe, variant));
                                    dirty = true;
                                }
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
                            // TODO:
                            break;
                            throw new NotImplementedException();
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

            // Compute pricing
            var remaining = progressData.Keys.ToList();
            while (remaining.Count > 0)
            {
                foreach (var item in remaining.ToList())
                {
                    var progress = progressData[item];
                    var completed = true;

                    (Recipe recipe, RecipeVariant variant) bestRecipe = (null, null);
                    decimal bestRecipeCost = decimal.MaxValue;
                    var bestIngredientCosts = new List<(string ingredient, long quantity, PriceInfo pricePerUnit)>();

                    if (!_settings.ItemPrices.ContainsKey(item))
                    {
                        foreach (var (recipe, variant) in progress.RemainingRecipesToCompute)
                        {
                            decimal currentRecipeCost = 0;
                            var currentIngredientCosts = new List<(string ingredient, long quantity, PriceInfo pricePerUnit)>();

                            foreach (var ingredient in variant.Ingredients)
                            {
                                if (ingredient.type == "ITEM")
                                {
                                    if (!progressData.ContainsKey(ingredient.name1) || !progressData[ingredient.name1].Completed)
                                    {
                                        completed = false;
                                        break;
                                    }
                                    currentRecipeCost += progressData[ingredient.name1].PriceInfo.TotalCost * ingredient.quantity;
                                    currentIngredientCosts.Add((ingredient.name1, ingredient.quantity, progressData[ingredient.name1].PriceInfo));
                                }
                                else if (ingredient.type == "TAG")
                                {
                                    var availableOptions = _itemData.Tags[ingredient.name1].Where(i => progressData.ContainsKey(i)).ToList();
                                    if (availableOptions.Count == 0 || availableOptions.Any(option => !progressData[option].Completed))
                                    {
                                        completed = false;
                                        break;
                                    }

                                    var orderedOptions = availableOptions.OrderBy(option => progressData[option].PriceInfo.TotalCost);
                                    var bestOption = orderedOptions.First();
                                    currentRecipeCost += progressData[bestOption].PriceInfo.TotalCost * ingredient.quantity;
                                    currentIngredientCosts.Add((bestOption, ingredient.quantity, progressData[bestOption].PriceInfo));
                                }
                            }

                            if (!completed)
                                break;

                            if (currentRecipeCost < bestRecipeCost)
                            {
                                // TODO: factor in all costs, somehow
                                bestRecipe = (recipe, variant);
                                bestRecipeCost = currentRecipeCost;
                                bestIngredientCosts = currentIngredientCosts;
                            }
                        }
                    }

                    if (completed)
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
                    }
                }
            }

            return progressData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.PriceInfo);
        }
    }
}
