using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Compatibility.Jam;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.InputSystem;

/// <summary>
/// Android-only touch adapter. It feeds the same actions consumed by the
/// desktop player controller; it does not implement a second movement model.
/// </summary>
internal sealed partial class AndroidTouchControls : CanvasLayer
{
    private readonly RuntimeNativePlayer _player;
    private readonly DesktopInputConfiguration _input;
    private Control _root = null!;
    private Panel _joystick = null!;
    private Panel _knob = null!;
    private Control _lookZone = null!;
    private Vector2 _joystickCenter;
    private float _joystickRadius;
    private bool _joystickHeld;

    internal AndroidTouchControls(RuntimeNativePlayer player, DesktopInputConfiguration input)
    {
        _player = player;
        _input = input;
        Name = "AndroidTouchControls";
        Layer = 40;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        _root = new Control { Name = "TouchControls", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        BuildJoystick();
        BuildLookZone();
        BuildButtons();
        GD.Print("OPENNV_ANDROID_TOUCH_CONTROLS_READY layout=virtual-stick-look-drag-action-buttons");
    }

    public override void _Process(double delta)
    {
        // Keep the controls on screen during furniture and modal UI states.
        // Movement/look are gated separately so the joystick cannot move the
        // player while a source interaction owns control.
        _root.Visible = true;
        if (!_player.TouchInputAllowed && _joystickHeld)
            ReleaseJoystick();
    }

    private void BuildJoystick()
    {
        _joystick = new Panel
        {
            Name = "MovementJoystick",
            AnchorLeft = 0.025f,
            AnchorTop = 0.665f,
            AnchorRight = 0.255f,
            AnchorBottom = 0.985f,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _joystick.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.08f, 0.10f, 0.38f),
            BorderColor = new Color(0.85f, 0.91f, 0.94f, 0.48f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 100,
            CornerRadiusTopRight = 100,
            CornerRadiusBottomLeft = 100,
            CornerRadiusBottomRight = 100
        });
        _root.AddChild(_joystick);
        _knob = new Panel
        {
            Name = "MovementJoystickKnob",
            Size = new Vector2(62, 62),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _knob.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.65f, 0.82f, 0.90f, 0.72f),
            BorderColor = new Color(0.95f, 0.98f, 1f, 0.9f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 40,
            CornerRadiusTopRight = 40,
            CornerRadiusBottomLeft = 40,
            CornerRadiusBottomRight = 40
        });
        _joystick.AddChild(_knob);
        _joystick.GuiInput += HandleJoystickInput;
        _joystick.Resized += CenterJoystick;
        CenterJoystick();
    }

    private void CenterJoystick()
    {
        _joystickCenter = _joystick.Size * 0.5f;
        _joystickRadius = Math.Max(30, Math.Min(_joystick.Size.X, _joystick.Size.Y) * 0.34f);
        PositionKnob(Vector2.Zero);
    }

    private void HandleJoystickInput(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventScreenTouch touch when touch.Pressed:
                _joystickHeld = true;
                UpdateJoystick(touch.Position);
                _joystick.AcceptEvent();
                break;
            case InputEventScreenDrag drag:
                _joystickHeld = true;
                UpdateJoystick(drag.Position);
                _joystick.AcceptEvent();
                break;
            case InputEventScreenTouch touch when !touch.Pressed:
                ReleaseJoystick();
                _joystick.AcceptEvent();
                break;
            case InputEventMouseButton mouse when mouse.ButtonIndex == MouseButton.Left:
                _joystickHeld = mouse.Pressed;
                if (_joystickHeld) UpdateJoystick(mouse.Position);
                else ReleaseJoystick();
                _joystick.AcceptEvent();
                break;
            case InputEventMouseMotion motion when _joystickHeld:
                UpdateJoystick(motion.Position);
                _joystick.AcceptEvent();
                break;
        }
    }

    private void UpdateJoystick(Vector2 position)
    {
        if (!_player.TouchInputAllowed) return;
        var offset = position - _joystickCenter;
        if (offset.Length() > _joystickRadius)
            offset = offset.Normalized() * _joystickRadius;
        PositionKnob(offset);
        var value = offset / _joystickRadius;
        SetAction(_input.MoveLeft.Action, value.X < -0.16f);
        SetAction(_input.MoveRight.Action, value.X > 0.16f);
        SetAction(_input.MoveForward.Action, value.Y < -0.16f);
        SetAction(_input.MoveBackward.Action, value.Y > 0.16f);
    }

    private void PositionKnob(Vector2 offset) =>
        _knob.Position = _joystickCenter + offset - _knob.Size * 0.5f;

    private void ReleaseJoystick()
    {
        _joystickHeld = false;
        SetAction(_input.MoveLeft.Action, false);
        SetAction(_input.MoveRight.Action, false);
        SetAction(_input.MoveForward.Action, false);
        SetAction(_input.MoveBackward.Action, false);
        PositionKnob(Vector2.Zero);
    }

    private void BuildLookZone()
    {
        _lookZone = new Control
        {
            Name = "CameraLookTouchArea",
            AnchorLeft = 0.43f,
            AnchorTop = 0.10f,
            AnchorRight = 0.985f,
            AnchorBottom = 0.64f,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _root.AddChild(_lookZone);
        _lookZone.GuiInput += inputEvent =>
        {
            if (inputEvent is InputEventScreenDrag drag && _player.TouchInputAllowed)
            {
                _player.ApplyTouchLook(drag.Relative);
                _lookZone.AcceptEvent();
            }
            else if (inputEvent is InputEventMouseMotion motion && _player.TouchInputAllowed && Input.IsMouseButtonPressed(MouseButton.Left))
            {
                _player.ApplyTouchLook(motion.Relative);
                _lookZone.AcceptEvent();
            }
        };
    }

    private void BuildButtons()
    {
        AddActionButton("INTERACT", _input.Activate.Action, false, 0.69f, 0.69f, 0.83f, 0.80f);
        AddActionButton("JUMP", _input.Jump.Action, false, 0.84f, 0.69f, 0.98f, 0.80f);
        AddActionButton("FIRE", _input.Fire.Action, true, 0.69f, 0.82f, 0.83f, 0.94f);
        AddActionButton("AIM", GamepadInputMap.AimAction, true, 0.84f, 0.82f, 0.98f, 0.94f);
        AddActionButton("RELOAD", _input.Reload.Action, false, 0.53f, 0.82f, 0.67f, 0.94f);
        AddActionButton("PIP-BOY", _input.PipBoy.Action, false, 0.53f, 0.69f, 0.67f, 0.80f);
        AddActionButton("SPRINT", _input.Sprint.Action, true, 0.30f, 0.82f, 0.44f, 0.94f);
        AddActionButton("PAUSE", _input.Cancel.Action, false, 0.88f, 0.025f, 0.98f, 0.105f);
    }

    private void AddActionButton(
        string label, string action, bool hold,
        float left, float top, float right, float bottom)
    {
        var button = new Button
        {
            Text = label,
            AnchorLeft = left,
            AnchorTop = top,
            AnchorRight = right,
            AnchorBottom = bottom,
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Modulate = new Color(1, 1, 1, 0.88f)
        };
        button.AddThemeFontSizeOverride("font_size", 17);
        button.AddThemeStyleboxOverride("normal", ButtonStyle(new Color(0.055f, 0.09f, 0.12f, 0.78f)));
        button.AddThemeStyleboxOverride("hover", ButtonStyle(new Color(0.12f, 0.23f, 0.29f, 0.92f)));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle(new Color(0.20f, 0.42f, 0.48f, 0.98f)));
        _root.AddChild(button);
        button.ButtonDown += () =>
        {
            if (hold) SendActionEvent(action, true);
            else PulseAction(action);
        };
        if (hold)
            button.ButtonUp += () => SendActionEvent(action, false);
    }

    private static StyleBoxFlat ButtonStyle(Color color) => new()
    {
        BgColor = color,
        BorderColor = new Color(0.72f, 0.85f, 0.89f, 0.75f),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 12,
        CornerRadiusTopRight = 12,
        CornerRadiusBottomLeft = 12,
        CornerRadiusBottomRight = 12
    };

    private static void PulseAction(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true, Strength = 1f });
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
    }

    private static void SendActionEvent(string action, bool pressed) =>
        Input.ParseInputEvent(new InputEventAction
        {
            Action = action,
            Pressed = pressed,
            Strength = pressed ? 1f : 0f
        });

    private static void SetAction(string action, bool pressed)
    {
        if (pressed) Input.ActionPress(action);
        else Input.ActionRelease(action);
    }
}
