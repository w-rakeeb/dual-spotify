using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

static class ManagedSecond
{
    const string Record = "SecondWindowsAccount.json";
    const string SharedRecord = "SharedWindowsAccount.json";
    const string CatalogRecord = "WindowsProfiles.json";
    const string Installer = "SpotifyFullSetupX64.exe";
    static readonly string SharedRoot = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    public static bool Configured { get { return SelectedAccount() != null; } }
    public static bool IsShared { get { var account = SelectedAccount(); return account != null && account.SteamHome != null; } }
    public sealed class Account
    {
        public string Name, Sid, Owner, Password, SteamHome, Folder;
        public bool Removed;
        public bool Active { get; set; } // Used when reading the v0.4.0 shared selection.
    }
    public sealed class Catalog
    {
        public List<Account> Accounts = new List<Account>();
        public string Selected;
    }

    internal static string Root(string name) { return Path.Combine(SharedRoot, name); }
    internal static bool ValidName(string name) { return Regex.IsMatch(name ?? "", @"\ADualSpotify_[a-f0-9]{8}\z"); }
    static string PlayerRoot(Account account) { return Root(account.Folder ?? account.Name); }
    static string UserSid { get { return WindowsIdentity.GetCurrent().User.Value; } }
    static string PipeName(string id) { return "DualSpotify.Provision." + id; }
    static Account CreateAccount()
    {
        var owner = new SecurityIdentifier(UserSid);
        var id = Guid.NewGuid().ToString("N");
        var name = "DualSpotify_" + id.Substring(0, 8);
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        Directory.CreateDirectory(Engine.Data);
        using (var pipe = new NamedPipeServerStream(PipeName(id), PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security))
        using (var elevated = Process.Start(new ProcessStartInfo(Path.Combine(Engine.Home, "Dual Spotify.exe"),
            "--create-second-user " + id + " " + owner.Value) { UseShellExecute = true, Verb = "runas" }))
        {
            var connection = pipe.BeginWaitForConnection(null, null);
            using (var wait = connection.AsyncWaitHandle)
            {
                if (!wait.WaitOne(35000))
                    throw new IOException("Windows did not finish creating the dedicated Spotify user.");
                pipe.EndWaitForConnection(connection);
            }
            using (var reader = new BinaryReader(pipe, Encoding.UTF8, true))
            using (var writer = new BinaryWriter(pipe, Encoding.UTF8, true))
            {
                if (!reader.ReadBoolean())
                    throw new IOException(reader.ReadString());
                var account = new Account { Name = name, Owner = owner.Value, Sid = reader.ReadString() };
                byte[] plaintext = Encoding.UTF8.GetBytes(reader.ReadString());
                try
                {
                    account.Password = Convert.ToBase64String(ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser));
                    SaveSelection(account);
                    writer.Write(true);
                    writer.Flush();
                    return account;
                }
                finally { Array.Clear(plaintext, 0, plaintext.Length); }
            }
        }
    }

    static Account ReadLegacy(string name)
    {
        var path = Path.Combine(Engine.Data, name);
        return File.Exists(path) ? new JavaScriptSerializer().Deserialize<Account>(File.ReadAllText(path)) : null;
    }

    static Catalog ReadCatalog()
    {
        var path = Path.Combine(Engine.Data, CatalogRecord);
        if (File.Exists(path))
        {
            var saved = new JavaScriptSerializer().Deserialize<Catalog>(File.ReadAllText(path));
            if (saved == null || saved.Accounts == null)
                throw new IOException("The saved Windows profile list could not be read.");
            return saved;
        }
        var catalog = new Catalog();
        var dedicated = ReadLegacy(Record);
        var shared = ReadLegacy(SharedRecord);
        if (dedicated != null) catalog.Accounts.Add(dedicated);
        if (shared != null) catalog.Accounts.Add(shared);
        catalog.Selected = shared != null && shared.Active ? shared.Sid : dedicated == null ? null : dedicated.Sid;
        return catalog;
    }

    static void SaveCatalog(Catalog catalog)
    {
        Engine.WriteDataText(CatalogRecord, new JavaScriptSerializer().Serialize(catalog));
    }

    public static Account[] Profiles() { return ReadCatalog().Accounts.Where(a => a != null && a.Owner == UserSid).ToArray(); }

    public static Account SelectedAccount()
    {
        var catalog = ReadCatalog();
        return catalog.Accounts.FirstOrDefault(a => a != null && a.Owner == UserSid && !a.Removed && a.Sid == catalog.Selected);
    }

    public static Account ReadAccount() { return VerifyAccount(SelectedAccount()); }

    static Account VerifyAccount(Account account)
    {
        if (account == null || account.Owner != UserSid || !ValidName(account.Folder ?? account.Name))
            throw new IOException("The saved Second Spotify Windows account could not be verified.");
        if (account.SteamHome != null)
        {
            var steam = SteamWindowsProfile.Read(account.SteamHome);
            if (account.Folder == null || account.Sid != steam.Sid || account.Owner != steam.Owner || account.Name != steam.Name)
                throw new IOException("Locate the original profile in Dual Steam again.");
        }
        else
        {
            if (account.Folder != null || !ValidName(account.Name))
                throw new IOException("The saved Second Spotify Windows account could not be verified.");
            WindowsProfileAccount.VerifyStandard(account.Name, account.Sid, account.Owner);
        }
        if (File.ReadAllText(Path.Combine(PlayerRoot(account), "identity.txt")).Trim() != account.Sid)
            throw new IOException("The saved Second Spotify Windows account could not be verified.");
        return account;
    }

    static void SaveSelection(Account account)
    {
        var catalog = ReadCatalog();
        catalog.Accounts.RemoveAll(a => a != null && a.Sid == account.Sid);
        account.Removed = false;
        catalog.Accounts.Add(account);
        catalog.Selected = account.Sid;
        SaveCatalog(catalog);
    }

    public static void CreateProfile() { CreateAccount(); }

    public static void SelectProfile(string sid)
    {
        var account = VerifyAccount(Profiles().FirstOrDefault(a => a.Sid == sid));
        Request(account, "verify");
        SaveSelection(account);
    }

    public static void SelectSteam(string home)
    {
        var candidate = SteamWindowsProfile.Read(home);
        var saved = Profiles().FirstOrDefault(a => a.Sid == candidate.Sid && a.SteamHome != null);
        if (saved != null && saved.Sid == candidate.Sid && saved.Owner == candidate.Owner &&
            ValidName(saved.Folder) && File.Exists(Path.Combine(PlayerRoot(saved), "identity.txt")) &&
            File.ReadAllText(Path.Combine(PlayerRoot(saved), "identity.txt")).Trim() == candidate.Sid)
            candidate.Folder = saved.Folder;
        else
        {
            candidate.Folder = "DualSpotify_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var owner = new SecurityIdentifier(candidate.Owner);
            var sid = new SecurityIdentifier(candidate.Sid);
            var folder = PlayerRoot(candidate);
            Directory.CreateDirectory(folder, Permissions(owner, sid, FileSystemRights.ReadAndExecute));
            Directory.CreateDirectory(Path.Combine(folder, "State"), Permissions(owner, sid, FileSystemRights.Modify));
            File.WriteAllText(Path.Combine(folder, "identity.txt"), candidate.Sid);
        }
        Request(candidate, "verify");
        SaveSelection(candidate);
    }

    public static void RemoveSelection()
    {
        var catalog = ReadCatalog();
        var account = catalog.Accounts.FirstOrDefault(a => a != null && a.Owner == UserSid && a.Sid == catalog.Selected);
        if (account != null) account.Removed = true;
        catalog.Selected = null;
        SaveCatalog(catalog);
    }

    public static void Verify() { Request(ReadAccount(), "verify"); }

    public static void RemoveDedicated()
    {
        var account = ReadAccount();
        WindowsProfileAccount.Change(account, "remove");
        var catalog = ReadCatalog();
        catalog.Accounts.RemoveAll(a => a != null && a.Sid == account.Sid);
        catalog.Selected = null;
        SaveCatalog(catalog);
    }

    public static int Provision(string id, string ownerSid)
    {
        if (!Regex.IsMatch(id ?? "", "^[a-f0-9]{32}$"))
            return 2;
        var admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(admin))
            return 2;
        var owner = new SecurityIdentifier(ownerSid);
        var name = "DualSpotify_" + id.Substring(0, 8);
        var folder = Root(name);
        bool newUser = false, committed = false;
        using (var pipe = new NamedPipeClientStream(".", PipeName(id), PipeDirection.InOut))
        {
            pipe.Connect(15000);
            using (var writer = new BinaryWriter(pipe, Encoding.UTF8, true))
            using (var reader = new BinaryReader(pipe, Encoding.UTF8, true))
            try
            {
                if (Directory.Exists(folder))
                    throw new IOException("The generated setup folder already exists.");
                byte[] random = new byte[32];
                using (var generator = RandomNumberGenerator.Create()) generator.GetBytes(random);
                var password = Convert.ToBase64String(random) + "!aA9";
                Array.Clear(random, 0, random.Length);
                var user = new UserInfo { Name = name, Password = password, Privilege = 1,
                    Comment = "Second Spotify desktop profile", Flags = 0x10201 };
                uint invalid;
                Check(NetUserAdd(null, 1, ref user, out invalid), "Create the Spotify Windows user");
                newUser = true;
                var sid = (SecurityIdentifier)new NTAccount(Environment.MachineName, name).Translate(typeof(SecurityIdentifier));
                var users = (NTAccount)new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null).Translate(typeof(NTAccount));
                var group = users.Value.Substring(users.Value.LastIndexOf('\\') + 1);
                var member = new LocalGroupMember { DomainAndName = Environment.MachineName + "\\" + name };
                int result = NetLocalGroupAddMembers(null, group, 3, ref member, 1);
                if (result != 1378)
                    Check(result, "Add the Spotify Windows user to Users");
                Directory.CreateDirectory(folder, Permissions(owner, sid, FileSystemRights.ReadAndExecute));
                Directory.CreateDirectory(Path.Combine(folder, "State"), Permissions(owner, sid, FileSystemRights.Modify));
                File.WriteAllText(Path.Combine(folder, "identity.txt"), sid.Value);
                writer.Write(true);
                writer.Write(sid.Value);
                writer.Write(password);
                writer.Flush();
                committed = reader.ReadBoolean();
                return committed ? 0 : 1;
            }
            catch (Exception ex)
            {
                try { writer.Write(false); writer.Write(ex.Message); writer.Flush(); }
                catch (IOException) { }
                return 1;
            }
            finally
            {
                if (newUser && !committed)
                    NetUserDel(null, name);
            }
        }
    }

    static DirectorySecurity Permissions(SecurityIdentifier owner, SecurityIdentifier user, FileSystemRights rights)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { owner, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(user, rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    static void Check(int error, string operation)
    {
        if (error != 0)
            throw new IOException(operation + ": " + new Win32Exception(error).Message);
    }

    public static void Open(bool minimized)
    {
        var account = ReadAccount();
        Request(account, minimized ? "minimized" : "open");
    }

    public static void Quit() { if (Configured) Request(ReadAccount(), "quit"); }

    static void Request(Account account, string action)
    {
        var root = PlayerRoot(account);
        var copy = Path.Combine(root, "Dual Spotify Profile " + typeof(ManagedSecond).Assembly.GetName().Version.ToString(3) + ".exe");
        if (!File.Exists(copy))
            File.Copy(Path.Combine(Engine.Home, "Dual Spotify.exe"), copy);
        var setup = Path.Combine(root, Installer);
        if ((action == "open" || action == "minimized") && !File.Exists(setup))
            SpotifyRuntime.Fetch(setup);
        DesktopAccess.Grant(account.Sid);
        var id = Guid.NewGuid().ToString("N");
        var readyPath = Path.Combine(root, "State", id + ".ready");
        using (var secret = account.SteamHome == null ? DedicatedPassword(account) : SteamWindowsProfile.Password(account.SteamHome))
        {
            var launch = new ProcessStartInfo(copy, "--managed-player " + action + " " + id) {
                WorkingDirectory = root, UserName = account.Name, Domain = Environment.MachineName,
                Password = secret, LoadUserProfile = true, UseShellExecute = false
            };
            using (var child = Process.Start(launch))
            {
                var result = Path.Combine(root, "State", id + ".txt");
                try
                {
                    var timeout = Stopwatch.StartNew();
                    while (!child.WaitForExit(125))
                    {
                        if (File.Exists(readyPath))
                            return;
                        if (timeout.ElapsedMilliseconds >= 125000)
                            throw new IOException("The independent Spotify process is still preparing. Try again shortly.");
                    }
                    if (child.ExitCode != 0)
                        throw new IOException(File.Exists(result) ? File.ReadAllText(result) : "The independent Spotify player could not be started.");
                }
                finally
                {
                    if (File.Exists(readyPath)) File.Delete(readyPath);
                    if (File.Exists(result)) File.Delete(result);
                }
            }
        }
    }

    static SecureString DedicatedPassword(Account account)
    {
        byte[] plaintext = ProtectedData.Unprotect(Convert.FromBase64String(account.Password), null, DataProtectionScope.CurrentUser);
        try
        {
            var secret = new SecureString();
            foreach (var ch in Encoding.UTF8.GetString(plaintext)) secret.AppendChar(ch);
            return secret;
        }
        finally { Array.Clear(plaintext, 0, plaintext.Length); }
    }

    public static int RunPlayer(string action, string id)
    {
        var root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        if (!ValidName(Path.GetFileName(root)) || !String.Equals(Path.GetDirectoryName(root), SharedRoot, StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(id ?? "", "^[a-f0-9]{32}$") || File.ReadAllText(Path.Combine(root, "identity.txt")).Trim() != UserSid ||
            action != "open" && action != "quit" && action != "minimized" && action != "verify")
            return 2;
        if (action == "verify")
            return 0;
        var state = Path.Combine(root, "State");
        try
        {
            var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Spotify\Spotify.exe");
            var client = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Dual Spotify\Client");
            var extracted = Path.Combine(client, "Spotify.exe");
            var profileArguments = "--mu=" + UserSid + " --disable-desktop-launcher";
            using (var current = FindPlayer(state, installed, extracted))
            {
                if (current != null)
                {
                    if (action == "quit")
                    {
                        if (!current.CloseMainWindow() || !current.WaitForExit(4500))
                            throw new IOException("Quit Spotify from the second player’s own menu.");
                        File.Delete(Path.Combine(state, "player.txt"));
                    }
                    else
                    {
                        RegisterSpotifyProtocol(current.MainModule.FileName, profileArguments);
                        if (action == "open" && current.MainWindowHandle != IntPtr.Zero)
                            Engine.FocusWindow(current.MainWindowHandle);
                        SavePlayer(state, current);
                    }
                    return 0;
                }
            }
            if (action == "quit")
            {
                File.Delete(Path.Combine(state, "player.txt"));
                return 0;
            }
            var player = File.Exists(installed) ? installed : extracted;
            if (!File.Exists(player))
                SpotifyRuntime.Extract(Path.Combine(root, Installer), client);
            var arguments = profileArguments +
                (action == "minimized" ? " --minimized" : "");
            using (var child = Process.Start(new ProcessStartInfo(player, arguments)
                { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(player) }))
            {
                if (action == "minimized" && !child.WaitForExit(3000))
                {
                    RegisterSpotifyProtocol(player, profileArguments);
                    KeepWindowsProfileLoaded(state, id, child);
                    return 0;
                }
                var startup = Stopwatch.StartNew();
                while (startup.ElapsedMilliseconds < 25000)
                {
                    using (var ready = FindPlayer(state, installed, extracted))
                        if (ready != null)
                        {
                            RegisterSpotifyProtocol(ready.MainModule.FileName, profileArguments);
                            KeepWindowsProfileLoaded(state, id, ready);
                            return 0;
                        }
                    Thread.Sleep(250);
                }
            }
            throw new IOException("Spotify did not open a window in the dedicated Windows account.");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(state, id + ".txt"), ex.Message);
            return 1;
        }
    }

    static Process FindPlayer(string state, string installed, string extracted)
    {
        int pid = PlayerFromState(state);
        Process found = null;
        foreach (var process in Process.GetProcessesByName("Spotify"))
        {
            if (found == null && IsPlayer(process, installed, extracted, pid))
                found = process;
            else
                process.Dispose();
        }
        return found;
    }

    static void RegisterSpotifyProtocol(string player, string profileArguments)
    {
        const string protocol = @"Software\Classes\spotify";
        using (var existing = Registry.CurrentUser.OpenSubKey(protocol + @"\shell\open\command"))
        {
            var current = existing == null ? null : existing.GetValue(null) as string;
            if (!String.IsNullOrWhiteSpace(current) && !Equals(existing.GetValue("DualSpotifyManaged"), 1) &&
                !String.Equals(Engine.Split(current)[0], player, StringComparison.OrdinalIgnoreCase))
                return;
        }
        using (var scheme = Registry.CurrentUser.CreateSubKey(protocol))
        {
            scheme.SetValue(null, "URL:Spotify");
            scheme.SetValue("URL Protocol", "");
            using (var command = scheme.CreateSubKey(@"shell\open\command"))
            {
                command.SetValue(null, "\"" + player + "\" " + profileArguments + " --protocol-uri=\"%1\"");
                command.SetValue("DualSpotifyManaged", 1, RegistryValueKind.DWord);
            }
        }
    }

    static void KeepWindowsProfileLoaded(string state, string id, Process player)
    {
        SavePlayer(state, player);
        // The alternate logon owns the loaded user hive. Retain it for the player's browser and settings calls.
        File.WriteAllBytes(Path.Combine(state, id + ".ready"), new byte[0]);
        player.WaitForExit();
    }

    static bool IsPlayer(Process process, string installed, string extracted, int knownPid)
    {
        try
        {
            if ((process.Id != knownPid && process.MainWindowHandle == IntPtr.Zero) ||
                !SpotifyInstallation.IsCurrentUserProcess(process.Id))
                return false;
            var path = process.MainModule.FileName;
            return String.Equals(path, installed, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(path, extracted, StringComparison.OrdinalIgnoreCase);
        }
        catch (Win32Exception) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    static void SavePlayer(string state, Process player)
    {
        File.WriteAllText(Path.Combine(state, "player.txt"), player.Id + "|" + player.StartTime.ToUniversalTime().Ticks);
    }

    public static int Player()
    {
        if (!Configured)
            return 0;
        try { return PlayerFromState(Path.Combine(PlayerRoot(ReadAccount()), "State")); }
        catch (IOException) { return 0; }
        catch (Win32Exception) { return 0; }
    }

    static int PlayerFromState(string state)
    {
        try
        {
            var path = Path.Combine(state, "player.txt");
            if (!File.Exists(path))
                return 0;
            var parts = File.ReadAllText(path).Split('|');
            int pid;
            long started;
            if (parts.Length != 2 || !Int32.TryParse(parts[0], out pid) || !Int64.TryParse(parts[1], out started))
                return 0;
            using (var process = Engine.TryProcess(pid))
                if (process != null && process.ProcessName.Equals("Spotify", StringComparison.OrdinalIgnoreCase) &&
                    MatchesStartTime(process, started))
                    return pid;
        }
        catch (Win32Exception) { }
        catch (IOException) { }
        catch (InvalidOperationException) { }
        return 0;
    }

    static bool MatchesStartTime(Process process, long started)
    {
        try { return process.StartTime.ToUniversalTime().Ticks == started; }
        catch (Win32Exception)
        {
            using (var current = new ManagementObject("Win32_Process.Handle='" + process.Id + "'"))
            {
                try
                {
                    var date = current["CreationDate"] as string;
                    return date != null && ManagementDateTimeConverter.ToDateTime(date).ToUniversalTime().Ticks == started - started % 10;
                }
                catch (ManagementException) { return false; }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct UserInfo
    {
        public string Name, Password;
        public uint PasswordAge, Privilege;
        public string Home, Comment;
        public uint Flags;
        public string Script;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct LocalGroupMember { public string DomainAndName; }
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern int NetUserAdd(string server, uint level, ref UserInfo info, out uint invalid);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern int NetUserDel(string server, string name);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern int NetLocalGroupAddMembers(string server, string group, uint level,
        ref LocalGroupMember member, uint count);
}
