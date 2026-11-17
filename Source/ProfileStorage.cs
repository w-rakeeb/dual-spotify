using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

static class ProfileStorage
{
    static bool Overlaps(string first, string second)
    {
        return (first + "\\").StartsWith(second + "\\", StringComparison.OrdinalIgnoreCase) ||
            (second + "\\").StartsWith(first + "\\", StringComparison.OrdinalIgnoreCase);
    }

    static void CheckLinks(string path)
    {
        for (var folder = new DirectoryInfo(path); folder != null; folder = folder.Parent)
            if (folder.Exists && (folder.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Choose a regular directory without linked folders.");
    }

    static IEnumerable<string> Files(string directory)
    {
        foreach (var file in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The profile contains a linked folder or file.");
            if ((attributes & FileAttributes.Directory) == 0)
                yield return file;
            else
                foreach (var child in Files(file))
                    yield return child;
        }
    }

    static byte[] Hash(string path)
    {
        using (var sha = SHA256.Create())
        using (var input = File.OpenRead(path))
            return sha.ComputeHash(input);
    }

    public static string CopyVerified(string source, string target, string[] protectedRoots)
    {
        source = Path.GetFullPath(source).TrimEnd('\\');
        target = Path.GetFullPath(target).TrimEnd('\\');
        var drive = Path.GetPathRoot(target);
        if (target.Length <= 3 || drive.Length != 3 || new DriveInfo(drive).DriveType != DriveType.Fixed ||
            Overlaps(source, target) || protectedRoots.Any(root => Overlaps(Path.GetFullPath(root).TrimEnd('\\'), target)))
            throw new IOException("Choose a separate folder on a local drive outside Spotify and the launcher.");
        CheckLinks(source);
        CheckLinks(target);
        if (File.Exists(target) || Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            throw new IOException("Choose an empty folder for the second profile.");
        var files = Directory.Exists(source) ? Files(source).ToArray() : new string[0];
        Directory.CreateDirectory(target);
        foreach (var file in files)
        {
            var destination = Path.Combine(target, file.Substring(source.Length + 1));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(file, destination, false);
            if (!Hash(file).SequenceEqual(Hash(destination)))
                throw new IOException("Profile copy verification failed. The original folder is still selected.");
        }
        if (Directory.Exists(source) && !new HashSet<string>(Files(source), StringComparer.OrdinalIgnoreCase).SetEquals(files))
            throw new IOException("The source profile changed during the copy. The original folder is still selected.");
        return target;
    }
}
