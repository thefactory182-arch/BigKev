namespace PadPilot.Controllers;

public sealed record DualSenseState(
    byte LeftX, byte LeftY, byte RightX, byte RightY,
    byte LeftTrigger, byte RightTrigger,
    IReadOnlySet<string> Buttons)
{
    public string Summary => Buttons.Count == 0
        ? $"Sticks L {LeftX},{LeftY}  R {RightX},{RightY}"
        : string.Join(" + ", Buttons);
}

