using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FortnitePorting.Controls.Navigation.Sidebar;
using FortnitePorting.Controls.WrapPanel;
using FortnitePorting.Framework;
using FortnitePorting.Models.Assets;
using FortnitePorting.Models.Assets.Asset;
using FortnitePorting.Models.Assets.Filters;
using FortnitePorting.Services;
using FortnitePorting.ViewModels;
using Newtonsoft.Json;
using BaseAssetItem = FortnitePorting.Models.Assets.Base.BaseAssetItem;

namespace FortnitePorting.Views;

public partial class AssetsView : ViewBase<AssetsViewModel>
{
    private bool _suppressSelectionChange;
    private PointerPressedEventArgs? _assetDragArgs;
    private Point _dragStartPosition;
    private EExportType? _previousAssetType;
    private CancellationTokenSource? _assetTransitionCts;

    public AssetsView()
    {
        InitializeComponent();

        Navigation.Assets.Initialize(Sidebar);
        Navigation.Assets.AddBehaviorResolver<EExportType>(ChangeTab);
        Navigation.Assets.AddBehaviorResolver<string>(type =>
        {
            if (!Enum.TryParse(type, true, out EExportType enumType)) return;
            ChangeTab(enumType);
        });

        PointerWheelChangedEvent.AddClassHandler<TopLevel>((_, args) =>
        {
            if ((args.KeyModifiers & KeyModifiers.Control) == 0) return;

            ViewModel.AdjustAssetScale(args.Delta.Y > 0);
            args.Handled = true;
        }, handledEventsToo: true);

        AssetsListBox.AddHandler(PointerPressedEvent, OnAssetItemPressed, RoutingStrategies.Tunnel);
        AssetsListBox.AddHandler(PointerMovedEvent, OnAssetItemPointerMoved, RoutingStrategies.Tunnel);
        AssetsListBox.AddHandler(PointerReleasedEvent, OnAssetItemPointerReleased, RoutingStrategies.Tunnel);
    }

    private void ChangeTab(EExportType assetType)
    {
        var previousAssetType = _previousAssetType;
        var direction = GetAssetTabIndex(assetType).CompareTo(GetAssetTabIndex(previousAssetType));

        AssetsListBox.SelectedItems?.Clear();
        ViewModel.ChangeTab(assetType);
        _previousAssetType = assetType;

        if (previousAssetType is null || direction == 0 || !AppSettings.Application.UseTabTransitions)
            return;

        TaskService.Run(() => AnimateWhenReadyAsync(assetType, direction));
    }

    private int GetAssetTabIndex(EExportType? assetType)
    {
        if (assetType is null) return -1;

        return ViewModel.AssetLoader.Categories
            .SelectMany(category => category.Loaders)
            .Select(loader => loader.Type)
            .ToList()
            .IndexOf(assetType.Value);
    }

    private async Task AnimateWhenReadyAsync(EExportType assetType, int direction)
    {
        while (ViewModel.AssetLoader.ActiveLoader?.Type == assetType &&
               !ViewModel.AssetLoader.ActiveLoader.FinishedLoading)
        {
            await Task.Delay(50);
        }

        if (ViewModel.AssetLoader.ActiveLoader?.Type != assetType)
            return;

        await Task.Delay(16);
        await Dispatcher.UIThread.InvokeAsync(() => _ = AnimateAssetListAsync(direction));
    }

