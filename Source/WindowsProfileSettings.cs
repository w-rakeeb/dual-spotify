using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using DualSpotify.UI;
using Forms = System.Windows.Forms;

public partial class App
{
    FrameworkElement WindowsProfileSettings()
    {
        var body = Stack();
        try
        {
            var selected = ManagedSecond.SelectedAccount();
            var catalog = ManagedSecond.Profiles();
            if (selected == null)
                body.Children.Add(Text("Choose a Windows profile for Second Spotify."));
            else
            {
                body.Children.Add(Text(ProfileName(selected), 16, "text.primary", true));
                if (selected.SteamHome != null)
                    body.Children.Add(Text("Managed by Dual Steam", 12, "text.secondary"));
            }

            var actions = ToolActions(
                Ui.Button("Add Windows profile…", true, () => AddWindowsProfile(catalog, selected)),
                ActionButton("Verify profile", () => RunWindowsProfileAction(ManagedSecond.Verify, "Windows profile verified.")),
                ActionButton("Remove selection", () => ChangeWindowsProfile(ManagedSecond.RemoveSelection, "Profile removed from Second Spotify.")));
            actions.Children[1].IsEnabled = selected != null;
            actions.Children[2].IsEnabled = selected != null;
            body.Children.Add(actions);

            if (selected != null && selected.SteamHome == null)
            {
                var appearance = new Expander { Header = "Windows account settings", Margin = new Thickness(0, 12, 0, 0) };
                Theme.Bind(appearance, Control.ForegroundProperty, "text.primary");
                appearance.Content = WindowsProfileAppearance(selected);
                body.Children.Add(appearance);
            }
        }
        catch (Exception ex) { body.Children.Add(Text(ex.Message)); }
        return SettingsCard("Windows profile", body);
    }

    static string ProfileName(ManagedSecond.Account account)
    {
        var name = account.Name;
        try { name = WindowsProfileAccount.DisplayName(account.Name); }
        catch (System.ComponentModel.Win32Exception) { }
        return name == account.Name && ManagedSecond.ValidName(name)
            ? "Spotify profile · " + name.Substring(name.Length - 4) : name;
    }

