using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

static class SpotifyInstallation
{
    public const string StoreFamily = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0";
    const string StoreApp = StoreFamily + "!Spotify";
    public static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Spotify\Spotify.exe");
    static readonly string CurrentSid = WindowsIdentity.GetCurrent().User.Value;

    public static bool IsCurrentUserProcess(int pid)
    {
        var process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero)
            return false;
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(process, 8, out token))
                return false;
            using (var identity = new WindowsIdentity(token))
                return identity.User.Value == CurrentSid;
        }
        catch (ArgumentException) { return false; }
        catch (Win32Exception) { return false; }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
            CloseHandle(process);
        }
    }

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
                "SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name='Spotify.exe' AND SessionId=" + session))
            using (var results = search.Get())
                foreach (ManagementObject process in results)
                    using (process)
                    {
                        var path = process["ExecutablePath"] as string;
                        var command = process["CommandLine"] as string;
                        if (IsCurrentUserProcess(Convert.ToInt32(process["ProcessId"])) &&
                            !String.IsNullOrEmpty(command) && command.IndexOf("--mu=", StringComparison.OrdinalIgnoreCase) < 0 &&
                            IsStandalone(path))
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

    public static string FindPackageFolder(string family)
    {
        uint count = 0, length = 0;
        if (GetPackagesByPackageFamily(family, ref count, IntPtr.Zero, ref length, IntPtr.Zero) != 122 ||
            count == 0 || length == 0)
            return null;

        IntPtr names = IntPtr.Zero, buffer = IntPtr.Zero;
        try
        {
            names = Marshal.AllocHGlobal(checked((int)count * IntPtr.Size));
            buffer = Marshal.AllocHGlobal(checked((int)length * sizeof(char)));
            if (GetPackagesByPackageFamily(family, ref count, names, ref length, buffer) != 0)
                return null;
            for (int index = 0; index < count; index++)
            {
                var fullName = Marshal.PtrToStringUni(Marshal.ReadIntPtr(names, index * IntPtr.Size));
                uint pathLength = 0;
                if (GetPackagePathByFullName(fullName, ref pathLength, null) != 122 || pathLength == 0)
                    continue;
                var path = new StringBuilder(checked((int)pathLength));
                if (GetPackagePathByFullName(fullName, ref pathLength, path) == 0)
                    return path.ToString();
            }
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(names);
        }
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
    static extern int GetPackagePathByFullName(string fullName, ref uint pathLength, StringBuilder path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int GetPackageFamilyName(IntPtr process, ref uint length, StringBuilder family);
    [DllImport("kernel32.dll")]
    static extern IntPtr OpenProcess(uint rights, bool inherit, int id);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr process, uint rights, out IntPtr token);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);
}
