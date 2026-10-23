# Trill Media Player

<img src="assets/trill-logo-v1.png" alt="Trill Media Player logo" width="96" />

A personal Windows media player for local photos, videos, and audio, with a shared playlist, thumbnail browsing, recently played folders, configurable keyboard shortcuts, and playback speed controls.

## Launch

Double-click **run.cmd**. It builds the app from the files in this folder, then opens Trill. After the first build, you can also launch **dist/Trill.exe** directly.

Requires Windows with **.NET Framework 4.8** and the Windows media components installed. The build uses the compiler included with .NET Framework; it does not download packages or require a .NET SDK. Windows N editions may need their Windows Media Feature Pack for video playback.

To build without opening the app, run this from the project folder in PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The optional `Trill.csproj` can be opened in a compatible Visual Studio installation with the .NET Framework 4.8 targeting pack. `build.ps1` works directly with the installed framework assemblies.

## Use

Open local files or a folder, or drag files into the window. Photos, videos, and audio appear together in the playlist. Select an item to play it; use the playback controls to pause, seek, adjust volume, or change playback speed.

The **Open files** and **Open folder** buttons open the standard Windows dialogs by default. To use the Trill thumbnail browser, turn off **Use Windows file dialogs** in **Settings → Files** and select **Apply**. The choice is remembered on this PC. Both pickers use the same media loading and local-file checks.

The bracket keys pause a video and step backward or forward by one source-frame interval. These bindings can be changed in Settings. Frame stepping needs a compatible Windows preview decoder.

Dragging the timeline shows live video frames in both directions and opens a magnified local timeline. Move the pointer upward into it for finer seeking without holding a key; release to resume the prior playback state. A background Windows decoder reads adjacent frames into a bounded memory cache while playback stays paused. Hold **Shift** while dragging for fine control: one pixel moves approximately one source frame. Releasing seeks playback to the selected position and restores the current play/pause choice. The preview uses up to 1280 pixels on its longest edge; normal playback keeps its original quality. Formats without a compatible preview decoder use throttled native seeking. Preview data is never written to disk, and changing files or closing Trill releases the cache and decoder.

The Trill picker shows thumbnail previews of the folder you are browsing. Everything in the folder is listed, including subfolders and files Trill cannot play, so you can see where you are; double-click a folder to go inside it, and use Up, Alt + Up, or Backspace to go back out. Files Trill cannot play are dimmed and cannot be selected. Tick **Media only** to hide them.

**Windows Explorer…** at the bottom of the open window switches to the standard Windows file dialog for that one action. Windows dialogs are the initial default; your picker preference is saved.

Folders you play are remembered in the **Recently played** list on the left of the open window. Select one to browse it again, or double-click one in the Open folder window to load it straight back as a playlist. **Forget** drops the selected folder and **Clear all** deletes the saved list. Open Settings to choose your keyboard shortcuts, playlist looping, and whether the open buttons use Trill's picker or the Windows one.

### The sidebar

Drag the divider between the sidebar and the stage to resize it. The sidebar lists the current playlist, and it also browses: open a folder and its subfolders appear alongside the photos, videos, and audio inside it, so you can walk down into a folder tree without leaving the player. The arrow button goes back up a level. Search filters the current folder or playlist without removing loaded files. The sort button offers name, newest date modified, and type. Click a folder breadcrumb to jump to an ancestor; scroll over breadcrumbs to reveal earlier path segments. The layout button beside the star switches between larger list thumbnails and a two-column thumbnail grid. Hiding the sidebar, including in fullscreen, gives its entire width back to the media.

Right-click any row, or use the star button, to favourite an item. The star in the sidebar footer shows just your favourites. Favourites are saved on this PC and survive a restart.

Hover over a video thumbnail, then move horizontally to skim through it silently. The preview does not select the video or change active playback. Leaving the thumbnail, scrolling, searching, or changing folders clears it. Hover frames use an independent 8 MiB memory cache and previews up to 320 pixels; formats without a preview decoder retain their static thumbnail.

### Playing

Videos and audio repeat by default. The loop button cycles through loop-off, loop-this-item, and loop-the-whole-playlist; the shuffle button plays the playlist in a random order without repeating anything until every item has had a turn.

To repeat one section rather than the whole file, use the **A-B** button. The first press marks the start at the current position, the second marks the end, and the third clears it; while a section is set, playback jumps back to the start each time it reaches the end. Changing media clears the marks.

The camera button saves the frame you are looking at as a PNG, at the video's own resolution rather than the reduced size used for on-screen previews. Files land in `Pictures\Trill`, named after the video and the position in it, and an existing file is never overwritten. Decoding happens in the background, so a large frame does not freeze playback.

Trill prepares up to two adjacent items in memory. Photos reuse decoded images, and video/audio players are prepared muted and paused before switching. While an uncached visual loads, the previous image remains briefly on screen. Changing the queue or clearing media releases obsolete preloads. Exact switching time still depends on the installed codec and storage.

Photos are different: they stay on screen until you move on. Nothing advances a photo on a timer.

Scroll the wheel over a photo or video to zoom in, and drag to pan around once you are past the fitted view. Scrolling back out stops at the normal fitted size rather than shrinking the media further. The percentage button at the top right resets zoom to fit. Double-click the stage or press F for full screen; the controls hide when the pointer goes still and come back when you move it.

Audio has no picture, so it shows a live spectrum and waveform instead. Trill decodes the file itself, off to one side, and matches the bars to the playing position — it never records or listens to your sound output.

Focus mode (Ctrl + Shift + F, or the focus button) hides the sidebar, transport, zoom button, and status overlays. Move to the left edge for the sidebar, the bottom edge for playback controls, or the top edge for zoom. These controls overlay the media without reserving space. Escape exits focus mode; double-clicking media still toggles fullscreen. Focus mode works in a normal window or fullscreen and is not restored at startup.

