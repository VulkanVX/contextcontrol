// CC-DESC: Item, command, scroll, and hover wiring for the local LLM catalog surface.

// CC-DESC: Draws the LLM catalogue as a fixed-row virtualized surface for fast scrolling.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public sealed partial class LocalLlmCatalogRenderControl
{
    private void AttachItems()
    {
        if (_itemsCollectionChanged is not null)
        {
            _itemsCollectionChanged.CollectionChanged -= OnItemsCollectionChanged;
            _itemsCollectionChanged = null;
        }

        foreach (var model in _subscribedModels)
        {
            model.PropertyChanged -= OnModelPropertyChanged;
        }

        _subscribedModels.Clear();

        if (Items is INotifyCollectionChanged collectionChanged)
        {
            _itemsCollectionChanged = collectionChanged;
            collectionChanged.CollectionChanged += OnItemsCollectionChanged;
        }

        SubscribeModels();
        RebuildExtent();
    }

    private void SubscribeModels()
    {
        var items = Items;
        if (items is null)
        {
            return;
        }

        foreach (var model in items)
        {
            if (model is INotifyPropertyChanged notify)
            {
                notify.PropertyChanged += OnModelPropertyChanged;
                _subscribedModels.Add(notify);
            }
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var model in _subscribedModels)
        {
            model.PropertyChanged -= OnModelPropertyChanged;
        }

        _subscribedModels.Clear();
        SubscribeModels();
        RebuildExtent();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private void RebuildExtent()
    {
        _hoveredIndex = -1;
        _hoveredKind = LocalLlmCatalogHitKind.None;
        _textCache.Clear();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void AttachToScrollViewer(ScrollViewer? scrollViewer)
    {
        if (ReferenceEquals(_scrollViewer, scrollViewer))
        {
            return;
        }

        _offsetSubscription?.Dispose();
        _viewportSubscription?.Dispose();
        _offsetSubscription = null;
        _viewportSubscription = null;
        _scrollViewer = scrollViewer;

        if (scrollViewer is null)
        {
            return;
        }

        _offsetSubscription = scrollViewer
            .GetObservable(ScrollViewer.OffsetProperty)
            .Subscribe(new ValueObserver<Vector>(_ => InvalidateVisual()));
        _viewportSubscription = scrollViewer
            .GetObservable(ScrollViewer.ViewportProperty)
            .Subscribe(new ValueObserver<Size>(_ => InvalidateVisual()));
    }

    private void AttachCommand(ICommand? command)
    {
        if (ReferenceEquals(_subscribedCommand, command))
        {
            return;
        }

        if (_subscribedCommand is not null)
        {
            _subscribedCommand.CanExecuteChanged -= OnCommandCanExecuteChanged;
        }

        _subscribedCommand = command;
        if (_subscribedCommand is not null)
        {
            _subscribedCommand.CanExecuteChanged += OnCommandCanExecuteChanged;
        }

        InvalidateVisual();
    }

    private void OnCommandCanExecuteChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    private void SetHoveredHit(int index, LocalLlmCatalogHitKind kind)
    {
        if (_hoveredIndex == index && _hoveredKind == kind)
        {
            return;
        }

        _hoveredIndex = index;
        _hoveredKind = kind;
        Cursor = kind is LocalLlmCatalogHitKind.Pull or LocalLlmCatalogHitKind.Icon ? HandCursor : ArrowCursor;
        InvalidateVisual();
    }

}
