using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DualSpotify;
using DualSpotify.UI;
using DualSpotifyNative;

[assembly: AssemblyTitle("Dual Spotify")]
[assembly: AssemblyVersion("0.2.0.0")]

public partial class App : Application
{
    const string BuildVersion = UpdateService.CurrentVersion;
    const string SignalPrefix = @"Local\WrakeebDualSpotify.";
    bool exiting, busy, refreshing;
    string currentPage = "Profiles";
    Window window;
    Grid shell;
    ColumnDefinition railColumn;
    TextBlock brand;
    ContentControl content;
    ScrollViewer pageScroll;
    Toast toast;
    ManagerTray tray;
    SpotifyTaskbarIdentity taskbar;
    DispatcherTimer timer;
    ProfileCard mainCard, secondCard;
    ProcessDetails mainDetails, secondDetails;
    readonly List<NavItem> navigation = new List<NavItem>();
    int[][] last = { new int[0], new int[0] };
    int[][] lastChildren = { new int[0], new int[0] };

    [STAThread]
    public static int Main(string[] args)
    {
        if (!UpdatePackage.RuntimeFilesPresent(Engine.Home))
        {
            MessageBox.Show("Extract the entire Dual Spotify ZIP into a writable folder and keep its files together.", "Complete app folder required");
            return 1;
        }
        bool direct = args.Contains("--second");
        bool startup = args.Contains("--startup");
        bool created;
        string token;
        using (var sha = SHA256.Create())
            token = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Engine.Home.ToUpperInvariant())));
        using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, SignalPrefix + token + ".Show"))
        using (var second = new EventWaitHandle(false, EventResetMode.AutoReset, SignalPrefix + token + ".Second"))
        using (var mutex = new Mutex(true, SignalPrefix + token + ".Manager", out created))
        {
            if (!created)
            {
                if (!startup)
                    (direct ? second : show).Set();
                return 0;
            }
            SpotifyTaskbarIdentity.SetManagerIdentity();
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var showWait = ThreadPool.RegisterWaitForSingleObject(show, (s, t) => app.Dispatcher.BeginInvoke(new Action(app.ShowManager)), null, -1, false);
            var secondWait = ThreadPool.RegisterWaitForSingleObject(second, (s, t) => app.Dispatcher.BeginInvoke(new Action(app.OpenSecond)), null, -1, false);
            app.Startup += (s, e) => app.Start(direct, startup);
            try { return app.Run(); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Dual Spotify");
                return 1;
            }
            finally { showWait.Unregister(null); secondWait.Unregister(null); }
        }
    }

    void Start(bool direct, bool startup)
    {
        Preferences.Load(Engine.Data);
        Theme.Apply(Preferences.Current.ThemeMode);
        Theme.InitStyles();
        BuildShell();
        MainWindow = window;
        taskbar = new SpotifyTaskbarIdentity(Engine.Home);
        tray = new ManagerTray(ShowManager, OpenSecond, QuitSecond, ExitManager, OpenMain,
            () => { ShowManager(); ShowPage("Settings"); }, CheckUpdatesFromShortcut,
            () => Safe(() => OpenPath(Engine.Home)));
        window.Closing += (s, e) =>
        {
            if (exiting)
                return;
            e.Cancel = true;
            if (Preferences.Current.TrayOnClose)
                window.Hide();
            else
                ExitManager();
        };
        ShowPage("Profiles");
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += async (s, e) => { await Refresh(); CheckUpdateSchedule(false); };
        timer.Start();
        window.Activated += (s, e) => CheckUpdateSchedule(true);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += WindowsPreferencesChanged;
        if (direct)
            OpenSecond();
        else if (startup && Preferences.Current.StartSecondWithWindows)
            RunProfile(true, true, Preferences.Current.StartSecondMinimized);
        else if (!startup)
            ShowManager();
        RefreshNow();
        CheckUpdateSchedule(false);
    }

    void WindowsPreferencesChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs args)
    {
        if (args.Category == Microsoft.Win32.UserPreferenceCategory.General ||
            args.Category == Microsoft.Win32.UserPreferenceCategory.Accessibility ||
            args.Category == Microsoft.Win32.UserPreferenceCategory.Color)
            Dispatcher.BeginInvoke(new Action(() => Theme.Apply(Preferences.Current.ThemeMode)));
    }

    async void RefreshNow() { await Refresh(); }

    void ShowManager()
    {
        if (exiting || window == null)
            return;
        window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }

    async void ExitManager()
    {
        if (updateCancellation != null)
        {
            exitAfterDownload = true;
            updateCancellation.Cancel();
            return;
        }
        if (busy || exiting)
            return;
        if (!Preferences.Current.KeepSecondRunning)
        {
            busy = true;
            try { await Task.Run(() => Engine.Quit(true)); }
            catch (Exception ex) { ShowManager(); toast.Show(ex.Message); return; }
            finally { busy = false; }
        }
        ShutdownManager();
    }

    void ShutdownManager()
    {
        exiting = true;
        timer.Stop();
        tray.Dispose();
        taskbar.Dispose();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= WindowsPreferencesChanged;
        Shutdown();
    }

    async Task Refresh()
    {
        if (exiting || refreshing || busy)
            return;
        refreshing = true;
        try
        {
            var snapshot = await Task.Run(() =>
            {
                int[][] children;
                return Tuple.Create(Engine.Scan(out children), children);
            });
            if (!exiting && !busy)
            {
                last = snapshot.Item1;
                lastChildren = snapshot.Item2;
                UpdateProfiles();
            }
        }
        catch (Exception ex) { if (!exiting && window.IsVisible) toast.Show(ex.Message); }
        finally { refreshing = false; }
    }

    void OpenMain() { RunProfile(false, true); }
    void OpenSecond() { RunProfile(true, true); }
    void QuitSecond() { RunProfile(true, false); }

    async void RunProfile(bool second, bool opening, bool minimized = false)
    {
        if (busy || exiting)
            return;
        busy = true;
        var card = second ? secondCard : mainCard;
        if (card != null)
            card.Loading(opening);
        try
        {
            await Task.Run(() =>
            {
                if (opening)
                    Engine.Open(second, minimized);
                else
                    Engine.Quit(second);
            });
        }
        catch (Exception ex)
        {
            if (!exiting)
            {
                ShowManager();
                toast.Show(ex.Message);
            }
        }
        finally
        {
            busy = false;
            if (card != null)
                card.EndLoading();
        }
        await Refresh();
    }

    void UpdateProfiles()
    {
        tray.Update(last[1].Length > 0, last[0].Length > 0);
        var secondProcesses = last[1].Select(Engine.TryProcess).Where(p => p != null).ToArray();
        try { taskbar.Update(secondProcesses, Preferences.Current.SeparateTaskbar); }
        finally { foreach (var process in secondProcesses) process.Dispose(); }
        if (mainCard == null)
            return;
        for (int i = 0; i < 2; i++)
        {
            var card = i == 0 ? mainCard : secondCard;
            var details = i == 0 ? mainDetails : secondDetails;
            var processes = last[i].Select(Engine.TryProcess).Where(p => p != null).ToArray();
            var helpers = lastChildren[i].Select(Engine.TryProcess).Where(p => p != null).ToArray();
            try
            {
                card.Update(processes, i == 0 || Directory.Exists(Engine.SecondProfile));
                details.Update(PerformanceSnapshot.Capture(processes, helpers));
            }
            finally { foreach (var process in processes.Concat(helpers)) process.Dispose(); }
        }
        mainCard.Quit.IsEnabled = false;
        if (!File.Exists(Engine.Spotify) && last.All(p => p.Length == 0))
        {
            mainCard.Pill.Set("Missing", "warning", "Main Spotify");
            secondCard.Pill.Set("Missing", "warning", "Second Spotify");
        }
    }

    void ShowPage(string page)
    {
        currentPage = page;
        ClearUpdateView();
        foreach (var item in navigation)
            item.Active(item.Page == page);
        mainCard = secondCard = null;
        content.Content = page == "Profiles" ? Profiles() : page == "Settings" ? Settings() :
            page == "Files" ? Files() : page == "Updates" ? Updates() : About();
        pageScroll.ScrollToTop();
        UpdateProfiles();
    }

    FrameworkElement Profiles()
    {
        var body = Page("Profiles", "Run two Spotify accounts side by side.");
        mainCard = Profile("Main Spotify", "Default desktop profile", "spotify.png", OpenMain);
        mainCard.Quit.ToolTip = "Use Quit Spotify in the main client.";
        secondCard = Profile("Second Spotify", "Separate sign-in and profile data", "second-spotify.png", OpenSecond);
        secondCard.Quit.Click += (s, e) => QuitSecond();
        body.Children.Add(mainCard);
        body.Children.Add(secondCard);
        var columns = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        mainDetails = new ProcessDetails("Main Spotify") { Margin = new Thickness(0, 0, 16, 0) };
        secondDetails = new ProcessDetails("Second Spotify") { Margin = new Thickness(16, 0, 0, 0) };
        var divider = new Border();
        Theme.Bind(divider, Border.BackgroundProperty, "border.subtle");
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(secondDetails, 2);
        columns.Children.Add(mainDetails);
        columns.Children.Add(divider);
        columns.Children.Add(secondDetails);
        var details = new Expander { Header = "Process details", IsExpanded = Preferences.Current.ExpandProcesses,
            Content = columns, Margin = new Thickness(0, 12, 0, 0) };
        body.Children.Add(details);
        var hint = Text("Client processes only · Memory is working set · CPU is sampled across all cores.", 12, "text.muted");
        hint.Margin = new Thickness(0, 20, 0, 0);
        body.Children.Add(hint);
        if (!File.Exists(Engine.Spotify))
            body.Children.Add(ActionButton("Get Spotify for Windows", () => OpenPath("https://www.spotify.com/download/windows/")));
        return body;
    }

    ProfileCard Profile(string name, string description, string artwork, Action open)
    {
        var card = new ProfileCard(name, description, "\uE8D6") { Margin = new Thickness(0, 0, 0, 12) };
        card.SetLogo(Path.Combine(Engine.Home, "Assets", artwork));
        card.Open.Click += (s, e) => open();
        return card;
    }

    FrameworkElement Choice(string title, string copy, bool selected, Action<bool> change)
    {
        var row = new SettingRow(title, copy) { Margin = new Thickness(0), MinHeight = 76 };
        row.Toggle.IsChecked = selected;
        row.Toggle.Checked += (s, e) => SaveChoice(row.Toggle, change);
        row.Toggle.Unchecked += (s, e) => SaveChoice(row.Toggle, change);
        return row;
    }

    void SaveChoice(ToggleSwitch toggle, Action<bool> change)
    {
        Safe(() => { change(toggle.IsChecked == true); Preferences.Save(); UpdateProfiles(); });
    }

    FrameworkElement Files()
    {
        var body = Page("Files", "View your player and profile locations.");
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        var profiles = Stack();
        profiles.Margin = new Thickness(0, 0, 6, 0);
        profiles.Children.Add(PathCard("Official Spotify client", Engine.Spotify, Path.GetDirectoryName(Engine.Spotify)));
        profiles.Children.Add(PathCard("Second Spotify profile", Engine.SecondProfile, Engine.SecondProfile));
        var manager = Stack();
        manager.Margin = new Thickness(6, 0, 0, 0);
        manager.Children.Add(PathCard("Dual Spotify folder", Engine.Home, Engine.Home));
        manager.Children.Add(PathCard("Launcher data", Engine.Data, Engine.Data));
        var backups = Path.Combine(Engine.Home, "Backups");
        manager.Children.Add(PathCard("App update backups", backups, backups));
        Grid.SetColumn(manager, 1);
        columns.Children.Add(profiles);
        columns.Children.Add(manager);
        body.Children.Add(columns);
        return body;
    }

    FrameworkElement PathCard(string name, string path, string folder)
    {
        var panel = Stack();
        panel.Children.Add(Text(name, 14, "text.primary", true));
        panel.Children.Add(PathText(path));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var open = Ui.IconButton("\uE838", "Open " + name, () => Safe(() => OpenPath(folder)));
        open.IsEnabled = Directory.Exists(folder);
        actions.Children.Add(open);
        actions.Children.Add(Ui.IconButton("\uE8C8", "Copy " + name + " path",
            () => Safe(() => { Clipboard.SetText(path); toast.Show("Path copied."); })));
        panel.Children.Add(actions);
        return Group(panel);
    }

    void LocateSpotify()
    {
        if (busy || Engine.Snapshot()[1].Length > 0)
            throw new InvalidOperationException("Quit Second Spotify before changing the installation.");
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Spotify executable|Spotify.exe", CheckFileExists = true };
        if (picker.ShowDialog(window) != true)
            return;
        if (busy || Engine.Snapshot()[1].Length > 0)
            throw new InvalidOperationException("Quit Second Spotify before changing the installation.");
        Engine.SelectSpotify(picker.FileName);
        ShowPage("Settings");
        toast.Show("Spotify installation saved.");
        RefreshNow();
    }

    FrameworkElement About()
    {
        var body = Page("About", "Two Spotify accounts on one Windows desktop.");
        body.Children.Add(SettingsCard("Dual Spotify  ·  " + BuildVersion,
            Text("Uses the official Spotify desktop client.", Theme.BodySize, "text.secondary"), new CreatorCredit(OpenPath)));
        body.Children.Add(UpdatePanel());
        body.Children.Add(SettingsCard("Getting started",
            Text("Open Main Spotify and Second Spotify from Profiles. Sign in to each account in its own window."),
            Text("Use Settings to repair shortcuts, manage profile storage, or change how the launcher starts.")));
        return body;
    }

    static void OpenPath(string path) { using (Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })) { } }
    void Safe(Action action) { try { action(); } catch (Exception ex) { toast.Show(ex.Message); } }
    static StackPanel Stack() { return new StackPanel(); }
    static TextBlock Text(string text, double size = Theme.BodySize, string color = "text.primary", bool bold = false) { return Ui.Text(text, size, color, bold); }
    static Card Group(UIElement child) { return new Card { Child = child, Margin = new Thickness(0, 0, 0, 12) }; }
    static StackPanel Page(string name, string subtitle)
    {
        var panel = Stack();
        var heading = Text(name, Theme.PageSize, "text.primary", true);
        Ui.Name(heading, "Page title: " + name);
        panel.Children.Add(heading);
        var description = Text(subtitle, Theme.BodySize, "text.secondary");
        description.Margin = new Thickness(0, 4, 0, 24);
        panel.Children.Add(description);
        return panel;
    }
    Button ActionButton(string title, Action action)
    {
        var button = Ui.Button(title, false, () => Safe(action));
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Margin = new Thickness(0, 8, 0, 0);
        return button;
    }
}
