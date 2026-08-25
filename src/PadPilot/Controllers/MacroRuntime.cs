using System.Collections.Concurrent;
using PadPilot.Models;

namespace PadPilot.Controllers;

public sealed class MacroRuntime : IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _pressed = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> UpdateAndGet(DualSenseState input, Profile? profile)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (profile is not null)
        {
            foreach (var macro in profile.Macros.Where(m => !string.IsNullOrWhiteSpace(m.TriggerControl)))
            {
                var key = $"macro:{macro.Id:N}";
                var held = input.Buttons.Contains(macro.TriggerControl);
                if (held) wanted.Add(key);
                if (held && !_running.ContainsKey(key))
                {
                    var cts = new CancellationTokenSource();
                    if (_running.TryAdd(key, cts)) _ = RunAsync(key, macro, cts.Token);
                }
            }
            foreach (var map in profile.Mappings.Where(m => m.Kind == MappingKind.Macro))
            {
                var held = input.Buttons.Contains(map.Source);
                if (held) wanted.Add(map.Source);
                if (held && !_running.ContainsKey(map.Source))
                {
                    var macro = profile.Macros.FirstOrDefault(m => m.Name.Equals(map.Target, StringComparison.OrdinalIgnoreCase));
                    if (macro is not null)
                    {
                        var cts = new CancellationTokenSource();
                        if (_running.TryAdd(map.Source, cts)) _ = RunAsync(map.Source, macro, cts.Token);
                    }
                }
            }
        }
        foreach (var source in _running.Keys.Where(k => !wanted.Contains(k)).ToList()) Stop(source);
        return _pressed.Where(p => p.Value > 0).Select(p => p.Key).ToArray();
    }

    private async Task RunAsync(string source, MacroDefinition macro, CancellationToken token)
    {
        try
        {
            do
            {
                foreach (var step in macro.Steps)
                {
                    token.ThrowIfCancellationRequested();
                    if (step.Action == MacroAction.Delay) await Task.Delay(step.DelayMs, token);
                    else if (step.Action == MacroAction.Press) _pressed.AddOrUpdate(step.Control, 1, (_, n) => n + 1);
                    else _pressed.AddOrUpdate(step.Control, 0, (_, n) => Math.Max(0, n - 1));
                }
            } while (macro.RepeatWhileHeld && !token.IsCancellationRequested);
        }
        catch (OperationCanceledException) { }
        finally
        {
            foreach (var step in macro.Steps.Where(s => s.Action == MacroAction.Press)) _pressed[step.Control] = 0;
            _running.TryRemove(source, out var cts); cts?.Dispose();
        }
    }

    private void Stop(string source) { if (_running.TryGetValue(source, out var cts)) cts.Cancel(); }
    public void Dispose() { foreach (var source in _running.Keys.ToList()) Stop(source); _pressed.Clear(); }
}

