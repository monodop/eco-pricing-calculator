using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace EcoRecipeExtractor
{
    public class BoolConverter : JsonConverter<bool>
    {
        public override bool ReadJson(JsonReader reader, Type objectType, bool existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            if (token.Type == JTokenType.String)
            {
                return token.ToObject<string>().ToLower() switch
                {
                    "yes" => true,
                    "true" => true,
                    "false" => false,
                    _ => throw new Exception("unknown value"),
                };
            }
            return false;
        }

        public override void WriteJson(JsonWriter writer, bool value, JsonSerializer serializer)
        {
            writer.WriteToken(JsonToken.Boolean, value);
        }
    }
    public class NullableBoolConverter : JsonConverter<bool?>
    {
        public override bool? ReadJson(JsonReader reader, Type objectType, bool? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            if (token.Type == JTokenType.String)
            {
                return token.ToObject<string>().ToLower() switch
                {
                    "yes" => true,
                    "true" => true,
                    "false" => false,
                    _ => throw new Exception("unknown value"),
                };
            }
            return false;
        }

        public override void WriteJson(JsonWriter writer, bool? value, JsonSerializer serializer)
        {
            if (value == null)
                writer.WriteToken(JsonToken.Null);
            else
                writer.WriteToken(JsonToken.Boolean, value);
        }
    }
}
