using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Devices.Input;

namespace PortableMap;

public sealed partial class MainPage
{
    private const int WhGetMessage = 3;
    private const uint PmRemove = 1;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmPointerWheel = 0x024E;

    private FrameworkElement? _windowsMapElement;
    private MessageHookProcedure? _windowsMessageHookProcedure;
    private nint _windowsMessageHook;
    private nint _windowsMessageHookWindow;
    private Windows.Foundation.Point _windowsLastPointerPosition;
    private bool _windowsPointerPanning;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint MessageHookProcedure(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint WindowHandle;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ScreenToClient(nint windowHandle, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int hookType,
        MessageHookProcedure callback,
        nint moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hookHandle);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hookHandle, int code, nint wParam, nint lParam);

    private void AttachWindowsMapInput()
    {
        _mapView.HandlerChanged += OnWindowsMapHandlerChanged;
        Loaded += OnWindowsMapLoaded;
        Unloaded += OnWindowsMapUnloaded;
        OnWindowsMapHandlerChanged(_mapView, EventArgs.Empty);
    }

    private void OnWindowsMapLoaded(object? sender, EventArgs e)
    {
        OnWindowsMapHandlerChanged(_mapView, EventArgs.Empty);
        AttachWindowsMessageHook();
    }

    private void OnWindowsMapUnloaded(object? sender, EventArgs e) =>
        DetachWindowsMessageHook();

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
        System.Diagnostics.Debug.WriteLine(
            $"MapWheelHandler attached size=({_windowsMapElement.ActualWidth:F1},{_windowsMapElement.ActualHeight:F1})");
    }

    private void AttachWindowsMessageHook()
    {
        var nativeWindow = Window?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (nativeWindow is null)
            return;

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        if (_windowsMessageHook != 0 && _windowsMessageHookWindow == windowHandle)
            return;

        DetachWindowsMessageHook();
        var threadId = GetWindowThreadProcessId(windowHandle, out _);
        if (threadId == 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"GetWindowThreadProcessId failed with error {Marshal.GetLastWin32Error()}.");
            return;
        }

        var callback = new MessageHookProcedure(OnWindowsMessageReceived);
        var hook = SetWindowsHookEx(WhGetMessage, callback, 0, threadId);
        if (hook == 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"SetWindowsHookEx failed with error {Marshal.GetLastWin32Error()}.");
            return;
        }

        _windowsMessageHookProcedure = callback;
        _windowsMessageHook = hook;
        _windowsMessageHookWindow = windowHandle;
        System.Diagnostics.Debug.WriteLine($"ThreadWheelHook attached thread={threadId}.");
    }

    private void DetachWindowsMessageHook()
    {
        if (_windowsMessageHook != 0 && !UnhookWindowsHookEx(_windowsMessageHook))
        {
            System.Diagnostics.Debug.WriteLine(
                $"UnhookWindowsHookEx failed with error {Marshal.GetLastWin32Error()}.");
            return;
        }

        _windowsMessageHook = 0;
        _windowsMessageHookWindow = 0;
        _windowsMessageHookProcedure = null;
    }

    private nint OnWindowsMessageReceived(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && wParam == (nint)PmRemove && _windowsMapElement is { IsLoaded: true } element)
        {
            var message = Marshal.PtrToStructure<NativeMessage>(lParam);
            if (message.Message == WmMouseWheel || message.Message == WmPointerWheel)
            {
                var delta = unchecked((short)((message.WParam.ToUInt64() >> 16) & 0xFFFF));
                var hasMapPosition = TryGetMapPosition(element, message.Point, out var mapPosition);
                var isInsideMap = hasMapPosition && IsInsideMapViewport(element, mapPosition);
                System.Diagnostics.Debug.WriteLine(
                    $"ThreadWheelObserved message=0x{message.Message:X} delta={delta} " +
                    $"screen=({message.Point.X},{message.Point.Y}) " +
                    $"map=({(hasMapPosition ? mapPosition.X : 0):F1},{(hasMapPosition ? mapPosition.Y : 0):F1}) " +
                    $"inside={isInsideMap} size=({element.ActualWidth:F1},{element.ActualHeight:F1})");
                if (delta != 0 && isInsideMap)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"ThreadWheelInput message=0x{message.Message:X} delta={delta} " +
                        $"map=({mapPosition.X:F1},{mapPosition.Y:F1}) zoom={_map.Zoom:F3}");
                    var zoom = _map.Zoom * Math.Pow(1.2, delta / 120d);
                    _map.ZoomAtPoint(zoom, mapPosition.X, mapPosition.Y);
                    _mapView.Invalidate();

                    message.Message = 0;
                    Marshal.StructureToPtr(message, lParam, false);
                }
            }
        }

        return CallNextHookEx(_windowsMessageHook, code, wParam, lParam);
    }

    private void OnWindowsPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        var point = e.GetCurrentPoint(element);
        var delta = point.Properties.MouseWheelDelta;
        var hasMapPosition = _map.TryMapInputPosition(
            point.Position.X,
            point.Position.Y,
            out var mapX,
            out var mapY);
        var mapPosition = new Windows.Foundation.Point(mapX, mapY);
        System.Diagnostics.Debug.WriteLine(
            $"MapWheelInput delta={delta} event=({point.Position.X:F1},{point.Position.Y:F1}) " +
            $"map=({(hasMapPosition ? mapPosition.X : 0):F1},{(hasMapPosition ? mapPosition.Y : 0):F1}) " +
            $"size=({element.ActualWidth:F1},{element.ActualHeight:F1}) zoom={_map.Zoom:F3}");
        if (delta == 0 || !hasMapPosition || !IsInsideMapViewport(element, mapPosition))
            return;

        var zoom = _map.Zoom * Math.Pow(1.2, delta / 120d);
        _map.ZoomAtPoint(zoom, mapPosition.X, mapPosition.Y);
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
            !TryGetMapPosition(element, out _windowsLastPointerPosition))
            return;

        _windowsPointerPanning = true;
        element.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnWindowsPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_windowsPointerPanning || sender is not FrameworkElement element)
            return;

        if (!TryGetMapPosition(element, out var position))
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

    private bool TryGetMapPosition(FrameworkElement element, out Windows.Foundation.Point mapPosition)
    {
        mapPosition = default;
        if (!GetCursorPos(out var screenPosition))
        {
            System.Diagnostics.Debug.WriteLine($"GetCursorPos failed with error {Marshal.GetLastWin32Error()}.");
            return false;
        }

        return TryGetMapPosition(element, screenPosition, out mapPosition);
    }

    private bool TryGetMapPosition(
        FrameworkElement element,
        NativePoint screenPosition,
        out Windows.Foundation.Point mapPosition)
    {
        mapPosition = default;
        var nativeWindow = Window?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        var root = nativeWindow?.Content;
        if (nativeWindow is null || root is null)
            return false;

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        var clientPosition = screenPosition;
        if (!ScreenToClient(windowHandle, ref clientPosition))
        {
            System.Diagnostics.Debug.WriteLine($"ScreenToClient failed with error {Marshal.GetLastWin32Error()}.");
            return false;
        }

        var dpi = GetDpiForWindow(windowHandle);
        if (dpi == 0)
        {
            System.Diagnostics.Debug.WriteLine("GetDpiForWindow returned zero.");
            return false;
        }

        var dpiScale = dpi / 96d;
        var rootPosition = new Windows.Foundation.Point(
            clientPosition.X / dpiScale,
            clientPosition.Y / dpiScale);
        var elementToRoot = element.TransformToVisual(root);
        var pointerPosition = elementToRoot.Inverse.TransformPoint(rootPosition);
        if (!_map.TryMapInputPosition(
                pointerPosition.X,
                pointerPosition.Y,
                out var x,
                out var y))
            return false;

        mapPosition = new Windows.Foundation.Point(x, y);
        return true;
    }

    private static bool IsInsideMapViewport(
        FrameworkElement element,
        Windows.Foundation.Point mapPosition) =>
        mapPosition.X >= 0 && mapPosition.X < element.ActualWidth &&
        mapPosition.Y >= 0 && mapPosition.Y < element.ActualHeight;
}
