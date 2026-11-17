using System;
using System.IO;
using System.ComponentModel;
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
        if (parent == null) return null;
        var candidate = Path.Combine(parent, "Dual Steam");
        try { return Read(candidate).SteamHome; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (Win32Exception) { return null; }
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
            throw new IOException("Choose the folder that contains your configured Dual Steam launcher.");
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
}
