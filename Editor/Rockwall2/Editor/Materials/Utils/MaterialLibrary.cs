using Microsoft.Xna.Framework.Graphics;
using Rockwall;
using Rockwall2.Editor.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Rockwall2.Editor.Materials.Utils;

public static class MaterialLibrary
{
    public static readonly string[] TextureExtensions = { ".png", ".jpg", ".jpeg", ".tga" };

    static readonly Dictionary<string, (DateTime stamp, Texture2D texture)> rawTextures = new(StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> EnumerateMaterialFiles()
    {
        string root = GlobalEditorData.MaterialSourceRoot;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return Enumerable.Empty<string>();
        return Directory.EnumerateFiles(root, "*.cmt", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
    }

    public static string RelativeToMaterialRoot(string path) =>
        Path.GetRelativePath(GlobalEditorData.MaterialSourceRoot, path).Replace('\\', '/');

    public static string ToTexturePath(string fullPath)
    {
        string relative = Path.GetRelativePath(GlobalEditorData.WorkingDirectory, fullPath);
        if (relative.StartsWith("..") || Path.IsPathRooted(relative)) return null;

        return Path.ChangeExtension(relative, null).Replace('\\', '/');
    }

    public static string FindTextureSource(string texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath)) return null;

        foreach (var ext in TextureExtensions)
        {
            string path = Path.Combine(GlobalEditorData.WorkingDirectory, texturePath.Trim() + ext);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public static bool TextureExists(string texturePath) =>
        FindTextureSource(texturePath) != null ||
        File.Exists(Path.Combine(GlobalEditorData.ContentPath, texturePath.Trim() + ".xnb"));

    public static bool IsNameTaken(string name, string originalName)
    {
        if (GlobalMapData.MaterialNameToIndex == null) return false;
        if (string.Equals(name, originalName, StringComparison.Ordinal)) return false;
        return GlobalMapData.MaterialNameToIndex.ContainsKey(name);
    }

    public static void MirrorToMounted(string sourcePath)
    {
        string sourceRoot = GlobalEditorData.MaterialSourceRoot;
        string mountedRoot = GlobalEditorData.MaterialsPath;
        if (string.IsNullOrEmpty(mountedRoot) || !Directory.Exists(mountedRoot)) return;
        if (string.Equals(Path.GetFullPath(sourceRoot), Path.GetFullPath(mountedRoot), StringComparison.OrdinalIgnoreCase)) return;

        string relative = Path.GetRelativePath(sourceRoot, sourcePath);
        if (relative.StartsWith("..") || Path.IsPathRooted(relative)) return;

        string destination = Path.Combine(mountedRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(sourcePath, destination, true);
    }

    // Deletes the source .cmt and its built copy, so it doesn't come back on next launch
    public static void DeleteMaterialFile(string sourcePath)
    {
        File.Delete(sourcePath);

        string sourceRoot = GlobalEditorData.MaterialSourceRoot;
        string mountedRoot = GlobalEditorData.MaterialsPath;
        if (string.IsNullOrEmpty(mountedRoot) || !Directory.Exists(mountedRoot)) return;
        if (string.Equals(Path.GetFullPath(sourceRoot), Path.GetFullPath(mountedRoot), StringComparison.OrdinalIgnoreCase)) return;

        string relative = Path.GetRelativePath(sourceRoot, sourcePath);
        if (relative.StartsWith("..") || Path.IsPathRooted(relative)) return;

        string mountedCopy = Path.Combine(mountedRoot, relative);
        if (File.Exists(mountedCopy)) File.Delete(mountedCopy);
    }

    public static void ApplyToLoaded(MaterialDocument doc)
    {
        Material material = doc.ToMaterial();
        foreach (var (key, path) in material.TexturePaths)
        {
            var texture = LoadTexture(path);
            if (texture != null) material.SetTexture(key, texture);
        }

        GlobalMapData.LoadedMaterials ??= Array.Empty<Material>();
        GlobalMapData.MaterialNameToIndex ??= new Dictionary<string, int>();

        int index;
        if (doc.OriginalName != null && GlobalMapData.MaterialNameToIndex.TryGetValue(doc.OriginalName, out index))
        {
            GlobalMapData.LoadedMaterials[index] = material;
            if (doc.OriginalName != material.Name)
            {
                GlobalMapData.MaterialNameToIndex.Remove(doc.OriginalName);
                GlobalMapData.MaterialNameToIndex[material.Name] = index;
            }
        }
        else if (GlobalMapData.MaterialNameToIndex.TryGetValue(material.Name, out index))
        {
            GlobalMapData.LoadedMaterials[index] = material;
        }
        else
        {
            index = GlobalMapData.LoadedMaterials.Length;
            GlobalMapData.LoadedMaterials = [.. GlobalMapData.LoadedMaterials, material];
            GlobalMapData.MaterialNameToIndex[material.Name] = index;
        }

        RefreshPickerEntry(index, doc);
    }

    static void RefreshPickerEntry(int index, MaterialDocument doc)
    {
        if (GlobalEditorData.TexturesAsImages == null) return;

        string relDir = doc.FilePath != null ? Path.GetDirectoryName(RelativeToMaterialRoot(doc.FilePath))?.Replace('\\', '/') ?? "" : "";
        var item = new TextureItem(GlobalEditorData.LoadThumbnail(doc.GetTexturePath(MaterialDocument.AlbedoKey), 128), doc.Name, index, relDir);

        if (index < GlobalEditorData.TexturesAsImages.Length)
            GlobalEditorData.TexturesAsImages[index] = item;
        else
            GlobalEditorData.TexturesAsImages = [.. GlobalEditorData.TexturesAsImages, item];

        MaterialPicker.RefreshMaterials();
    }

    public static Texture2D LoadTexture(string texturePath)
    {
        var content = EditorHost.Instance.Content;
        string previousRoot = content.RootDirectory;
        try
        {
            content.RootDirectory = GlobalEditorData.ContentPath;
            return content.Load<Texture2D>(texturePath);
        }
        catch
        {
            //fall through to the source image.
        }
        finally
        {
            content.RootDirectory = previousRoot;
        }

        string source = FindTextureSource(texturePath);
        if (source == null) return null;

        DateTime stamp = File.GetLastWriteTimeUtc(source);
        if (rawTextures.TryGetValue(source, out var cached) && cached.stamp == stamp) return cached.texture;

        try
        {
            var texture = Texture2D.FromFile(EditorHost.Instance.GraphicsDevice, source);
            rawTextures[source] = (stamp, texture);
            return texture;
        }
        catch
        {
            return null;
        }
    }
}
