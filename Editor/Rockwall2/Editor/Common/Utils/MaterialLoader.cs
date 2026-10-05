using Newtonsoft.Json;
using Rockwall;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rockwall2.Editor.Common.Utils;

public static class MaterialLoader
{
    /// <summary>
    /// Mounts all .cmt material files from <paramref name="pathToRoot"/> and its subdirectories.
    /// </summary>
    /// <param name="pathToRoot">Path to the root of the materials folder.</param>
    public static void MountMaterials(string pathToRoot)
    {
        List<Material> materials = new List<Material>();
        Dictionary<string, int> matNames = GlobalMapData.MaterialNameToIndex?.ToDictionary() ?? new Dictionary<string, int>();

        int count = 0;
        foreach (var file in Directory.EnumerateFiles(pathToRoot, "*.cmt", SearchOption.AllDirectories))
        {
            var mat = JsonConvert.DeserializeObject<Material>(File.ReadAllText(file));
            materials.Add(mat);

            matNames.Add(mat.Name, (GlobalMapData.LoadedMaterials?.Length ?? 0) + count);
            count++;
        }

        if (GlobalMapData.LoadedMaterials == null) GlobalMapData.LoadedMaterials = materials.ToArray();
        else GlobalMapData.LoadedMaterials = GlobalMapData.LoadedMaterials.Concat(materials).ToArray();

        GlobalMapData.MaterialNameToIndex = matNames;
    }
}