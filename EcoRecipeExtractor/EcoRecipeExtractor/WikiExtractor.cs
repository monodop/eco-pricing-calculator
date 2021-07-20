using EcoRecipeExtractor.Models;
using HtmlAgilityPack;
using Newtonsoft.Json;
using NLua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace EcoRecipeExtractor
{
    public class WikiExtractor
    {
        public WikiExtractor()
        {

        }

        private async Task<T> GetAndParseAsync<T>(string endpoint)
        {
            using var httpClient = new HttpClient();
            var page = await httpClient.GetAsync(endpoint);
            var contents = await page.Content.ReadAsStringAsync();
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(contents);
            var lua = htmlDoc.DocumentNode.SelectSingleNode("(//pre[contains(@class,'mw-code mw-script')])[1]").InnerHtml;

            JsonConvert.DefaultSettings = () =>
            {
                return new JsonSerializerSettings()
                {
                    Converters = new List<JsonConverter>()
                    {
                        new TupleConverter(),
                        new BoolConverter(),
                        new NullableBoolConverter(),
                    }
                };
            };

            //            var lua = @"
            //-- Eco Version : 0.9.3.4 beta release-226

            //return {foo = ""bar""}
            //";
            lua = lua.Replace("return", "result = ");

            lua = $@"
{lua}
return extractor:Normalize(result)
";

            using var luaState = new Lua();
            luaState["extractor"] = this;
            var result = luaState.DoString(lua)[0] as Dictionary<string, object>;
            var json = JsonConvert.SerializeObject(result);
            var formatted = JsonConvert.DeserializeObject<T>(json);
            return formatted;
        }

        public Task<RecipeResponse> GetRecipesAsync()
        {
            return GetAndParseAsync<RecipeResponse>("https://wiki.play.eco/en/Module:CraftingRecipes");
        }

        public Task<ItemDataResponse> GetItemDataAsync()
        {
            return GetAndParseAsync<ItemDataResponse>("https://wiki.play.eco/en/Module:ItemData");
        }

        public object? Normalize(object value)
        {
            if (value is LuaTable table)
            {
                var keys = table.Keys.Cast<object>().ToList();
                var values = table.Values.Cast<object>().ToList();

                if (keys.Count == 0)
                    return null;

                if (keys.All(k => k is long || k is int || int.TryParse(k.ToString(), out var _)))
                {
                    return values.Select(Normalize).ToList();
                }
                var pairs = keys.Zip(values, (k, v) => new KeyValuePair<string, object>(k!.ToString(), Normalize(v)));
                return new Dictionary<string, object>(pairs);
            }
            if (value is string)
            {
                return value;
            }
            return value;
        }
    }
}
