# Dual Spotify

Dual Spotify opens two accounts in the official Windows Spotify desktop client. Both profiles run the same Spotify executable under the current Windows user.

## Open both profiles

1. Install the standalone Windows client from [Spotify](https://www.spotify.com/download/windows/).
2. Download **Dual-Spotify-Windows.zip** from [GitHub Releases](https://github.com/w-rakeeb/dual-spotify/releases/latest), extract the entire ZIP to a writable folder, and open **Dual Spotify.exe**. Keep its files together. No administrator installation is required.
3. Open **Main Spotify** and **Second Spotify** from Profiles. Sign in to the second account in the second Spotify window.
4. Use **Settings → Add Second Spotify desktop shortcut** for direct access.

**Quit** on the second card requests a graceful close of the identified second window. If Spotify keeps the client in its notification area, use that client's own Quit Spotify menu. Closing the dashboard can hide it to the tray. **Exit Dual Spotify** closes the manager. The players have their own lifetimes.

## Files and compatibility

- The launcher runs Spotify with `--mu`, a dedicated cache, process-local AppData folders and direct startup. It keeps the second profile in `Data\Profiles\Second Spotify`. That folder can contain account data.
- Windows 10 or 11 x64 with .NET Framework 4.8 and the standalone Spotify install in Roaming AppData is the target. The Microsoft Store client has a different install model.
- For a custom standalone installation, open **Settings → Locate Spotify** and select its `Spotify.exe`.
- **Updates → Check for updates** checks the public GitHub releases. **Download and install** verifies the release ZIP's SHA-256, backs up app files, and restarts the launcher. Profile data stays in the app folder. Spotify manages updates for its own player.
- UI components and the shell were adapted from the local Dual Steam project under its MIT license. Spotify branding belongs to Spotify.
- The profile artwork is the [green Spotify icon](https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/svg/spotify.svg) and the [dark Spotify icon](https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/svg/spotify-dark.svg) from Dashboard Icons. Run `powershell.exe -STA -File .\Source\Render-ProfileIcons.ps1` to regenerate the WPF bitmaps.
- Second Spotify uses a silver rendering of the supplied circle-and-wave artwork for its profile card, taskbar button, shortcut, and launcher tray control. Spotify can still supply its own green icon to Windows for the native hover thumbnail.

## Build and verify

Run `Source\Build.ps1`, then `Tests\Run.ps1` and `Source\Package.ps1`. Use `Tests\Run.ps1 -Live` with Main Spotify running to exercise the visible launcher, direct command, and second-client quit and reopen. Share the clean ZIP from Releases. The development folder can contain Spotify sign-ins, preferences, and backups.
