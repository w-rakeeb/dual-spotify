using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
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
        var body = Page("Settings", "Startup, profile storage, compatibility and appearance.");
        var tabs = new TabControl { Style = Theme.Style("SettingsTabs") };
        Ui.Name(tabs, "Settings sections");
        foreach (var item in new[] {
            new TabItem { Header = "General", Content = GeneralSettings() },
            new TabItem { Header = "Storage", Content = StorageSettings() },
            new TabItem { Header = "Compatibility", Content = CompatibilitySettings() },
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
            Choice("Start with Windows", "Launch the manager in the notification area when you sign in.",
                DesktopShortcut.StartsWithWindows, DesktopShortcut.SetStartup),
            Ui.Divider(),
            Choice("Open Second Spotify at startup", "Start the second player when Dual Spotify starts with Windows.",
                prefs.StartSecondWithWindows, v => prefs.StartSecondWithWindows = v),
            Ui.Divider(),
            Choice("Start Second Spotify minimized", "Apply when the second player opens with Windows.",
                prefs.StartSecondMinimized, v => prefs.StartSecondMinimized = v)));
        body.Children.Add(SettingsCard("Window and background",
            Choice("Close to tray", "Hide the window and keep the manager available in the notification area.",
                prefs.TrayOnClose, v => prefs.TrayOnClose = v),
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
            Choice("Automatically check for updates", "Check stable GitHub releases at startup, on reopen and periodically.",
                prefs.AutoUpdates, v => prefs.AutoUpdates = v),
            ActionButton("Open Updates", () => ShowPage("Updates"))));
        body.Children.Add(SettingsCard("Maintenance", ToolActions(
            ActionButton("Repair desktop shortcuts", () => { DesktopShortcut.Repair(); toast.Show("Desktop shortcuts updated."); }),
            ActionButton("Reset app preferences", ResetPreferences),
            ActionButton("Save diagnostics", SaveDiagnostics))));
        body.Children.Add(SettingsCard("Exit application",
            Text("The Keep Second Spotify running setting controls whether the second player closes."),
            ActionButton("Exit Dual Spotify", ExitManager)));
        return body;
    }

    FrameworkElement StorageSettings()
    {
        var body = Stack();
        body.Children.Add(SettingsCard("Profile storage", PathText(Engine.SecondProfile),
            Text("Choose an empty local folder. Dual Spotify verifies the copy and keeps the original folder."),
            ToolActions(ActionButton("Move Second Spotify files…", MoveSecondProfile),
                ActionButton("View file locations", () => ShowPage("Files")))));
        body.Children.Add(SettingsCard("Spotify installation", PathText(Engine.Spotify),
            ToolActions(ActionButton("Locate Spotify…", LocateSpotify), ActionButton("Detect Spotify", DetectSpotify))));
        return body;
    }

    FrameworkElement CompatibilitySettings()
    {
        var body = Stack();
        var openStore = ActionButton("Open Microsoft Store Spotify", SpotifyInstallation.OpenStore);
        openStore.IsEnabled = Engine.StoreAvailable;
        body.Children.Add(SettingsCard("Spotify desktop client",
            Text(Engine.DesktopAvailable ? "Standalone installation detected." : "Standalone installation missing."),
            Text(Engine.StoreAvailable ? "Microsoft Store installation detected." : "Microsoft Store installation not detected."),
            Text("Use Open on Profiles for either edition. You can find the registered Microsoft Store installation under Files."),
            ToolActions(ActionButton("Verify native setup", VerifySetup), openStore,
                ActionButton("Get Spotify for Windows", OpenSpotifyDownload))));
        body.Children.Add(SettingsCard("Windows requirements",
            Text("Windows 10 or 11 x64 and .NET Framework 4.8. The launcher runs under your Windows account.")));
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

    async void MoveSecondProfile()
    {
        if (busy || updateCancellation != null)
            return;
        busy = true;
        try
        {
            if (Engine.Snapshot()[1].Length > 0)
                throw new IOException("Quit Second Spotify before moving its files.");
            using (var picker = new Forms.FolderBrowserDialog { Description = "Choose an empty folder for Second Spotify" })
            {
                if (picker.ShowDialog() != Forms.DialogResult.OK)
                    return;
                var destination = picker.SelectedPath;
                toast.Show("Copying and verifying the second profile…");
                await Task.Run(() => Engine.MoveProfile(destination));
            }
            if (currentPage == "Settings")
                ShowPage("Settings");
            toast.Show("Profile files moved. The original folder was kept.");
        }
        catch (Exception ex) { toast.Show(ex.Message); }
        finally { busy = false; }
    }

    void VerifySetup()
    {
        if (!Engine.CanOpen)
            throw new IOException("Use Locate Spotify to select an installed Spotify.exe.");
        var state = Engine.Snapshot();
        toast.Show("Spotify detected. Main processes: " + state[0].Length + " · Second processes: " + state[1].Length + ".");
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
