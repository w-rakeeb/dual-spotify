using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

static class SpotifyInstallation
{
    public const string StoreFamily = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0";
    const string StoreApp = StoreFamily + "!Spotify";
    public static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Spotify\Spotify.exe");

    public static bool IsStandalone(string path)
    {
        try
        {
            if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !File.Exists(path) ||
                !String.Equals(Path.GetFileName(path), "Spotify.exe", StringComparison.OrdinalIgnoreCase) ||
                path.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return String.Equals(FileVersionInfo.GetVersionInfo(path).ProductName, "Spotify", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (Win32Exception) { return false; }
    }

    public static string FindStandalone(string selected, int session)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[] { selected, DefaultPath,
            Path.Combine(local, @"Spotify\Spotify.exe"), Path.Combine(local, @"Programs\Spotify\Spotify.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Spotify\Spotify.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Spotify\Spotify.exe")
        };
        var found = candidates.Concat(RegisteredPaths()).FirstOrDefault(IsStandalone);
        if (found != null)
            return found;

        try
        {
            using (var search = new ManagementObjectSearcher(
                "SELECT ExecutablePath FROM Win32_Process WHERE Name='Spotify.exe' AND SessionId=" + session))
            using (var results = search.Get())
                foreach (ManagementObject process in results)
                    using (process)
                    {
                        var path = process["ExecutablePath"] as string;
                        if (IsStandalone(path))
                            return path;
                    }
        }
        catch (ManagementException) { }
        catch (COMException) { }
        catch (UnauthorizedAccessException) { }
        return DefaultPath;
    }

    static IEnumerable<string> RegisteredPaths()
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                using (var root = RegistryKey.OpenBaseKey(hive, view))
                {
                    using (var app = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\Spotify.exe"))
                        if (app != null)
                            yield return CleanPath(app.GetValue(null) as string);
                    using (var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Spotify"))
                    {
                        var folder = uninstall == null ? null : CleanPath(uninstall.GetValue("InstallLocation") as string);
                        if (!String.IsNullOrWhiteSpace(folder))
                            yield return folder.TrimEnd('\\') + @"\Spotify.exe";
                    }
                }
    }

    static string CleanPath(string value)
    {
        return value == null ? null : Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
    }

    public static bool StoreInstalled()
    {
        uint count = 0, length = 0;
        int result = GetPackagesByPackageFamily(StoreFamily, ref count, IntPtr.Zero, ref length, IntPtr.Zero);
        return (result == 0 || result == 122) && count > 0;
    }

    public static string StoreAlias()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\WindowsApps", StoreFamily, "Spotify.exe");
        try { return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ? path : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static bool IsStoreProcess(int id)
    {
        var handle = OpenProcess(0x1000, false, id);
        if (handle == IntPtr.Zero)
            return false;
        try
        {
            uint length = 128;
            var family = new StringBuilder((int)length);
            return GetPackageFamilyName(handle, ref length, family) == 0 &&
                String.Equals(family.ToString(), StoreFamily, StringComparison.OrdinalIgnoreCase);
        }
        finally { CloseHandle(handle); }
    }

    public static void OpenStore()
    {
        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        try
        {
            uint pid;
            Marshal.ThrowExceptionForHR(manager.ActivateApplication(StoreApp, null, 0, out pid));
        }
        finally { Marshal.FinalReleaseComObject(manager); }
    }

    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    class ApplicationActivationManager { }
    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string app,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint pid);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr names, ref uint length, IntPtr buffer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int GetPackageFamilyName(IntPtr process, ref uint length, StringBuilder family);
    [DllImport("kernel32.dll")]
    static extern IntPtr OpenProcess(uint rights, bool inherit, int id);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);
}
