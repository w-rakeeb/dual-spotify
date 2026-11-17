using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Security;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

static class SteamWindowsProfile
{
    const string Native = @"Data\Native";
    const string ProfileList = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\";
    const string Credential = "password.dpapi";

    public static string Detect()
    {
        var parent = Path.GetDirectoryName(Engine.Home);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var downloads = KnownDownloads() ?? Path.Combine(user, "Downloads");
        var places = new[] { Engine.Home, parent, desktop, documents, downloads, user,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
        var links = new[] { desktop, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) };
        foreach (var candidate in RunningHomes().Concat(ShortcutHomes(links)).Concat(CandidateHomes(places)))
        {
            if (!found.Add(candidate)) continue;
            try { return Read(candidate).SteamHome; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            catch (IdentityNotMappedException) { }
        }
        return null;
    }

    static IEnumerable<string> RunningHomes()
    {
        foreach (var process in Process.GetProcessesByName("Dual Steam"))
        using (process)
        {
            string home;
            try { home = Path.GetDirectoryName(process.MainModule.FileName); }
            catch (Win32Exception) { continue; }
            catch (InvalidOperationException) { continue; }
            yield return home;
        }
    }

    static IEnumerable<string> ShortcutHomes(string[] roots)
    {
        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var folder in new[] { root }.Concat(Folders(root, "*Steam*")))
                foreach (var name in new[] { "Dual Steam.lnk", "Second Steam.lnk" })
                {
                    var path = Path.Combine(folder, name);
                    if (!File.Exists(path)) continue;
                    var home = ShortcutHome(path);
                    if (home != null) yield return home;
                }
    }

    public static string ShortcutHome(string path)
    {
        object shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            shortcut = ((dynamic)shell).CreateShortcut(path);
            string target = ((dynamic)shortcut).TargetPath;
            return String.Equals(Path.GetFileName(target), "Dual Steam.exe", StringComparison.OrdinalIgnoreCase) &&
                IsLocal(target) ? Path.GetDirectoryName(target) : null;
        }
        catch (COMException) { return null; }
        catch (ArgumentException) { return null; }
        finally
        {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
    }

    public static IEnumerable<string> CandidateHomes(string[] roots)
    {
        foreach (var root in roots.Where(IsLocal).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return root;
            foreach (var folder in Folders(root, "*Steam*"))
            {
                yield return folder;
                foreach (var nested in Folders(folder, "*Steam*"))
                    yield return nested;
            }
        }
    }

    static string[] Folders(string root, string pattern)
    {
        if (!IsLocal(root)) return new string[0];
        try
        {
            return Directory.EnumerateDirectories(root, pattern).Take(40)
                .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0).ToArray();
        }
        catch (IOException) { return new string[0]; }
        catch (UnauthorizedAccessException) { return new string[0]; }
    }

    static bool IsLocal(string path)
    {
        return !String.IsNullOrEmpty(path) && path.Length >= 3 && Char.IsLetter(path[0]) && path[1] == ':' && path[2] == '\\';
    }

    static string KnownDownloads()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        IntPtr path;
        if (SHGetKnownFolderPath(ref id, 0x4000, IntPtr.Zero, out path) != 0)
            return null;
        try { return Marshal.PtrToStringUni(path); }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    public static ManagedSecond.Account Read(string home)
    {
        home = Path.GetFullPath(home);
        var folder = Path.Combine(home, Native);
        var file = Path.Combine(folder, "account.json");
        var password = Path.Combine(folder, Credential);
        if (File.Exists(Path.Combine(home, @"Data\Removal\request.json")))
            throw new IOException("Dual Steam has a removal in progress.");
        if (!File.Exists(file) || !File.Exists(password))
            throw new IOException("Choose the folder containing Dual Steam.exe and finish Second Steam setup there first.");
        var record = new JavaScriptSerializer().Deserialize<SteamAccount>(ReadSmallFile(file));
        var owner = WindowsIdentity.GetCurrent().User.Value;
        if (record == null || record.IssuerSID != owner || String.IsNullOrEmpty(record.ManagedBy) ||
            !record.ManagedBy.StartsWith("Dual Steam", StringComparison.Ordinal) ||
            !System.Text.RegularExpressions.Regex.IsMatch(record.User ?? "", @"\A(DualSteamLocal|DualSteam_[a-f0-9]{8})\z"))
            throw new IOException("This Dual Steam profile belongs to a different setup.");
        WindowsProfileAccount.VerifyStandard(record.User, record.SID, owner);
        var registered = Registry.GetValue(ProfileList + record.SID, "ProfileImagePath", null) as string;
        if (String.IsNullOrWhiteSpace(registered) || String.IsNullOrWhiteSpace(record.Profile) ||
            !String.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(registered)),
                Path.GetFullPath(record.Profile), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Windows could not verify the registered Dual Steam profile folder.");
        return new ManagedSecond.Account { Name = record.User, Owner = owner, Sid = record.SID, SteamHome = home };
    }

    public static SecureString Password(string home)
    {
        var encrypted = ReadSmallFile(Path.Combine(home, Native, Credential)).Trim();
        if (encrypted.Length == 0 || encrypted.Length % 2 != 0)
            throw new IOException("Dual Steam’s saved Windows credential is unreadable.");
        var bytes = new byte[encrypted.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            if (!Byte.TryParse(encrypted.Substring(2 * i, 2), System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                throw new IOException("Dual Steam’s saved Windows credential is unreadable.");
        var plaintext = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        try
        {
            if (plaintext.Length == 0 || plaintext.Length % 2 != 0)
                throw new IOException("Dual Steam’s saved Windows credential is unreadable.");
            var secret = new SecureString();
            for (int i = 0; i < plaintext.Length; i += 2)
                secret.AppendChar((char)(plaintext[i] | plaintext[i + 1] << 8));
            secret.MakeReadOnly();
            return secret;
        }
        finally { Array.Clear(plaintext, 0, plaintext.Length); }
    }

    static string ReadSmallFile(string path)
    {
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > 16384)
            throw new IOException("Dual Steam setup metadata could not be verified.");
        return File.ReadAllText(path);
    }

    public sealed class SteamAccount
    {
        public string User { get; set; }
        public string SID { get; set; }
        public string Profile { get; set; }
        public string ManagedBy { get; set; }
        public string IssuerSID { get; set; }
    }

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, IntPtr token, out IntPtr path);
}
