using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ValheimNewUI;

internal sealed class Theme
{
    internal readonly bool Enabled;
    internal readonly float Padding, CompassHighlightDistance, PlayerDetectionDistance;
    internal readonly Color Frame, Background, Health, Stamina, Text;

    internal Theme(string json)
    {
        var data = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        JToken Field(string key) => data[key] ?? throw new FormatException($"Missing '{key}'.");

        float Number(string key, float min, float max)
        {
            var token = Field(key);
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                float value = token.Value<float>();
                if (value >= min && value <= max) return value;
            }
            throw new FormatException($"'{key}' must be a number between {min} and {max}.");
        }
        string String(string key)
        {
            var token = Field(key);
            if (token.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)token))
                return (string)token;
            throw new FormatException($"'{key}' must be a nonempty string.");
        }
        Color Tint(string key)
        {
            string hex = String(key);
            if ((hex.Length != 7 && hex.Length != 9) || hex[0] != '#' ||
                !uint.TryParse(hex.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgba))
                throw new FormatException($"'{key}' must be #RRGGBB or #RRGGBBAA.");
            if (hex.Length == 7) rgba = (rgba << 8) | 255;
            return new Color32((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        }

        if (Field("enabled").Type != JTokenType.Boolean) throw new FormatException("'enabled' must be true or false.");
        Enabled = (bool)data["enabled"];
        Padding = Number("padding", 0, 57);
        CompassHighlightDistance = data["compassHighlightDistance"] == null
            ? 500 : Number("compassHighlightDistance", 0, float.MaxValue);
        PlayerDetectionDistance = data["playerDetectionDistance"] == null
            ? 500 : Number("playerDetectionDistance", 0, float.MaxValue);
        Frame = Tint("frame");
        Background = Tint("background");
        Health = Tint("health");
        Stamina = Tint("stamina");
        Text = Tint("text");
    }
}
