using System;
using System.IO;
using System.Runtime.InteropServices;
using DualSpotifyNative;

static class DesktopShortcut
{
    static readonly string Executable = Path.Combine(Engine.Home, "Dual Spotify.exe");
    public static readonly string StartupFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Dual Spotify.lnk");

    public static bool StartsWithWindows
    {
        get { return File.Exists(StartupFile) && Edit(StartupFile, "--startup", false); }
    }

    public static void SetStartup(bool enabled)
    {
        if (enabled)
            Edit(StartupFile, "--startup", true);
        else if (StartsWithWindows)
            File.Delete(StartupFile);
    }

    public static void Repair()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Edit(Path.Combine(desktop, "Dual Spotify.lnk"), "", true);
        Edit(Path.Combine(desktop, "Second Spotify.lnk"), "--second", true);
    }

    static bool Edit(string path, string arguments, bool write)
    {
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
        object link = null;
        try
        {
            link = ((dynamic)shell).CreateShortcut(path);
            dynamic shortcut = link;
            var ours = String.Equals((string)shortcut.TargetPath, Executable, StringComparison.OrdinalIgnoreCase);
            if (!write)
                return ours && (string)shortcut.Arguments == arguments;
            if (File.Exists(path) && !ours)
                throw new IOException("A different shortcut already exists at " + Path.GetFileName(path) + ".");
            var second = arguments == "--second";
            shortcut.TargetPath = Executable;
            shortcut.Arguments = arguments;
            shortcut.WorkingDirectory = Engine.Home;
            shortcut.IconLocation = Path.Combine(Engine.Home, "Assets",
                second ? SpotifyTaskbarIdentity.SecondIconFile : "dual-spotify-green.ico") + ",0";
            shortcut.Save();
            SpotifyTaskbarIdentity.SetShortcut(path, second ? SpotifyTaskbarIdentity.SecondSpotifyId : SpotifyTaskbarIdentity.ManagerId);
            return true;
        }
        finally
        {
            if (link != null)
                Marshal.FinalReleaseComObject(link);
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
