using EcoRecipeExtractor.Models;
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
                "Settings!D2:F1000", // 1 Tables 
                "Settings!H2:J1000", // 2 Raw material prices
                "Settings!L2:N1000", // 3 Byproduct handling prices
                "Settings!P2:R1000", // 4 Raw material demand
                "Settings!T2:T1000", // 5 Additional Raw Material (ingredient list)
                "Settings!V1:W1000", // 6 Other settings
            });
            var response = await request.ExecuteAsync(cancellationToken);

            var settings = new AvailabilitySettings();
            settings.AvailableSkills = response.ValueRanges[0].Values?.ToDictionary(v => v[0].ToString(), v => int.Parse(v[1].ToString())) ?? new Dictionary<string, int>();
            settings.AvailableTables = response.ValueRanges[1].Values?.Where(v => v[2].ToString() == "TRUE").Select(v => v[0].ToString()).ToList() ?? new List<string>();
            settings.TableUpgradeTypes = response.ValueRanges[1].Values?.ToDictionary(v => v[0].ToString(), v => ParseUpgradeType(v[1].ToString())) ?? new Dictionary<string, AvailabilitySettings.UpgradeType>();
            settings.ItemPrices = response.ValueRanges[2].Values?.Where(v => v[0].ToString() == "ITEM").ToDictionary(v => v[1].ToString(), v => decimal.Parse(v[2].ToString())) ?? new Dictionary<string, decimal>();
            settings.TagPrices = response.ValueRanges[2].Values?.Where(v => v[0].ToString() == "TAG").ToDictionary(v => v[1].ToString(), v => decimal.Parse(v[2].ToString())) ?? new Dictionary<string, decimal>();
            settings.ItemByproductHandlingPrices = response.ValueRanges[3].Values?.Where(v => v[0].ToString() == "ITEM").ToDictionary(v => v[1].ToString(), v => decimal.Parse(v[2].ToString())) ?? new Dictionary<string, decimal>();
            settings.TagByproductHandlingPrices = response.ValueRanges[3].Values?.Where(v => v[0].ToString() == "TAG").ToDictionary(v => v[1].ToString(), v => decimal.Parse(v[2].ToString())) ?? new Dictionary<string, decimal>();
            settings.AdditionalRawMaterialsForIngredientsList = response.ValueRanges[5].Values?.Select(v => v[0].ToString()).ToList() ?? new List<string>();

            // TODO: demand
            settings.CostPerLaborPoint = decimal.Parse(response.ValueRanges[6].Values.First(v => v[0].ToString() == "Cost Per Labor Pt")[1].ToString());
            settings.CostPerMinute = decimal.Parse(response.ValueRanges[6].Values.First(v => v[0].ToString() == "Cost Per Minute")[1].ToString());
            settings.NormalDemandMargin = decimal.Parse(response.ValueRanges[6].Values.First(v => v[0].ToString() == "Normal Demand Margin")[1].ToString());
            settings.HighDemandMargin = decimal.Parse(response.ValueRanges[6].Values.First(v => v[0].ToString() == "High Demand Margin")[1].ToString());

            settings.BasicUpgradePct = decimal.Parse(response.ValueRanges[6].Values.First(v => v[0].ToString() == "Basic Upgrade %")[1].ToString().TrimEnd('%')) / 100;
            settings.AdvancedUpgradePct = decimal.Parse(response.ValueRanges[6].Values.First(v => v[0].ToString() == "Advanced Upgrade %")[1].ToString().TrimEnd('%')) / 100;
            settings.ModernUpgradePct = decimal.Parse(response.ValueRanges[6].Values.First(v => v[0].ToString() == "Modern Upgrade %")[1].ToString().TrimEnd('%')) / 100;
            return settings;
        }

        private AvailabilitySettings.UpgradeType ParseUpgradeType(string upgradeType)
        {
            return upgradeType switch
            {
                "None" => AvailabilitySettings.UpgradeType.None,
                "Basic" => AvailabilitySettings.UpgradeType.Basic,
                "Advanced" => AvailabilitySettings.UpgradeType.Advanced,
                "Modern" => AvailabilitySettings.UpgradeType.Modern,
                _ => AvailabilitySettings.UpgradeType.Unknown,
            };
        }

        public async Task CommitPricesAsync(Dictionary<string, RecipeGraph.PriceInfo> prices, ItemDataResponse itemData, CancellationToken cancellationToken)
        {
            using var sheetService = await _getSheetsServiceAsync(cancellationToken);

            var pairs = prices.OrderByDescending(kvp => kvp.Value.SuggestedPrice).ToList();
            var tagPairs = itemData.Tags.OrderBy(t => t.Key).Select(t => (t.Key, prices.Where(p => t.Value.Contains(p.Key)).OrderBy(p => p.Value.SuggestedPrice).FirstOrDefault().Value)).Where(t => t.Value != null).ToList();

            var batchUpdateBody = new BatchUpdateValuesRequest()
            {
                Data = new List<ValueRange>()
                {
                    new ValueRange()
                    {
                        Range = "Prices!A2:L" + (pairs.Count + 1),
                        Values = pairs.Select(kvp => new List<object>()
                        {
                            kvp.Key,
                            kvp.Value.TotalCost,
                            kvp.Value.SuggestedPrice,
                            kvp.Value.IngredientsCost,
                            kvp.Value.SuggestedIngredientsPrice,
                            kvp.Value.LaborCost,
                            kvp.Value.TimeCost,
                            kvp.Value.EnergyCost,
                            kvp.Value.WasteProductHandlingCost,
                            kvp.Value.SuggestedMarkup,
                            kvp.Value.VariantUsed?.ToString(),
                            string.Join(", ", kvp.Value.TotalIngredients.Select(ingredient => $"{ingredient.Key}: {ingredient.Value.ToString("0.##")}")),
                        }).ToList<IList<object>>(),
                    },
                    new ValueRange()
                    {
                        Range = "TagPrices!A2:K" + (tagPairs.Count + 1),
                        Values = tagPairs.Select(kvp => new List<object>()
                        {
                            kvp.Key,
                            kvp.Value.TotalCost,
                            kvp.Value.SuggestedPrice,
                            kvp.Value.IngredientsCost,
                            kvp.Value.SuggestedIngredientsPrice,
                            kvp.Value.LaborCost,
                            kvp.Value.TimeCost,
                            kvp.Value.EnergyCost,
                            kvp.Value.WasteProductHandlingCost,
                            kvp.Value.SuggestedMarkup,
                            kvp.Value.VariantUsed?.ToString() ?? kvp.Value.ItemName,
                        }).ToList<IList<object>>(),
                    },
                },
                ValueInputOption = "USER_ENTERED",
            };

            var clearRequest = sheetService.Spreadsheets.Values.Clear(new ClearValuesRequest(), _spreadsheetId, "Prices!A2:L1000");
            await clearRequest.ExecuteAsync(cancellationToken);

            clearRequest = sheetService.Spreadsheets.Values.Clear(new ClearValuesRequest(), _spreadsheetId, "TagPrices!A2:K1000");
            await clearRequest.ExecuteAsync(cancellationToken);

            var request = sheetService.Spreadsheets.Values.BatchUpdate(batchUpdateBody, _spreadsheetId);
            await request.ExecuteAsync(cancellationToken);
        }
    }
}