    void AddWindowsProfile(ManagedSecond.Account[] catalog, ManagedSecond.Account selected)
    {
        if (busy || exiting) return;
        var dialog = new Window { Owner = window, Title = "Choose a Windows profile", Width = 430,
            SizeToContent = SizeToContent.Height, MaxHeight = Math.Max(220, SystemParameters.WorkArea.Height - 48),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        Theme.Bind(dialog, Control.BackgroundProperty, "bg.window");
        Theme.Bind(dialog, Control.ForegroundProperty, "text.primary");
        dialog.Icon = BitmapFrame.Create(new Uri(Path.Combine(Engine.Home, @"Assets\dual-spotify-green.ico")));
        var body = Stack();
        body.Margin = new Thickness(20);
        dialog.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        Action<Action> choose = action => { dialog.Close(); action(); };
        var savedProfiles = catalog.Where(a => selected == null || a.Sid != selected.Sid).ToArray();
        if (savedProfiles.Length > 0)
            body.Children.Add(Text("Saved profiles", Theme.NoteSize, "text.secondary", true));
        foreach (var profile in savedProfiles)
        {
            var saved = profile;
            var button = ActionButton(ProfileName(saved), () => choose(() => ChangeWindowsProfile(
                () => ManagedSecond.SelectProfile(saved.Sid), "Windows profile selected.")));
            button.ToolTip = saved.SteamHome == null ? saved.Name : "Managed by Dual Steam";
            body.Children.Add(button);
        }
        if (savedProfiles.Length > 0)
            body.Children.Add(Ui.Divider());
        var steam = SteamWindowsProfile.Detect();
        if (steam != null && !catalog.Any(a => a.SteamHome != null && a.SteamHome.Equals(steam, StringComparison.OrdinalIgnoreCase)))
            body.Children.Add(ActionButton("Use detected Dual Steam profile", () => choose(() => SelectSteamProfile(steam))));
        body.Children.Add(ActionButton("Locate Dual Steam…", () => choose(BrowseSteamProfile)));
        body.Children.Add(ActionButton("Create Windows profile", () => choose(() =>
            ChangeWindowsProfile(ManagedSecond.CreateProfile, "Windows profile created."))));
        var cancel = ActionButton("Cancel", dialog.Close);
        cancel.IsCancel = true;
        body.Children.Add(cancel);
        foreach (FrameworkElement item in body.Children)
        {
            item.Margin = new Thickness(0, 0, 0, 8);
            item.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        cancel.HorizontalAlignment = HorizontalAlignment.Right;
        dialog.ShowDialog();
    }

    FrameworkElement WindowsProfileAppearance(ManagedSecond.Account account)
    {
        var body = Stack();
        body.Margin = new Thickness(0, 12, 0, 0);
        var label = Text("Windows display name");
        var name = new TextBox { Text = ProfileName(account), MaxLength = 80,
            Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 6, 0, 8) };
        Theme.Bind(name, Control.BackgroundProperty, "bg.window");
        Theme.Bind(name, Control.ForegroundProperty, "text.primary");
        Theme.Bind(name, Control.BorderBrushProperty, "border.subtle");
        Ui.Name(name, "Windows display name");
        AutomationProperties.SetLabeledBy(name, label);
        body.Children.Add(label);
        body.Children.Add(name);
        var hidden = new SettingRow("Hide from Windows sign-in", "Hide this Windows account from the sign-in screen.")
            { Margin = new Thickness(0), MinHeight = 68 };
        hidden.Toggle.IsChecked = WindowsProfileAccount.Hidden(account.Name);
        body.Children.Add(hidden);
        var apply = ActionButton("Save account settings", () => Safe(() =>
        {
            var displayName = WindowsProfileAccount.ValidateName(name.Text);
            var hide = hidden.Toggle.IsChecked == true;
            RunWindowsProfileAction(() => WindowsProfileAccount.Change(account, "appearance", displayName, hide),
                "Windows account settings saved.");
        }));
        body.Children.Add(ToolActions(apply, ActionButton("Delete Windows account…", RemoveDedicatedProfile)));
        return body;
    }

    void BrowseSteamProfile()
    {
        using (var picker = new Forms.FolderBrowserDialog { Description = "Choose the Dual Steam launcher folder",
            ShowNewFolderButton = false })
            if (picker.ShowDialog() == Forms.DialogResult.OK)
                SelectSteamProfile(picker.SelectedPath);
    }

    void SelectSteamProfile(string home)
    {
        ChangeWindowsProfile(() => ManagedSecond.SelectSteam(home), "Windows profile selected.");
    }

    void ChangeWindowsProfile(Action operation, string success)
    {
        RunWindowsProfileAction(() =>
        {
            RequireSecondClosed();
            operation();
        }, success);
    }

    static void RequireSecondClosed()
    {
        if (Engine.Snapshot()[1].Length > 0)
            throw new IOException("Quit Second Spotify before changing its Windows profile.");
    }

    void RemoveDedicatedProfile()
    {
        if (busy || exiting) return;
        if (MessageBox.Show(window, "Permanently delete this Windows account and its local Spotify data?",
            "Delete Windows account", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
            ChangeWindowsProfile(ManagedSecond.RemoveDedicated, "Windows account deleted.");
    }

    async void RunWindowsProfileAction(Action operation, string success)
    {
        if (busy || exiting) return;
        busy = true;
        try
        {
            await Task.Run(operation);
            if (!exitAfterAction && currentPage == "Settings")
                ShowPage("Settings");
            if (!exitAfterAction)
                toast.Show(success);
        }
        catch (Exception ex) { if (!exitAfterAction) toast.Show(ex.Message); }
        finally { busy = false; }
        if (exitAfterAction)
        {
            exitAfterAction = false;
            ExitManager();
        }
        else
            await Refresh();
    }
}
