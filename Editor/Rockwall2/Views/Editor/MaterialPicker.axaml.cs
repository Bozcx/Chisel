using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Rockwall2.Editor.Common;
using Rockwall2.Editor.Mapper;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Rockwall2;

public partial class MaterialPicker : Window
{
    private static MaterialPicker instance;

    public int Result { get; private set; } = -1;
    private bool isWindowDragInEffect = false;
    private Point cursorDragStart = new(0, 0);

    private ObservableCollection<TextureItem> allItems = new ObservableCollection<TextureItem>();
    private ObservableCollection<TextureItem> filteredItems = new ObservableCollection<TextureItem>();

    private static List<string> cachedDistinctPaths;

    private TextureItem selectedItem;
    private MaterialPickerState state;

    private TaskCompletionSource<int> pendingResult;

    private MaterialPicker()
    {
        InitializeComponent();
        if (Design.IsDesignMode) return;

        textureList.ItemsSource = filteredItems;
        LoadItems();

        state ??= new MaterialPickerState();

        pathFilterBox.Text = state.PathFilter;
        nameFilterBox.Text = state.NameFilter;

        ApplyFilters();

        this.Opened += (sender, e) =>
        {
            textureScroll.Offset = new Vector(0, state.ScrollOffset);
        };

        this.Closing += OnClosing;
    }

    private void OnClosing(object sender, WindowClosingEventArgs e)
    {
        e.Cancel = true;

        state.PathFilter = pathFilterBox.Text ?? "";
        state.NameFilter = nameFilterBox.Text ?? "";
        state.ScrollOffset = textureScroll.Offset.Y;

        Hide();

        pendingResult?.TrySetResult(Result);
        pendingResult = null;
    }

    public static Task<int> PickAsync(Window owner)
    {
        instance ??= new MaterialPicker();

        instance.pendingResult = new TaskCompletionSource<int>();
        instance.Result = -1;
        if (instance.selectedItem != null)
        {
            instance.selectedItem.IsSelected = false;
            instance.selectedItem = null;
        }

        if (instance.IsVisible)
        {
            instance.Activate();
        }
        else
        {
            instance.Show(owner);
        }

        return instance.pendingResult.Task;
    }

    private void LoadItems()
    {
        allItems.Clear();
        foreach (var img in GlobalEditorData.TexturesAsImages)
        {
            allItems.Add(img);
        }

        cachedDistinctPaths ??= allItems
            .Select(i => i.RelativePath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        pathFilterBox.ItemsSource = cachedDistinctPaths;
    }

    public static void RefreshMaterials()
    {
        cachedDistinctPaths = null;
        if (instance == null) return;

        instance.SelectItem(null);
        instance.LoadItems();
        instance.ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (filteredItems == null) return;

        string pathFilter = (pathFilterBox.Text ?? "").Trim().Trim('/').ToLowerInvariant();
        string nameFilter = (nameFilterBox.Text ?? "").Trim();

        filteredItems.Clear();

        foreach (var item in allItems)
        {
            if (!string.IsNullOrEmpty(pathFilter))
            {
                string itemPath = (item.RelativePath ?? "").ToLowerInvariant();
                bool pathMatches = itemPath == pathFilter || itemPath.StartsWith(pathFilter + "/");
                if (!pathMatches) continue;
            }

            if (!string.IsNullOrEmpty(nameFilter))
            {
                if (item.Name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            }

            filteredItems.Add(item);
        }
    }

    private void pathFilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();
    private void nameFilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

    private void SelectItem(TextureItem item)
    {
        if (selectedItem != null)
        {
            selectedItem.IsSelected = false;
        }
        selectedItem = item;
        if (selectedItem != null)
        {
            selectedItem.IsSelected = true;
        }
        Result = item?.MaterialIdx ?? -1;
    }

    private void Item_Tapped(object sender, TappedEventArgs e)
    {
        if (sender is Control c && c.DataContext is TextureItem item)
        {
            SelectItem(item);
        }
    }

    private void Item_DoubleTapped(object sender, TappedEventArgs e)
    {
        if (sender is Control c && c.DataContext is TextureItem item)
        {
            SelectItem(item);
            Close();
        }
    }

    private void cancelButton_Click(object sender, RoutedEventArgs e)
    {
        Result = -1;
        Close();
    }

    private void acceptButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (isWindowDragInEffect)
        {
            Point currentCursorPosition = e.GetPosition(this);
            Point delta = currentCursorPosition - cursorDragStart;
            Position = this.PointToScreen(delta);
        }
    }
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen) return;
        isWindowDragInEffect = true;
        cursorDragStart = e.GetPosition(this);
    }
    private void TitleBar_PointerReleased(object? sender, PointerReleasedEventArgs e)
        => isWindowDragInEffect = false;
}