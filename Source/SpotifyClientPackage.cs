using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

static class SpotifyClientPackage
{
    public const string FileName = "SpotifyClient-v1.zip";
    const string Manifest = "client-manifest.json";
    const long MaximumSize = 2L * 1024 * 1024 * 1024;
    static readonly HashSet<string> RootFiles = new HashSet<string>(new[] {
        "Spotify.exe", "Spotify.exe.sig", "Spotify.dll", "Spotify.dll.sig",
        "SpotifyLauncher.exe", "spotify_cli.exe", "save-to-spotify.exe", "crashpad_handler.exe",
        "chrome_elf.dll", "chrome_100_percent.pak", "chrome_200_percent.pak",
        "d3dcompiler_47.dll", "libcef.dll", "libcef.dll.sig", "libEGL.dll", "libGLESv2.dll",
        "icudtl.dat", "resources.pak", "snapshot_blob.bin", "v8_context_snapshot.bin",
        "vk_swiftshader.dll", "vk_swiftshader_icd.json", "vulkan-1.dll", "toast_icon.png", "crash_reporter.cfg"
    }, StringComparer.OrdinalIgnoreCase);
    static readonly string[] Required = {
        "Spotify.exe", "Spotify.dll", "libcef.dll", "chrome_elf.dll", "icudtl.dat", "resources.pak"
    };

    public static bool Complete(string client)
    {
        try
        {
            return Required.All(name => File.Exists(Path.Combine(client, name))) &&
                String.Equals(FileVersionInfo.GetVersionInfo(Path.Combine(client, "Spotify.exe")).FileDescription,
                    "Spotify", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(client, @"Apps\xpui.spa")) &&
                Directory.Exists(Path.Combine(client, "locales")) &&
                Directory.EnumerateFiles(Path.Combine(client, "locales"), "*.pak").Any();
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (Win32Exception) { return false; }
    }

    public static void Prepare(string destination, string installed, Action<string> progress)
    {
        if (!String.IsNullOrWhiteSpace(installed))
        {
            var client = Path.GetDirectoryName(installed);
            if (Complete(client))
            {
                try
                {
                    if (progress != null) progress("Preparing Spotify files…");
                    Create(client, destination);
                    return;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.Cryptography.CryptographicException) { }
            }
        }

        var work = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Dual Spotify Setup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var installer = Path.Combine(work, "SpotifyFullSetupX64.exe");
            if (progress != null) progress("Downloading Spotify…");
            SpotifyRuntime.Fetch(installer, progress);
            if (progress != null) progress("Preparing Spotify files…");
            var extracted = Path.Combine(work, "Client");
            SpotifyRuntime.Extract(installer, extracted);
            Create(extracted, destination);
        }
        finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
    }

    public static void Create(string client, string destination)
    {
        if (!Complete(client)) throw new IOException("The Spotify application files are incomplete.");
        SpotifyRuntime.VerifyPublisher(Path.Combine(client, "Spotify.exe"));
        foreach (var folder in new[] { Path.Combine(client, "Apps"), Path.Combine(client, "locales") })
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked Spotify application folders are not supported.");
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var files = RootFiles.Select(name => Path.Combine(client, name)).Where(File.Exists)
                .Concat(Directory.EnumerateFiles(Path.Combine(client, "Apps"), "*.spa"))
                .Concat(Directory.EnumerateFiles(Path.Combine(client, "locales"), "*.pak")).ToArray();
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long size = 0;
            using (var zip = ZipFile.Open(staging, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Linked Spotify application files are not supported.");
                    var name = file.Substring(client.TrimEnd('\\').Length + 1).Replace('\\', '/');
                    if (!Allowed(name) || hashes.Count >= 600)
                        throw new IOException("Unexpected Spotify application files.");
                    var before = Hash(file);
                    using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var output = zip.CreateEntry(name, CompressionLevel.Fastest).Open())
                    {
                        size += input.Length;
                        if (size > MaximumSize) throw new IOException("The Spotify client package is too large.");
                        input.CopyTo(output);
                    }
                    if (!String.Equals(before, Hash(file), StringComparison.Ordinal))
                        throw new IOException("Spotify updated while preparing its application files. Retry setup.");
                    hashes.Add(name, before);
                }
                using (var writer = new StreamWriter(zip.CreateEntry(Manifest).Open()))
                    writer.Write(new JavaScriptSerializer().Serialize(hashes));
            }
            if (File.Exists(destination)) File.Replace(staging, destination, null);
            else File.Move(staging, destination);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    public static void Install(string package, string client)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(client));
        var staging = client + "." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            using (var zip = ZipFile.OpenRead(package))
            {
                var manifest = zip.GetEntry(Manifest);
                if (manifest == null || manifest.Length > 128 * 1024 || zip.Entries.Count > 601)
                    throw new IOException("The Spotify client package is incomplete.");
                Dictionary<string, string> hashes;
                using (var reader = new StreamReader(manifest.Open()))
                    hashes = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
                if (hashes == null || hashes.Count != zip.Entries.Count - 1)
                    throw new IOException("The Spotify client manifest does not match its files.");
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long size = 0;
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName == Manifest) continue;
                    string expected;
                    if (!Allowed(entry.FullName) || !found.Add(entry.FullName) ||
                        !hashes.TryGetValue(entry.FullName, out expected) ||
                        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                        throw new IOException("The Spotify package contains an unexpected file.");
                    size += entry.Length;
                    if (size > MaximumSize) throw new IOException("The Spotify client package is too large.");
                    var file = Path.Combine(staging, entry.FullName.Replace('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    using (var input = entry.Open())
                    using (var output = new FileStream(file, FileMode.CreateNew))
                    {
                        var buffer = new byte[128 * 1024];
                        long copied = 0;
                        int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            copied += count;
                            if (copied > entry.Length) throw new IOException("Spotify package file size mismatch.");
                            output.Write(buffer, 0, count);
                        }
                        if (copied != entry.Length) throw new IOException("The Spotify client package is truncated.");
                    }
                    if (!String.Equals(expected, Hash(file), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("The Spotify client package checksum did not match.");
                }
            }
            if (!Complete(staging)) throw new IOException("The Spotify client package is incomplete.");
            SpotifyRuntime.VerifyPublisher(Path.Combine(staging, "Spotify.exe"));
            var previous = Directory.Exists(client) ? client + ".previous-" + Guid.NewGuid().ToString("N") : null;
            if (previous != null) Directory.Move(client, previous);
            try { Directory.Move(staging, client); }
            catch
            {
                if (previous != null && !Directory.Exists(client)) Directory.Move(previous, client);
                throw;
            }
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    public static bool Allowed(string name)
    {
        if (String.IsNullOrEmpty(name) || name.Contains("\\") || name.Contains(":") || name.StartsWith("/"))
            return false;
        var parts = name.Split('/');
        if (parts.Any(p => String.IsNullOrEmpty(p) || p == "." || p == ".." ||
            p.EndsWith(".") || p.EndsWith(" ") || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            return false;
        if (parts.Length == 1) return RootFiles.Contains(name);
        return parts.Length == 2 &&
            ((parts[0] == "Apps" && parts[1].EndsWith(".spa", StringComparison.OrdinalIgnoreCase)) ||
             (parts[0] == "locales" && parts[1].EndsWith(".pak", StringComparison.OrdinalIgnoreCase)));
    }

    static string Hash(string path)
    {
        using (var sha = SHA256.Create())
        using (var input = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }
}
