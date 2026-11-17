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
[assembly: AssemblyVersion("0.1.3.0")]

public partial class App : Application
{
    const string BuildVersion = UpdateService.CurrentVersion;
    const string SignalPrefix = @"Local\WrakeebDualSpotify.";
    static readonly string PreferencesFile = Path.Combine(Engine.Data, "Preferences.txt");
    public static bool SecondTray = true;
    bool trayOnClose = true, exiting, busy, refreshing;
    string mode = "Dark";
    Window window;
    Grid shell;
    ColumnDefinition railColumn;
    TextBlock brand;
    ContentControl content;
    Toast toast;
    ManagerTray tray;
    SpotifyTaskbarIdentity taskbar;
    DispatcherTimer timer;
    ProfileCard mainCard, secondCard;
    ProcessDetails mainDetails, secondDetails;
    readonly List<NavItem> navigation = new List<NavItem>();
    int[][] last = { new int[0], new int[0] };

    [STAThread]
    public static int Main(string[] args)
    {
        if (!UpdatePackage.RuntimeFilesPresent(Engine.Home))
        {
            MessageBox.Show("Extract the entire Dual Spotify ZIP into a writable folder and keep its files together.", "Complete app folder required");
            return 1;
        }
        bool direct = args.Contains("--second");
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
                (direct ? second : show).Set();
                return 0;
            }
            SpotifyTaskbarIdentity.SetManagerIdentity();
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var showWait = ThreadPool.RegisterWaitForSingleObject(show, (s, t) => app.Dispatcher.BeginInvoke(new Action(app.ShowManager)), null, -1, false);
            var secondWait = ThreadPool.RegisterWaitForSingleObject(second, (s, t) => app.Dispatcher.BeginInvoke(new Action(app.OpenSecond)), null, -1, false);
            app.Startup += (s, e) => app.Start(direct);
            try { return app.Run(); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Dual Spotify");
                return 1;
            }
            finally { showWait.Unregister(null); secondWait.Unregister(null); }
        }
    }

    void Start(bool direct)
    {
        LoadPreferences();
        Theme.Apply(mode);
        Theme.InitStyles();
        Resources.MergedDictionaries.Add(Theme.Resources);
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
            if (trayOnClose)
                window.Hide();
            else
                ExitManager();
        };
        ShowPage("Profiles");
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += async (s, e) => await Refresh();
        timer.Start();
        if (direct)
            OpenSecond();
        else
            ShowManager();
        RefreshNow();
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

    void ExitManager()
    {
        exiting = true;
        timer.Stop();
        tray.Dispose();
        taskbar.Dispose();
        Shutdown();
    }

    async Task Refresh()
    {
        if (exiting || refreshing || busy)
            return;
        refreshing = true;
        try
        {
            var snapshot = await Task.Run(() => Engine.Snapshot());
            if (!exiting && !busy)
            {
                last = snapshot;
                UpdateProfiles();
            }
        }
        catch (Exception ex) { if (!exiting && window.IsVisible) toast.Show(ex.Message); }
        finally { refreshing = false; }
    }

    void OpenMain() { RunProfile(false, true); }
    void OpenSecond() { RunProfile(true, true); }
    void QuitSecond() { RunProfile(true, false); }

    async void RunProfile(bool second, bool opening)
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
                    Engine.Open(second);
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
        try { taskbar.Update(secondProcesses, true); }
        finally { foreach (var process in secondProcesses) process.Dispose(); }
        if (mainCard == null)
            return;
        for (int i = 0; i < 2; i++)
        {
            var card = i == 0 ? mainCard : secondCard;
            var details = i == 0 ? mainDetails : secondDetails;
            var processes = last[i].Select(Engine.TryProcess).Where(p => p != null).ToArray();
            try
            {
                card.Update(processes, i == 0 || Directory.Exists(Engine.SecondProfile));
                details.Update(PerformanceSnapshot.Capture(processes));
            }
            finally { foreach (var process in processes) process.Dispose(); }
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
        foreach (var item in navigation)
            item.Active(item.Page == page);
        mainCard = secondCard = null;
        content.Content = page == "Profiles" ? Profiles() : page == "Settings" ? Settings() :
            page == "Files" ? Files() : page == "Updates" ? Updates() : About();
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
        body.Children.Add(new Expander { Header = "Process details", IsExpanded = true, Content = columns, Margin = new Thickness(0, 12, 0, 0) });
        var hint = Text("Metrics cover each profile’s main process.", 12, "text.muted");
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

    FrameworkElement Settings()
    {
        var body = Page("Settings", "Choose how Dual Spotify looks and behaves.");
        var appearance = Stack();
        var themePicker = new ComboBox { MinWidth = 130, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        foreach (string option in new[] { "Dark", "Light", "System" })
            themePicker.Items.Add(option);
        themePicker.SelectedItem = mode;
        Ui.Name(themePicker, "Appearance");
        themePicker.SelectionChanged += (s, e) => Safe(() => { mode = (string)themePicker.SelectedItem; Theme.Apply(mode); SavePreferences(); });
        appearance.Children.Add(Text("Appearance", 14, "text.primary", true));
        appearance.Children.Add(themePicker);
        body.Children.Add(Group(appearance));
        var behavior = Stack();
        behavior.Children.Add(Choice("Close to tray", "Keep the dashboard available from the notification area.", trayOnClose, value => trayOnClose = value));
        behavior.Children.Add(Ui.Divider());
        behavior.Children.Add(Choice("Second Spotify tray icon", "Show a second profile shortcut while it is running.", SecondTray, value => SecondTray = value));
        body.Children.Add(Group(behavior));
        var install = Stack();
        install.Children.Add(Text("Spotify installation", Theme.TitleSize, "text.primary", true));
        install.Children.Add(Text(Engine.Spotify, Theme.NoteSize, "text.secondary"));
        install.Children.Add(ActionButton("Locate Spotify…", LocateSpotify));
        body.Children.Add(Group(install));
        body.Children.Add(ActionButton("Add Second Spotify desktop shortcut", () =>
        {
            CreateShortcut();
            toast.Show("Second Spotify shortcut added.");
        }));
        return body;
    }

    FrameworkElement Choice(string title, string copy, bool selected, Action<bool> change)
    {
        var row = new SettingRow(title, copy);
        row.Toggle.IsChecked = selected;
        row.Toggle.Checked += (s, e) => SaveChoice(row.Toggle, change);
        row.Toggle.Unchecked += (s, e) => SaveChoice(row.Toggle, change);
        return row;
    }

    void SaveChoice(ToggleSwitch toggle, Action<bool> change)
    {
        Safe(() => { change(toggle.IsChecked == true); SavePreferences(); UpdateProfiles(); });
    }

    FrameworkElement Files()
    {
        var body = Page("Files", "View your player and profile locations.");
        body.Children.Add(PathCard("Official Spotify client", Engine.Spotify, Path.GetDirectoryName(Engine.Spotify)));
        body.Children.Add(PathCard("Second Spotify profile", Engine.SecondProfile, Engine.SecondProfile));
        body.Children.Add(PathCard("Dual Spotify folder", Engine.Home, Engine.Home));
        return body;
    }

    FrameworkElement PathCard(string name, string path, string folder)
    {
        var panel = Stack();
        panel.Children.Add(Text(name, 14, "text.primary", true));
        panel.Children.Add(Text(path, 12, "text.secondary"));
        var button = ActionButton("Open folder", () => OpenPath(folder));
        button.IsEnabled = Directory.Exists(folder);
        panel.Children.Add(button);
        return Group(panel);
    }

    void LocateSpotify()
    {
        if (busy || Engine.Snapshot()[1].Length > 0)
            throw new InvalidOperationException("Quit Second Spotify before changing the installation.");
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Spotify executable|Spotify.exe", CheckFileExists = true };
        if (picker.ShowDialog(window) != true)
            return;
        Engine.SelectSpotify(picker.FileName);
        ShowPage("Settings");
        toast.Show("Spotify installation saved.");
        RefreshNow();
    }

    FrameworkElement About()
    {
        var body = Page("About", "Dual Spotify");
        body.Children.Add(Group(Text("Open two accounts in the official Spotify desktop client. Sign in inside each Spotify window.", 14)));
        body.Children.Add(new CreatorCredit(OpenPath));
        return body;
    }

    void LoadPreferences()
    {
        if (!File.Exists(PreferencesFile))
            return;
        var lines = File.ReadAllLines(PreferencesFile);
        if (lines.Length != 3)
            return;
        if (new[] { "Dark", "Light", "System" }.Contains(lines[0]))
            mode = lines[0];
        bool.TryParse(lines[1], out trayOnClose);
        bool.TryParse(lines[2], out SecondTray);
    }

    void SavePreferences()
    {
        Directory.CreateDirectory(Engine.Data);
        var staging = PreferencesFile + ".tmp";
        File.WriteAllLines(staging, new[] { mode, trayOnClose.ToString(), SecondTray.ToString() });
        if (File.Exists(PreferencesFile))
            File.Replace(staging, PreferencesFile, null);
        else
            File.Move(staging, PreferencesFile);
    }

    static void CreateShortcut()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Second Spotify.lnk");
        var executable = Path.Combine(Engine.Home, "Dual Spotify.exe");
        var shellType = Type.GetTypeFromProgID("WScript.Shell", true);
        dynamic shell = Activator.CreateInstance(shellType);
        dynamic shortcut = shell.CreateShortcut(path);
        if (File.Exists(path) && !String.Equals((string)shortcut.TargetPath, executable, StringComparison.OrdinalIgnoreCase))
            throw new IOException("A different Second Spotify shortcut already exists on your desktop.");
        shortcut.TargetPath = executable;
        shortcut.Arguments = "--second";
        shortcut.WorkingDirectory = Engine.Home;
        shortcut.IconLocation = Path.Combine(Engine.Home, "Assets", SpotifyTaskbarIdentity.SecondIconFile) + ",0";
        shortcut.Save();
        SpotifyTaskbarIdentity.SetShortcut(path);
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
