using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace ValheimNewUI;

internal sealed class Artwork : IDisposable
{
    private readonly Dictionary<string, Sprite> sprites = new();
    internal Sprite this[string name] => sprites[name];

    internal Artwork(string directory, ManualLogSource log)
    {
        try
        {
            foreach (string name in new[] { "panel", "slot", "button", "selected", "map-frame", "item-card", "reticle", "player-marker", "meter-fill", "quality-star" })
            {
                string path = Path.Combine(directory, name + ".png");
                if (File.Exists(path))
                {
                    try
                    {
                        sprites.Add(name, Decode(name, File.ReadAllBytes(path)));
                        continue;
                    }
                    catch (Exception ex) { log.LogWarning($"Invalid artwork {path}: {ex.Message}"); }
                }
                using var stream = typeof(Artwork).Assembly.GetManifestResourceStream("ValheimNewUI.artwork." + name + ".png");
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                sprites.Add(name, Decode(name, buffer.ToArray()));
            }
        }
        catch { Dispose(); throw; }
    }

    private static Sprite Decode(string name, byte[] png)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(texture, png, true)) throw new InvalidDataException("Invalid PNG");
            int inset = name is "reticle" or "player-marker" or "meter-fill" or "quality-star" ? 0 : 12;
            if (texture.width <= inset * 2 || texture.height <= inset * 2) throw new InvalidDataException("Image too small for 12 px borders");
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var rect = new Rect(0, 0, texture.width, texture.height);
            var sprite = Sprite.Create(texture, rect, Vector2.one * 0.5f, 100, 0, SpriteMeshType.FullRect, Vector4.one * inset);
            sprite.name = "ValheimNewUI/" + name;
            return sprite;
        }
        catch { UnityEngine.Object.Destroy(texture); throw; }
    }

    public void Dispose()
    {
        foreach (var sprite in sprites.Values)
        {
            UnityEngine.Object.Destroy(sprite.texture);
            UnityEngine.Object.Destroy(sprite);
        }
        sprites.Clear();
    }
}
