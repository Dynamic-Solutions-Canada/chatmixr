using ChatMixr.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WinUIAppKit.Localization;

namespace ChatMixr.Classes;

/// <summary>
/// The device list, the same in the tray popup and on the Devices page: one <see cref="CardRow"/> per
/// visible playback device with its icon, name, what is playing through it, a Switch to button (with
/// a menu for single roles), a Test button on the device in use, and one slider from Game to Chat. A
/// wheel device's slider shows its wheel and snaps back if moved. The list is a ListView whose rows can be dragged into a new order; every row has Hide in
/// its right-click menu, and on the Devices page also Move up, Move down and Hide buttons. Hidden
/// devices are left out (the Devices page lists them separately).
/// </summary>
internal static class DeviceList
{
    /// <summary>Set up a ListView to hold the list: no selection, drag to reorder.</summary>
    /// <param name="list">The ListView from the page or popup XAML.</param>
    public static void Attach(ListView list)
    {
        list.SelectionMode = ListViewSelectionMode.None;
        list.CanDragItems = true;
        list.CanReorderItems = true;
        list.AllowDrop = true;
        list.DragItemsCompleted += (_, _) =>
        {
            var order = list.Items.OfType<DeviceRow>().Select(r => r.DeviceId).ToList();
            DeviceActions.Reorder(order);
        };
    }

    /// <summary>The devices the list shows: every device the user hasn't hidden.</summary>
    public static List<OutputDevice> Visible(MixSnapshot snapshot) =>
        snapshot.Devices.Where(d => !DeviceActions.IsHidden(d.Id)).ToList();

