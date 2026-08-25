using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PadPilot.Models;
using PadPilot.Services;
using PadPilot.Controllers;
using System.Diagnostics;
using System.Windows.Navigation;
using System.Windows.Media;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using System.Net.Http;

namespace PadPilot;

public partial class MainWindow : Window
{
    private readonly ProfileStore _store = new();
    private readonly DualSenseUsbReader _controller = new();
    private readonly VirtualDualSenseOutput _output = new();
    private readonly ObservableCollection<Profile> _profiles = [];
    private bool _bindingProfile;
    private Profile? _outputProfile;
    private DualSenseState? _latestControllerState;
    private int _controllerTestActive;
    private readonly DispatcherTimer _controllerDisplayTimer;
    private Profile? Current => ProfilesList.SelectedItem as Profile;
    private MacroDefinition? CurrentMacro => MacrosList.SelectedItem as MacroDefinition;

    public MainWindow()
    {
        InitializeComponent();
        _controllerDisplayTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _controllerDisplayTimer.Tick += (_, _) =>
        {
            var state = Volatile.Read(ref _latestControllerState);
            if (state is null) return;
            LiveInputLabel.Text = state.Summary;
            UpdateControllerDisplay(state);
        };
        MappingKindColumn.ItemsSource = Enum.GetValues<MappingKind>();
        MacroControlInput.ItemsSource = new[] { "Cross", "Circle", "Square", "Triangle", "L1", "R1", "L2", "R2", "Create", "Options", "Left Stick Click (L3)", "Right Stick Click (R3)", "Left Stick Up", "Left Stick Down", "Left Stick Left", "Left Stick Right", "Right Stick Up", "Right Stick Down", "Right Stick Left", "Right Stick Right", "PS", "Touchpad", "Mute", "Dpad Up", "Dpad Down", "Dpad Left", "Dpad Right" };
        MacroTriggerInput.ItemsSource = MacroControlInput.ItemsSource;
        MacroControlInput.SelectedIndex = 0;
        MacroTriggerInput.SelectedIndex = 0;
        OutputModeInput.ItemsSource = Enum.GetValues<VirtualOutputMode>();
        VersionLabel.Text = $"Installed version {UpdateService.CurrentVersionText}";
        _controller.StatusChanged += message => Dispatcher.Invoke(() => ConnectionLabel.Text = message);
        _controller.StateChanged += state =>
        {
            // Controller output stays on the USB reader thread so rendering or resizing the
            // window can never add input latency. The UI reads only the latest state at 30 Hz.
            _output.Update(state, Volatile.Read(ref _outputProfile));
            if (Volatile.Read(ref _controllerTestActive) == 1)
                Volatile.Write(ref _latestControllerState, state);
        };
        _output.StatusChanged += message => Dispatcher.Invoke(() =>
        {
            StatusLabel.Text = message;
            _controller.SetManagedIndicator(_output.IsReady);
        });
        _output.VirtualDualSensePathsChanged += paths => _controller.ExcludeDevicePaths(paths);
        Closed += (_, _) => { _controllerDisplayTimer.Stop(); _controller.Dispose(); _output.Dispose(); };
        ProfilesList.ItemsSource = _profiles;
        Loaded += async (_, _) =>
        {
            foreach (var profile in await _store.LoadAsync()) _profiles.Add(profile);
            if (_profiles.Count == 0) _profiles.Add(CreateStarterProfile());
            ProfilesList.SelectedIndex = 0;
            var physicalBeforeOutput = DualSenseUsbReader.CurrentDevicePaths();
            _output.Start(Current?.OutputMode ?? VirtualOutputMode.XboxXInput);
            await Task.Delay(600);
            var pathsAfterOutput = DualSenseUsbReader.CurrentDevicePaths();
            _controller.ExcludeDevicePaths(pathsAfterOutput.Except(physicalBeforeOutput));
            _controller.Start();
            DriverInstallButton.Content = _output.IsReady ? "Driver installed" : "Install DualSense driver";
            DriverInstallButton.IsEnabled = !_output.IsReady;
        };
    }

    private static Profile CreateStarterProfile() => new()
    {
        Name = "Default",
        Mappings =
        [
            new() { Source = "Cross", Target = "Cross" }, new() { Source = "Circle", Target = "Circle" },
            new() { Source = "Square", Target = "Square" }, new() { Source = "Triangle", Target = "Triangle" }
        ]
    };

