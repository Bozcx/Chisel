using Avalonia.Media.Imaging;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using Rockwall;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Rockwall2.Editor.Common;

public static class GlobalEditorData
{
    public static string EDSFile = "", WorkingDirectory = "", ContentPath = "", CompileProgramPath = "";
    public static string[] RegisteredClassnames;
    public static Dictionary<string, EntityClassMetadata> RegisteredEntityMeta = new Dictionary<string, EntityClassMetadata>();
    public static EditorOverrides EditorOverrides;
    public static TextureItem[] TexturesAsImages;

    private static bool texturesLoaded = false;

    static readonly string[] RawExtensions = { ".png", ".jpg", ".jpeg", ".tga" };

    public static string MaterialsPath = "";
    public static string MaterialSourceRoot
    {
        get
        {
            if (string.IsNullOrEmpty(WorkingDirectory)) return MaterialsPath;
            string source = Path.Combine(WorkingDirectory, "Materials");
            return Directory.Exists(source) ? source : MaterialsPath;
        }
    }

    public static void LoadTex(bool forceReload = false)
    {
        if (texturesLoaded && !forceReload) return;
        if (!Directory.Exists(WorkingDirectory)) return;
        if (GlobalMapData.LoadedMaterials == null) return;

        var relativePaths = new Dictionary<string, string>();
        string materialsRoot = Path.Combine(ContentPath, "Materials");

        if (Directory.Exists(materialsRoot))
        {
            foreach (var file in Directory.EnumerateFiles(materialsRoot, "*.cmt", SearchOption.AllDirectories))
            {
                string name;
                try
                {
                    var mat = JsonConvert.DeserializeObject<Material>(File.ReadAllText(file));
                    name = mat.Name;
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(name)) continue;

                string dir = Path.GetDirectoryName(file) ?? materialsRoot;
                string relDir = Path.GetRelativePath(materialsRoot, dir).Replace('\\', '/');
                if (relDir == ".") relDir = "";

                relativePaths[name] = relDir;
            }
        }

        const int thumbnailSize = 128;

        TexturesAsImages = new TextureItem[GlobalMapData.LoadedMaterials.Length];
        for (int i = 0; i < GlobalMapData.LoadedMaterials.Length; i++)
        {
            var mat = GlobalMapData.LoadedMaterials[i];
            relativePaths.TryGetValue(mat.Name, out string relPath);

            Bitmap bmp = LoadThumbnail(mat.TextureName, thumbnailSize);

            TexturesAsImages[i] = new TextureItem(bmp, mat.Name, i, relPath ?? "");
        }

        texturesLoaded = true;
    }
    public static Bitmap LoadThumbnail(string textureName, int thumbnailSize)
    {
        foreach (var ext in RawExtensions)
        {
            string path = Path.Combine(WorkingDirectory, textureName + ext);
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, thumbnailSize);
            }
        }

        try
        {
            var tex = EditorHost.Instance.Content.Load<Texture2D>(textureName);
            return TextureToThumbnail(tex, thumbnailSize);
        }
        catch
        {
            return null;
        }
    }
    static Bitmap TextureToThumbnail(Texture2D tex, int thumbnailSize)
    {
        var pixels = new Microsoft.Xna.Framework.Color[tex.Width * tex.Height];
        tex.GetData(pixels);

        int dstW = thumbnailSize;
        int dstH = Math.Max(1, thumbnailSize * tex.Height / tex.Width);

        var bgra = new byte[dstW * dstH * 4];
        for (int dy = 0; dy < dstH; dy++)
        {
            int sy = dy * tex.Height / dstH;
            for (int dx = 0; dx < dstW; dx++)
            {
                int sx = dx * tex.Width / dstW;
                var c = pixels[sy * tex.Width + sx];
                int o = (dy * dstW + dx) * 4;
                bgra[o + 0] = c.B;
                bgra[o + 1] = c.G;
                bgra[o + 2] = c.R;
                bgra[o + 3] = c.A;
            }
        }

        var bmp = new WriteableBitmap(
            new Avalonia.PixelSize(dstW, dstH),
            new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Unpremul);

        using (var fb = bmp.Lock())
            System.Runtime.InteropServices.Marshal.Copy(bgra, 0, fb.Address, bgra.Length);

        return bmp;
    }
}

public class TextureItem : INotifyPropertyChanged
{
    public Bitmap Image { get; }
    public string Name { get; }
    public int MaterialIdx { get; }
    public string RelativePath { get; }

    private bool isSelected;
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected != value)
            {
                isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public TextureItem(Bitmap image, string name, int materialIdx, string relativePath)
    {
        Image = image;
        Name = name;
        MaterialIdx = materialIdx;
        RelativePath = relativePath;
    }

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string prop = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}

public class MaterialPickerState
{
    public double ScrollOffset { get; set; }
    public string NameFilter { get; set; } = "";
    public string PathFilter { get; set; } = "";
}