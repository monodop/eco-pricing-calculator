using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EcoRecipeExtractor
{
    public class SheetManager
    {
        private readonly string _credentialsPath;
        private readonly string _spreadsheetId;

        public SheetManager(string credentialsPath, string spreadsheetId)
        {
            _credentialsPath = credentialsPath;
            _spreadsheetId = spreadsheetId;
        }

        private async Task<SheetsService> _getSheetsServiceAsync(CancellationToken cancellationToken)
        {
            using var stream = new FileStream(_credentialsPath, FileMode.Open, FileAccess.Read);
            var credential = await GoogleCredential.FromStreamAsync(stream, cancellationToken);
            var scoped = credential.CreateScoped(SheetsService.Scope.Spreadsheets);

            var sheetService = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = scoped,
                ApplicationName = "Eco Pricing Calculator Sheet",
            });

            return sheetService;
        }

        public async Task<AvailabilitySettings> GetAvailabilitySettingsAsync(CancellationToken cancellationToken)
        {
            using var sheetService = await _getSheetsServiceAsync(cancellationToken);

            var request = sheetService.Spreadsheets.Values.BatchGet(_spreadsheetId);
            request.Ranges = new Repeatable<string>(new[]
            {
                "Settings!A2:B1000", // 0 Skills
                "Settings!D2:E1000", // 1 Tables 
                "Settings!G2:I1000", // 2 Raw material prices
                "Settings!K2:M1000", // 3 Byproduct handling prices
                "Settings!O2:Q1000", // 4 Raw material demand
                "Settings!S1:T1000", // 5 Other settings
            });
            var response = await request.ExecuteAsync(cancellationToken);

            var settings = new AvailabilitySettings();
            settings.AvailableSkills = response.ValueRanges[0].Values?.ToDictionary(v => v[0].ToString(), v => int.Parse(v[1].ToString())) ?? new Dictionary<string, int>();
            settings.AvailableTables = response.ValueRanges[1].Values?.Where(v => v[1].ToString() == "TRUE").Select(v => v[0].ToString()).ToList() ?? new List<string>();
            settings.ItemPrices = response.ValueRanges[2].Values?.Where(v => v[0].ToString() == "ITEM").ToDictionary(v => v[1].ToString(), v => decimal.Parse(v[2].ToString())) ?? new Dictionary<string, decimal>();
            settings.TagPrices = response.ValueRanges[2].Values?.Where(v => v[0].ToString() == "TAG").ToDictionary(v => v[1].ToString(), v => decimal.Parse(v[2].ToString())) ?? new Dictionary<string, decimal>();
            settings.ItemByproductHandlingPrices = response.ValueRanges[3].Values?.Where(v => v[0].ToString() == "ITEM").ToDictionary(v => v[1].ToString(), v => decimal.Parse(v[2].ToString())) ?? new Dictionary<string, decimal>();
            settings.TagByproductHandlingPrices = response.ValueRanges[3].Values?.Where(v => v[0].ToString() == "TAG").ToDictionary(v => v[1].ToString(), v => decimal.Parse(v[2].ToString())) ?? new Dictionary<string, decimal>();
            // TODO: demand
            settings.CostPerLaborPoint = decimal.Parse(response.ValueRanges[5].Values.First(v => v[0].ToString() == "Cost Per Labor Pt")[1].ToString());
            settings.CostPerMinute = decimal.Parse(response.ValueRanges[5].Values.First(v => v[0].ToString() == "Cost Per Minute")[1].ToString());
            settings.NormalDemandMargin = decimal.Parse(response.ValueRanges[5].Values.First(v => v[0].ToString() == "Normal Demand Margin")[1].ToString());
            settings.HighDemandMargin = decimal.Parse(response.ValueRanges[5].Values.First(v => v[0].ToString() == "High Demand Margin")[1].ToString());
            return settings;
        }

        public async Task CommitPricesAsync(Dictionary<string, RecipeGraph.PriceInfo> prices, CancellationToken cancellationToken)
        {
            using var sheetService = await _getSheetsServiceAsync(cancellationToken);

            var clearRequest = sheetService.Spreadsheets.Values.Clear(new ClearValuesRequest(), _spreadsheetId, "Prices!A2:K1000");
            await clearRequest.ExecuteAsync(cancellationToken);

            var pairs = prices.OrderBy(kvp => kvp.Key).ToList();

            var valueRange = new ValueRange()
            {
                Range = "Prices!A2:K" + (pairs.Count + 1),
                Values = pairs.Select(kvp => new List<object>()
                {
                    kvp.Key,
                    kvp.Value.TotalCost,
                    kvp.Value.GetSuggestedPrice(),
                    kvp.Value.IngredientsCost,
                    kvp.Value.GetSuggestedIngredientsPrice(),
                    kvp.Value.LaborCost,
                    kvp.Value.TimeCost,
                    kvp.Value.EnergyCost,
                    kvp.Value.WasteProductHandlingCost,
                    kvp.Value.GetSuggestedMarkup(),
                    kvp.Value.VariantUsed?.ToString(),
                }).ToList<IList<object>>(),
            };
            var request = sheetService.Spreadsheets.Values.Update(valueRange, _spreadsheetId, valueRange.Range);
            request.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            await request.ExecuteAsync(cancellationToken);
        }
    }
}
