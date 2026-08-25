using HidSharp;
using System.IO;

namespace PadPilot.Controllers;

/// <summary>Reads the 64-byte USB input report used by the wired PS5 DualSense.</summary>
public sealed class DualSenseUsbReader : IDisposable
{
    public const int SonyVendorId = 0x054C;
    public const int DualSenseProductId = 0x0CE6;

    private CancellationTokenSource? _stop;
    private Task? _readerTask;
    private HashSet<string> _excludedPaths = new(StringComparer.OrdinalIgnoreCase);
    private HidStream? _stream;
    private readonly object _outputSync = new();
    private bool _managedIndicatorEnabled;

    public event Action<string>? StatusChanged;
    public event Action<DualSenseState>? StateChanged;

    public static IReadOnlySet<string> CurrentDevicePaths() => DeviceList.Local
        .GetHidDevices(SonyVendorId, DualSenseProductId).Select(d => d.DevicePath)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public void ExcludeDevicePaths(IEnumerable<string> paths) =>
        _excludedPaths = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);

    public void Start()
    {
        if (_readerTask is not null) return;
        _stop = new CancellationTokenSource();
        _readerTask = Task.Run(() => FindAndReadAsync(_stop.Token));
    }

    private async Task FindAndReadAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var device = DeviceList.Local.GetHidDevices(SonyVendorId, DualSenseProductId)
                .FirstOrDefault(d => d.GetMaxInputReportLength() >= 64 && !_excludedPaths.Contains(d.DevicePath));
            if (device is null)
            {
                StatusChanged?.Invoke("Connect a DualSense with a USB-C cable");
                await Task.Delay(1000, token).ConfigureAwait(false);
                continue;
            }

            try
            {
                if (!device.TryOpen(out var stream))
                {
                    StatusChanged?.Invoke("DualSense found, but Windows would not open it");
                    await Task.Delay(1000, token).ConfigureAwait(false);
                    continue;
                }

                using (stream)
                {
                    lock (_outputSync) _stream = stream;
                    ApplyManagedIndicator();
                    stream.ReadTimeout = 1000;
                    StatusChanged?.Invoke("DualSense connected by USB");
                    var report = new byte[Math.Max(64, device.GetMaxInputReportLength())];
                    while (!token.IsCancellationRequested)
                    {
                        int read;
                        try { read = await stream.ReadAsync(report, token).ConfigureAwait(false); }
                        catch (TimeoutException) { continue; }
                        if (read < 11 || report[0] != 0x01) continue;
                        StateChanged?.Invoke(Parse(report));
                    }
                    lock (_outputSync) _stream = null;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lock (_outputSync) _stream = null;
                StatusChanged?.Invoke("DualSense disconnected");
                await Task.Delay(500, token).ConfigureAwait(false);
            }
        }
    }

    public static DualSenseState Parse(ReadOnlySpan<byte> report)
    {
        if (report.Length < 11 || report[0] != 0x01) throw new ArgumentException("Not a wired DualSense input report.");
        var pressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var face = report[8];
        var dpad = face & 0x0F;
        if (dpad is 0 or 1 or 7) pressed.Add("Dpad Up");
        if (dpad is 1 or 2 or 3) pressed.Add("Dpad Right");
        if (dpad is 3 or 4 or 5) pressed.Add("Dpad Down");
        if (dpad is 5 or 6 or 7) pressed.Add("Dpad Left");
        AddBit(pressed, face, 4, "Square"); AddBit(pressed, face, 5, "Cross");
        AddBit(pressed, face, 6, "Circle"); AddBit(pressed, face, 7, "Triangle");
        var middle = report[9];
        AddBit(pressed, middle, 0, "L1"); AddBit(pressed, middle, 1, "R1");
        AddBit(pressed, middle, 2, "L2"); AddBit(pressed, middle, 3, "R2");
        AddBit(pressed, middle, 4, "Create"); AddBit(pressed, middle, 5, "Options");
        AddBit(pressed, middle, 6, "L3"); AddBit(pressed, middle, 7, "R3");
        var system = report[10];
        AddBit(pressed, system, 0, "PS"); AddBit(pressed, system, 1, "Touchpad"); AddBit(pressed, system, 2, "Mute");
        return new(report[1], report[2], report[3], report[4], report[5], report[6], pressed);
    }

    private static void AddBit(HashSet<string> target, byte value, int bit, string name)
    {
        if ((value & (1 << bit)) != 0) target.Add(name);
    }

    public void SetManagedIndicator(bool enabled)
    {
        _managedIndicatorEnabled = enabled;
        ApplyManagedIndicator();
    }

    private void ApplyManagedIndicator()
    {
        lock (_outputSync)
        {
            if (_stream is null) return;
            try
            {
                var report = new byte[63];
                report[0] = 0x02; // Wired DualSense output report.
                if (_managedIndicatorEnabled)
                {
                    report[2] = 0x04; // Lightbar control enabled.
                    report[45] = 139; // BigKev purple: RGB 139, 108, 255.
                    report[46] = 108;
                    report[47] = 255;
                }
                else
                {
                    report[2] = 0x08; // Release LEDs back to the controller/game.
                }
                _stream.Write(report);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public void Dispose()
    {
        SetManagedIndicator(false);
        _stop?.Cancel();
        _stop?.Dispose();
        _stop = null;
        _readerTask = null;
    }
}

