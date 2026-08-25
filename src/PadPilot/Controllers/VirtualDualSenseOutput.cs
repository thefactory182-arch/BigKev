using HIDMaestro;
using PadPilot.Models;

namespace PadPilot.Controllers;

public sealed class VirtualDualSenseOutput : IDisposable
{
    private HMContext? _context;
    private HMController? _controller;
    private HMProfile? _profile;
    private readonly object _sync = new();
    private readonly MacroRuntime _macros = new();
    public bool IsReady => _controller is not null;
    public event Action<string>? StatusChanged;

    public void Start()
    {
        try
        {
            _context = new HMContext();
            _context.LoadDefaultProfiles();
            if (!_context.IsDriverInstalled) { StatusChanged?.Invoke("DualSense output driver required — use the download link"); return; }
            _profile = _context.GetProfile("dualsense") ?? throw new InvalidOperationException("DualSense profile unavailable.");
            _controller = _context.CreateController(_profile);
            StatusChanged?.Invoke("Virtual PS5 DualSense active");
        }
        catch (UnauthorizedAccessException) { Dispose(); StatusChanged?.Invoke("Run BigKev as administrator for virtual DualSense output"); }
        catch (Exception ex) { Dispose(); StatusChanged?.Invoke($"DualSense output unavailable: {ex.Message}"); }
    }

    public void InstallDriver()
    {
        try
        {
            Dispose();
            using (var installer = new HMContext())
            {
                installer.LoadDefaultProfiles();
                installer.InstallDriver();
            }
            StatusChanged?.Invoke("DualSense driver installed successfully");
            Start();
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Driver installation failed: {ex.Message}");
        }
    }

    public void Update(DualSenseState input, Profile? profile)
    {
        lock (_sync)
        {
            if (_controller is null || _profile is null) return;
            var leftDz = profile?.LeftStickDeadZone ?? 0.08; var rightDz = profile?.RightStickDeadZone ?? 0.08;
            var macroActions = _macros.UpdateAndGet(input, profile);
            var leftX = Axis(input.LeftX, leftDz); var leftY = Axis(input.LeftY, leftDz);
            var rightX = Axis(input.RightX, rightDz); var rightY = Axis(input.RightY, rightDz);
            leftX = MacroAxis(leftX, macroActions, "Left Stick Left", "Left Stick Right");
            leftY = MacroAxis(leftY, macroActions, "Left Stick Up", "Left Stick Down");
            rightX = MacroAxis(rightX, macroActions, "Right Stick Left", "Right Stick Right");
            rightY = MacroAxis(rightY, macroActions, "Right Stick Up", "Right Stick Down");
            var state = new HMGamepadState
            {
                Axes = HMGamepadStateHelpers.StandardAxes(_profile, leftX, leftY, rightX, rightY, input.LeftTrigger / 255f, input.RightTrigger / 255f),
                Buttons = MapButtons(input, profile, macroActions), Hat = MapHat(input.Buttons), BatteryLevel = 100, BatteryCharging = true
            };
            _controller.SubmitState(in state);
        }
    }

    private static float Axis(byte value, double deadZone)
    {
        var n = (value - 127.5) / 127.5; var magnitude = Math.Abs(n);
        n = magnitude <= deadZone ? 0 : Math.Sign(n) * (magnitude - deadZone) / (1 - deadZone);
        return (float)Math.Clamp((n + 1) / 2, 0, 1);
    }

    private static float MacroAxis(float physical, IReadOnlyCollection<string> actions, string negative, string positive)
    {
        var hasNegative = actions.Contains(negative, StringComparer.OrdinalIgnoreCase);
        var hasPositive = actions.Contains(positive, StringComparer.OrdinalIgnoreCase);
        if (hasNegative && hasPositive) return 0.5f;
        if (hasNegative) return 0f;
        if (hasPositive) return 1f;
        return physical;
    }

    private static HMButton MapButtons(DualSenseState input, Profile? profile, IReadOnlyCollection<string> macroButtons)
    {
        var result = HMButton.None;
        foreach (var source in input.Buttons)
        {
            if (source.StartsWith("Dpad", StringComparison.OrdinalIgnoreCase)) continue;
            var map = profile?.Mappings.FirstOrDefault(m => m.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
            if (map?.Kind == MappingKind.Disabled || (map is not null && map.Kind != MappingKind.Gamepad)) continue;
            result |= Target(map?.Target ?? source);
        }
        foreach (var macroButton in macroButtons) result |= Target(macroButton);
        return result;
    }

    private static HMButton Target(string name) => name.Trim().ToLowerInvariant() switch
    {
        "cross" or "a" => HMButton.Cross, "circle" or "b" => HMButton.Circle, "square" or "x" => HMButton.Square, "triangle" or "y" => HMButton.Triangle,
        "l1" or "lb" => HMButton.LeftBumper, "r1" or "rb" => HMButton.RightBumper, "create" or "back" => HMButton.Share, "options" or "start" => HMButton.Start,
        "l3" or "left stick" or "left stick click (l3)" => HMButton.LeftStick, "r3" or "right stick" or "right stick click (r3)" => HMButton.RightStick, "ps" or "guide" => HMButton.Guide,
        "touchpad" => HMButton.Touchpad, "mute" => HMButton.Misc1, _ => HMButton.None
    };

    private static HMHat MapHat(IReadOnlySet<string> b)
    {
        var u=b.Contains("Dpad Up"); var d=b.Contains("Dpad Down"); var l=b.Contains("Dpad Left"); var r=b.Contains("Dpad Right");
        if(u&&r)return HMHat.NorthEast;if(d&&r)return HMHat.SouthEast;if(d&&l)return HMHat.SouthWest;if(u&&l)return HMHat.NorthWest;
        if(u)return HMHat.North;if(r)return HMHat.East;if(d)return HMHat.South;if(l)return HMHat.West;return HMHat.None;
    }

    public void Dispose() { lock (_sync) { _macros.Dispose(); _controller?.Dispose(); _controller=null; _profile=null; _context?.Dispose(); _context=null; } }
}

