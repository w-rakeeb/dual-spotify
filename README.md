# Dual Spotify

Dual Spotify opens two accounts in the official Windows Spotify desktop client. Main Spotify uses your Windows account. Second Spotify can reuse a verified Dual Steam Windows profile or use a dedicated Spotify profile.

[Download the latest Windows release](https://github.com/w-rakeeb/dual-spotify/releases/latest).

## Open both profiles

1. For Main Spotify, install Spotify from the Microsoft Store or [Spotify's website](https://www.spotify.com/download/windows/).
2. Extract the complete **Dual-Spotify-Windows.zip** into a writable folder and open **Dual Spotify.exe**.
3. Under **Second Spotify**, click **Set up**, then **Add Windows profile**. Pick a saved profile, use a detected Dual Steam profile, or create a new Windows profile. If Dual Steam was extracted elsewhere, use **Locate Dual Steam** and choose its executable. Finish Second Steam setup in Dual Steam before sharing that profile. Creating a Windows profile requires administrator approval.
4. Open **Second Spotify** and sign in inside its window. If the selected Windows profile has no desktop client, the launcher downloads and extracts the signed Spotify installer. The first download may take a few minutes. Main Spotify can stay open during setup.
5. Search for **Dual Spotify** or **Second Spotify** in Start. **Settings → General → Repair shortcuts** also creates the desktop shortcuts.

**Quit** on the second card requests a graceful close of the identified second window. If Spotify keeps the client in its notification area, use that client's own Quit Spotify menu. In **Settings → General**, choose what starts with Windows, whether closing hides the dashboard, and whether Second Spotify keeps running when the manager exits. The launcher leaves Main Spotify open.

## Files and compatibility

- Second Spotify uses the selected Windows account’s AppData. A shared profile uses the Windows credential protected by Dual Steam on this PC. Bring only the launcher ZIP to a different PC and choose its profile there.
- After closing Second Spotify, use **Settings → Windows profiles** to add, switch, or remove the current selection. Removing the selection keeps the Windows profile, its Spotify sign-in, and local data. Pick it under **Add Windows profile** to restore it. **Windows account settings** lets you rename, hide, or delete a Spotify-created Windows account. Manage Dual Steam account settings in Dual Steam.
- Dual Spotify targets Windows 10 or 11 x64 with .NET Framework 4.8. The launcher detects standard standalone installation folders, registered locations, and a running standalone Spotify. If the Profiles page says **Missing**, click **Locate Spotify** and select the installed `Spotify.exe`.
- Main Spotify opens a running client first, then prefers the standalone edition when both editions are installed. To explicitly open Store Spotify, use **Settings → Storage**.
- The Windows-user method uses a separate local Windows profile. Previous launcher profile folders are preserved. Sign in again in the new second window.
- **Settings → Storage** offers **Locate Spotify** and **Detect Spotify** for Main Spotify.
- The dashboard’s close button exits the manager by default. Close to tray remains available in Settings.
- **Settings → Appearance** offers Dark, Light, and System themes and controls whether Process details opens expanded.
- The automatic update option checks stable GitHub releases at startup, when reopening after one minute, and every fifteen minutes. Ordinary failures retry after a minute. Rate limits back off for fifteen minutes. Each version gets one notice per session. Manual checks are in About and Updates.
- **Install update** downloads a cancellable ZIP, verifies SHA-256 and expected files, backs up app files, and restarts the launcher. Use the browser ZIP if needed. Spotify manages updates for its own player.
- UI components and the shell were adapted from the local Dual Steam project under its MIT license. Spotify branding belongs to Spotify.
- The profile artwork is the [green Spotify icon](https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/svg/spotify.svg) and the [dark Spotify icon](https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/svg/spotify-dark.svg) from Dashboard Icons. Run `powershell.exe -STA -File .\Source\Render-ProfileIcons.ps1` to regenerate the WPF bitmaps.
- Second Spotify uses white waves on a dark circle. Run `powershell.exe -STA -File .\Source\Render-ProfileIcons.ps1 -HighResolution` to export the 4096-pixel artwork. Spotify can still supply its own icon for native tray and hover previews.

## Build and verify

In Windows PowerShell, run `Source\Build.ps1`, `Tests\Run.ps1`, `Tests\Settings.ps1`, `Tests\Profiles.ps1`, `Source\Package.ps1`, `Tests\Installation.ps1`, and `Tests\Package.ps1`. Set up Second Spotify first, then use `Tests\Run.ps1 -Live` with Main Spotify running to exercise the visible launcher, direct command, and second-client quit and reopen. Share the clean release ZIP. The development folder can contain Spotify sign-ins, preferences, and backups.