To change a shortcut, click its binding and press the keys you want. Duplicate shortcuts are rejected. Escape cancels capture, Clear unassigns an action, and Reset shortcuts restores the defaults. Apply saves your changes on this PC; Cancel leaves the current settings unchanged. Shortcuts work while Trill is focused.

Default shortcuts:

| Action | Shortcut |
| --- | --- |
| Play / pause | Space |
| Next item | Ctrl + Right |
| Previous item | Ctrl + Left |
| Seek forward 10 seconds | Right |
| Seek backward 10 seconds | Left |
| Previous / next frame | [ / ] |
| Slower / faster | Comma / Period |
| Normal speed | Slash |
| Volume up / down | Up / Down |
| Mute | M |
| Full screen | F |
| Focus mode | Ctrl + Shift + F |
| Open files | Ctrl + O |
| Open folder | Ctrl + Shift + O |
| Show / hide playlist | Ctrl + L |
| Loop a section (A-B) | L |
| Save current frame | Ctrl + S |

Seeking and playback speed apply to videos and audio. Your shortcuts, playback preferences, sidebar width and visibility, list/grid choice, sort order, and window size are remembered on this PC. Fullscreen itself is not restored on startup.

The interface uses muted grayscale controls, borders, and glass surfaces. This does not alter the colors of media.

On Windows 11 build 22621 or later, the windows use the compositor's frosted acrylic backdrop, so they take on a blurred tint of whatever sits behind them. Older Windows versions fall back to a solid dark background automatically; nothing else changes.

In **Settings → Files**, **Open data folder** opens the folder containing saved preferences, favorites, and recent folders in Windows File Explorer.

## Offline and private

Trill is a desktop application with no backend, local web server, account, telemetry, or automatic update service. It contains no networking code and does not upload your files. Media stays in its original location. The current playlist and search text are kept only in memory. Preferences and shortcuts are saved locally.

Three files are saved under `%APPDATA%\Trill\`. `recent-folders.txt` holds the folders you have played, one per line with how many playable items it had and when you opened it. `favorites.txt` holds the items you have starred, one full file path per line — so unlike the recents list, this one does record individual file names, because it has to in order to find them again. settings.txt holds preferences and shortcut bindings, without media paths or search terms. All three are plain text you can read or delete yourself; **Clear all** in the open window deletes the recents file, and unstarring an item removes it from favourites. Trill starts with an empty list if either file is missing. Deleting settings.txt while Trill is closed restores defaults. There are no app logs or disk thumbnail caches. Saving a frame is the one action that writes a media file: it creates `Pictures\Trill` if needed and puts a PNG there, only when you ask for it.

Only existing files on local fixed, removable, or optical drives are accepted. URLs, UNC paths, mapped network drives, symbolic links, junctions, and cloud placeholder files are rejected. Folder loading includes supported files directly inside that folder.

Thumbnails are produced inside Trill and held in memory only: photos are decoded at preview size, and video frames are read from a hidden media element. The Trill picker does not request Windows shell thumbnails. When you choose the standard Windows dialogs, Windows manages their previews and shell state. The native dialog requests that opened items not be added to the Windows recent-documents list; the existing recently played folders remain available.

The source and compiled application files exist on your disk, and building writes the executable and build files to `dist/`. While running, Windows and its media components may still keep operating-system caches, paging data, crash information, or other system records. Trill cannot prevent Windows, administrators, security software, or other privileged local software from accessing data, and it cannot guarantee that Microsoft or another party can never obtain it through the operating system. This app provides no backend or sharing mechanism; it does not provide operating-system isolation, encryption, or access controls beyond your Windows file permissions.

## Formats

Photos: PNG, JPG/JPEG, BMP, GIF, TIF/TIFF, and WDP/JXR. GIF images are displayed as still images. Support for an individual file depends on the installed Windows image decoders.

Audio: MP3, M4A, AAC, WAV, WMA, FLAC, OGG/OGA, OPUS, AIF/AIFF, MKA, and AC3. Audio plays through the same Windows media components as video and shows a now-playing panel with a live spectrum instead of a picture. The spectrum needs a Media Foundation decoder for the format; where Windows has none the bars simply stay hidden and playback is unaffected. MP3, WAV, WMA, and M4A/AAC are the most dependable; FLAC, OGG, and OPUS need the matching Windows codecs installed. Audio files have no preview frame, so they appear with a music icon rather than a thumbnail.

Videos use Windows' built-in WPF `MediaElement` playback support. Common MP4 files with H.264 video and AAC audio are the best starting point. Container extensions alone do not guarantee playback: MKV, HEVC/H.265, and other codecs depend on your installed Windows media components. Trill does not bundle a codec engine or fetch codecs from the internet.

## Responsiveness

Full-size photo decoding and custom-picker folder scans run in the background. Rapid navigation cancels obsolete results. The thumbnail grid creates controls only for visible rows and a small buffer, and thumbnail handles release images after cache eviction. Playback position updates at 30 Hz; paused playback avoids unnecessary timeline updates. Photos use high-quality scaling, and interface motion honors the Windows animation preference.

Regression coverage includes 10,000-item grid scrolling, resizing, rapid photo/folder changes, closing during folder loading, mixed-media playback, drag seeking, and native picker configuration. Run `tests/run-player-tests.ps1` and `tests/run-tests.ps1` after building.

## Project structure

- `src/`: application code and window layout.
- `build.ps1`: offline compiler invocation.
- `run.cmd`: build and launch shortcut.
- `dist/`: generated application files; excluded from version control.
