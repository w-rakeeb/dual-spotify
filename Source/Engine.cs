using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

static class Engine
{
    public static readonly string Home = Path.GetDirectoryName(typeof(Engine).Assembly.Location);
    public static readonly string Data = Path.Combine(Home, "Data");
    public static readonly string DefaultProfile = Path.Combine(Data, "Profiles", "Second Spotify");
    static readonly string secondProfile = ReadProfile();
    public static string SecondProfile { get { return secondProfile; } }
    public static string SecondCache { get { return Path.Combine(SecondProfile, "Cache"); } }
    static readonly string SpotifyPathFile = Path.Combine(Data, "SpotifyPath.txt");
    static readonly int Session = Process.GetCurrentProcess().SessionId;
    static string spotify = ReadSpotifyPath();
    static bool storeAvailable = SpotifyInstallation.StoreInstalled();
    static string storeFolder = SpotifyInstallation.FindPackageFolder(SpotifyInstallation.StoreFamily);
    public static bool StoreAvailable { get { return storeAvailable; } }
    public static string StoreFolder { get { return storeFolder; } }
    static string StoreExecutable { get { return StoreFolder == null ? null : Path.Combine(StoreFolder, "Spotify.exe"); } }
    public static string Spotify { get { return spotify; } }
    public static bool DesktopAvailable { get { return File.Exists(Spotify); } }
    public static bool CanOpen { get { return DesktopAvailable || StoreAvailable; } }
    public const string SecondTag = "--mu=WrakeebDualSpotifyProbeA";

    static string ReadProfile()
    {
        var file = Path.Combine(Data, "SecondProfilePath.txt");
        if (!File.Exists(file))
            return DefaultProfile;
        var value = File.ReadAllText(file).Trim();
        if (!Path.IsPathRooted(value))
            throw new IOException("The saved Second Spotify storage location is invalid.");
        return Path.GetFullPath(value);
    }

    static string ReadSpotifyPath()
    {
        var selected = File.Exists(SpotifyPathFile) ? File.ReadAllText(SpotifyPathFile).Trim() : null;
        return SpotifyInstallation.FindStandalone(selected, Session);
    }

    public static void DetectInstallations()
    {
        spotify = ReadSpotifyPath();
        storeAvailable = SpotifyInstallation.StoreInstalled();
        storeFolder = SpotifyInstallation.FindPackageFolder(SpotifyInstallation.StoreFamily);
    }

    public static void SelectSpotify(string path)
    {
        var selected = Path.GetFullPath(path);
        if (!SpotifyInstallation.IsStandalone(selected))
            throw new IOException("Choose the Spotify.exe from a standalone Spotify installation.");
        WriteDataText("SpotifyPath.txt", selected);
        spotify = selected;
    }

    public static void WriteDataText(string file, string value)
    {
        Directory.CreateDirectory(Data);
        var target = Path.Combine(Data, file);
        var staging = target + ".tmp";
        File.WriteAllText(staging, value);
        if (File.Exists(target))
            File.Replace(staging, target, null);
        else
            File.Move(staging, target);
    }

    public static string SecondArguments(string profile)
    {
        // shortcut: this switch is specific to the native Spotify build, reverify isolation after Spotify updates.
        return SecondTag + " --cache-path=\"" + profile + "\" --disable-desktop-launcher";
    }

