using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rockwall;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Rockwall2.Editor.Materials.Utils;

public class MaterialTextureSlot
{
    public string Key = "";
    public string Path = "";
}

public class MaterialShaderFlag
{
    public string Key = "";
    public bool Value;
}

public class MaterialDocument
{
    public const string AlbedoKey = "texture";
    public const string SpecularKey = "specular";
    public const string NormalKey = "normal";

    public static readonly string[] BuiltinTextureKeys = { AlbedoKey, SpecularKey, NormalKey };

    public static readonly string[] KnownShaderFlags = { "receivePlanarReflection", "receiveRefractionTexture" };

    static readonly HashSet<string> reservedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "surfaceType", "shaderName", "reflectivity", "transparent",
        "alphaClip", "noCull", "texelsPerUnit", "shaderFlags",
    };

    public string FilePath;

    public string Name = "";
    public string SurfaceType = "concrete";
    public string ShaderName = "";
    public float Reflectivity = 1;
    public bool Transparent;
    public bool AlphaClip;
    public bool NoCull;
    public int TexelsPerUnit;

    public List<MaterialTextureSlot> Textures = new();
    public List<MaterialShaderFlag> ShaderFlags = new();

    public string OriginalName;

    JObject unknown = new();

    public MaterialDocument()
    {
        foreach (var key in BuiltinTextureKeys)
            Textures.Add(new MaterialTextureSlot { Key = key });
    }

    public static bool IsReservedKey(string key) => reservedKeys.Contains(key);

    public string GetTexturePath(string key) =>
        Textures.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase))?.Path ?? "";

    public static MaterialDocument Load(string path)
    {
        var json = JObject.Parse(File.ReadAllText(path));
        var doc = new MaterialDocument { FilePath = path };

        foreach (var prop in json.Properties())
        {
            switch (prop.Name.ToLowerInvariant())
            {
                case "name": doc.Name = prop.Value.Value<string>() ?? ""; break;
                case "surfacetype": doc.SurfaceType = prop.Value.Value<string>() ?? ""; break;
                case "shadername": doc.ShaderName = prop.Value.Value<string>() ?? ""; break;
                case "reflectivity": doc.Reflectivity = prop.Value.Value<float>(); break;
                case "transparent": doc.Transparent = prop.Value.Value<bool>(); break;
                case "alphaclip": doc.AlphaClip = prop.Value.Value<bool>(); break;
                case "nocull": doc.NoCull = prop.Value.Value<bool>(); break;
                case "texelsperunit": doc.TexelsPerUnit = prop.Value.Value<int>(); break;
                case "shaderflags":
                    if (prop.Value is JObject flags)
                        foreach (var flag in flags.Properties())
                            doc.ShaderFlags.Add(new MaterialShaderFlag { Key = flag.Name, Value = flag.Value.Value<bool>() });
                    break;
                default:
                    if (prop.Value.Type == JTokenType.String)
                    {
                        var slot = doc.Textures.FirstOrDefault(t => string.Equals(t.Key, prop.Name, StringComparison.OrdinalIgnoreCase));
                        if (slot == null) doc.Textures.Add(slot = new MaterialTextureSlot { Key = prop.Name });
                        slot.Path = prop.Value.Value<string>() ?? "";
                    }
                    else
                    {
                        doc.unknown[prop.Name] = prop.Value.DeepClone();
                    }
                    break;
            }
        }

        doc.OriginalName = doc.Name;
        return doc;
    }

    public MaterialDocument Clone()
    {
        var copy = (MaterialDocument)MemberwiseClone();
        copy.Textures = Textures.Select(t => new MaterialTextureSlot { Key = t.Key, Path = t.Path }).ToList();
        copy.ShaderFlags = ShaderFlags.Select(f => new MaterialShaderFlag { Key = f.Key, Value = f.Value }).ToList();
        copy.unknown = (JObject)unknown.DeepClone();
        return copy;
    }

    public string Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Material needs a name.";

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in Textures)
        {
            if (string.IsNullOrWhiteSpace(slot.Key)) return "A texture slot is missing its name.";
            if (IsReservedKey(slot.Key)) return $"\"{slot.Key}\" is a material property and can't be used as a texture slot name.";
            if (!seen.Add(slot.Key)) return $"Texture slot \"{slot.Key}\" is listed twice.";
        }

        seen.Clear();
        foreach (var flag in ShaderFlags)
        {
            if (string.IsNullOrWhiteSpace(flag.Key)) return "A shader flag is missing its name.";
            if (!seen.Add(flag.Key)) return $"Shader flag \"{flag.Key}\" is listed twice.";
        }

        return null;
    }

    public string ToJson()
    {
        var json = new JObject { ["name"] = Name };

        foreach (var slot in Textures)
            if (!string.IsNullOrWhiteSpace(slot.Path))
                json[slot.Key] = slot.Path.Trim();

        json["surfaceType"] = SurfaceType;
        json["reflectivity"] = Reflectivity;
        json["transparent"] = Transparent;

        if (AlphaClip) json["alphaClip"] = true;
        if (NoCull) json["noCull"] = true;
        if (TexelsPerUnit > 0) json["texelsPerUnit"] = TexelsPerUnit;
        if (!string.IsNullOrWhiteSpace(ShaderName)) json["shaderName"] = ShaderName.Trim();

        if (ShaderFlags.Count > 0)
        {
            var flags = new JObject();
            foreach (var flag in ShaderFlags) flags[flag.Key] = flag.Value;
            json["shaderFlags"] = flags;
        }

        foreach (var prop in unknown.Properties())
            json[prop.Name] = prop.Value.DeepClone();

        return json.ToString(Formatting.Indented);
    }

    public void Save(string path)
    {
        File.WriteAllText(path, ToJson());
        FilePath = path;
    }

    public Material ToMaterial()
    {
        return new Material
        {
            Name = Name,
            SurfaceType = SurfaceType,
            ShaderName = string.IsNullOrWhiteSpace(ShaderName) ? null : ShaderName.Trim(),
            Reflectivity = Reflectivity,
            Transparent = Transparent,
            AlphaClip = AlphaClip,
            NoCull = NoCull,
            TexelsPerUnit = TexelsPerUnit,
            ShaderFlags = ShaderFlags.Count > 0 ? ShaderFlags.ToDictionary(f => f.Key, f => f.Value) : null,
            TexturePaths = Textures
                .Where(t => !string.IsNullOrWhiteSpace(t.Path))
                .ToDictionary(t => t.Key, t => t.Path.Trim()),
        };
    }
}
