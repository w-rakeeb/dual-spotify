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
    static string secondProfile = ReadProfile();
    public static string SecondProfile { get { return secondProfile; } }
    public static string SecondCache { get { return Path.Combine(SecondProfile, "Cache"); } }
    static readonly string DefaultSpotify = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Spotify\Spotify.exe");
    static readonly string SpotifyPathFile = Path.Combine(Data, "SpotifyPath.txt");
    static string spotify = ReadSpotifyPath();
    public static string Spotify { get { return spotify; } }
    public const string SecondTag = "--mu=WrakeebDualSpotifyProbeA";
    static readonly int Session = Process.GetCurrentProcess().SessionId;

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
            Home, Path.GetDirectoryName(Spotify),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spotify")
        });
        WriteDataText("SecondProfilePath.txt", target);
        secondProfile = target;
    }

    static string ReadSpotifyPath()
    {
        if (!File.Exists(SpotifyPathFile))
            return DefaultSpotify;
        var selected = File.ReadAllText(SpotifyPathFile).Trim();
        return Path.IsPathRooted(selected) && File.Exists(selected) ? selected : DefaultSpotify;
    }

    public static void SelectSpotify(string path)
    {
        var selected = Path.GetFullPath(path);
        if (!File.Exists(selected) || !String.Equals(Path.GetFileName(selected), "Spotify.exe", StringComparison.OrdinalIgnoreCase) ||
            FileVersionInfo.GetVersionInfo(selected).ProductName != "Spotify")
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
                if (String.IsNullOrEmpty(command) ||
                    !String.Equals(current["ExecutablePath"] as string, Spotify, StringComparison.OrdinalIgnoreCase))
                    continue;
                var args = Split(command);
                var pid = Convert.ToInt32(current["ProcessId"]);
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
        }
        if (!File.Exists(Spotify))
            throw new FileNotFoundException("Install the Windows desktop edition from spotify.com/download/windows, then try again.");
        var arguments = second ? SecondArguments(SecondCache) : "";
        if (minimized)
            arguments += " --minimized";
        var start = new ProcessStartInfo(Spotify, arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Spotify)
        };
        if (second)
        {
            Directory.CreateDirectory(SecondCache);
            start.EnvironmentVariables["APPDATA"] = Directory.CreateDirectory(Path.Combine(SecondProfile, "Roaming")).FullName;
            start.EnvironmentVariables["LOCALAPPDATA"] = Directory.CreateDirectory(Path.Combine(SecondProfile, "Local")).FullName;
        }
        using (Process.Start(start)) { }
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
