using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using MsBox.Avalonia;
using Newtonsoft.Json.Linq;
using Rockwall;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Common.Input;
using Rockwall2.Editor.Materials;
using Rockwall2.Editor.Materials.Utils;
using Rockwall2.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Rockwall2;

public class MaterialListEntry
{
    public string FullPath { get; init; } = "";
    public string Name { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public Bitmap Thumbnail { get; init; }
}

public partial class MaterialEditor : UserControl
{
    static readonly IBrush NormalText = new SolidColorBrush(Color.Parse("#B0B0B0"));
    static readonly IBrush ErrorText = new SolidColorBrush(Color.Parse("#E07070"));
    static readonly IBrush MissingText = new SolidColorBrush(Color.Parse("#E0A050"));

    MaterialDocument doc;
    MaterialDocument savedState;
    bool dirty;
    bool updating;
    bool changingSelection;
    bool listLoaded;

    List<MaterialListEntry> entries = new();
    MaterialPreview preview;

    public MaterialEditor()
    {
        InitializeComponent();
        if (Design.IsDesignMode) return;

        preview = new MaterialPreview(previewControl);
        previewControl.Host = App.Host;
        previewControl.Scene = preview;

        AttachedToVisualTree += (_, _) =>
        {
            if (!listLoaded) RefreshList();
        };

        UpdateHeader();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            _ = SaveMaterial();
        }
    }

    public void RefreshList()
    {
        entries = MaterialLibrary.EnumerateMaterialFiles().Select(CreateEntry).ToList();
        listLoaded = true;

        string root = GlobalEditorData.MaterialSourceRoot;
        rootLabel.Text = string.IsNullOrEmpty(root) ? "No materials folder found." : $"{entries.Count} materials in {root}";

        ApplyFilter();
    }

    static MaterialListEntry CreateEntry(string path)
    {
        string name;
        try
        {
            name = JObject.Parse(File.ReadAllText(path)).GetValue("name", StringComparison.OrdinalIgnoreCase)?.Value<string>() ?? "";
        }
        catch
        {
            name = "";
        }
        if (string.IsNullOrEmpty(name)) name = $"({Path.GetFileNameWithoutExtension(path)})";

        Bitmap thumbnail = null;
        if (GlobalMapData.MaterialNameToIndex != null && GlobalEditorData.TexturesAsImages != null &&
            GlobalMapData.MaterialNameToIndex.TryGetValue(name, out int index) && index < GlobalEditorData.TexturesAsImages.Length)
        {
            thumbnail = GlobalEditorData.TexturesAsImages[index]?.Image;
        }

        return new MaterialListEntry
        {
            FullPath = path,
            Name = name,
            RelativePath = MaterialLibrary.RelativeToMaterialRoot(path),
            Thumbnail = thumbnail,
        };
    }

    void ApplyFilter()
    {
        string filter = searchBox.Text?.Trim() ?? "";
        var visible = string.IsNullOrEmpty(filter)
            ? entries
            : entries.Where(e => e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                 e.RelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        changingSelection = true;
        materialList.ItemsSource = visible;
        materialList.SelectedItem = visible.FirstOrDefault(e => SamePath(e.FullPath, doc?.FilePath));
        changingSelection = false;
    }

    void SelectInList(string path)
    {
        changingSelection = true;
        materialList.SelectedItem = (materialList.ItemsSource as IEnumerable<MaterialListEntry>)?.FirstOrDefault(e => SamePath(e.FullPath, path));
        changingSelection = false;
    }

    static bool SamePath(string a, string b) =>
        a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private async void MaterialList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (changingSelection) return;
        if (materialList.SelectedItem is not MaterialListEntry entry) return;
        if (SamePath(entry.FullPath, doc?.FilePath)) return;

        if (!await ConfirmDiscardChanges())
        {
            SelectInList(doc?.FilePath);
            return;
        }

        OpenMaterial(entry.FullPath);
    }

