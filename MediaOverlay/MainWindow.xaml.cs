using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;

namespace MediaOverlay;

public partial class MainWindow : Window
{
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private string _lastTrackId = string.Empty;
    
    // Settings
    private AppSettings _settings = new();

    private void LoadSettings()
    {
        try
        {
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaOverlay");
            var file = System.IO.Path.Combine(folder, "settings.json");
            if (File.Exists(file))
            {
                var json = File.ReadAllText(file);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null) _settings = loaded;
            }
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaOverlay");
            Directory.CreateDirectory(folder);
            var file = System.IO.Path.Combine(folder, "settings.json");
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json);
        }
        catch { }
    }

    // Tray
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private CancellationTokenSource? _hideTimerCts;
    
    // File Watcher & Menu Fields
    private FileSystemWatcher? _settingsWatcher;
    private System.Windows.Forms.ToolStripMenuItem? _mnuKeepVisible;
    private System.Windows.Forms.ToolStripMenuItem? _mnuEscHiding;
    private System.Windows.Forms.ToolStripMenuItem? _mnuSpotify;
    private System.Windows.Forms.ToolStripMenuItem? _mnuLockPosition;
    private System.Windows.Forms.ToolStripMenuItem? _mnuDynamicBorder;
    
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
        MouseDown += (s, e) => {
            if (!_settings.LockPosition && e.ChangedButton == MouseButton.Left)
                DragMove();
        };
        LocationChanged += (s, e) => {
            if (IsLoaded)
            {
                _settings.WindowLeft = Left;
                _settings.WindowTop = Top;
                SaveSettings();
            }
        };
    }
    
    private void OnClosed(object? sender, EventArgs e)
    {
        if (_hookID != IntPtr.Zero)
            UnhookWindowsHookEx(_hookID);
            
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
    
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadSettings();
        if (double.IsNaN(_settings.WindowLeft) || double.IsNaN(_settings.WindowTop))
        {
            PositionTopRight();
        }
        else
        {
            Left = _settings.WindowLeft;
            Top = _settings.WindowTop;
        }

        ApplyVisualSettings();
        ApplyClickThroughState();
        SetupGlobalHook();
        SetupTrayIcon();
        SetupSettingsWatcher();

        _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _sessionManager.CurrentSessionChanged += async (_, _) => await UpdateCurrentSessionAsync();

        await UpdateCurrentSessionAsync();
        
    }

    private void PositionTopRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 10;
        Top = workArea.Top + 10;
    }
    
    private async Task UpdateCurrentSessionAsync()
    {
        if (_currentSession != null)
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;

        if (_settings.ListenOnlyForSpotify && _sessionManager != null)
        {
            var sessions = _sessionManager.GetSessions();
            _currentSession = sessions.FirstOrDefault(s => s.SourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            _currentSession = _sessionManager?.GetCurrentSession();
        }

        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
            await ProcessMediaChangeAsync(_currentSession);
        }
    }
    
    private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        await ProcessMediaChangeAsync(sender);
    }
    
    private async Task ProcessMediaChangeAsync(GlobalSystemMediaTransportControlsSession session)
    {
        if (!session.SourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
            return;
        
        var mediaProperties = await session.TryGetMediaPropertiesAsync();
        if (mediaProperties == null) return;
        
        var media = await session.TryGetMediaPropertiesAsync();
        if (media == null || string.IsNullOrWhiteSpace(media.Title))
            return;
        
        string trackKey = $"{media.Title}-{media.Artist}";
        if (trackKey == _lastTrackId)
            return;
        
        _lastTrackId = trackKey;
        BitmapImage? artwork = await LoadThumbnailAsync(media.Thumbnail);
        
        Dispatcher.Invoke((Action)(() =>
        {
            TitleText.Text = media.Title;
            ArtistText.Text = media.Artist;
            AlbumArtImage.ImageSource = artwork;
            BackgroundImage.ImageSource = artwork;

            // Dynamically tint the outline to match the album artwork
            if (artwork != null && _settings.UseDynamicBorderColor)
            {
                Color accentColor = GetDominantColor(artwork);
                OverlayBorder.BorderBrush = new SolidColorBrush(accentColor);
            }
            else
            {
                Color defaultColor = Color.FromRgb(64, 64, 64);
                OverlayBorder.BorderBrush = new SolidColorBrush(defaultColor);
            }
            
            ShowOverlayTemporarily();
        }));
    }
    
    private static async Task<BitmapImage?> LoadThumbnailAsync(IRandomAccessStreamReference? streamRef)
    {
        if (streamRef == null) return null;

        using var stream = await streamRef.OpenReadAsync();
        await using var netStream = stream.AsStreamForRead();

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = netStream;
        bitmap.EndInit();
        bitmap.Freeze();

        return bitmap;
    }
    private static Color GetDominantColor(BitmapImage bitmap)
    {
        try
        {
            // Sample a 1x1 average representation of the image
            var scaled = new TransformedBitmap(bitmap, new ScaleTransform(1.0 / bitmap.PixelWidth, 1.0 / bitmap.PixelHeight));
            byte[] pixels = new byte[4];
            scaled.CopyPixels(pixels, 4, 0);

            // Returns BGRA format
            return Color.FromArgb(255, pixels[2], pixels[1], pixels[0]);
        }
        catch
        {
            return Color.FromRgb(64, 64, 64);
        }
    }

    #region Win32 Click-Through
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int GWL_EXSTYLE = -20;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    private void ApplyClickThroughState()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        if (_settings.LockPosition)
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
        else
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
    }
    #endregion

    #region Global Keyboard Hook for ESC
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private static LowLevelKeyboardProc? _proc;
    private static IntPtr _hookID = IntPtr.Zero;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private void SetupGlobalHook()
    {
        _proc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        if (curModule?.ModuleName != null)
        {
            _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == 0x1B && _settings.EnableEscKeyHiding) // VK_ESCAPE
            {
                Dispatcher.Invoke(HideOverlay);
            }
        }
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    private void HideOverlay()
    {
        var fadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromSeconds(0.2)
        };
        CardContainer.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }
    #endregion

    #region Settings and Tray Icon
    private bool CheckStartWithWindows()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
        return key?.GetValue("MediaOverlay") != null;
    }

    private void SetStartWithWindows(bool enable)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
        if (enable)
        {
            string path = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            key?.SetValue("MediaOverlay", $"\"{path}\"");
        }
        else
        {
            key?.DeleteValue("MediaOverlay", false);
        }
    }

    private void SetupSettingsWatcher()
    {
        var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaOverlay");
        Directory.CreateDirectory(folder);
        _settingsWatcher = new FileSystemWatcher(folder, "settings.json")
        {
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _settingsWatcher.Changed += (s, e) => {
            System.Threading.Thread.Sleep(100); 
            Dispatcher.Invoke(() => {
                LoadSettings();
                ApplyVisualSettings();
                UpdateTrayMenuChecks();
            });
        };
    }

    private void ApplyVisualSettings()
    {
        Opacity = _settings.OverlayOpacity;
        AlbumArtBorder.Visibility = _settings.ShowAlbumArt ? Visibility.Visible : Visibility.Collapsed;
        BackgroundGrid.Visibility = _settings.ShowBackgroundArt ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTrayMenuChecks()
    {
        if (_mnuKeepVisible != null) _mnuKeepVisible.Checked = _settings.KeepOverlayVisible;
        if (_mnuEscHiding != null) _mnuEscHiding.Checked = _settings.EnableEscKeyHiding;
        if (_mnuSpotify != null) _mnuSpotify.Checked = _settings.ListenOnlyForSpotify;
        if (_mnuLockPosition != null)
        {
            _mnuLockPosition.Checked = _settings.LockPosition;
            ApplyClickThroughState();
        }
        if (_mnuDynamicBorder != null) _mnuDynamicBorder.Checked = _settings.UseDynamicBorderColor;
    }

    private void SetupTrayIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Visible = true,
            Text = "Media Overlay"
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();

        var mnuShow = new System.Windows.Forms.ToolStripMenuItem("Show overlay", null, (s, e) => ShowOverlayTemporarily());
        
        _mnuKeepVisible = new System.Windows.Forms.ToolStripMenuItem("Keep overlay visible", null, (s, e) => 
        {
            _settings.KeepOverlayVisible = !_settings.KeepOverlayVisible;
            ((System.Windows.Forms.ToolStripMenuItem)s!).Checked = _settings.KeepOverlayVisible;
            if (!_settings.KeepOverlayVisible) HideOverlay(); // hide immediately if untoggled
            else ShowOverlayTemporarily(); // show if toggled on
            SaveSettings();
        }) { Checked = _settings.KeepOverlayVisible };

        _mnuEscHiding = new System.Windows.Forms.ToolStripMenuItem("Enable ESC key hiding", null, (s, e) => 
        {
            _settings.EnableEscKeyHiding = !_settings.EnableEscKeyHiding;
            ((System.Windows.Forms.ToolStripMenuItem)s!).Checked = _settings.EnableEscKeyHiding;
            SaveSettings();
        }) { Checked = _settings.EnableEscKeyHiding };

        _mnuSpotify = new System.Windows.Forms.ToolStripMenuItem("Listen only for Spotify", null, async (s, e) => 
        {
            _settings.ListenOnlyForSpotify = !_settings.ListenOnlyForSpotify;
            ((System.Windows.Forms.ToolStripMenuItem)s!).Checked = _settings.ListenOnlyForSpotify;
            SaveSettings();
            await UpdateCurrentSessionAsync();
        }) { Checked = _settings.ListenOnlyForSpotify };

        _mnuLockPosition = new System.Windows.Forms.ToolStripMenuItem("Lock position", null, (s, e) => 
        {
            _settings.LockPosition = !_settings.LockPosition;
            ((System.Windows.Forms.ToolStripMenuItem)s!).Checked = _settings.LockPosition;
            ApplyClickThroughState();
            SaveSettings();
        }) { Checked = _settings.LockPosition };

        var mnuStartWindows = new System.Windows.Forms.ToolStripMenuItem("Start with Windows", null, (s, e) => 
        {
            bool currentState = CheckStartWithWindows();
            SetStartWithWindows(!currentState);
            ((System.Windows.Forms.ToolStripMenuItem)s!).Checked = !currentState;
        }) { Checked = CheckStartWithWindows() };

        _mnuDynamicBorder = new System.Windows.Forms.ToolStripMenuItem("Use dynamic border color", null, (s, e) => 
        {
            _settings.UseDynamicBorderColor = !_settings.UseDynamicBorderColor;
            ((System.Windows.Forms.ToolStripMenuItem)s!).Checked = _settings.UseDynamicBorderColor;
            SaveSettings();
            
            // Re-trigger color update
            _ = UpdateCurrentSessionAsync();
        }) { Checked = _settings.UseDynamicBorderColor };

        var mnuResetPos = new System.Windows.Forms.ToolStripMenuItem("Reset position", null, (s, e) => 
        {
            PositionTopRight();
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
            SaveSettings();
        });

        var mnuAdvancedSettings = new System.Windows.Forms.ToolStripMenuItem("Advanced Settings...", null, (s, e) => 
        {
            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MediaOverlay");
            var file = System.IO.Path.Combine(folder, "settings.json");
            
            // Ensure settings exist before opening
            if (!File.Exists(file)) SaveSettings();

            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        });

        var mnuExit = new System.Windows.Forms.ToolStripMenuItem("Exit", null, (s, e) => Application.Current.Shutdown());

        menu.Items.Add(mnuShow);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(_mnuKeepVisible);
        menu.Items.Add(_mnuEscHiding);
        menu.Items.Add(_mnuSpotify);
        menu.Items.Add(_mnuLockPosition);
        menu.Items.Add(mnuStartWindows);
        menu.Items.Add(_mnuDynamicBorder);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(mnuResetPos);
        menu.Items.Add(mnuAdvancedSettings);
        menu.Items.Add(mnuExit);

        _notifyIcon.ContextMenuStrip = menu;
    }

    private async void ShowOverlayTemporarily()
    {
        var sb = (Storyboard)Resources["PopAndGlowStoryboard"];
        sb.Begin();

        _hideTimerCts?.Cancel();
        
        if (!_settings.KeepOverlayVisible)
        {
            _hideTimerCts = new CancellationTokenSource();
            var token = _hideTimerCts.Token;

            try
            {
                await Task.Delay(4000, token);
                if (!token.IsCancellationRequested && !_settings.KeepOverlayVisible)
                {
                    HideOverlay();
                }
            }
            catch (TaskCanceledException) { }
        }
    }
    #endregion
}

public class AppSettings
{
    public bool KeepOverlayVisible { get; set; } = false;
    public bool EnableEscKeyHiding { get; set; } = true;
    public bool ListenOnlyForSpotify { get; set; } = true;
    public bool LockPosition { get; set; } = true;
    public bool UseDynamicBorderColor { get; set; } = true;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    
    // Advanced Settings
    public int SecondsShown { get; set; } = 4;
    public double OverlayOpacity { get; set; } = 1.0;
    public bool ShowAlbumArt { get; set; } = true;
    public bool ShowBackgroundArt { get; set; } = true;
}
