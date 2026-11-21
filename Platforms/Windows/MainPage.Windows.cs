using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Devices.Input;

namespace PortableMap;

public sealed partial class MainPage
{
    private FrameworkElement? _windowsMapElement;
    private Windows.Foundation.Point _windowsLastPointerPosition;
    private bool _windowsPointerPanning;

    private void AttachWindowsMapInput()
    {
        _mapView.HandlerChanged += OnWindowsMapHandlerChanged;
        Loaded += OnWindowsMapLoaded;
        Unloaded += OnWindowsMapUnloaded;
        OnWindowsMapHandlerChanged(_mapView, EventArgs.Empty);
    }

    private void OnWindowsMapLoaded(object? sender, EventArgs e) =>
        OnWindowsMapHandlerChanged(_mapView, EventArgs.Empty);

    private void OnWindowsMapUnloaded(object? sender, EventArgs e) =>
        _windowsPointerPanning = false;

    private void OnWindowsMapHandlerChanged(object? sender, EventArgs e)
    {
        if (_windowsMapElement is not null)
        {
            _windowsMapElement.PointerWheelChanged -= OnWindowsPointerWheelChanged;
            _windowsMapElement.PointerPressed -= OnWindowsPointerPressed;
            _windowsMapElement.PointerMoved -= OnWindowsPointerMoved;
            _windowsMapElement.PointerReleased -= OnWindowsPointerReleased;
            _windowsMapElement.PointerCaptureLost -= OnWindowsPointerCaptureLost;
        }

        _windowsMapElement = _mapView.Handler?.PlatformView as FrameworkElement;
        if (_windowsMapElement is null)
            return;

        _windowsMapElement.PointerWheelChanged += OnWindowsPointerWheelChanged;
        _windowsMapElement.PointerPressed += OnWindowsPointerPressed;
        _windowsMapElement.PointerMoved += OnWindowsPointerMoved;
        _windowsMapElement.PointerReleased += OnWindowsPointerReleased;
        _windowsMapElement.PointerCaptureLost += OnWindowsPointerCaptureLost;
    }

    private void OnWindowsPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        var point = e.GetCurrentPoint(element);
        var delta = point.Properties.MouseWheelDelta;
        if (delta == 0 || !_map.TryMapInputPosition(
                point.Position.X,
                point.Position.Y,
                out var mapX,
                out var mapY))
            return;

        var zoom = _map.Zoom * Math.Pow(1.2, delta / 120d);
        _map.ZoomAtPoint(zoom, mapX, mapY);
        _mapView.Invalidate();
        e.Handled = true;
    }

    private void OnWindowsPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            !e.Pointer.PointerDeviceType.Equals(PointerDeviceType.Mouse))
            return;

        var point = e.GetCurrentPoint(element);
        if (!point.Properties.IsLeftButtonPressed ||
            point.Position.X < 0 || point.Position.X >= element.ActualWidth ||
            point.Position.Y < 0 || point.Position.Y >= element.ActualHeight)
            return;

        _windowsLastPointerPosition = point.Position;
        _windowsPointerPanning = true;
        element.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnWindowsPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_windowsPointerPanning || sender is not FrameworkElement element)
            return;

        var position = e.GetCurrentPoint(element).Position;
        _map.PanX += position.X - _windowsLastPointerPosition.X;
        _map.PanY += position.Y - _windowsLastPointerPosition.Y;
        _windowsLastPointerPosition = position;
        _mapView.Invalidate();
        e.Handled = true;
    }

    private void OnWindowsPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_windowsPointerPanning || !e.Pointer.PointerDeviceType.Equals(PointerDeviceType.Mouse))
            return;

        _windowsPointerPanning = false;
        if (sender is FrameworkElement element)
            element.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnWindowsPointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        _windowsPointerPanning = false;
}
