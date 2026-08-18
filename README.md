# MediaOverlay

MediaOverlay is a lightweight Windows desktop application that displays a sleek, customizable overlay for your currently playing media. Built with C# and WPF on .NET 10, it hooks into the Windows Global System Media Transport Controls to seamlessly show track information and album artwork.

## Features

- **Now Playing Information:** Displays track title, artist name, and album artwork as soon as a song changes.
- **Dynamic Theming:** Automatically extracts the dominant color from the album artwork and uses it as the overlay's border color.
- **Spotify Integration:** Optional toggle to exclusively listen to and display media from Spotify.
- **Click-Through (Locked Position):** Lock the overlay in place to make it fully click-through, ensuring it never interrupts your workflow.
- **Global ESC Hiding:** Quickly hide the overlay at any time by pressing the `ESC` key (can be disabled).
- **System Tray Controls:** Easily manage all settings (Start with Windows, Keep visible, Reset position, etc.) from a convenient system tray icon.
- **Auto-Hide:** Automatically fades out after a few seconds unless the "Keep overlay visible" option is enabled.
- **Advanced Settings:** Open the advanced JSON settings file directly from the tray menu to tweak properties like overlay duration, opacity, and artwork visibility.

## Requirements

- **Operating System:** Windows 10 (Build 19041 or later) or Windows 11
- **Runtime:** [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

## Installation and Usage

**Option 1: Download Pre-compiled Release**
1. Go to the [Releases](../../releases) section of this repository.
2. Download the latest executable/zip file.
3. Extract (if necessary) and run the `MediaOverlay` application.

**Option 2: Build from Source**
1. Clone or download the repository.
2. Open `MediaOverlay.sln` in your preferred IDE.
3. Build and run the project.

### Getting Started
1. Once running, an icon will appear in your system tray. 
2. Play some music (e.g., on Spotify) and the overlay will automatically pop up in the top-right corner of your screen!

Right-click the system tray icon to configure your preferences, lock the overlay's position, or enable "Start with Windows".

## Advanced Settings

Advanced settings are stored in `%AppData%\MediaOverlay\settings.json`. You can modify the following properties to fine-tune the overlay:
- `SecondsShown`: Duration (in seconds) the overlay stays visible before fading out.
- `OverlayOpacity`: The overall transparency of the window (between 0.0 and 1.0).
- `ShowAlbumArt`: Toggle album artwork visibility.
- `ShowBackgroundArt`: Toggle the blurred background artwork visibility.

## Technologies Used

- **C# / WPF**
- **.NET 10**
- **Windows Runtime API (WinRT)** for `GlobalSystemMediaTransportControlsSessionManager`
- **Win32 API** for global keyboard hooking and click-through transparency

## Disclaimer

This project was created with the help of AI. Contributions and feedback are welcome to improve the project further.