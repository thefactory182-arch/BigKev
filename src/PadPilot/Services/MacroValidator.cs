using PadPilot.Models;

namespace PadPilot.Services;

public static class MacroValidator
{
    public static string? Validate(MacroDefinition macro)
    {
        if (string.IsNullOrWhiteSpace(macro.Name)) return "Give the macro a name.";
        if (macro.Steps.Count == 0) return "Add at least one step.";
        if (macro.Steps.Count > 128) return "Macros are limited to 128 steps.";
        if (macro.Steps.Any(s => s.DelayMs is < 0 or > 30_000)) return "Each delay must be between 0 and 30 seconds.";

        var held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in macro.Steps)
        {
            if (step.Action == MacroAction.Press) held.Add(step.Control);
            if (step.Action == MacroAction.Release && !held.Remove(step.Control))
                return $"{step.Control} is released before it is pressed.";
        }
        return held.Count == 0 ? null : $"Release these controls before the macro ends: {string.Join(", ", held)}.";
    }
}

