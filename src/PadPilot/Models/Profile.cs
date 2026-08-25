using System.Text.Json.Serialization;

namespace PadPilot.Models;

public sealed class Profile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New profile";
    public List<ButtonMapping> Mappings { get; set; } = [];
    public List<MacroDefinition> Macros { get; set; } = [];
    public double LeftStickDeadZone { get; set; } = 0.08;
    public double RightStickDeadZone { get; set; } = 0.08;
}

public sealed class ButtonMapping
{
    public string Source { get; set; } = "Cross";
    public string Target { get; set; } = "A";
    public MappingKind Kind { get; set; } = MappingKind.Gamepad;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MappingKind { Gamepad, Keyboard, Macro, Disabled }

public sealed class MacroDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New macro";
    public string TriggerControl { get; set; } = "Cross";
    public bool RepeatWhileHeld { get; set; }
    public List<MacroStep> Steps { get; set; } = [];
}

public sealed class MacroStep
{
    public MacroAction Action { get; set; } = MacroAction.Press;
    public string Control { get; set; } = "A";
    public int DelayMs { get; set; } = 50;

    public override string ToString() => Action == MacroAction.Delay
        ? $"Wait {DelayMs} ms"
        : $"{Action} {Control}";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MacroAction { Press, Release, Delay }