    public static int Classify(string[] arguments, string profile)
    {
        if (arguments.Skip(1).Any(v => v.StartsWith("--type=", StringComparison.OrdinalIgnoreCase)))
            return -1;
        var tags = arguments.Skip(1).Where(v => v.StartsWith("--mu=", StringComparison.OrdinalIgnoreCase)).ToArray();
        var caches = arguments.Skip(1).Where(v => v.StartsWith("--cache-path=", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (tags.Length == 0 && caches.Length == 0)
            return 0;
        return tags.Length == 1 && tags[0] == SecondTag && caches.Length == 1 &&
            String.Equals(caches[0], "--cache-path=" + profile, StringComparison.OrdinalIgnoreCase) ? 1 : -1;
    }

    public static int[][] Snapshot()
    {
        int[][] children;
        return Scan(out children);
    }

    public static int[][] Scan(out int[][] children)
    {
        var instances = new[] { new List<int>(), new List<int>() };
        var helpers = new List<KeyValuePair<int, int>>();
        var managed = ManagedSecond.Player();
        if (managed > 0)
            instances[1].Add(managed);
        using (var search = new ManagementObjectSearcher(
            "SELECT ProcessId, ParentProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name='Spotify.exe' AND SessionId=" + Session))
        using (var results = search.Get())
        {
            foreach (ManagementObject current in results)
            using (current)
            {
                var command = current["CommandLine"] as string;
                var pid = Convert.ToInt32(current["ProcessId"]);
                var path = current["ExecutablePath"] as string;
                var desktop = String.Equals(path, Spotify, StringComparison.OrdinalIgnoreCase);
                if (pid == managed || !SpotifyInstallation.IsCurrentUserProcess(pid))
                    continue;
                var packagePath = StoreExecutable;
                if (!desktop && (!StoreAvailable || !(packagePath != null && String.Equals(path, packagePath, StringComparison.OrdinalIgnoreCase)) &&
                    !SpotifyInstallation.IsStoreProcess(pid)))
                    continue;
                if (String.IsNullOrEmpty(command))
                {
                    if (!desktop)
                        using (var process = TryProcess(pid))
                            if (process != null && process.MainWindowHandle != IntPtr.Zero)
                                instances[0].Add(pid);
                    continue;
                }
                var args = Split(command);
                var kind = Classify(args, SecondCache);
                if (kind >= 0)
                    instances[kind].Add(pid);
                else if (args.Skip(1).Any(v => v.StartsWith("--type=", StringComparison.OrdinalIgnoreCase)))
                    helpers.Add(new KeyValuePair<int, int>(Convert.ToInt32(current["ParentProcessId"]), pid));
            }
        }
        var byParent = helpers.ToLookup(p => p.Key, p => p.Value);
        children = instances.Select(roots =>
        {
            var found = new HashSet<int>(roots);
            var queue = new Queue<int>(roots);
            while (queue.Count > 0)
                foreach (var child in byParent[queue.Dequeue()])
                    if (found.Add(child))
                        queue.Enqueue(child);
            return found.Except(roots).ToArray();
        }).ToArray();
        return instances.Select(x => x.ToArray()).ToArray();
    }

    internal static string[] Split(string command)
    {
        int count;
        IntPtr pointer = CommandLineToArgvW(command, out count);
        if (pointer == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception();
        try
        {
            var values = new string[count];
            for (var i = 0; i < count; i++)
                values[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size));
            return values;
        }
        finally { LocalFree(pointer); }
    }

    public static void Open(bool second, bool minimized = false)
    {
        if (second)
        {
            OpenSecond(minimized, null);
            return;
        }
        var instances = Snapshot()[0];
        if (instances.Length > 0)
        {
            if (minimized)
                return;
            foreach (int id in instances)
                using (var process = TryProcess(id))
                    if (process != null && process.MainWindowHandle != IntPtr.Zero)
                    {
                        var handle = process.MainWindowHandle;
                        FocusWindow(handle);
                        return;
                    }
            if (StoreAvailable && instances.Any(SpotifyInstallation.IsStoreProcess))
            {
                SpotifyInstallation.OpenStore();
                return;
            }
        }
        if (!DesktopAvailable)
        {
            DetectInstallations();
            if (!DesktopAvailable && StoreAvailable)
            {
                SpotifyInstallation.OpenStore();
                return;
            }
            if (!DesktopAvailable)
                throw new FileNotFoundException("Use Locate Spotify to choose an installed Spotify.exe.");
        }
        using (Process.Start(new ProcessStartInfo(Spotify) { UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Spotify) })) { }
    }

    public static void OpenSecond(bool minimized, Action<string> progress)
    {
        var active = Snapshot()[1];
        var independent = ManagedSecond.Player();
        if (active.Any(id => id != independent))
            throw new IOException("Quit the old Second Spotify window before starting the independent profile.");
        ManagedSecond.OpenWithProgress(minimized, progress);
    }

    public static void FocusWindow(IntPtr handle)
    {
        ShowWindowAsync(handle, IsIconic(handle) ? 9 : 5);
        SetForegroundWindow(handle);
    }

    public static void Quit(bool second)
    {
        var independent = second ? ManagedSecond.Player() : 0;
        if (second && ManagedSecond.Configured)
            ManagedSecond.Quit();
        foreach (int id in Snapshot()[second ? 1 : 0])
            if (id != independent)
            using (var process = TryProcess(id))
            {
                if (process == null)
                    continue;
                if (process.HasExited)
                    continue;
                // Spotify's graceful close switch can target a different instance. Request WM_CLOSE on this window.
                if (!process.CloseMainWindow())
                    throw new InvalidOperationException("Spotify has no visible window. Open the profile window before quitting.");
                if (!process.WaitForExit(2200))
                    throw new InvalidOperationException("Spotify stayed open in the background. Use Quit Spotify from that client’s tray menu.");
            }
    }

    public static Process TryProcess(int id)
    {
        try { return Process.GetProcessById(id); }
        catch (ArgumentException) { return null; }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr handle);
    [DllImport("user32.dll")]
    static extern bool ShowWindowAsync(IntPtr handle, int command);
    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr handle);
}
