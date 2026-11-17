using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

static class SpotifyRuntime
{
    const string DownloadUrl = "https://download.scdn.co/SpotifyFullSetupX64.exe";
    const long MaximumDownload = 512L * 1024 * 1024;

    public static void Extract(string setup, string client)
    {
        VerifyPublisher(setup);
        Directory.CreateDirectory(Path.GetDirectoryName(client));
        var staging = client + "." + Guid.NewGuid().ToString("N");
        try
        {
            using (var installer = Process.Start(new ProcessStartInfo(setup, "/extract \"" + staging + "\"") {
                UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(client) }))
            {
                if (!installer.WaitForExit(85000))
                {
                    installer.Kill();
                    installer.WaitForExit();
                    throw new IOException("Extracting the Spotify desktop client timed out.");
                }
                if (installer.ExitCode != 0)
                    throw new IOException("Spotify could not extract its desktop client (exit code " + installer.ExitCode + ").");
            }
            VerifyPublisher(Path.Combine(staging, "Spotify.exe"));
            if (!File.Exists(Path.Combine(staging, "libcef.dll")) || !Directory.Exists(Path.Combine(staging, "Apps")))
                throw new IOException("The extracted Spotify desktop client is incomplete.");
            // A failed earlier extraction can leave a partial Client folder. Keep it for recovery.
            var previous = Directory.Exists(client) ? client + ".previous-" + Guid.NewGuid().ToString("N") : null;
            if (previous != null) Directory.Move(client, previous);
            try { Directory.Move(staging, client); }
            catch
            {
                if (previous != null && !Directory.Exists(client)) Directory.Move(previous, client);
                throw;
            }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }
    public static void Fetch(string destination, Action<string> progress)
    {
        var staging = destination + ".download";
        if (File.Exists(staging))
            File.Delete(staging);
        try
        {
            Download(staging, progress);
            if (progress != null) progress("Verifying Spotify…");
            VerifyPublisher(staging);
            File.Move(staging, destination);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }

    static void Download(string destination, Action<string> progress)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var request = (HttpWebRequest)WebRequest.Create(DownloadUrl);
        request.AllowAutoRedirect = false;
        request.Timeout = request.ReadWriteTimeout = 30000;
        request.UserAgent = "DualSpotify";
        using (var deadline = new Timer(_ => request.Abort(), null, TimeSpan.FromMinutes(4), Timeout.InfiniteTimeSpan))
        try
        {
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var input = response.GetResponseStream())
            using (var output = new FileStream(destination, FileMode.CreateNew))
            {
                if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > MaximumDownload)
                    throw new IOException("Spotify’s download server returned an unexpected response.");
                var buffer = new byte[128 * 1024];
                var timer = Stopwatch.StartNew();
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (output.Length + read > MaximumDownload)
                        throw new IOException("Spotify’s installer exceeded the download size limit.");
                    output.Write(buffer, 0, read);
                    if (progress != null && response.ContentLength > 0 && timer.ElapsedMilliseconds >= 300)
                    {
                        progress("Downloading… " + Math.Min(100, output.Length * 100 / response.ContentLength) + "%");
                        timer.Restart();
                    }
                }
                if (response.ContentLength >= 0 && response.ContentLength != output.Length)
                    throw new IOException("Spotify’s installer download was incomplete. Try opening Second Spotify again.");
            }
        }
        catch (WebException ex)
        {
            if (ex.Response != null) ex.Response.Dispose();
            throw new IOException(ex.Status == WebExceptionStatus.Timeout || ex.Status == WebExceptionStatus.RequestCanceled
                ? "The Spotify download timed out. Check your connection and try opening Second Spotify again."
                : "Could not download Spotify from its official server. Check your connection and try again.", ex);
        }
    }

    public static void VerifyPublisher(string path)
    {
        var file = new TrustFile { Size = (uint)Marshal.SizeOf(typeof(TrustFile)), Path = path };
        IntPtr filePointer = Marshal.AllocHGlobal((int)file.Size);
        Marshal.StructureToPtr(file, filePointer, false);
        try
        {
            var trust = new TrustData { Size = (uint)Marshal.SizeOf(typeof(TrustData)), UiChoice = 2,
                UnionChoice = 1, File = filePointer };
            var policy = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
            if (WinVerifyTrust(new IntPtr(-1), ref policy, ref trust) != 0)
                throw new IOException("Windows could not verify the Spotify download’s signature.");
            using (var signed = X509Certificate.CreateFromSignedFile(path))
            using (var certificate = new X509Certificate2(signed))
                if (certificate.GetNameInfo(X509NameType.SimpleName, false) != "Spotify AB")
                    throw new IOException("The download was not signed by Spotify AB.");
        }
        finally
        {
            Marshal.DestroyStructure(filePointer, typeof(TrustFile));
            Marshal.FreeHGlobal(filePointer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TrustFile
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public IntPtr Handle, Subject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TrustData
    {
        public uint Size;
        public IntPtr Policy, Sip;
        public uint UiChoice, Revocation, UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr State, Url;
        public uint Flags, UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
}
