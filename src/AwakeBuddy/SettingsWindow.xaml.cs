using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AwakeBuddy.Settings;
using FormsScreen = System.Windows.Forms.Screen;

namespace AwakeBuddy;

public partial class SettingsWindow : Window
{
    private readonly Action<AppSettings> _onSave;
    private readonly Action<double>? _onOverlayOpacityPreview;
    private int _schemaVersion = AppSettings.CurrentSchemaVersion;
    private bool _isLoading;

    private const string AllDisplaysToken = "*";
    private readonly DisplayOption[] _displayOptions;

    private sealed class DisplayOption : INotifyPropertyChanged
    {
        public required string DeviceName { get; init; }
        public required string Label { get; init; }
        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public SettingsWindow(AppSettings settings, string settingsPath, Action<AppSettings> onSave, Action<double>? onOverlayOpacityPreview = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _onSave = onSave ?? throw new ArgumentNullException(nameof(onSave));
        _onOverlayOpacityPreview = onOverlayOpacityPreview;
        _displayOptions = CreateDisplayOptions();

        InitializeComponent();
        SleepProtectionScopeComboBox.ItemsSource = Enum.GetValues<SleepProtectionScope>();
        TargetDisplaysItemsControl.ItemsSource = _displayOptions;
        LoadSettings(settings, settingsPath);
    }

    private static DisplayOption[] CreateDisplayOptions()
    {
        var screens = FormsScreen.AllScreens;

        DisplayOption[] options = new DisplayOption[screens.Length];

        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            string primaryTag = screen.Primary ? " (Primary)" : string.Empty;
            string label = $"{screen.DeviceName}{primaryTag} {screen.Bounds.Width}x{screen.Bounds.Height}";
            options[i] = new DisplayOption
            {
                DeviceName = screen.DeviceName,
                Label = label
            };
        }

        return options;
    }

    public void LoadSettings(AppSettings settings, string settingsPath)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _isLoading = true;

        _schemaVersion = settings.SchemaVersion > 0
            ? settings.SchemaVersion
            : AppSettings.CurrentSchemaVersion;

        SettingsPathTextBlock.Text = settingsPath;
        IdleThresholdTextBox.Text = settings.IdleThresholdSeconds.ToString(CultureInfo.InvariantCulture);
        OverlayEnabledCheckBox.IsChecked = settings.OverlayEnabled;
        ApplyDisplaySelection(settings.OverlayMonitorDeviceName);
        OverlayOpacitySlider.Value = Math.Round(settings.OverlayOpacity * 100d, 0);
        OverlayOpacityValueText.Text = $"{OverlayOpacitySlider.Value:0}%";
        AntiSleepEnabledCheckBox.IsChecked = settings.AntiSleepEnabled;
        AntiSleepIntervalTextBox.Text = settings.AntiSleepIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        SleepProtectionScopeComboBox.SelectedItem = settings.SleepProtectionScope;
        IgnoreInjectedInputForIdleCheckBox.IsChecked = settings.IgnoreInjectedInputForIdle;

        _isLoading = false;
    }

    private void ApplyDisplaySelection(string selectionSpec)
    {
        string spec = selectionSpec?.Trim() ?? string.Empty;
        bool allDisplays = string.Equals(spec, AllDisplaysToken, StringComparison.Ordinal);

        AllDisplaysCheckBox.IsChecked = allDisplays;

        foreach (DisplayOption option in _displayOptions)
        {
            option.IsSelected = false;
        }

        if (allDisplays)
        {
            foreach (DisplayOption option in _displayOptions)
            {
                option.IsSelected = true;
            }

            TargetDisplaysItemsControl.IsEnabled = false;
            return;
        }

        TargetDisplaysItemsControl.IsEnabled = true;

        if (string.IsNullOrWhiteSpace(spec))
        {
            return;
        }

        string[] tokens = spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string token in tokens)
        {
            foreach (DisplayOption option in _displayOptions)
            {
                if (string.Equals(option.DeviceName, token, StringComparison.OrdinalIgnoreCase))
                {
                    option.IsSelected = true;
                    break;
                }
            }
        }
    }

    private void OnAllDisplaysCheckBoxChanged(object sender, RoutedEventArgs e)
    {
        bool allDisplays = AllDisplaysCheckBox.IsChecked == true;
        TargetDisplaysItemsControl.IsEnabled = !allDisplays;

        if (!allDisplays)
        {
            return;
        }

        foreach (DisplayOption option in _displayOptions)
        {
            option.IsSelected = true;
        }
    }

    private void OnOverlayOpacitySliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OverlayOpacityValueText is not null)
        {
            OverlayOpacityValueText.Text = $"{e.NewValue:0}%";
        }

        if (!_isLoading)
        {
            _onOverlayOpacityPreview?.Invoke(Math.Round(e.NewValue / 100d, 2));
        }
    }

    private void OnSaveButtonClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IdleThresholdTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idleThresholdSeconds) ||
            idleThresholdSeconds < 0)
        {
            ShowValidationError("Idle threshold must be 0 or a positive number of seconds.");
            return;
        }

        if (!int.TryParse(AntiSleepIntervalTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int antiSleepIntervalSeconds) ||
            antiSleepIntervalSeconds <= 0)
        {
            ShowValidationError("Anti-sleep interval must be a positive number of seconds.");
            return;
        }

        if (SleepProtectionScopeComboBox.SelectedItem is not SleepProtectionScope sleepProtectionScope)
        {
            ShowValidationError("Select a valid sleep protection scope.");
            return;
        }

        AppSettings nextSettings = new()
        {
            SchemaVersion = _schemaVersion,
            IdleThresholdSeconds = idleThresholdSeconds,
            OverlayEnabled = OverlayEnabledCheckBox.IsChecked == true,
            OverlayMonitorDeviceName = BuildOverlayMonitorSelectionSpec(),
            OverlayOpacity = Math.Round(OverlayOpacitySlider.Value / 100d, 2),
            AntiSleepEnabled = AntiSleepEnabledCheckBox.IsChecked == true,
            AntiSleepIntervalSeconds = antiSleepIntervalSeconds,
            SleepProtectionScope = sleepProtectionScope,
            IgnoreInjectedInputForIdle = IgnoreInjectedInputForIdleCheckBox.IsChecked == true
        };

        _onSave(nextSettings);
        Close();
    }

    private void OnCancelButtonClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ShowValidationError(string message)
    {
        MessageBox.Show(this, message, "AwakeBuddy Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private string BuildOverlayMonitorSelectionSpec()
    {
        if (AllDisplaysCheckBox.IsChecked == true)
        {
            return AllDisplaysToken;
        }

        string[] selected = GetSelectedDisplayDeviceNames();
        if (selected.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(';', selected);
    }

    private string[] GetSelectedDisplayDeviceNames()
    {
        int count = 0;

        foreach (DisplayOption option in _displayOptions)
        {
            if (option.IsSelected)
            {
                count++;
            }
        }

        if (count == 0)
        {
            return Array.Empty<string>();
        }

        string[] deviceNames = new string[count];
        int index = 0;

        foreach (DisplayOption option in _displayOptions)
        {
            if (!option.IsSelected)
            {
                continue;
            }

            deviceNames[index] = option.DeviceName;
            index++;
        }

        return deviceNames;
    }
}
