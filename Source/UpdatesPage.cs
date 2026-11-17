using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using DualSpotify;
using DualSpotify.UI;

public partial class App
{
    const string Releases = "https://github.com/" + UpdateService.Repository + "/releases/latest";
    readonly UpdateSchedule updateSchedule = new UpdateSchedule();
    readonly UpdateFeedback updateFeedback = new UpdateFeedback();
    bool checkingUpdates, suppressActivationCheck, exitAfterDownload;
    CancellationTokenSource updateCancellation;
    ReleaseInfo availableUpdate;
    UpdateNotice updateNotice;
    TextBlock updateTitle, updateDetail, updateChecked;
    Button checkUpdateButton, installUpdateButton, cancelUpdateButton;
    ProgressBar updateProgress;
    int downloadPercent;

    FrameworkElement Updates()
    {
        var body = Page("Updates", "Stable releases from GitHub. Your Spotify profiles stay in place.");
        body.Children.Add(UpdatePanel());
        return body;
    }

    FrameworkElement UpdatePanel()
    {
        updateTitle = Text("", Theme.TitleSize, "text.primary", true);
        updateDetail = Text("", Theme.BodySize, "text.secondary");
        updateChecked = Text("", Theme.NoteSize, "text.muted");
        updateProgress = new ProgressBar { Height = 5, Maximum = 100, Margin = new Thickness(0, 8, 0, 0) };
        checkUpdateButton = ActionButton("Check for updates", () => CheckUpdates(true));
        installUpdateButton = ActionButton("Install update", InstallUpdate);
        installUpdateButton.Style = Theme.Style("Primary");
        cancelUpdateButton = ActionButton("Cancel download", () => updateCancellation.Cancel());
        var card = SettingsCard("App updates", updateTitle, updateDetail, updateChecked, updateProgress,
            ToolActions(checkUpdateButton, installUpdateButton, cancelUpdateButton,
                ActionButton("Download ZIP in browser", () => OpenPath(Releases))));
        ShowUpdateState();
        return card;
    }

    void ClearUpdateView()
    {
        updateTitle = updateDetail = updateChecked = null;
        checkUpdateButton = installUpdateButton = cancelUpdateButton = null;
        updateProgress = null;
    }

    void CheckUpdatesFromShortcut()
    {
        suppressActivationCheck = true;
        try { ShowManager(); if (currentPage != "Updates" && currentPage != "About") ShowPage("About"); }
        finally { suppressActivationCheck = false; }
        CheckUpdates(true);
    }

    void CheckUpdateSchedule(bool reopened)
    {
        if (exiting || busy || checkingUpdates || updateCancellation != null || suppressActivationCheck)
            return;
        if (updateSchedule.TakeCheck(Preferences.Current.AutoUpdates, DateTime.UtcNow, reopened))
            CheckUpdates(false);
    }

    void ShowUpdateState()
    {
        if (updateTitle == null)
            return;
        updateTitle.Text = updateFeedback.Title;
        updateDetail.Text = updateFeedback.Detail;
        updateChecked.Text = updateFeedback.LastChecked.HasValue
            ? "Last checked: " + updateFeedback.LastChecked.Value.ToString("g") : "Not checked this session";
        checkUpdateButton.IsEnabled = !checkingUpdates && !busy && updateCancellation == null;
        installUpdateButton.IsEnabled = availableUpdate != null && checkUpdateButton.IsEnabled;
        cancelUpdateButton.Visibility = updateCancellation == null ? Visibility.Collapsed : Visibility.Visible;
        updateProgress.Visibility = checkingUpdates || updateCancellation != null ? Visibility.Visible : Visibility.Collapsed;
        updateProgress.IsIndeterminate = checkingUpdates || downloadPercent == 0;
        updateProgress.Value = downloadPercent;
    }

    async void CheckUpdates(bool manual)
    {
        if (checkingUpdates || updateCancellation != null || busy || exiting)
            return;
        checkingUpdates = true;
        availableUpdate = null;
        updateSchedule.Succeeded(DateTime.UtcNow);
        updateFeedback.Begin();
        ShowUpdateState();
        try
        {
            var latest = await Task.Run(() => UpdateService.GetLatest());
            var installed = UpdateService.ParseVersion(BuildVersion);
            if (latest.Version > installed)
                availableUpdate = latest;
            updateSchedule.Succeeded(DateTime.UtcNow);
            updateFeedback.Complete(installed, latest.Version);
            if (!exiting && !manual && Preferences.Current.AutoUpdates && availableUpdate != null && updateSchedule.Announce(latest.Tag))
            {
                ShowManager();
                updateNotice.ShowRelease(latest.Tag);
            }
        }
        catch (Exception ex)
        {
            updateSchedule.Failed(DateTime.UtcNow, ex is UpdateRateLimitException);
            updateFeedback.Fail(ex.Message);
        }
        finally { checkingUpdates = false; if (!exiting) ShowUpdateState(); }
    }

    async void InstallUpdate()
    {
        if (checkingUpdates || updateCancellation != null || busy || availableUpdate == null || exiting)
            return;
        var cancellation = new CancellationTokenSource();
        updateCancellation = cancellation;
        downloadPercent = 0;
        updateFeedback.Title = "Downloading update";
        updateFeedback.Detail = "Getting the stable Windows ZIP from GitHub.";
        ShowUpdateState();
        try
        {
            var release = availableUpdate;
            int reported = -1;
            var package = await Task.Run(() => UpdateService.Download(release, Engine.Home, (done, total) =>
            {
                int percent = (int)(100 * done / total);
                if (percent == reported)
                    return;
                reported = percent;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (updateCancellation != cancellation)
                        return;
                    downloadPercent = percent;
                    updateFeedback.Detail = percent == 100 ? "Verifying the ZIP checksum and contents…" :
                        percent + "% downloaded · " + (done / 1024) + " / " + (total / 1024) + " KB";
                    ShowUpdateState();
                }));
            }, cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();
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
            ShutdownManager();
        }
        catch (OperationCanceledException)
        {
            updateFeedback.Title = "Download canceled";
            updateFeedback.Detail = "You can retry the download or open the release in your browser.";
        }
        catch (Exception ex)
        {
            updateFeedback.Title = "Update could not be installed";
            updateFeedback.Detail = ex.Message;
        }
        finally
        {
            updateCancellation = null;
            cancellation.Dispose();
            if (!exiting)
            {
                ShowUpdateState();
                if (exitAfterDownload) { exitAfterDownload = false; ExitManager(); }
            }
        }
    }
}