    private void NewButton_Click(object? sender, RoutedEventArgs e) => NewMaterial();
    private void DuplicateButton_Click(object? sender, RoutedEventArgs e) => DuplicateMaterial();
    private void RefreshButton_Click(object? sender, RoutedEventArgs e)
    {
        preview?.ClearTextureCache();
        RefreshList();
    }

    private void ShapeBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (preview == null) return;
        preview.Shape = (PreviewShape)shapeBox.SelectedIndex;
    }

    private void ResetLight_Click(object? sender, RoutedEventArgs e) => preview?.ResetLight();

    public void OpenMaterial(string path)
    {
        try
        {
            SetDocument(MaterialDocument.Load(path), false);
            SetStatus($"Working File: {MaterialLibrary.RelativeToMaterialRoot(path)}");
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't open {Path.GetFileName(path)}: {ex.Message}", true);
        }
        SelectInList(doc?.FilePath);
    }

    public async void NewMaterial()
    {
        if (!await ConfirmDiscardChanges()) return;

        var created = new MaterialDocument { Name = "new_material" };

        foreach (var (key, path) in new[] { (MaterialDocument.SpecularKey, "Textures/base/basicSpecular"), (MaterialDocument.NormalKey, "Textures/base/basicNormal") })
            if (MaterialLibrary.TextureExists(path))
                created.Textures.First(t => t.Key == key).Path = path;

        SetDocument(created, true);
        SelectInList(null);
        SetStatus("New material. Pick a name and textures, then save.");

        nameBox.Focus();
        nameBox.SelectAll();
    }

    public async void DuplicateMaterial()
    {
        if (doc == null) return;
        if (!await ConfirmDiscardChanges()) return;

        var copy = (savedState ?? doc).Clone();
        copy.FilePath = null;
        copy.OriginalName = null;
        copy.Name += "_copy";

        SetDocument(copy, true);
        SelectInList(null);
        SetStatus("Duplicated. Save to choose where the copy goes.");
    }

    public async void DeleteMaterial()
    {
        if (doc == null) return;

        // Never saved, so there's no file, just drop it
        if (doc.FilePath == null)
        {
            ClearDocument();
            SetStatus("Discarded unsaved material.");
            return;
        }

        var result = await MessageBoxManager.GetMessageBoxStandard("Delete material",
                $"Delete \"{doc.Name}\"?\n\n{doc.FilePath}\n\nMaps that use it will lose it. This can't be undone.",
                MsBox.Avalonia.Enums.ButtonEnum.YesNo).ShowAsPopupAsync(MainWindow.Instance);
        if (result != MsBox.Avalonia.Enums.ButtonResult.Yes) return;

        string path = doc.FilePath;
        try
        {
            MaterialLibrary.DeleteMaterialFile(path);
        }
        catch (Exception ex)
        {
            SetStatus($"Delete failed: {ex.Message}", true);
            return;
        }

        ClearDocument();
        RefreshList();
        SetStatus($"Deleted {MaterialLibrary.RelativeToMaterialRoot(path)}.");
    }

    void ClearDocument()
    {
        doc = null;
        savedState = null;
        dirty = false;
        preview.Document = null;
        UpdateHeader();
        SelectInList(null);
    }

    private void DeleteButton_Click(object? sender, RoutedEventArgs e) => DeleteMaterial();

    void SetDocument(MaterialDocument document, bool isDirty)
    {
        doc = document;
        savedState = document.Clone();
        preview.Document = document;
        dirty = isDirty;

        PopulateFields();
        UpdateHeader();
    }

    async Task<bool> ConfirmDiscardChanges()
    {
        if (doc == null || !dirty) return true;

        var result = await MessageBoxManager.GetMessageBoxStandard("Unsaved changes",
                $"Save changes to \"{doc.Name}\"?",
                MsBox.Avalonia.Enums.ButtonEnum.YesNoCancel).ShowAsPopupAsync(MainWindow.Instance);

        return result switch
        {
            MsBox.Avalonia.Enums.ButtonResult.Yes => await SaveMaterial(),
            MsBox.Avalonia.Enums.ButtonResult.No => true,
            _ => false,
        };
    }

    public Task<bool> SaveMaterial() => Save(false);
    public Task<bool> SaveMaterialAs() => Save(true);

    async Task<bool> Save(bool saveAs)
    {
        if (doc == null) return false;

        string error = doc.Validate();
        if (error != null)
        {
            SetStatus(error, true);
            return false;
        }

        string path = doc.FilePath;
        if (saveAs || path == null)
        {
            path = await PickSavePath();
            if (path == null) return false;
        }

        string sourceRoot = GlobalEditorData.MaterialSourceRoot;
        string relative = Path.GetRelativePath(sourceRoot, path);
        if (relative.StartsWith("..") || Path.IsPathRooted(relative))
        {
            SetStatus($"Materials have to be saved inside {sourceRoot} to be loaded.", true);
            return false;
        }

        bool sameFile = SamePath(path, doc.FilePath);
        string originalName = sameFile ? doc.OriginalName : null;

        bool nameTaken = MaterialLibrary.IsNameTaken(doc.Name, originalName) ||
                         entries.Any(e => e.Name == doc.Name && !SamePath(e.FullPath, path));
        if (nameTaken)
        {
            SetStatus($"Another material is already named \"{doc.Name}\". Material names must be unique.", true);
            return false;
        }

        bool isNewFile = !File.Exists(path);
        string renamedFrom = originalName != null && originalName != doc.Name ? originalName : null;
        string previousOriginal = doc.OriginalName;

        try
        {
            doc.OriginalName = originalName;
            doc.Save(path);
            MaterialLibrary.MirrorToMounted(path);
            MaterialLibrary.ApplyToLoaded(doc);
            doc.OriginalName = doc.Name;
        }
        catch (Exception ex)
        {
            doc.OriginalName = previousOriginal;
            SetStatus($"Save failed: {ex.Message}", true);
            return false;
        }

        savedState = doc.Clone();
        dirty = false;
        UpdateHeader();
        RefreshList();

        string message = $"Saved {MaterialLibrary.RelativeToMaterialRoot(path)}";
        if (renamedFrom != null)
            message += $". Renamed from \"{renamedFrom}\": maps saved with the old name will need it reassigned";
        if (isNewFile)
            message += ". New file: add it to your content project (Content.mgcb) so game builds include it";
        SetStatus(message + ".");

        KeyboardManager.ClearKeys();
        return true;
    }

    async Task<string> PickSavePath()
    {
        var topLevel = TopLevel.GetTopLevel(MainWindow.Instance)!;
        string startDir = doc?.FilePath != null ? Path.GetDirectoryName(doc.FilePath)! : GlobalEditorData.MaterialSourceRoot;
        var startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(startDir);

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Material",
            DefaultExtension = ".cmt",
            SuggestedFileName = SanitizeFileName(doc?.Name ?? "material"),
            FileTypeChoices = new[] { new FilePickerFileType("Chisel Material") { Patterns = new[] { "*.cmt" } } },
            SuggestedStartLocation = startFolder,
        });

        KeyboardManager.ClearKeys();
        return file?.Path.LocalPath;
    }

    static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(name.Trim().Select(c => invalid.Contains(c) || c == ' ' ? '_' : char.ToLowerInvariant(c)).ToArray());
        return string.IsNullOrEmpty(cleaned) ? "material" : cleaned;
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e) => await SaveMaterial();

    private void RevertButton_Click(object? sender, RoutedEventArgs e)
    {
        if (savedState == null) return;
        SetDocument(savedState.Clone(), savedState.FilePath == null);
        SetStatus("Reverted.");
    }

    void PopulateFields()
    {
        if (doc == null) return;

        updating = true;
        try
        {
            nameBox.Text = doc.Name;

            surfaceBox.ItemsSource = (GlobalMapData.LoadedMaterials ?? Array.Empty<Material>())
                .Select(m => m.SurfaceType)
                .Append("concrete")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order()
                .ToList();
            surfaceBox.Text = doc.SurfaceType;

            reflectivitySlider.Value = doc.Reflectivity;
            reflectivityBox.Value = (decimal)doc.Reflectivity;
            texelsBox.Value = doc.TexelsPerUnit;

            transparentCheck.IsChecked = doc.Transparent;
            alphaClipCheck.IsChecked = doc.AlphaClip;
            noCullCheck.IsChecked = doc.NoCull;
            shaderBox.Text = doc.ShaderName;

            RebuildTextureRows();
            RebuildFlagRows();
        }
        finally
        {
            updating = false;
        }

        UpdatePreview();
    }

    void MarkDirty()
    {
        if (dirty) return;
        dirty = true;
        UpdateHeader();
    }

    void UpdateHeader()
    {
        bool hasDoc = doc != null;
        editorScroll.IsVisible = hasDoc;
        emptyText.IsVisible = !hasDoc;
        saveButton.IsEnabled = hasDoc;
        revertButton.IsEnabled = hasDoc && dirty;

        if (doc == null) return;

        titleText.Text = (string.IsNullOrWhiteSpace(doc.Name) ? "(unnamed)" : doc.Name) + (dirty ? "  *" : "");
        pathText.Text = doc.FilePath != null ? doc.FilePath : "Not saved yet";
    }

    void UpdatePreview()
    {
        string albedo = doc?.GetTexturePath(MaterialDocument.AlbedoKey) ?? "";
        Bitmap bitmap = string.IsNullOrWhiteSpace(albedo) ? null : GlobalEditorData.LoadThumbnail(albedo.Trim(), 256);
        previewImage.Source = bitmap;
        previewMissing.IsVisible = bitmap == null;
        previewMissing.Text = string.IsNullOrWhiteSpace(albedo) ? "No albedo" : "Not found";
    }

    void SetStatus(string text, bool error = false)
    {
        statusText.Text = text;
        statusText.Foreground = error ? ErrorText : NormalText;
    }

    private void NameBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (updating || doc == null || doc.Name == (nameBox.Text ?? "")) return;
        doc.Name = nameBox.Text ?? "";
        MarkDirty();
        UpdateHeader();
    }

    private void SurfaceBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (updating || doc == null || doc.SurfaceType == (surfaceBox.Text ?? "")) return;
        doc.SurfaceType = surfaceBox.Text ?? "";
        MarkDirty();
    }

    private void ReflectivitySlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (updating || doc == null) return;
        float value = (float)Math.Round(e.NewValue, 3);
        if (value == doc.Reflectivity) return;

        doc.Reflectivity = value;
        updating = true;
        reflectivityBox.Value = (decimal)value;
        updating = false;
        MarkDirty();
    }

    private void ReflectivityBox_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (updating || doc == null || e.NewValue == null) return;
        float value = (float)e.NewValue.Value;
        if (value == doc.Reflectivity) return;

        doc.Reflectivity = value;
        updating = true;
        reflectivitySlider.Value = value;
        updating = false;
        MarkDirty();
    }

    private void TexelsBox_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (updating || doc == null) return;
        int value = (int)(e.NewValue ?? 0);
        if (value == doc.TexelsPerUnit) return;

        doc.TexelsPerUnit = value;
        MarkDirty();
    }

    private void RenderFlag_Changed(object? sender, RoutedEventArgs e)
    {
        if (updating || doc == null) return;

        bool transparent = transparentCheck.IsChecked == true;
        bool alphaClip = alphaClipCheck.IsChecked == true;
        bool noCull = noCullCheck.IsChecked == true;
        if (transparent == doc.Transparent && alphaClip == doc.AlphaClip && noCull == doc.NoCull) return;

        doc.Transparent = transparent;
        doc.AlphaClip = alphaClip;
        doc.NoCull = noCull;
        MarkDirty();
    }

    private void ShaderBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (updating || doc == null || doc.ShaderName == (shaderBox.Text ?? "")) return;
        doc.ShaderName = shaderBox.Text ?? "";
        MarkDirty();
    }

    void RebuildTextureRows()
    {
        textureRows.Children.Clear();
        if (doc == null) return;

        foreach (var slot in doc.Textures)
            textureRows.Children.Add(CreateTextureRow(slot));
    }

    static string SlotDisplayName(string key) => key switch
    {
        MaterialDocument.AlbedoKey => "Albedo",
        MaterialDocument.SpecularKey => "Specular",
        MaterialDocument.NormalKey => "Normal",
        _ => key,
    };

    Control CreateTextureRow(MaterialTextureSlot slot)
    {
        bool builtin = MaterialDocument.BuiltinTextureKeys.Contains(slot.Key);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("52,120,*,Auto,Auto") };

        var thumb = new Image { Stretch = Stretch.UniformToFill };
        grid.Children.Add(new Border
        {
            Width = 48,
            Height = 48,
            Background = new SolidColorBrush(Color.Parse("#0C0C0C")),
            CornerRadius = new CornerRadius(2),
            Child = thumb,
        });

        Control keyControl;
        if (builtin)
        {
            var label = new TextBlock { Text = SlotDisplayName(slot.Key), Margin = new Thickness(8, 0) };
            label.Classes.Add("label");
            ToolTip.SetTip(label, $"Saved as \"{slot.Key}\"");
            keyControl = label;
        }
        else
        {
            var keyBox = new TextBox { Text = slot.Key, Watermark = "sampler name", Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(keyBox, "Bound to the shader as a sampler uniform with this name.");
            keyBox.TextChanged += (_, _) =>
            {
                if (slot.Key == (keyBox.Text ?? "")) return;
                slot.Key = keyBox.Text ?? "";
                MarkDirty();
            };
            keyControl = keyBox;
        }
        Grid.SetColumn(keyControl, 1);
        grid.Children.Add(keyControl);

        var pathBox = new TextBox { Text = slot.Path, Watermark = "Textures/...", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(pathBox, 2);
        grid.Children.Add(pathBox);

        void UpdateSlotPreview()
        {
            string path = slot.Path.Trim();
            bool exists = path.Length > 0 && MaterialLibrary.TextureExists(path);

            thumb.Source = exists ? GlobalEditorData.LoadThumbnail(path, 64) : null;
            if (path.Length > 0 && !exists) pathBox.Foreground = MissingText;
            else pathBox.ClearValue(TextBox.ForegroundProperty);
            ToolTip.SetTip(pathBox, path.Length > 0 && !exists ? "Texture not found in the working or content directory." : null);

            if (slot.Key == MaterialDocument.AlbedoKey) UpdatePreview();
        }

        pathBox.TextChanged += (_, _) =>
        {
            if (slot.Path == (pathBox.Text ?? "")) return;
            slot.Path = pathBox.Text ?? "";
            MarkDirty();
        };
        pathBox.LostFocus += (_, _) => UpdateSlotPreview();
        pathBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) UpdateSlotPreview();
        };

        var browse = new Button { Content = "Browse...", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        browse.Click += async (_, _) =>
        {
            string picked = await BrowseForTexture(slot.Path);
            if (picked == null) return;

            pathBox.Text = picked;
            UpdateSlotPreview();
        };
        Grid.SetColumn(browse, 3);
        grid.Children.Add(browse);

        var clear = new Button { Content = builtin ? "Clear" : "Remove", Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        clear.Click += (_, _) =>
        {
            if (builtin)
            {
                pathBox.Text = "";
                UpdateSlotPreview();
                return;
            }

            doc?.Textures.Remove(slot);
            textureRows.Children.Remove(grid);
            MarkDirty();
        };
        Grid.SetColumn(clear, 4);
        grid.Children.Add(clear);

        UpdateSlotPreview();
        return grid;
    }

    async Task<string> BrowseForTexture(string currentPath)
    {
        var topLevel = TopLevel.GetTopLevel(MainWindow.Instance)!;

        string currentSource = MaterialLibrary.FindTextureSource(currentPath);
        string startDir = currentSource != null ? Path.GetDirectoryName(currentSource)! : Path.Combine(GlobalEditorData.WorkingDirectory, "Textures");
        if (!Directory.Exists(startDir)) startDir = GlobalEditorData.WorkingDirectory;
        var startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(startDir);

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose Texture",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = MaterialLibrary.TextureExtensions.Select(e => "*" + e).ToArray() } },
            SuggestedStartLocation = startFolder,
        });
        KeyboardManager.ClearKeys();

        if (files.Count == 0) return null;

        string texturePath = MaterialLibrary.ToTexturePath(files[0].Path.LocalPath);
        if (texturePath == null)
        {
            SetStatus($"Textures have to be inside the working directory ({GlobalEditorData.WorkingDirectory}).", true);
            return null;
        }
        return texturePath;
    }

    private void AddTextureSlot_Click(object? sender, RoutedEventArgs e)
    {
        if (doc == null) return;

        var slot = new MaterialTextureSlot { Key = "" };
        doc.Textures.Add(slot);
        var row = CreateTextureRow(slot);
        textureRows.Children.Add(row);
        MarkDirty();

        (row as Grid)?.Children.OfType<TextBox>().FirstOrDefault()?.Focus();
    }

    void RebuildFlagRows()
    {
        flagRows.Children.Clear();
        if (doc != null)
            foreach (var flag in doc.ShaderFlags)
                flagRows.Children.Add(CreateFlagRow(flag));

        noFlagsText.IsVisible = flagRows.Children.Count == 0;
    }

    Control CreateFlagRow(MaterialShaderFlag flag)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        var check = new CheckBox { IsChecked = flag.Value, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(check, "Flag value");
        check.IsCheckedChanged += (_, _) =>
        {
            bool value = check.IsChecked == true;
            if (flag.Value == value) return;
            flag.Value = value;
            MarkDirty();
        };
        grid.Children.Add(check);

        var keyBox = new TextBox { Text = flag.Key, Watermark = "flag name", VerticalAlignment = VerticalAlignment.Center };
        keyBox.TextChanged += (_, _) =>
        {
            if (flag.Key == (keyBox.Text ?? "")) return;
            flag.Key = keyBox.Text ?? "";
            MarkDirty();
        };
        Grid.SetColumn(keyBox, 1);
        grid.Children.Add(keyBox);

        var remove = new Button { Content = "Remove", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        remove.Click += (_, _) =>
        {
            doc?.ShaderFlags.Remove(flag);
            RebuildFlagRows();
            MarkDirty();
        };
        Grid.SetColumn(remove, 2);
        grid.Children.Add(remove);

        return grid;
    }

    void AddFlag(string key)
    {
        if (doc == null) return;

        var flag = new MaterialShaderFlag { Key = key, Value = true };
        doc.ShaderFlags.Add(flag);
        RebuildFlagRows();
        MarkDirty();

        if (key.Length == 0)
            (flagRows.Children.LastOrDefault() as Grid)?.Children.OfType<TextBox>().FirstOrDefault()?.Focus();
    }

    private void AddFlag_Click(object? sender, RoutedEventArgs e)
    {
        if (doc == null) return;

        var menu = new MenuFlyout();
        foreach (var known in MaterialDocument.KnownShaderFlags)
        {
            if (doc.ShaderFlags.Any(f => f.Key == known)) continue;

            var item = new MenuItem { Header = known };
            item.Click += (_, _) => AddFlag(known);
            menu.Items.Add(item);
        }

        var custom = new MenuItem { Header = "Custom flag..." };
        custom.Click += (_, _) => AddFlag("");
        menu.Items.Add(custom);

        menu.ShowAt(addFlagButton);
    }

}
