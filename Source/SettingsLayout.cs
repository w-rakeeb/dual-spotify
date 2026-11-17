using System;
using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using DualSpotify.UI;
using Forms = System.Windows.Forms;

public partial class App
{
    string settingsSection = "General";

    FrameworkElement Settings()
    {
        var body = Page("Settings", "");
        var tabs = new TabControl { Style = Theme.Style("SettingsTabs") };
        Ui.Name(tabs, "Settings sections");
        foreach (var item in new[] {
            new TabItem { Header = "General", Content = GeneralSettings() },
            new TabItem { Header = "Windows profiles", Content = WindowsProfileSettings() },
            new TabItem { Header = "Storage", Content = StorageSettings() },
            new TabItem { Header = "Appearance", Content = AppearanceSettings() }
        })
        {
            item.Style = Theme.Style("SettingsTab");
            Ui.Name(item, item.Header + " settings");
            tabs.Items.Add(item);
            if ((string)item.Header == settingsSection)
                tabs.SelectedItem = item;
        }
        tabs.SelectionChanged += (s, e) =>
        {
            if (e.Source == tabs && tabs.SelectedItem is TabItem)
            {
                settingsSection = (string)((TabItem)tabs.SelectedItem).Header;
                pageScroll.ScrollToTop();
            }
        };
        body.Children.Add(tabs);
        return body;
    }

    FrameworkElement GeneralSettings()
    {
        var prefs = Preferences.Current;
        var body = Stack();
        body.Children.Add(SettingsCard("Startup",
            Choice("Start with Windows", "Open the manager in the notification area.",
                DesktopShortcut.StartsWithWindows, DesktopShortcut.SetStartup),
            Ui.Divider(),
            Choice("Open Second Spotify at startup", "Requires Start with Windows.",
                prefs.StartSecondWithWindows, v => prefs.StartSecondWithWindows = v),
            Ui.Divider(),
            Choice("Start Second Spotify minimized", "Applies when the player starts with Windows.",
                prefs.StartSecondMinimized, v => prefs.StartSecondMinimized = v)));
        body.Children.Add(SettingsCard("Window and background",
            Choice("Close to tray", "Keep the manager in the notification area.",
                prefs.CloseToTray, v => prefs.CloseToTray = v),
            Ui.Divider(),
            Choice("Second Spotify tray control", "Show a second notification-area icon while the second player is open.",
                prefs.SecondTray, v => prefs.SecondTray = v),
            Ui.Divider(),
            Choice("Separate taskbar group", "Give Second Spotify its own taskbar group and profile icon.",
                prefs.SeparateTaskbar, v => prefs.SeparateTaskbar = v),
            Ui.Divider(),
            Choice("Keep Second Spotify running on exit", "Leave the second player open when you exit the manager.",
                prefs.KeepSecondRunning, v => prefs.KeepSecondRunning = v)));
        body.Children.Add(SettingsCard("Updates",
            Choice("Automatically check for updates", "Check for stable GitHub releases.",
                prefs.AutoUpdates, v => prefs.AutoUpdates = v),
            ActionButton("Open Updates", () => ShowPage("Updates"))));
        body.Children.Add(SettingsCard("Maintenance", ToolActions(
            ActionButton("Repair shortcuts", () => { DesktopShortcut.Repair(); toast.Show("Start menu and desktop shortcuts updated."); }),
            ActionButton("Reset app preferences", ResetPreferences),
            ActionButton("Save diagnostics", SaveDiagnostics))));
        return body;
    }

    FrameworkElement StorageSettings()
    {
        var body = Stack();
        body.Children.Add(SettingsCard("Standalone Spotify",
            Engine.DesktopAvailable ? PathText(Engine.Spotify) : Text("Spotify.exe was not detected."),
            ToolActions(ActionButton("Locate Spotify…", LocateSpotify), ActionButton("Detect Spotify", DetectSpotify),
                ActionButton("Get Spotify for Windows", OpenSpotifyDownload))));
        if (Engine.StoreAvailable)
            body.Children.Add(SettingsCard("Microsoft Store Spotify",
                ActionButton("Open Microsoft Store Spotify", SpotifyInstallation.OpenStore)));
        body.Children.Add(SettingsCard("Launcher files", ActionButton("View file locations", () => ShowPage("Files"))));
        return body;
    }

    FrameworkElement AppearanceSettings()
    {
        var body = Stack();
        var choices = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var name in new[] { "Dark", "Light", "System" })
        {
            var radio = new RadioButton { Content = name, GroupName = "Theme",
                IsChecked = Preferences.Current.ThemeMode == name, Margin = new Thickness(0, 0, 24, 0) };
            Theme.Bind(radio, Control.ForegroundProperty, "text.primary");
            radio.Checked += (s, e) => Safe(() => { Preferences.Current.ThemeMode = name; Preferences.Save(); Theme.Apply(name); });
            choices.Children.Add(radio);
        }
        body.Children.Add(SettingsCard("Theme", choices));
        body.Children.Add(SettingsCard("Profiles", Choice("Expand process details by default",
            "Show the process metrics when you open Profiles.", Preferences.Current.ExpandProcesses,
            value => Preferences.Current.ExpandProcesses = value)));
        return body;
    }

    void ResetPreferences()
    {
        if (MessageBox.Show(window, "Restore the default appearance, startup, background and update preferences? Your sign-ins and profile location are preserved.",
            "Reset preferences", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        DesktopShortcut.SetStartup(false);
        Engine.WriteDataText("preferences-before-reset.json", new JavaScriptSerializer().Serialize(Preferences.Current));
        Preferences.Current = new Preferences();
        Preferences.Save();
        Theme.Apply(Preferences.Current.ThemeMode);
        ShowPage("Settings");
        toast.Show("Preferences reset.");
    }

    void SaveDiagnostics()
    {
        var state = Engine.Snapshot();
        var report = new { Version = BuildVersion, Windows = Environment.OSVersion.Version.ToString(),
            StandaloneDetected = File.Exists(Engine.Spotify), SpotifyVersion = File.Exists(Engine.Spotify)
                ? FileVersionInfo.GetVersionInfo(Engine.Spotify).ProductVersion : null,
            StoreDetected = Engine.StoreAvailable,
            MainProcesses = state[0].Length, SecondProcesses = state[1].Length,
            CustomStorage = Engine.SecondProfile != Engine.DefaultProfile };
        Engine.WriteDataText("diagnostics.json", new JavaScriptSerializer().Serialize(report));
        toast.Show("Saved diagnostics.json in the Data folder.");
    }

    static TextBlock PathText(string path)
    {
        var text = Text(path, Theme.NoteSize, "text.muted");
        Theme.Bind(text, TextBlock.FontFamilyProperty, "Font.Mono");
        return text;
    }

    static Card SettingsCard(string title, params UIElement[] children)
    {
        var body = Stack();
        var heading = Text(title, Theme.TitleSize, "text.primary", true);
        heading.Margin = new Thickness(0, 0, 0, 8);
        body.Children.Add(heading);
        foreach (var child in children)
            body.Children.Add(child);
        return Group(body);
    }

    static WrapPanel ToolActions(params Button[] buttons)
    {
        var row = new WrapPanel();
        foreach (var button in buttons)
        {
            button.MinHeight = 40;
            button.Margin = new Thickness(0, 8, 8, 0);
            row.Children.Add(button);
        }
        return row;
    }
}
