using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

static class Engine
{
    public static readonly string Home = Path.GetDirectoryName(typeof(Engine).Assembly.Location);
    public static readonly string Data = Path.Combine(Home, "Data");
    public static readonly string DefaultProfile = Path.Combine(Data, "Profiles", "Second Spotify");
    static string secondProfile = ReadProfile();
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

    public static void MoveProfile(string path)
    {
        var target = ProfileStorage.CopyVerified(SecondProfile, path, new[] {
            Home, Path.GetDirectoryName(Spotify), Path.GetDirectoryName(SpotifyInstallation.DefaultPath),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spotify"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", SpotifyInstallation.StoreFamily)
        });
        WriteDataText("SecondProfilePath.txt", target);
        secondProfile = target;
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
        var instances = Snapshot()[second ? 1 : 0];
        if (instances.Length > 0)
        {
            if (minimized)
                return;
            foreach (int id in instances)
                using (var process = TryProcess(id))
                    if (process != null && process.MainWindowHandle != IntPtr.Zero)
                    {
                        var handle = process.MainWindowHandle;
                        ShowWindowAsync(handle, IsIconic(handle) ? 9 : 5);
                        SetForegroundWindow(handle);
                        return;
                    }
            if (second)
                throw new InvalidOperationException("Second Spotify is running without a visible window. Open it from Spotify’s tray icon or wait for it to finish starting.");
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
                if (second)
                    OpenStoreSecond(minimized);
                else
                    SpotifyInstallation.OpenStore();
                return;
            }
            if (!DesktopAvailable)
                throw new FileNotFoundException("Use Locate Spotify to choose an installed Spotify.exe.");
        }
        var arguments = second ? SecondArguments(SecondCache) : "";
        if (minimized)
            arguments += " --minimized";
        StartPlayer(Spotify, arguments, second, Path.GetDirectoryName(Spotify));
    }

    static void StartPlayer(string executable, string arguments, bool second, string workingDirectory)
    {
        var start = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        if (second)
        {
            Directory.CreateDirectory(SecondCache);
            start.EnvironmentVariables["APPDATA"] = Directory.CreateDirectory(Path.Combine(SecondProfile, "Roaming")).FullName;
            start.EnvironmentVariables["LOCALAPPDATA"] = Directory.CreateDirectory(Path.Combine(SecondProfile, "Local")).FullName;
        }
        using (Process.Start(start)) { }
    }

    static void OpenStoreSecond(bool minimized)
    {
        var arguments = SecondArguments(SecondCache) + (minimized ? " --minimized" : "");
        Directory.CreateDirectory(SecondCache);
        var launched = false;
        foreach (var path in new[] { StoreExecutable, SpotifyInstallation.StoreAlias() })
        {
            if (path == null)
                continue;
            try
            {
                StartPlayer(path, arguments, true, SecondProfile);
                launched = true;
                break;
            }
            catch (Win32Exception) { }
            catch (UnauthorizedAccessException) { }
        }
        if (!launched)
            SpotifyInstallation.OpenStore(arguments);
        VerifyStoreSecond();
    }

    static void VerifyStoreSecond()
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(20))
        {
            int[][] children;
            var roots = Scan(out children)[1];
            if (roots.Length > 0 && StoreRendererUsesCache(children[1]))
                return;
            Thread.Sleep(750);
        }
        // Only the root with our exact tag and cache path can be closed here.
        try { Quit(true); }
        catch (InvalidOperationException) { }
        throw new IOException("This Store build did not open a separate Spotify window. Use the standalone client for Second Spotify.");
    }

    static bool StoreRendererUsesCache(int[] helpers)
    {
        if (helpers.Length == 0)
            return false;
        using (var search = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE " +
            String.Join(" OR ", helpers.Select(id => "ProcessId=" + id))))
        using (var results = search.Get())
            foreach (ManagementObject helper in results)
                using (helper)
                {
                    if (UsesStoreCache(helper["CommandLine"] as string, SecondCache))
                        return true;
                }
        return false;
    }

    public static bool UsesStoreCache(string command, string cache)
    {
        if (String.IsNullOrEmpty(command))
            return false;
        var args = Split(command);
        return args.Contains("--type=renderer") &&
            args.Contains("--user-data-dir=" + cache, StringComparer.OrdinalIgnoreCase);
    }

    public static void Quit(bool second)
    {
        foreach (int id in Snapshot()[second ? 1 : 0])
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
