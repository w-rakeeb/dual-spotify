using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

static class WindowsProfileAccount
{
    const string UserList = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList";
    const string Comment = "Second Spotify desktop profile";

    public static void VerifyStandard(string name, string sid, string owner)
    {
        var id = new SecurityIdentifier(sid ?? "");
        SecurityIdentifier actual;
        try { actual = (SecurityIdentifier)new NTAccount(Environment.MachineName, name).Translate(typeof(SecurityIdentifier)); }
        catch (IdentityNotMappedException) { throw new IOException("The saved Windows account no longer exists."); }
        if (!id.IsAccountSid() || id != actual || sid == owner)
            throw new IOException("The saved Windows account identity does not match Windows.");
        IntPtr buffer;
        int count, total;
        Check(NetUserGetLocalGroups(null, name, 0, 1, out buffer, -1, out count, out total));
        try
        {
            var admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            for (int i = 0; i < count; i++)
            {
                var group = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, i * IntPtr.Size));
                if (admin == (SecurityIdentifier)new NTAccount(group).Translate(typeof(SecurityIdentifier)))
                    throw new IOException("Second Spotify requires a standard Windows account.");
            }
        }
        finally { NetApiBufferFree(buffer); }
    }

    public static string DisplayName(string name)
    {
        var info = ReadInfo(name);
        return String.IsNullOrWhiteSpace(info.FullName) ? name : info.FullName;
    }

    public static bool Hidden(string name)
    {
        var value = Registry.GetValue(@"HKEY_LOCAL_MACHINE\" + UserList, name, null);
        return value is int && (int)value == 0;
    }

    public static string ValidateName(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > 80 || name.Any(Char.IsControl))
            throw new ArgumentException("Enter a display name between 1 and 80 characters.");
        return name;
    }

    public static void Change(ManagedSecond.Account account, string operation, string name = "", bool hidden = false)
    {
        if (account.SteamHome != null) throw new IOException("Manage the shared Windows account in Dual Steam.");
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(name)) + (hidden ? ".1" : ".0");
        var arguments = "--spotify-account " + operation + " " + account.Name + " " + account.Sid + " " + account.Owner + " " + encoded;
        using (var child = Process.Start(new ProcessStartInfo(Path.Combine(Engine.Home, "Dual Spotify.exe"), arguments)
            { UseShellExecute = true, Verb = "runas" }))
        {
            if (!child.WaitForExit(90000))
                throw new IOException("Windows is still working on the account change.");
            if (child.ExitCode != 0)
                throw new IOException(child.ExitCode == 3 ? "The Windows profile is still loaded. Quit Second Spotify and try again." :
                    "Windows could not complete the account change. Verify the setup and try again.");
        }
        if (operation == "appearance" && (DisplayName(account.Name) != name || Hidden(account.Name) != hidden))
            throw new IOException("Windows did not confirm the requested account appearance.");
    }

    public static int Apply(string operation, string user, string sid, string owner, string argument)
    {
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator) ||
            !ManagedSecond.ValidName(user) || (operation != "appearance" && operation != "remove"))
            return 2;
        try
        {
            VerifyStandard(user, sid, owner);
            if (ReadInfo(user).Comment != Comment ||
                File.ReadAllText(Path.Combine(ManagedSecond.Root(user), "identity.txt")).Trim() != sid)
                return 2;
            if (operation == "remove")
            {
                using (var profile = new ManagementObject("Win32_UserProfile.SID='" + sid + "'"))
                {
                    try
                    {
                        if ((bool)profile["Loaded"]) return 3;
                        if (!DeleteProfile(sid, null, null)) throw new Win32Exception();
                    }
                    catch (ManagementException ex) { if (ex.ErrorCode != ManagementStatus.NotFound) throw; }
                }
                Check(NetUserDel(null, user));
                using (var key = Registry.LocalMachine.OpenSubKey(UserList, true))
                    if (key != null) key.DeleteValue(user, false);
                return 0;
            }
            var parts = argument.Split('.');
            if (parts.Length != 2 || (parts[1] != "0" && parts[1] != "1")) return 2;
            var full = ValidateName(Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])));
            var previous = ReadInfo(user).FullName;
            using (var key = Registry.LocalMachine.CreateSubKey(UserList))
            {
                SetName(user, full);
                try
                {
                    if (parts[1] == "1") key.SetValue(user, 0, RegistryValueKind.DWord);
                    else key.DeleteValue(user, false);
                }
                catch { SetName(user, previous); throw; }
            }
            return 0;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            return 2;
        }
    }

    static Info ReadInfo(string name)
    {
        IntPtr buffer;
        Check(NetUserGetInfo(null, name, 10, out buffer));
        try { return (Info)Marshal.PtrToStructure(buffer, typeof(Info)); }
        finally { NetApiBufferFree(buffer); }
    }

    static void SetName(string user, string name)
    {
        var info = new NameInfo { Name = name };
        int invalid;
        Check(NetUserSetInfo(null, user, 1011, ref info, out invalid));
    }

    static void Check(int code) { if (code != 0) throw new Win32Exception(code); }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Info { public string Name, Comment, UserComment, FullName; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NameInfo { public string Name; }
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] static extern int NetUserGetInfo(string server, string user, int level, out IntPtr buffer);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] static extern int NetUserGetLocalGroups(string server, string user, int level, int flags, out IntPtr buffer, int max, out int entriesRead, out int totalEntries);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] static extern int NetUserSetInfo(string server, string user, int level, ref NameInfo info, out int invalid);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] static extern int NetUserDel(string server, string user);
    [DllImport("netapi32.dll")] static extern int NetApiBufferFree(IntPtr buffer);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool DeleteProfile(string sid, string path, string computer);
}
