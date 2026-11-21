using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Windows.Devices.Input;

namespace PortableMap;

public sealed partial class MainPage
{
    private const int WhGetMessage = 3;
    private const uint PmRemove = 1;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmPointerWheel = 0x024E;

    private FrameworkElement? _windowsMapElement;
    private GraphicsView? _compassView;
    private CompassDrawable? _compassDrawable;
    private MessageHookProcedure? _windowsMessageHookProcedure;
    private nint _windowsMessageHook;
    private nint _windowsMessageHookWindow;
    private Windows.Foundation.Point _windowsLastPointerPosition;
    private bool _windowsPointerPanning;
    private bool _windowsCompassDragging;
    private double _windowsCompassLastAngle;

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
        _compassDrawable = new CompassDrawable();
        _compassView = new GraphicsView
        {
            Drawable = _compassDrawable,
            WidthRequest = 68,
            HeightRequest = 68,
            Margin = new Microsoft.Maui.Thickness(12),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            BackgroundColor = Colors.Transparent
        };
        SemanticProperties.SetDescription(_compassView, "Drag the compass to rotate the map");
        _compassView.StartInteraction += OnWindowsCompassStartInteraction;
        _compassView.DragInteraction += OnWindowsCompassDragInteraction;
        _compassView.EndInteraction += OnWindowsCompassInteractionEnded;
        _compassView.CancelInteraction += OnWindowsCompassInteractionEnded;
        _mapSurface.Children.Add(_compassView);

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

    private void OnWindowsMapUnloaded(object? sender, EventArgs e)
    {
        _windowsPointerPanning = false;
        _windowsCompassDragging = false;
        DetachWindowsMessageHook();
    }

    private void UpdateWindowsCompass()
    {
        if (_compassDrawable is null || _compassView is null)
            return;

        _compassDrawable.RotationDegrees = _map.RotationDegrees;
        _compassView.Invalidate();
    }

    private void OnWindowsCompassStartInteraction(object? sender, TouchEventArgs e)
    {
        var compassView = _compassView;
        var touches = e.Touches.ToArray();
        if (compassView is null || touches.Length == 0)
            return;

        var center = new PointF((float)(compassView.Width / 2), (float)(compassView.Height / 2));
        var position = touches[0];
        var radius = GetCompassRadius(position, center);
        if (radius < 4 || radius > Math.Min(compassView.Width, compassView.Height) / 2 - 3)
            return;

        _windowsPointerPanning = false;
        _windowsCompassLastAngle = GetCompassAngle(position, center);
        _windowsCompassDragging = true;
    }

    private void OnWindowsCompassDragInteraction(object? sender, TouchEventArgs e)
    {
        var compassView = _compassView;
        var touches = e.Touches.ToArray();
        if (!_windowsCompassDragging || compassView is null || touches.Length == 0)
            return;

        var center = new PointF((float)(compassView.Width / 2), (float)(compassView.Height / 2));
        var position = touches[0];
        if (GetCompassRadius(position, center) >= 4)
        {
            var angle = GetCompassAngle(position, center);
            var delta = (angle - _windowsCompassLastAngle + 540) % 360 - 180;
            _windowsCompassLastAngle = angle;
            _map.RotateBy(delta);
            _mapView.Invalidate();
            UpdateWindowsCompass();
        }
    }

    private void OnWindowsCompassInteractionEnded(object? sender, EventArgs e) =>
        _windowsCompassDragging = false;

    private static double GetCompassAngle(
        PointF position,
        PointF center) =>
        Math.Atan2(position.Y - center.Y, position.X - center.X) * 180 / Math.PI;

    private static double GetCompassRadius(
        PointF position,
        PointF center)
    {
        var x = position.X - center.X;
        var y = position.Y - center.Y;
        return Math.Sqrt(x * x + y * y);
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
        if (code >= 0 && wParam == (nint)PmRemove &&
            _windowsMapElement is { IsLoaded: true } element)
        {
            var message = Marshal.PtrToStructure<NativeMessage>(lParam);
            if (message.Message == WmMouseWheel || message.Message == WmPointerWheel)
            {
                var delta = unchecked((short)((message.WParam.ToUInt64() >> 16) & 0xFFFF));
                if (delta != 0 && TryGetMapPosition(element, message.Point, out var mapPosition))
                {
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
            !e.Pointer.PointerDeviceType.Equals(PointerDeviceType.Mouse))
            return;

        var point = e.GetCurrentPoint(element);
        if (!point.Properties.IsLeftButtonPressed ||
            point.Position.X < 0 || point.Position.X >= element.ActualWidth ||
            point.Position.Y < 0 || point.Position.Y >= element.ActualHeight)
            return;

        if (!TryGetMapPosition(element, point.Position, out _windowsLastPointerPosition))
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

        _map.PanByScreenDelta(
            position.X - _windowsLastPointerPosition.X,
            position.Y - _windowsLastPointerPosition.Y);
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

    private void OnWindowsPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _windowsPointerPanning = false;
    }

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
            System.Diagnostics.Debug.WriteLine(
                $"ScreenToClient failed with error {Marshal.GetLastWin32Error()}.");
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
        if (pointerPosition.X < 0 || pointerPosition.X >= element.ActualWidth ||
            pointerPosition.Y < 0 || pointerPosition.Y >= element.ActualHeight)
            return false;

        return TryGetMapPosition(element, pointerPosition, out mapPosition);
    }
}

internal sealed class CompassDrawable : IDrawable
{
    public double RotationDegrees { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var centerX = dirtyRect.X + dirtyRect.Width / 2f;
        var centerY = dirtyRect.Y + dirtyRect.Height / 2f;
        var radius = Math.Min(dirtyRect.Width, dirtyRect.Height) / 2f - 3;
        if (radius <= 0)
            return;

        canvas.FillColor = Color.FromRgba(255, 255, 255, 230);
        canvas.FillEllipse(centerX - radius, centerY - radius, radius * 2, radius * 2);
        canvas.StrokeColor = Color.FromArgb("#475569");
        canvas.StrokeSize = 1.5f;
        canvas.DrawEllipse(centerX - radius, centerY - radius, radius * 2, radius * 2);

        var angle = RotationDegrees * Math.PI / 180;
        var markerX = centerX + (float)(Math.Sin(angle) * radius * 0.58);
        var markerY = centerY - (float)(Math.Cos(angle) * radius * 0.58);
        canvas.StrokeColor = Color.FromArgb("#1D4ED8");
        canvas.StrokeSize = 2;
        canvas.DrawLine(centerX, centerY, markerX, markerY);
        canvas.FontColor = Color.FromArgb("#1D4ED8");
        canvas.FontSize = 12;
        canvas.DrawString(
            "N",
            markerX - 8,
            markerY - 8,
            16,
            16,
            Microsoft.Maui.Graphics.HorizontalAlignment.Center,
            Microsoft.Maui.Graphics.VerticalAlignment.Center);
    }
}
