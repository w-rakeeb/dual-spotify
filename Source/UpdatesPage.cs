using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using DualSpotify;
using DualSpotify.UI;

public partial class App
{
    const string Releases = "https://github.com/" + UpdateService.Repository + "/releases/latest";
    Button checkUpdateButton, installUpdateButton;
    InfoBanner updateStatus;
    ReleaseInfo availableUpdate;
    bool updateBusy;
    string updateMessage = "Check GitHub for a newer version of Dual Spotify.";

    FrameworkElement Updates()
    {
        var body = Page("Updates", "Manage updates for Dual Spotify.");
        var details = Stack();
        details.Children.Add(Text("Dual Spotify " + BuildVersion, Theme.TitleSize, "text.primary", true));
        checkUpdateButton = ActionButton("Check for updates", CheckUpdates);
        installUpdateButton = ActionButton("Download and install", InstallUpdate);
        details.Children.Add(checkUpdateButton);
        details.Children.Add(installUpdateButton);
        details.Children.Add(ActionButton("Open GitHub releases", () => OpenPath(Releases)));
        body.Children.Add(Group(details));
        updateStatus = new InfoBanner(updateMessage);
        body.Children.Add(updateStatus);
        ShowUpdateState();
        return body;
    }

    void CheckUpdatesFromShortcut()
    {
        ShowManager();
        ShowPage("Updates");
        CheckUpdates();
    }

    void ShowUpdateState()
    {
        if (checkUpdateButton == null)
            return;
        checkUpdateButton.IsEnabled = !updateBusy;
        installUpdateButton.IsEnabled = !updateBusy && availableUpdate != null;
        updateStatus.Message(updateMessage);
    }

    async void CheckUpdates()
    {
        if (updateBusy || exiting)
            return;
        updateBusy = true;
        availableUpdate = null;
        updateMessage = "Checking GitHub…";
        ShowUpdateState();
        try
        {
            var latest = await Task.Run(() => UpdateService.GetLatest());
            if (latest.Version > UpdateService.ParseVersion(BuildVersion))
                availableUpdate = latest;
            updateMessage = availableUpdate == null ? "You’re on the latest published version." :
                "Version " + latest.Tag + " is available. The launcher will restart to install it.";
        }
        catch (Exception ex) { updateMessage = ex.Message; }
        finally { updateBusy = false; ShowUpdateState(); }
    }

    async void InstallUpdate()
    {
        if (updateBusy || availableUpdate == null || exiting)
            return;
        updateBusy = true;
        updateMessage = "Downloading and verifying the release…";
        ShowUpdateState();
        try
        {
            var release = availableUpdate;
            var package = await Task.Run(() => UpdateService.Download(release, Engine.Home));
            if (exiting)
                return;
            var folder = Path.GetDirectoryName(package);
            var requestPath = Path.Combine(folder, "request.json");
            var updater = Path.Combine(folder, "Dual Spotify Updater.exe");
            File.Copy(Path.Combine(Engine.Home, "Dual Spotify Updater.exe"), updater);
            using (var manager = Process.GetCurrentProcess())
            {
                var request = new UpdateRequest { Target = Engine.Home, Package = package, Sha256 = release.Sha256,
                    Version = release.Tag, ManagerPid = manager.Id, ManagerStartTicks = manager.StartTime.ToUniversalTime().Ticks };
                File.WriteAllText(requestPath, new JavaScriptSerializer().Serialize(request));
            }
            using (Process.Start(new ProcessStartInfo(updater, "--request \"" + requestPath + "\"") { UseShellExecute = false })) { }
            ExitManager();
        }
        catch (Exception ex) { updateMessage = ex.Message; }
        finally { updateBusy = false; if (!exiting) ShowUpdateState(); }
    }
}