    private async Task AnimateAssetListAsync(int direction)
    {
        _assetTransitionCts?.Cancel();
        _assetTransitionCts?.Dispose();
        _assetTransitionCts = new CancellationTokenSource();

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(TranslateTransform.XProperty, 96d * direction)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(TranslateTransform.XProperty, 0d)
                    }
                }
            }
        };

        try
        {
            await animation.RunAsync(AssetListCard, _assetTransitionCts.Token);
        }
        catch (OperationCanceledException)
        {
            // A newer tab was selected before this transition completed.
        }
        finally
        {
            AssetListCard.Opacity = 1;
            AssetListCard.RenderTransform = null;
        }
    }

    private void OnRandomButtonPressed(object? sender, RoutedEventArgs routedEventArgs)
    {
        var index = ViewModel.GetRandomIndex(AssetsListBox.Items.Count);
        if (index < 0) return;

        AssetsListBox.SelectedIndex = index;
        if (AssetsListBox.SelectedItem is not AssetItem item) return;
        if (item.IconDisplayImage is not null) return;

        TaskService.Run(item.LoadBitmapAsync);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox) return;
        if (listBox.SelectedItems is null || listBox.SelectedItems.Count == 0) return;

        ViewModel.SyncSelectedAssets(listBox.SelectedItems.Cast<BaseAssetItem>());
    }

    private void OnScrollAssets(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;

        switch (e.Delta.Y)
        {
            case < 0: scrollViewer.LineLeft(); break;
            case > 0: scrollViewer.LineRight(); break;
        }
    }

    private void OnFilterChecked(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Content: not null, IsChecked: { } isChecked, DataContext: FilterItem filterItem }) return;

        ViewModel.UpdateFilter(filterItem, isChecked);
    }

    private void OnItemSelected(object? sender, SidebarItemSelectedArgs e)
    {
        if (e.Tag is not EExportType assetType) return;

        ChangeTab(assetType);
    }

    private void OnItemRealized(object? sender, ItemRealizedEventArgs e)
    {
        // The loader finishes thumbnail work before this grid becomes visible.
    }

    private void OnStyleBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: AssetStyleInfo { RequiredSelection: false } assetStyleInfo }) return;

        assetStyleInfo.SelectedStyleIndex = -1;
        assetStyleInfo.SelectedItems.Clear();
    }

    private void OnStyleFlyoutItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not ListBox listBox) return;
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (e.Source is not Control source || source.FindAncestorOfType<ListBoxItem>() is null) return;

        if (listBox.FindAncestorOfType<FlyoutPresenter>()?.Parent is Popup popup)
            popup.IsOpen = false;
    }

    private void OnAssetItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control { DataContext: AssetItem item }) return;

        if (AssetsListBox.SelectedItems?.Contains(item) ?? false)
        {
            e.Handled = true;
            _suppressSelectionChange = true;
        }

        _assetDragArgs = e;
        _dragStartPosition = e.GetPosition(AssetsListBox);
    }

    private void OnAssetItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_assetDragArgs is null) return;

        var pos = e.GetPosition(AssetsListBox);
        var delta = pos - _dragStartPosition;
        if (Math.Abs(delta.X) < 8 && Math.Abs(delta.Y) < 8) return;

        var args = _assetDragArgs;
        ResetDragState();

        TaskService.RunDispatcher(async () => await StartDragAsync(args));
    }

    private void OnAssetItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var suppress = _suppressSelectionChange;
        var dragArgs = _assetDragArgs;
        ResetDragState();

        if (!suppress || dragArgs is null) return;
        if (e.Source is not Control { DataContext: AssetItem item }) return;

        var isMultiSelect = (e.KeyModifiers & KeyModifiers.Control) != 0
                         || (e.KeyModifiers & KeyModifiers.Shift) != 0;

        if (isMultiSelect)
        {
            if (AssetsListBox.SelectedItems?.Contains(item) == true)
                AssetsListBox.SelectedItems.Remove(item);
            else
                AssetsListBox.SelectedItems?.Add(item);
        }
        else
        {
            AssetsListBox.SelectedItems?.Clear();
            AssetsListBox.SelectedItems?.Add(item);
        }
    }

    private void ResetDragState()
    {
        _suppressSelectionChange = false;
        _assetDragArgs = null;
    }

    private async Task StartDragAsync(PointerEventArgs e)
    {
        var paths = ViewModel.GetSelectedAssetPaths();
        var dragDropInfoFile = await WriteDragDropInfoAsync(paths);

        TaskService.Run(ExportClient.DiscoverAsync);

        var storageFile = await TopLevel.GetTopLevel(this)!
            .StorageProvider
            .TryGetFileFromPathAsync(new Uri(dragDropInfoFile));

        if (storageFile is null) return;

        var data = new DataObject();
        data.Set(DataFormats.Files, new[] { storageFile });

        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private static async Task<string> WriteDragDropInfoAsync(string[] paths)
    {
        var filePath = Path.Combine(App.DataFolder.FullName, "info.fp_drag_drop");
        await File.WriteAllTextAsync(filePath, JsonConvert.SerializeObject(new { Paths = paths.ToList() }));
        return filePath;
    }
}
