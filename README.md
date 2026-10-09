# Tiago Music

A small Windows music player for choosing a playlist, shuffling your music, and reading lyrics while playing along.

## Download and run

Download **Tiago-Music-Windows-v1.0.0.zip** from the [latest release](https://github.com/TiagoCalvados/tiago-music/releases/latest), extract it to a folder you can write to, then open **Tiago Music.exe**. Keep the extracted app folder together. No installer or account is needed.

Built for Windows with .NET Framework 4 or later. Playback uses Windows' installed audio decoders. Music and saved lyrics work offline; fetching new lyrics needs internet access.

## Listening

- Click **Playlists** to choose a saved playlist, or **Add folder** to import your music.
- Opening the app starts a random song from your last selected playlist. **Shuffle switches on at every launch**, even if you switched it off before closing.
- Play/Pause, Previous, Next, seeking, and volume are available in the player. Previous restarts the current song after three seconds; use it again near the beginning to return to the previous song.
- Double-click a song to play it. Shuffle visits every song once before starting another shuffled cycle.
- **Hide lyrics** makes the player narrower.

MP3, WAV, WMA, and M4A files are accepted where Windows has the required decoder. Protected recordings may not play.

## Lyrics

Lyrics update when the song changes. Local `.lrc` files alongside your music and saved lyrics load first. With **Auto** enabled, the player looks up the current song on [LRCLIB](https://lrclib.net), prefers timed lyrics, and saves the result for offline use.

Automatic lookup sends the song's artist/title to LRCLIB. Your audio files remain on your PC. Turn **Auto** off for local lyrics and manual searches only. Lyrics are not available for every song or recording.

Timed lyrics highlight and scroll to the current line, including after you seek. Use **−0.5s / +0.5s** to adjust timing for recordings with a different intro; **Reset** returns to zero. Plain lyrics display without synchronized highlighting.

**Find lyrics** lets you manually choose another recording. **Load LRC** accepts local `.lrc` and `.txt` lyric files.

## Bring music from a Samsung phone

1. In Samsung Music, use **Settings → Manage playlists → Export my playlists** when available. Menu labels vary by version.
2. Connect your unlocked phone to the PC with a USB data cable and choose **File transfer** on the phone.
3. In File Explorer, **copy** your music and exported `.m3u` / `.m3u8` playlist files into a PC folder, keeping album folders together where possible.
4. Choose **Add folder** in Tiago Music. It searches subfolders for playlists and songs, and matches phone paths to copied local files.

Copying songs alone does **not** preserve custom playlists saved only inside Samsung Music. Export those playlists first. With songs but no exported playlist, the app offers the folder as a playlist. Add each named music folder separately if you want one playlist per folder.

Missing or ambiguous entries appear under **Playlists → Import details**. Adding the same folder again refreshes it. Original songs and playlist files remain intact; removing a playlist only removes it from the player's saved library.

[Samsung's playlist export instructions](https://www.samsung.com/levant/support/apps-services/how-to-import-and-export-samsung-music-playlists/)

## Saved settings

The app stores playlists, preferences, and cached lyrics in a **Library** folder beside the executable. Keep that folder with the app when updating. Music files are referenced in their existing folders, so keep those folders in place too.

The optional **Greeting** checkbox enables the offline Windows voice greeting: “Tiago, which playlist would you like to listen to?” There is no voice recognition or microphone use.

## Build from source

Run `Build.ps1` in PowerShell on Windows. It uses the installed .NET Framework C# compiler and WPF/System.Speech assemblies; no downloaded packages or dotnet CLI are required.

```powershell
./Build.ps1
```

`Package.ps1` creates a ZIP containing the app, configuration, icon, instructions, and license. It copies only those named files.

```powershell
./Package.ps1
```

## Validation

Offline checks cover phone-path matching, duplicate filenames, playlist encodings, preservation of originals, shuffle/history, settings backup recovery, startup shuffle, timed lyrics, lyric caching, and canceled lookups. Windows audio checks cover MP3 decoding, pause/resume, seeking, volume, and track-end notification. Live lyric checks verify matching, timed parsing, and offline caching.

Run self-tests in a scratch directory:

```powershell
Start-Process './Tiago Music.exe' -ArgumentList '--self-test', 'C:\Temp\TiagoTests' -Wait
Get-Content 'C:\Temp\TiagoTests\self-test-results.txt'
```

Some tests require an installed Windows speech voice and access to audio components.

## License

The player source code is [MIT licensed](LICENSE). Music and lyric content have their own rights and are not included in this repository or download.
