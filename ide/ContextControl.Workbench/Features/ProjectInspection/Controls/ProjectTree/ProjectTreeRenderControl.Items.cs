// CC-DESC: Item subscription, row measurement, scroll, and hover wiring for the project tree surface.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public sealed partial class ProjectTreeRenderControl
{
    private void AttachItems()
    {
        if (_itemsCollectionChanged is not null)
        {
            _itemsCollectionChanged.CollectionChanged -= OnItemsCollectionChanged;
            _itemsCollectionChanged = null;
        }

        foreach (var row in _subscribedRows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        _subscribedRows.Clear();

        if (Items is INotifyCollectionChanged collectionChanged)
        {
            _itemsCollectionChanged = collectionChanged;
            collectionChanged.CollectionChanged += OnItemsCollectionChanged;
        }

        SubscribeRows();
        RebuildRowTops();
    }

    private void SubscribeRows()
    {
        var items = Items;
        if (items is null)
        {
            return;
        }

        foreach (var row in items)
        {
            if (row is INotifyPropertyChanged notify)
            {
                notify.PropertyChanged += OnRowPropertyChanged;
                _subscribedRows.Add(notify);
            }
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var row in _subscribedRows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        _subscribedRows.Clear();
        SubscribeRows();
        RebuildRowTops();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TreeRowViewModel.RowHeight))
        {
            RebuildRowTops();
            return;
        }

        InvalidateVisual();
    }

    private void RebuildRowTops()
    {
        var items = Items;
        var count = items?.Count ?? 0;
        var rowTops = new double[count + 1];
        var y = ContentTop;
        rowTops[0] = y;

        if (items is not null)
        {
            for (var index = 0; index < count; index++)
            {
                y += Math.Max(1.0, items[index].RowHeight);
                rowTops[index + 1] = y;
            }
        }

        _rowTops = rowTops;
        _totalHeight = y + ContentBottom;
        _hoveredIndex = -1;
        _hoveredKind = ProjectTreeHitKind.None;
        _textCache.Clear();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private int FindRowIndexAtOrAfter(double y)
    {
        var items = Items;
        var count = items?.Count ?? 0;
        if (count == 0 || _rowTops.Length <= count)
        {
            return -1;
        }

        var lo = 0;
        var hi = count - 1;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (_rowTops[mid + 1] <= y)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
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

    private void SetHoveredHit(int index, ProjectTreeHitKind kind)
    {
        if (_hoveredIndex == index && _hoveredKind == kind)
        {
            return;
        }

        _hoveredIndex = index;
        _hoveredKind = kind;
        Cursor = kind is ProjectTreeHitKind.Toggle or ProjectTreeHitKind.Include ? HandCursor : ArrowCursor;
        InvalidateVisual();
    }

}
