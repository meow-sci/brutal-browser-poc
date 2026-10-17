using Brutal.Numerics;
using System.Globalization;
using Brutal.GlfwApi;
using HelloMesh.Scene;

namespace HelloMesh.Input;

/// <summary>
/// Maps GLFW window events to camera and sound commands: left-drag rotates, wheel and Up/Down zoom,
/// Left/Right pan, R resets, Space plays the sound.
/// </summary>
public sealed class InputController
{
    private const float DegreesPerPixel = 0.4f;
    private const float ZoomStep = 0.9f;
    private const float WheelZoomBase = 0.9f;
    private const float PanStep = 0.05f;

    private readonly GlfwWindow _window;
    private readonly OrbitCamera _camera;
    private readonly Action _playSound;
    private bool _dragging;
    private double2 _lastCursor;
    private int _reportedVersion;

    public InputController(GlfwWindow window, OrbitCamera camera, Action playSound)
    {
        _window = window;
        _camera = camera;
        _playSound = playSound;
        window.OnMouseButton += OnMouseButton;
        window.OnCursorPos += OnCursorPos;
        window.OnScroll += OnScroll;
        window.OnKey += OnKey;
    }

    /// <summary>Prints one <c>CAMERA</c> line if the camera changed since the previous call; call once per frame.</summary>
    public void ReportCamera()
    {
        if (_camera.Version == _reportedVersion)
            return;
        _reportedVersion = _camera.Version;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"CAMERA yaw={_camera.Yaw:F2} pitch={_camera.Pitch:F2} dist={_camera.Distance:F4} pan={_camera.Pan.X:F4},{_camera.Pan.Y:F4}"));
    }

    private void OnMouseButton(GlfwWindow window, GlfwMouseButton button, GlfwButtonAction action, GlfwModifier mods)
    {
        if (button != GlfwMouseButton.Left)
            return;
        _dragging = action == GlfwButtonAction.Press;
        _lastCursor = window.CursorPos;
    }

    private void OnCursorPos(GlfwWindow window, double2 position)
    {
        if (_dragging)
            _camera.Rotate((float)(position.X - _lastCursor.X) * DegreesPerPixel, (float)(position.Y - _lastCursor.Y) * DegreesPerPixel);
        _lastCursor = position;
    }

    private void OnScroll(GlfwWindow window, double2 offset) =>
        _camera.Zoom(MathF.Pow(WheelZoomBase, (float)offset.Y));

    private void OnKey(GlfwWindow window, GlfwKey key, int scancode, GlfwKeyAction action, GlfwModifier mods)
    {
        if (action == GlfwKeyAction.Release)
            return;
        switch (key)
        {
            case GlfwKey.Up: _camera.Zoom(ZoomStep); break;
            case GlfwKey.Down: _camera.Zoom(1f / ZoomStep); break;
            case GlfwKey.Left: _camera.PanBy(-PanStep, 0f); break;
            case GlfwKey.Right: _camera.PanBy(PanStep, 0f); break;
            case GlfwKey.R when action == GlfwKeyAction.Press: _camera.Reset(); break;
            case GlfwKey.Space when action == GlfwKeyAction.Press: _playSound(); break;
        }
    }
}
