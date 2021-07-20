using System;
using System.Collections.Generic;
using System.Text;

namespace EcoRecipeExtractor.Models
{
    public class ItemDataResponse
    {
        public Dictionary<string, Item> Items = new Dictionary<string, Item>();
        public Dictionary<string, List<string>> Tags = new Dictionary<string, List<string>>();
    }
}