    /// <summary>
    /// Show the snapshot's visible devices. When the list already shows the same devices in the same
    /// order, their rows are updated in place, so sliders move from where they were and a drag in
    /// progress isn't interrupted. Otherwise the rows are rebuilt.
    /// </summary>
    /// <param name="withPageButtons">True on the Devices page: each row gets Move up, Move down and Hide.</param>
    public static void Fill(ListView list, MixSnapshot snapshot, bool withPageButtons)
    {
        var devices = Visible(snapshot);
        var rows = list.Items.OfType<DeviceRow>().ToList();
        bool same = rows.Count == list.Items.Count
            && rows.Select(r => r.DeviceId).SequenceEqual(devices.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
        if (!same)
        {
            list.Items.Clear();
            rows = devices.Select(d => new DeviceRow(d.Id, withPageButtons)).ToList();
            foreach (var row in rows)
            {
                list.Items.Add(row);
            }
        }

        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].Update(devices[i], isFirst: i == 0, isLast: i == rows.Count - 1, snapshot);
        }
    }

    /// <summary>One device's card.</summary>
    private sealed partial class DeviceRow : CardRow
    {
        private readonly ContentPresenter _useHost = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),   // hit-testable for its tooltip
        };
        private readonly Slider _slider = new()
        {
            Minimum = SliderMix.Min,
            Maximum = SliderMix.Max,
            StepFrequency = 1,
            SmallChange = SliderMix.KeyStep,
            LargeChange = SliderMix.Max / 2,
            TickFrequency = SliderMix.Max,
            TickPlacement = TickPlacement.Outside,
            ThumbToolTipValueConverter = new LevelsBubble(),   // the levels while dragging, as in the Game Bar widget
            VerticalAlignment = VerticalAlignment.Center,
        };
        private readonly TextBlock _gameLabel = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _chatLabel = new() { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        private readonly MenuFlyoutItem _hideItem = new() { Icon = new FontIcon { Glyph = MixVisuals.HideGlyph } };
        private readonly Button _test;

        /// <summary>Segoe Fluent Icons: Play and Stop, for the Test button.</summary>
        private const string PlayGlyph = "\uE768", StopGlyph = "\uE71A";
        private readonly Button? _up;
        private readonly Button? _down;
        private readonly Button? _hide;

        /// <summary>True while <see cref="Update"/> sets the slider, so that isn't taken as the user dragging it.</summary>
        private bool _updating;
        private bool _hasWheel;

        /// <summary>The roles, power and language the switch button was built for.</summary>
        private (bool IsDefault, bool IsCalls, bool HasMicrophone, bool IsMicrophone, bool Off, string Locale)? _shownSwitch;

        /// <summary>The device this row shows.</summary>
        public string DeviceId { get; }

        public DeviceRow(string deviceId, bool withPageButtons)
        {
            DeviceId = deviceId;
            ShowsIndicator = true;
            var resources = Application.Current.Resources;
            _gameLabel.Style = (Style)resources["CaptionTextBlockStyle"];
            _chatLabel.Style = (Style)resources["CaptionTextBlockStyle"];
            _slider.ValueChanged += OnSliderChanged;

            // The slider handles pointer events itself, so these listen to handled events too.
            _slider.AddHandler(PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _dragging = true), handledEventsToo: true);
            _slider.AddHandler(PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _dragging = false), handledEventsToo: true);
            _slider.AddHandler(PointerCaptureLostEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _dragging = false), handledEventsToo: true);
            _slider.AddHandler(PointerCanceledEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => _dragging = false), handledEventsToo: true);

            // Test, then Switch to (or, on the device in use, its menu alone) at the right edge: the
            // device's main action ends the row.
            _test = IconTextButton(PlayGlyph, "", () => DeviceActions.ToggleTest(DeviceId));
            Actions.Children.Add(_test);
            Actions.Children.Add(_useHost);
            if (withPageButtons)
            {
                _up = IconButton("", "", "", () => DeviceActions.Move(DeviceId, -1));
                _down = IconButton("", "", "", () => DeviceActions.Move(DeviceId, 1));
                _hide = IconButton(MixVisuals.HideGlyph, "", "", () => DeviceActions.Hide(DeviceId));
                Actions.Children.Add(_up);
                Actions.Children.Add(_down);
                Actions.Children.Add(_hide);
            }

            // Right-click (or the context key): Hide, in the popup too.
            _hideItem.Click += (_, _) => DeviceActions.Hide(DeviceId);
            var menu = new MenuFlyout();
            menu.Items.Add(_hideItem);
            ContextFlyout = menu;

            // The mix: Game, the slider, Chat.
            var mix = new Grid { ColumnSpacing = 10 };
            mix.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mix.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mix.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_slider, 1);
            Grid.SetColumn(_chatLabel, 2);
            mix.Children.Add(_gameLabel);
            mix.Children.Add(_slider);
            mix.Children.Add(_chatLabel);
            AddBody(mix);
        }

        /// <summary>Show the device's current state; also relabels after a language change.</summary>
        public void Update(OutputDevice device, bool isFirst, bool isLast, MixSnapshot snapshot)
        {
            _hasWheel = device.HasWheel;
            Icon.Glyph = MixVisuals.Glyph(device.Kind);
            bool testing = string.Equals(snapshot.TestingDeviceId, device.Id, StringComparison.OrdinalIgnoreCase);
            // The endpoint's own name, with the device it belongs to under it in quieter type (instead
            // of Windows' "Name (Device)"). Screen readers get the full name.
            SetText(device.EndpointName, testing ? UiText.Get("device.testing") : MixVisuals.Activity(device));
            SetSubtitle(device.AdapterName);
            AutomationProperties.SetName(this, device.Name);
            SetAccent(device.IsDefault);

            Badge.Content = Badges(device);

            // A headset that is off can't play anything: its icon and slider are dimmed (its text stays
            // readable; the badge says "Off").
            bool off = device.Headset is { On: false };
            Icon.Opacity = off ? 0.5 : 1;

            // Screen readers don't see the indicator bar: they hear "In use" as the card's status instead.
            AutomationProperties.SetItemStatus(this, device.IsDefault ? UiText.Get("device.in-use") : "");

            // The switch button is rebuilt only when what it offers changes, so a click isn't lost to a
            // rebuild.
            var offer = (device.IsDefault, device.IsCalls, device.HasMicrophone, device.IsMicrophone, off, UiText.Locale);
            if (_shownSwitch != offer)
            {
                _useHost.Content = SwitchButton(device, off);
                _shownSwitch = offer;

                // A disabled button shows no tooltip, so the reason sits on its holder instead.
                ToolTipService.SetToolTip(_useHost, off && !device.IsDefault ? UiText.Get("device.switch-off-tip") : null);
            }

            // A wheel sets its own device's mix: the slider shows it and can be focused and hovered (so
            // its tooltip and screen readers say why it doesn't move), but any change snaps back.
            _slider.Opacity = off || (device.HasWheel && device.Levels == null) ? 0.5 : 1;
            ToolTipService.SetToolTip(_slider, device.HasWheel ? UiText.Get("slider.wheel-tip") : null);
            _slider.IsThumbToolTipEnabled = !device.HasWheel;   // a wheel's slider snaps back, so no drag bubble
            AutomationProperties.SetName(_slider, UiText.Format("slider.name", ("device", device.Name)));

            // Screen readers hear the two levels, not the slider's raw -100 to 100.
            var shown = device.Levels ?? new MixLevels(100, 100);
            AutomationProperties.SetHelpText(_slider, UiText.Format(device.HasWheel ? "slider.levels-wheel" : SliderMix.LevelsKey,
                ("game", shown.Game), ("chat", shown.Chat)));

            int position = device.Levels is MixLevels levels ? SliderMix.PositionOf(levels) : 0;
            _wheelPosition = position;
            if ((int)_slider.Value != position && (device.HasWheel || !IsDragging))
            {
                _updating = true;
                _slider.Value = position;
                _updating = false;
            }

            _gameLabel.Text = UiText.Get("mix.game");
            _chatLabel.Text = UiText.Get("mix.chat");

            // Test: only on the device in use (where sound actually plays), or wherever a test still runs
            // so it can be stopped. Play while stopped, stop while playing.
            _test.Visibility = device.IsDefault || testing ? Visibility.Visible : Visibility.Collapsed;
            SetIconLabel(_test, testing ? StopGlyph : PlayGlyph, UiText.Get(testing ? "device.test-stop" : "device.test"));
            Relabel(_test,
                UiText.Format(testing ? "device.test-stop-name" : "device.test-name", ("device", device.Name)),
                UiText.Get(testing ? "device.test-stop-tip" : "device.test-tip"));


            string hideName = UiText.Format("device.hide-name", ("device", device.Name));
            _hideItem.Text = UiText.Get("device.hide");
            if (_up != null && _down != null && _hide != null)
            {
                _up.IsEnabled = !isFirst;
                _down.IsEnabled = !isLast;
                Relabel(_up, UiText.Format("device.move-up", ("device", device.Name)), UiText.Get("device.move-up-tip"));
                Relabel(_down, UiText.Format("device.move-down", ("device", device.Name)), UiText.Get("device.move-down-tip"));
                Relabel(_hide, hideName, UiText.Get("device.hide-tip"));
            }
        }

        /// <summary>
        /// The badges after the name: the headset's battery or power, then the roles this device holds
        /// apart from the device in use (so a split setup is visible): calls, and its microphone.
        /// </summary>
        private static UIElement? Badges(OutputDevice device)
        {
            var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            if (HeadsetBadge.Create(device.Headset) is { } headset)
            {
                badges.Children.Add(headset);
            }

            if (device.IsCalls && !device.IsDefault)
            {
                badges.Children.Add(Pill.Create(CallsGlyph, "BadgeIconStyle", UiText.Get("device.role-calls")));
            }

            if (device.IsMicrophone && !device.IsDefault)
            {
                badges.Children.Add(Pill.Create(MicrophoneGlyph, "BadgeIconStyle", UiText.Get("device.role-mic")));
            }

            return badges.Children.Count > 0 ? badges : null;
        }

        /// <summary>Segoe Fluent Icons: Volume, Phone, Microphone, for the switch menu and role badges.</summary>
        private const string OutputGlyph = "\uE767", CallsGlyph = "\uE717", MicrophoneGlyph = "\uE720";

        /// <summary>
        /// The switch control. Any other device: "Switch to" (follow-along, <see cref="SwitchScope.Follow"/>)
        /// with a menu for moving one role at a time. The device in use: the menu alone, for calls and its
        /// microphone, when either is elsewhere. Null when there is nothing to offer. A headset that is
        /// off can't be switched to.
        /// </summary>
        private UIElement? SwitchButton(OutputDevice device, bool off)
        {
            var menu = SwitchMenu(device);
            if (device.IsDefault)
            {
                bool anythingElsewhere = !device.IsCalls || (device.HasMicrophone && !device.IsMicrophone);
                if (!anythingElsewhere)
                {
                    return null;
                }

                var more = new DropDownButton { Flyout = menu, VerticalAlignment = VerticalAlignment.Center };
                Relabel(more, UiText.Format("device.switch-more-name", ("device", device.Name)), UiText.Get("device.switch-more-tip"));
                return more;
            }

            var button = new SplitButton
            {
                Content = UiText.Get("device.switch"),
                Flyout = menu,
                IsEnabled = !off,
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(button, UiText.Format("device.switch-name", ("device", device.Name)));
            ToolTipService.SetToolTip(button, UiText.Get("device.switch-tip"));
            button.Click += (_, _) => DeviceActions.SwitchTo(DeviceId, SwitchScope.Follow);
            return button;
        }

        /// <summary>The switch menu: each role on its own, then everything. Roles already here are greyed.</summary>
        private MenuFlyout SwitchMenu(OutputDevice device)
        {
            var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
            menu.Items.Add(MenuItem(OutputGlyph, "device.switch-output", SwitchScope.Output, !device.IsDefault));
            menu.Items.Add(MenuItem(CallsGlyph, "device.switch-calls", SwitchScope.Calls, !device.IsCalls));
            if (device.HasMicrophone)
            {
                menu.Items.Add(MenuItem(MicrophoneGlyph, "device.switch-mic", SwitchScope.Microphone, !device.IsMicrophone));
            }

            bool allHere = device.IsDefault && device.IsCalls && (!device.HasMicrophone || device.IsMicrophone);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem(null, device.HasMicrophone ? "device.switch-all" : "device.switch-all-no-mic", SwitchScope.All, !allHere));
            return menu;
        }

        private MenuFlyoutItem MenuItem(string? glyph, string key, SwitchScope scope, bool enabled)
        {
            var item = new MenuFlyoutItem { Text = UiText.Get(key), IsEnabled = enabled };
            if (glyph != null)
            {
                item.Icon = new FontIcon { Glyph = glyph };
            }

            item.Click += (_, _) => DeviceActions.SwitchTo(DeviceId, scope);
            return item;
        }

        /// <summary>
        /// True while a mouse button, finger or pen is down on the slider. Focus can't tell: a slider
        /// keeps pointer focus after it is released, which stopped the shortcuts' changes showing.
        /// </summary>
        private bool IsDragging => _dragging;

        private bool _dragging;

        /// <summary>Where a wheel device's slider belongs; a drag or key press on it snaps back here.</summary>
        private int _wheelPosition;

        private void OnSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_updating)
            {
                return;
            }

            if (_hasWheel)
            {
                SetQuietly(_wheelPosition);
                return;
            }

            // Dragged: the centre detent, so close to the middle snaps to it and the thumb jumps there.
            // Keys: stop at the middle on the way through, without snapping (a key press must leave it).
            int raw = (int)Math.Round(e.NewValue);
            int position = _dragging ? SliderMix.Snap(raw) : SliderMix.KeyMove((int)Math.Round(e.OldValue), raw);
            if (position != raw)
            {
                SetQuietly(position);
            }

            DeviceActions.SetSlider(DeviceId, position);
        }

        /// <summary>Move the thumb without that counting as the user moving it.</summary>
        private void SetQuietly(int position)
        {
            _updating = true;
            _slider.Value = position;
            _updating = false;
        }
    }
}
