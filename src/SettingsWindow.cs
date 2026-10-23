using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Trill
{
    public sealed class SettingsWindow : Window
    {
        private readonly AppSettings draft;
        private readonly Dictionary<PlayerAction, Button> shortcutButtons = new Dictionary<PlayerAction, Button>();
        private readonly Brush textBrush = BrushFrom("#F3F3F3");
        private readonly Brush mutedBrush = BrushFrom("#9E9E9E");
        private readonly Brush accentBrush = BrushFrom("#B8B8B8");
        private readonly Brush controlBrush = BrushFrom("#1FFFFFFF");
        private PlayerAction? capturing;
        private TextBlock shortcutStatus;
        private CheckBox loopPlaylist;
        private CheckBox windowsFileDialogs;

        public AppSettings Result { get; private set; }

        public SettingsWindow(AppSettings initial)
        {
            draft = (initial ?? AppSettings.CreateDefault()).Clone();
            Title = "Trill Media Player settings";
            Width = 640;
            Height = 750;
            MinWidth = 540;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = BrushFrom("#121212");
            Foreground = textBrush;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 14;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResize;
            Glass.Attach(this, null);
            PreviewKeyDown += OnPreviewKeyDown;
            Deactivated += delegate { EndCapture(); };
            Content = BuildContent();
            Loaded += delegate { UiMotion.FadeIn((UIElement)Content); };
        }

        private UIElement BuildContent()
        {
            Grid layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            StackPanel heading = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            heading.Children.Add(new TextBlock { Text = "Settings", FontSize = 25, FontWeight = FontWeights.SemiBold, Foreground = textBrush });
            layout.Children.Add(heading);

            StackPanel content = new StackPanel();
            content.Children.Add(SectionTitle("PLAYLIST"));
            loopPlaylist = MakeCheckBox("Wrap next / previous", draft.LoopPlaylist);
            content.Children.Add(loopPlaylist);
            content.Children.Add(new Border { Height = 1, Background = BrushFrom("#323232"), Margin = new Thickness(0, 18, 0, 18) });

            content.Children.Add(SectionTitle("FILES"));
            windowsFileDialogs = MakeCheckBox("Use Windows file dialogs", draft.UseWindowsFileDialogs);
            windowsFileDialogs.Margin = new Thickness(0, 12, 0, 0);
            content.Children.Add(windowsFileDialogs);
            Button dataFolder = MakeButton("Open data folder", 156);
            dataFolder.Name = "OpenDataFolderButton";
            dataFolder.ToolTip = RecentStore.FolderPath;
            dataFolder.HorizontalAlignment = HorizontalAlignment.Left;
            dataFolder.Margin = new Thickness(0, 14, 0, 0);
            dataFolder.Click += OnOpenDataFolder;
            content.Children.Add(dataFolder);
            content.Children.Add(new Border { Height = 1, Background = BrushFrom("#323232"), Margin = new Thickness(0, 18, 0, 18) });

            DockPanel shortcutHeading = new DockPanel();
            Button reset = MakeButton("Reset shortcuts", 128);
            reset.Margin = new Thickness(12, 0, 0, 0);
            reset.Click += delegate
            {
                EndCapture();
                draft.Shortcuts = AppSettings.CreateDefault().Shortcuts;
                RefreshShortcutButtons();
                SetStatus("Default shortcuts restored.", false);
            };
            DockPanel.SetDock(reset, Dock.Right);
            shortcutHeading.Children.Add(reset);
            TextBlock shortcutTitle = SectionTitle("SHORTCUTS");
            shortcutTitle.VerticalAlignment = VerticalAlignment.Center;
            shortcutHeading.Children.Add(shortcutTitle);
            content.Children.Add(shortcutHeading);
            content.Children.Add(new TextBlock
            {
                Text = "Select a shortcut, then press your keys.",
                FontSize = 12, Foreground = mutedBrush, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 12)
            });
            foreach (PlayerAction action in Enum.GetValues(typeof(PlayerAction)))
                content.Children.Add(ShortcutRow(action));



            ScrollViewer scroll = new ScrollViewer
            {
                Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 10, 0)
            };
            scroll.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), CreateScrollBarStyle());
            Grid.SetRow(scroll, 1);
            layout.Children.Add(scroll);

            StackPanel footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            shortcutStatus = new TextBlock
            {
                Text = "", Foreground = mutedBrush,
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
                MinHeight = 22, Margin = new Thickness(0, 0, 0, 10)
            };
            footer.Children.Add(shortcutStatus);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button cancel = MakeButton("Cancel", 90);
            cancel.Click += delegate { DialogResult = false; };
            Button save = MakeButton("Apply", 100);
            save.Background = accentBrush;
            save.Foreground = BrushFrom("#1B1B1B");
            save.Margin = new Thickness(10, 0, 0, 0);
            save.Click += OnSave;
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            Grid actionBar = new Grid();
            actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actionBar.Children.Add(new TextBlock
            {
                Text = "Saved on this PC.", FontSize = 12, Foreground = mutedBrush,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
            });
            Grid.SetColumn(buttons, 1);
            actionBar.Children.Add(buttons);
            footer.Children.Add(actionBar);
            Grid.SetRow(footer, 2);
            layout.Children.Add(footer);
            return new Border { Background = BrushFrom("#59101010"), Padding = new Thickness(26, 22, 26, 20), Child = layout };
        }

        private UIElement ShortcutRow(PlayerAction action)
        {
            Grid row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(177) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            TextBlock label = new TextBlock
            {
                Text = PlayerActionNames.GetLabel(action), Foreground = textBrush,
                VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 12, 0), FontSize = 13
            };
            row.Children.Add(label);
            if (!draft.Shortcuts.ContainsKey(action))
                draft.Shortcuts[action] = AppSettings.CreateDefault().Shortcuts[action];
            Button binding = MakeButton(draft.Shortcuts[action].Display, Double.NaN);
            binding.FontSize = 12;
            binding.ToolTip = "Change shortcut for " + PlayerActionNames.GetLabel(action).ToLowerInvariant();
            binding.Click += delegate
            {
                EndCapture();
                capturing = action;
                binding.Content = "Press keys...";
                binding.Background = BrushFrom("#444444");
                SetStatus("Setting " + PlayerActionNames.GetLabel(action).ToLowerInvariant() + ". Press Escape to cancel.", false);
                binding.Focus();
            };
            binding.LostKeyboardFocus += delegate
            {
                if (capturing.HasValue && capturing.Value == action)
                {
                    EndCapture();
                    SetStatus("Shortcut capture canceled.", false);
                }
            };
            shortcutButtons[action] = binding;
            Grid.SetColumn(binding, 1);
            row.Children.Add(binding);
            Button clear = MakeButton("Clear", Double.NaN);
            clear.FontSize = 11;
            clear.Padding = new Thickness(6, 5, 6, 5);
            clear.Margin = new Thickness(6, 0, 0, 0);
            clear.Foreground = mutedBrush;
            clear.ToolTip = "Remove this shortcut";
            clear.Click += delegate
            {
                EndCapture();
                draft.Shortcuts[action] = new ShortcutBinding(Key.None, ModifierKeys.None);
                RefreshShortcutButtons();
                SetStatus(PlayerActionNames.GetLabel(action) + " shortcut cleared.", false);
            };
            Grid.SetColumn(clear, 2);
            row.Children.Add(clear);
            return row;
        }

        private void OnOpenDataFolder(object sender, RoutedEventArgs e)
        {
            EndCapture();
            try
            {
                System.IO.Directory.CreateDirectory(RecentStore.FolderPath);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = RecentStore.FolderPath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (System.IO.IOException) { SetStatus("Unable to open data folder.", true); }
            catch (UnauthorizedAccessException) { SetStatus("Unable to access data folder.", true); }
            catch (System.ComponentModel.Win32Exception) { SetStatus("Unable to open File Explorer.", true); }
            catch (System.Security.SecurityException) { SetStatus("Unable to access data folder.", true); }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (capturing.HasValue)
            {
                e.Handled = true;
                if (key == Key.Escape)
                {
                    EndCapture();
                    SetStatus("Shortcut capture canceled.", false);
                    return;
                }
                if (!SettingsStore.IsBindableKey(key)) return;
                PlayerAction changedAction = capturing.Value;
                string assignmentError;
                if (!ShortcutRules.TryAssign(draft, changedAction, key, e.KeyboardDevice.Modifiers, out assignmentError))
                {
                    SetStatus(assignmentError, true);
                    return;
                }
                ShortcutBinding proposed = draft.Shortcuts[changedAction];
                EndCapture();
                SetStatus(PlayerActionNames.GetLabel(changedAction) + ": " + proposed.Display + ".", false);
            }
            else if (key == Key.Escape)
            {
                e.Handled = true;
                DialogResult = false;
            }
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            EndCapture();
            draft.LoopPlaylist = loopPlaylist.IsChecked == true;
            draft.UseWindowsFileDialogs = windowsFileDialogs.IsChecked == true;
            Result = draft;
            DialogResult = true;
        }

        private void EndCapture()
        {
            capturing = null;
            RefreshShortcutButtons();
        }

        private void RefreshShortcutButtons()
        {
            foreach (KeyValuePair<PlayerAction, Button> pair in shortcutButtons)
            {
                pair.Value.Content = draft.Shortcuts[pair.Key].Display;
                pair.Value.Background = controlBrush;
            }
        }

        private void SetStatus(string message, bool error)
        {
            if (shortcutStatus == null) return;
            shortcutStatus.Text = message;
            shortcutStatus.Foreground = error ? BrushFrom("#C9C9C9") : mutedBrush;
        }

        private TextBlock SectionTitle(string text)
        {
            return new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = accentBrush };
        }

        private CheckBox MakeCheckBox(string text, bool isChecked)
        {
            return new CheckBox
            {
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = textBrush, FontSize = 13 },
                Style = CreateCheckBoxStyle(),
                IsChecked = isChecked, Margin = new Thickness(0, 0, 0, 12),
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private static Style CreateScrollBarStyle()
        {
            return (Style)System.Windows.Markup.XamlReader.Parse(@"
<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
       xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""{x:Type ScrollBar}"">
  <Setter Property=""Width"" Value=""7""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""{x:Type ScrollBar}"">
        <Grid Background=""Transparent"" Width=""7"" HorizontalAlignment=""Right"">
          <Track x:Name=""PART_Track"" Orientation=""Vertical"" IsDirectionReversed=""True""
                 Minimum=""{TemplateBinding Minimum}"" Maximum=""{TemplateBinding Maximum}""
                 Value=""{TemplateBinding Value}"" ViewportSize=""{TemplateBinding ViewportSize}"">
            <Track.DecreaseRepeatButton>
              <RepeatButton Command=""{x:Static ScrollBar.PageUpCommand}"" Opacity=""0"" Focusable=""False""/>
            </Track.DecreaseRepeatButton>
            <Track.Thumb>
              <Thumb MinHeight=""32"">
                <Thumb.Template>
                  <ControlTemplate TargetType=""{x:Type Thumb}"">
                    <Border x:Name=""Handle"" Background=""#404040"" CornerRadius=""3""/>
                    <ControlTemplate.Triggers>
                      <Trigger Property=""IsMouseOver"" Value=""True""><Setter TargetName=""Handle"" Property=""Background"" Value=""#707070""/></Trigger>
                      <Trigger Property=""IsDragging"" Value=""True""><Setter TargetName=""Handle"" Property=""Background"" Value=""#B8B8B8""/></Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command=""{x:Static ScrollBar.PageDownCommand}"" Opacity=""0"" Focusable=""False""/>
            </Track.IncreaseRepeatButton>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>");
        }

        private static Style CreateCheckBoxStyle()
        {
            return (Style)System.Windows.Markup.XamlReader.Parse(@"
<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
       xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""{x:Type CheckBox}"">
  <Setter Property=""Cursor"" Value=""Hand""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""{x:Type CheckBox}"">
        <BulletDecorator Background=""Transparent"">
          <BulletDecorator.Bullet>
            <Border x:Name=""Box"" Width=""16"" Height=""16"" CornerRadius=""4""
                    Background=""#2A2A2A"" BorderBrush=""#505050"" BorderThickness=""1"">
              <Path x:Name=""Check"" Data=""M 3,7 L 6,10 L 12,3"" Stroke=""#1B1B1B""
                    StrokeThickness=""1.8"" StrokeStartLineCap=""Round"" StrokeEndLineCap=""Round"" Visibility=""Collapsed""/>
            </Border>
          </BulletDecorator.Bullet>
          <ContentPresenter Margin=""8,0,0,0"" VerticalAlignment=""Center"" RecognizesAccessKey=""True""/>
        </BulletDecorator>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsChecked"" Value=""True"">
            <Setter TargetName=""Box"" Property=""Background"" Value=""#B8B8B8""/>
            <Setter TargetName=""Box"" Property=""BorderBrush"" Value=""#B8B8B8""/>
            <Setter TargetName=""Check"" Property=""Visibility"" Value=""Visible""/>
          </Trigger>
          <Trigger Property=""IsMouseOver"" Value=""True""><Setter TargetName=""Box"" Property=""BorderBrush"" Value=""#B8B8B8""/></Trigger>
          <Trigger Property=""IsKeyboardFocused"" Value=""True""><Setter TargetName=""Box"" Property=""BorderBrush"" Value=""#FFFFFF""/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>");
        }
        private Button MakeButton(string text, double width)
        {
            Button button = new Button
            {
                Content = text, Width = width, MinHeight = 33, Padding = new Thickness(10, 6, 10, 6),
                Background = controlBrush, Foreground = textBrush,
                BorderBrush = BrushFrom("#393939"), BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand, FontSize = 12,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "ButtonBorder";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            ControlTemplate template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, accentBrush, "ButtonBorder"));
            template.Triggers.Add(hover);
            Trigger focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focused.Setters.Add(new Setter(Border.BorderBrushProperty, accentBrush, "ButtonBorder"));
            template.Triggers.Add(focused);
            Trigger pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.75, "ButtonBorder"));
            template.Triggers.Add(pressed);
            button.Template = template;
            return button;
        }

        private static Brush BrushFrom(string color)
        {
            SolidColorBrush brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color);
            brush.Freeze();
            return brush;
        }
    }
}
