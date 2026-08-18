using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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

namespace MediaOverlay;

public partial class MainWindow : Window
{
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private string _lastTrackId = string.Empty;
    
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }
    
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionTopRight();
        EnableClickThrough();

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

        _currentSession = _sessionManager?.GetCurrentSession();

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
            if (artwork != null)
            {
                Color accentColor = GetDominantColor(artwork);
                OverlayBorder.BorderBrush = new SolidColorBrush(accentColor);
            }
            else
            {
                Color defaultSpotifyGreen = Color.FromRgb(29, 185, 84);
                OverlayBorder.BorderBrush = new SolidColorBrush(defaultSpotifyGreen);
            }
            
            var sb = (Storyboard)Resources["PopAndGlowStoryboard"];
            sb.Begin();
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

    private void EnableClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
    }
    #endregion
}
