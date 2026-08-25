using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PadPilot.Models;
using PadPilot.Services;
using PadPilot.Controllers;
using System.Diagnostics;
using System.Windows.Navigation;
using System.Windows.Media;

namespace PadPilot;

public partial class MainWindow : Window
{
    private readonly ProfileStore _store = new();
    private readonly DualSenseUsbReader _controller = new();
    private readonly VirtualDualSenseOutput _output = new();
    private readonly ObservableCollection<Profile> _profiles = [];
    private Profile? Current => ProfilesList.SelectedItem as Profile;
    private MacroDefinition? CurrentMacro => MacrosList.SelectedItem as MacroDefinition;

    public MainWindow()
    {
        InitializeComponent();
        MappingKindColumn.ItemsSource = Enum.GetValues<MappingKind>();
        MacroControlInput.ItemsSource = new[] { "Cross", "Circle", "Square", "Triangle", "L1", "R1", "L2", "R2", "Create", "Options", "Left Stick Click (L3)", "Right Stick Click (R3)", "Left Stick Up", "Left Stick Down", "Left Stick Left", "Left Stick Right", "Right Stick Up", "Right Stick Down", "Right Stick Left", "Right Stick Right", "PS", "Touchpad", "Mute", "Dpad Up", "Dpad Down", "Dpad Left", "Dpad Right" };
        MacroTriggerInput.ItemsSource = MacroControlInput.ItemsSource;
        MacroControlInput.SelectedIndex = 0;
        MacroTriggerInput.SelectedIndex = 0;
        _controller.StatusChanged += message => Dispatcher.Invoke(() => ConnectionLabel.Text = message);
        _controller.StateChanged += state =>
        {
            // StateChanged fires on the background USB-reading thread. Every touch of the UI
            // (including reading Current, which reads ProfilesList.SelectedItem) must happen on
            // the dispatcher thread or WPF throws.
            Dispatcher.Invoke(() =>
            {
                _output.Update(state, Current);
                LiveInputLabel.Text = state.Summary;
                UpdateControllerDisplay(state);
            });
        };
        _output.StatusChanged += message => Dispatcher.Invoke(() => StatusLabel.Text = message);
        Closed += (_, _) => { _controller.Dispose(); _output.Dispose(); };
        ProfilesList.ItemsSource = _profiles;
        Loaded += async (_, _) =>
        {
            foreach (var profile in await _store.LoadAsync()) _profiles.Add(profile);
            if (_profiles.Count == 0) _profiles.Add(CreateStarterProfile());
            ProfilesList.SelectedIndex = 0;
            var physicalBeforeOutput = DualSenseUsbReader.CurrentDevicePaths();
            _output.Start();
            await Task.Delay(600);
            var pathsAfterOutput = DualSenseUsbReader.CurrentDevicePaths();
            _controller.ExcludeDevicePaths(pathsAfterOutput.Except(physicalBeforeOutput));
            _controller.Start();
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
        ProfileName.Text = profile.Name;
        MappingsGrid.ItemsSource = profile.Mappings;
        MacrosList.ItemsSource = profile.Macros;
        LeftDeadZone.Value = profile.LeftStickDeadZone;
        RightDeadZone.Value = profile.RightStickDeadZone;
        L1DownAssistEnabled.IsChecked = profile.L1RightStickDownAssistEnabled;
        L1DownAssistAmount.Value = profile.L1RightStickDownAssistAmount;
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
            L1RightStickDownAssistEnabled = Current.L1RightStickDownAssistEnabled, L1RightStickDownAssistAmount = Current.L1RightStickDownAssistAmount,
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
    private void ApplyStickTuning()
    {
        if (Current is null) return;
        Current.LeftStickDeadZone = LeftDeadZone.Value; Current.RightStickDeadZone = RightDeadZone.Value;
        Current.L1RightStickDownAssistEnabled = L1DownAssistEnabled.IsChecked == true; Current.L1RightStickDownAssistAmount = L1DownAssistAmount.Value;
    }
    // Sliders and the checkbox apply immediately (like the mappings grid already does) so you can
    // hold L1 and drag the amount slider to feel out the right value before saving to disk.
    private void StickTuningSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyStickTuning();
    private void StickTuningCheckBox_Changed(object sender, RoutedEventArgs e) => ApplyStickTuning();

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
    private async void SaveMacro_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentMacro is null || Current is null) return;
        CurrentMacro.Name = MacroName.Text; CurrentMacro.TriggerControl = MacroTriggerInput.SelectedItem?.ToString() ?? "Cross"; CurrentMacro.RepeatWhileHeld = MacroRepeat.IsChecked == true;
        var error = MacroValidator.Validate(CurrentMacro); MacroMessage.Text = error ?? "Macro looks good.";
        if (error is not null) return;
        await _store.SaveAsync(Current); MacrosList.Items.Refresh(); StatusLabel.Text = $"Saved macro {CurrentMacro.Name}";
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
        StatusLabel.Text = _output.IsReady ? "Virtual PS5 DualSense active" : "Driver setup did not complete. Check the status message above, then try again.";
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
}

