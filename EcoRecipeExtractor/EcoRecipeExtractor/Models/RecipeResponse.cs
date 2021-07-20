using System;
using System.Collections.Generic;
using System.Text;

namespace EcoRecipeExtractor.Models
{
    public class RecipeResponse
    {
        public Dictionary<string, Ingredient> Ingredients = new Dictionary<string, Ingredient>();
        public Dictionary<string, Product> Products = new Dictionary<string, Product>();
        public Dictionary<string, Recipe> Recipes = new Dictionary<string, Recipe>();
        public Dictionary<string, Table> Tables = new Dictionary<string, Table>();
    }
}
