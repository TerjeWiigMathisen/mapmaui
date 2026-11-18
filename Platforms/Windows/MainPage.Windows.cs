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
        OnWindowsMapHandlerChanged(_mapView, EventArgs.Empty);
    }

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
        if (delta == 0 || !TryGetMapPosition(element, point.Position, out var mapPosition))
            return;

        var zoom = _map.Zoom * Math.Pow(1.2, delta / 120d);
        _map.ZoomAtPoint(zoom, mapPosition.X, mapPosition.Y);
        _mapView.Invalidate();
        e.Handled = true;
    }

    private void OnWindowsPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            !e.Pointer.PointerDeviceType.Equals(PointerDeviceType.Mouse) ||
            !e.GetCurrentPoint(element).Properties.IsLeftButtonPressed)
            return;

        if (!TryGetMapPosition(element, e.GetCurrentPoint(element).Position, out _windowsLastPointerPosition))
            return;

        _windowsPointerPanning = true;
        element.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnWindowsPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_windowsPointerPanning || sender is not FrameworkElement element)
            return;

        if (!TryGetMapPosition(element, e.GetCurrentPoint(element).Position, out var position))
            return;

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

    private bool TryGetMapPosition(
        FrameworkElement element,
        Windows.Foundation.Point pointerPosition,
        out Windows.Foundation.Point mapPosition)
    {
        if (!_map.TryMapInputPosition(
                pointerPosition.X,
                pointerPosition.Y,
                element.ActualWidth,
                element.ActualHeight,
                out var x,
                out var y))
        {
            mapPosition = default;
            return false;
        }

        mapPosition = new Windows.Foundation.Point(x, y);
        return true;
    }
}
