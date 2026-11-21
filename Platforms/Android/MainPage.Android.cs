using Android.Views;

namespace PortableMap;

public sealed partial class MainPage
{
    private Android.Views.View? _androidMapElement;
    private bool _androidRotationTracking;
    private double _androidPreviousAngle;

    private void AttachAndroidMapRotation()
    {
        _mapView.HandlerChanged += OnAndroidMapHandlerChanged;
        OnAndroidMapHandlerChanged(_mapView, EventArgs.Empty);
    }

    private void OnAndroidMapHandlerChanged(object? sender, EventArgs e)
    {
        if (_androidMapElement is not null)
            _androidMapElement.Touch -= OnAndroidMapTouch;

        _androidMapElement = _mapView.Handler?.PlatformView as Android.Views.View;
        if (_androidMapElement is not null)
            _androidMapElement.Touch += OnAndroidMapTouch;
    }

    private void OnAndroidMapTouch(object? sender, Android.Views.View.TouchEventArgs e)
    {
        var motion = e.Event;
        if (motion is null)
            return;

        switch (motion.ActionMasked)
        {
            case MotionEventActions.PointerDown when motion.PointerCount >= 2:
                _androidRotationTracking = true;
                _androidPreviousAngle = GetAndroidPointerAngle(motion);
                break;
            case MotionEventActions.Move when _androidRotationTracking && motion.PointerCount >= 2:
                var angle = GetAndroidPointerAngle(motion);
                var delta = (angle - _androidPreviousAngle + 540) % 360 - 180;
                if (Math.Abs(delta) > 0.01)
                {
                    _map.RotateBy(delta);
                    _mapView.Invalidate();
                }
                _androidPreviousAngle = angle;
                break;
            case MotionEventActions.PointerUp:
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                _androidRotationTracking = false;
                break;
        }
    }

    private static double GetAndroidPointerAngle(MotionEvent motion)
    {
        var deltaX = motion.GetX(1) - motion.GetX(0);
        var deltaY = motion.GetY(1) - motion.GetY(0);
        return Math.Atan2(deltaY, deltaX) * 180 / Math.PI;
    }
}