    private void BindProfile(Profile? profile)
    {
        if (profile is null) return;
        _bindingProfile = true;
        try
        {
            ProfileName.Text = profile.Name;
            MappingsGrid.ItemsSource = profile.Mappings;
            MacrosList.ItemsSource = profile.Macros;
            LeftDeadZone.Value = profile.LeftStickDeadZone;
            RightDeadZone.Value = profile.RightStickDeadZone;
            LeftDeadZoneEnabled.IsChecked = profile.LeftStickDeadZoneEnabled;
            RightDeadZoneEnabled.IsChecked = profile.RightStickDeadZoneEnabled;
            OutputModeInput.SelectedItem = profile.OutputMode;
            L2DownAssistEnabled.IsChecked = profile.L2RightStickDownAssistEnabled;
            L2DownAssistAmount.Value = profile.L2RightStickDownAssistAmount;
        }
        finally { _bindingProfile = false; }
        RefreshOutputProfile();
    }

    private void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e) => BindProfile(Current);
    private void ProfileName_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Profile isn't a bindable/observable type, so the sidebar ListBox won't pick up the
        // renamed profile on its own — nudge it to refresh so the list stays in sync as you type.
        if (Current is null) return;
        Current.Name = ProfileName.Text;
        ProfilesList.Items.Refresh();
    }
    private void NewProfile_Click(object sender, RoutedEventArgs e) { var p = CreateStarterProfile(); p.Id = Guid.NewGuid(); p.Name = "New profile"; _profiles.Add(p); ProfilesList.SelectedItem = p; }
    private void DuplicateProfile_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null) return;
        var copy = new Profile { Name = Current.Name + " copy", LeftStickDeadZone = Current.LeftStickDeadZone, RightStickDeadZone = Current.RightStickDeadZone,
            LeftStickDeadZoneEnabled = Current.LeftStickDeadZoneEnabled, RightStickDeadZoneEnabled = Current.RightStickDeadZoneEnabled, OutputMode = Current.OutputMode,
            L2RightStickDownAssistEnabled = Current.L2RightStickDownAssistEnabled, L2RightStickDownAssistAmount = Current.L2RightStickDownAssistAmount,
            Mappings = Current.Mappings.Select(m => new ButtonMapping { Source = m.Source, Target = m.Target, Kind = m.Kind }).ToList(),
            Macros = Current.Macros.Select(CloneMacro).ToList() };
        _profiles.Add(copy); ProfilesList.SelectedItem = copy;
    }
    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null || _profiles.Count == 1) return;
        var doomed = Current;
        var confirmed = MessageBox.Show($"Delete the profile \"{doomed.Name}\"? This cannot be undone.", "Delete profile",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        if (!confirmed) return;
        _profiles.Remove(doomed); _store.Delete(doomed); ProfilesList.SelectedIndex = 0;
    }

    private async void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null) return;
        ApplyStickTuning();
        var dialog = new SaveFileDialog { Filter = "BigKev profile (*.bigkev.json)|*.bigkev.json|JSON (*.json)|*.json", FileName = SafeFileName(Current.Name) + ".bigkev.json" };
        if (dialog.ShowDialog(this) != true) return;
        await _store.ExportProfileAsync(Current, dialog.FileName);
        StatusLabel.Text = $"Exported {Current.Name}";
    }

    private async void ExportAll_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "BigKev profile library (*.bigkev.json)|*.bigkev.json|JSON (*.json)|*.json", FileName = "BigKev-profile-library.bigkev.json" };
        if (dialog.ShowDialog(this) != true) return;
        await _store.ExportLibraryAsync(_profiles, dialog.FileName);
        StatusLabel.Text = $"Exported {_profiles.Count} profiles";
    }

    private async void ImportProfiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "BigKev settings (*.bigkev.json;*.json)|*.bigkev.json;*.json", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = await _store.ImportAsync(dialog.FileName);
            foreach (var profile in imported) _profiles.Add(profile);
            ProfilesList.SelectedItem = imported[0];
            StatusLabel.Text = $"Imported {imported.Count} profile{(imported.Count == 1 ? "" : "s")}";
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Could not import settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c));
    }
    private void ApplyStickTuning()
    {
        if (_bindingProfile || Current is null) return;
        Current.LeftStickDeadZone = LeftDeadZone.Value; Current.RightStickDeadZone = RightDeadZone.Value;
        Current.LeftStickDeadZoneEnabled = LeftDeadZoneEnabled.IsChecked == true;
        Current.RightStickDeadZoneEnabled = RightDeadZoneEnabled.IsChecked == true;
        Current.OutputMode = OutputModeInput.SelectedItem is VirtualOutputMode mode ? mode : VirtualOutputMode.XboxXInput;
        Current.L2RightStickDownAssistEnabled = L2DownAssistEnabled.IsChecked == true; Current.L2RightStickDownAssistAmount = L2DownAssistAmount.Value;
        RefreshOutputProfile();
    }

    private void RefreshOutputProfile()
    {
        if (Current is not { } profile) { Volatile.Write(ref _outputProfile, null); return; }
        var snapshot = new Profile
        {
            Id = profile.Id,
            Name = profile.Name,
            LeftStickDeadZone = profile.LeftStickDeadZone,
            RightStickDeadZone = profile.RightStickDeadZone,
            LeftStickDeadZoneEnabled = profile.LeftStickDeadZoneEnabled,
            RightStickDeadZoneEnabled = profile.RightStickDeadZoneEnabled,
            OutputMode = profile.OutputMode,
            L2RightStickDownAssistEnabled = profile.L2RightStickDownAssistEnabled,
            L2RightStickDownAssistAmount = profile.L2RightStickDownAssistAmount,
            Mappings = profile.Mappings.Select(m => new ButtonMapping { Source = m.Source, Target = m.Target, Kind = m.Kind }).ToList(),
            Macros = profile.Macros.Select(CloneMacro).ToList()
        };
        Volatile.Write(ref _outputProfile, snapshot);
    }
    // Sliders and the checkbox apply immediately (like the mappings grid already does) so you can
    // hold L2 and drag the amount slider to feel out the right value before saving to disk.
    private void StickTuningSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyStickTuning();
    private void StickTuningCheckBox_Changed(object sender, RoutedEventArgs e) => ApplyStickTuning();
    private void StickDeadZoneCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        LeftDeadZone.IsEnabled = LeftDeadZoneEnabled.IsChecked == true;
        RightDeadZone.IsEnabled = RightDeadZoneEnabled.IsChecked == true;
        ApplyStickTuning();
    }
    private void OutputModeInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_bindingProfile) return;
        ApplyStickTuning();
        StatusLabel.Text = "Output mode will switch on the next controller input.";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null) return;
        ApplyStickTuning();
        await _store.SaveAsync(Current); ProfilesList.Items.Refresh(); StatusLabel.Text = $"Saved {Current.Name}";
    }

    private void NewMacro_Click(object sender, RoutedEventArgs e) { if (Current is null) return; var m = new MacroDefinition(); Current.Macros.Add(m); MacrosList.Items.Refresh(); MacrosList.SelectedItem = m; }
    private void MacrosList_SelectionChanged(object sender, SelectionChangedEventArgs e) { var m = CurrentMacro; if (m is null) return; MacroName.Text = m.Name; MacroTriggerInput.SelectedItem = m.TriggerControl; MacroRepeat.IsChecked = m.RepeatWhileHeld; StepsList.ItemsSource = m.Steps; }
    private void AddPress_Click(object s, RoutedEventArgs e) => AddStep(MacroAction.Press, MacroControlInput.SelectedItem?.ToString() ?? "A", 0);
    private void AddRelease_Click(object s, RoutedEventArgs e) => AddStep(MacroAction.Release, MacroControlInput.SelectedItem?.ToString() ?? "A", 0);
    private void AddWait_Click(object s, RoutedEventArgs e)
    {
        if (!int.TryParse(MacroDelayInput.Text, out var delay) || delay is < 0 or > 30_000)
        {
            MacroMessage.Text = "Enter a wait between 0 and 30,000 milliseconds.";
            return;
        }
        AddStep(MacroAction.Delay, "", delay);
    }
    private void AddStep(MacroAction action, string control, int delay) { if (CurrentMacro is null) { MacroMessage.Text = "Create or select a macro first."; return; } CurrentMacro.Steps.Add(new() { Action = action, Control = control, DelayMs = delay }); StepsList.Items.Refresh(); MacroMessage.Text = ""; }
    private void RemoveStep_Click(object sender, RoutedEventArgs e) { if (CurrentMacro is null || StepsList.SelectedItem is not MacroStep step) return; CurrentMacro.Steps.Remove(step); StepsList.Items.Refresh(); }
    private async void ExportMacro_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentMacro is null) return;
        var dialog = new SaveFileDialog { Filter = "BigKev macro (*.bigkev-macro.json)|*.bigkev-macro.json|JSON (*.json)|*.json", FileName = SafeFileName(CurrentMacro.Name) + ".bigkev-macro.json" };
        if (dialog.ShowDialog(this) != true) return;
        await _store.ExportMacroAsync(CurrentMacro, dialog.FileName);
        StatusLabel.Text = $"Exported macro {CurrentMacro.Name}";
    }

    private async void ImportMacro_Click(object sender, RoutedEventArgs e)
    {
        if (Current is null) return;
        var dialog = new OpenFileDialog { Filter = "BigKev macro (*.bigkev-macro.json;*.json)|*.bigkev-macro.json;*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var macro = await _store.ImportMacroAsync(dialog.FileName);
            Current.Macros.Add(macro);
            await _store.SaveAsync(Current);
            MacrosList.Items.Refresh();
            MacrosList.SelectedItem = macro;
            StatusLabel.Text = $"Imported macro {macro.Name}";
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Could not import macro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private async void SaveMacro_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentMacro is null || Current is null) return;
        CurrentMacro.Name = MacroName.Text; CurrentMacro.TriggerControl = MacroTriggerInput.SelectedItem?.ToString() ?? "Cross"; CurrentMacro.RepeatWhileHeld = MacroRepeat.IsChecked == true;
        var error = MacroValidator.Validate(CurrentMacro); MacroMessage.Text = error ?? "Macro looks good.";
        if (error is not null) return;
        await _store.SaveAsync(Current); MacrosList.Items.Refresh(); StatusLabel.Text = $"Saved macro {CurrentMacro.Name}";
        RefreshOutputProfile();
    }
    private static MacroDefinition CloneMacro(MacroDefinition m) => new() { Name = m.Name, TriggerControl = m.TriggerControl, RepeatWhileHeld = m.RepeatWhileHeld, Steps = m.Steps.Select(s => new MacroStep { Action = s.Action, Control = s.Control, DelayMs = s.DelayMs }).ToList() };

    private void DriverLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
        StatusLabel.Text = "Opened the official HIDMaestro driver page in your browser.";
    }

    private async void InstallDriver_Click(object sender, RoutedEventArgs e)
    {
        DriverInstallButton.IsEnabled = false;
        DriverInstallButton.Content = "Installing…";
        StatusLabel.Text = "Installing the DualSense output driver. This can take a few seconds.";
        _controller.Dispose();
        var physicalBeforeOutput = DualSenseUsbReader.CurrentDevicePaths();
        await Task.Run(() => _output.InstallDriver());
        await Task.Delay(600);
        _controller.ExcludeDevicePaths(DualSenseUsbReader.CurrentDevicePaths().Except(physicalBeforeOutput));
        _controller.Start();
        DriverInstallButton.Content = _output.IsReady ? "Driver installed" : "Install DualSense driver";
        DriverInstallButton.IsEnabled = !_output.IsReady;
        StatusLabel.Text = _output.IsReady ? "Virtual Xbox controller active (XInput)" : "Driver setup did not complete. Check the status message above, then try again.";
    }

    private void UpdateControllerDisplay(DualSenseState state)
    {
        var on = new SolidColorBrush(Color.FromArgb(185, 139, 108, 255));
        var off = Brushes.Transparent;
        void Light(Border control, string button) => control.Background = state.Buttons.Contains(button) ? on : off;
        Light(VizCross, "Cross"); Light(VizCircle, "Circle"); Light(VizSquare, "Square"); Light(VizTriangle, "Triangle");
        Light(VizL1, "L1"); Light(VizR1, "R1"); Light(VizDpadUp, "Dpad Up"); Light(VizDpadDown, "Dpad Down");
        Light(VizDpadLeft, "Dpad Left"); Light(VizDpadRight, "Dpad Right"); Light(VizCreate, "Create");
        Light(VizOptions, "Options"); Light(VizPS, "PS");
        VizL3.Fill = state.Buttons.Contains("L3") ? on : off; VizR3.Fill = state.Buttons.Contains("R3") ? on : off;
        VizStickValues.Text = $"L {state.LeftX},{state.LeftY}    R {state.RightX},{state.RightY}";
    }

    private void ToggleControllerTest_Click(object sender, RoutedEventArgs e)
    {
        if (Interlocked.CompareExchange(ref _controllerTestActive, 1, 0) == 0)
        {
            ControllerTestButton.Content = "Stop controller test";
            LiveInputLabel.Text = "Controller test running…";
            _controllerDisplayTimer.Start();
            return;
        }

        Interlocked.Exchange(ref _controllerTestActive, 0);
        _controllerDisplayTimer.Stop();
        Volatile.Write(ref _latestControllerState, null);
        ControllerTestButton.Content = "Start controller test";
        LiveInputLabel.Text = "Controller visualization paused";
        UpdateControllerDisplay(new DualSenseState(128, 128, 128, 128, 0, 0,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        VizStickValues.Text = "Controller test stopped";
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckForUpdatesButton.IsEnabled = false;
        UpdateMessage.Text = "Checking GitHub for updates…";
        try
        {
            var update = await UpdateService.CheckAsync();
            if (update is null)
            {
                UpdateMessage.Text = $"BigKev {UpdateService.CurrentVersionText} is up to date.";
                return;
            }
            UpdateMessage.Text = $"BigKev {update.Version} is available.";
            var install = MessageBox.Show($"BigKev {update.Version} is available. Download, install, and restart now?",
                "BigKev update available", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes;
            if (!install) return;
            UpdateMessage.Text = "Downloading update… BigKev will restart when ready.";
            await UpdateService.DownloadAndRestartAsync(update);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or JsonException or TaskCanceledException)
        {
            UpdateMessage.Text = "Update check failed.";
            MessageBox.Show(ex.Message, "Could not update BigKev", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { CheckForUpdatesButton.IsEnabled = true; }
    }
}

