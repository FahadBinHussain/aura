using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Windows.Input;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime; // For AsStreamForWrite extension method
using System.IO; // For MemoryStream
using Windows.Storage.Streams; // For InMemoryRandomAccessStream
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Aura.Services;

namespace Aura.Models
{
    // Model for wallpaper items
    public class WallpaperItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty; // URL for the thumbnail
        public string FullPhotoUrl { get; set; } = string.Empty; // URL for the full size image
        public string SourceUrl { get; set; } = string.Empty; // URL for the source webpage
        public string Platform { get; set; } = string.Empty; // Platform source (e.g., "Backiee", "AlphaCoders")
        public string Category { get; set; } = string.Empty; // browsing category it came from (history sticker)
        private BitmapImage _imageSource;
        public BitmapImage ImageSource
        {
            get => _imageSource;
            set
            {
                if (_imageSource != value)
                {
                    _imageSource = value;
                    OnPropertyChanged();
                }
            }
        }
        public string Resolution { get; set; } = string.Empty;

        // Properties for the tags
        public string QualityTag { get; set; } = string.Empty; // e.g., 4K, 8K, UltraHD
        public bool IsAI { get; set; }
        public string Likes { get; set; } = "0";
        public string Downloads { get; set; } = "0";

        public ICommand DownloadCommand { get; set; }

        // Get the appropriate logo path based on the quality tag
        public string QualityLogoPath
        {
            get
            {
                if (QualityTag?.ToUpper() == "4K") return "ms-appx:///Assets/4k_logo.png";
                if (QualityTag?.ToUpper() == "5K") return "ms-appx:///Assets/5k_logo.png";
                if (QualityTag?.ToUpper() == "8K") return "ms-appx:///Assets/8k_logo.png";
                // Add other quality types if needed
                return string.Empty;
            }
        }

        // per-CDN referer: i.pximg.net returns 403 to anything that is not Referer pixiv.net
        // (verified 200 pixiv.net / 403 wall.alphacoders.com - automata-private/www.pixiv.net);
        // every other host keeps the alphacoders-compatible default
        private static string GetRefererFor(string url)
        {
            return url.Contains("i.pximg.net", StringComparison.OrdinalIgnoreCase)
                ? "https://www.pixiv.net/"
                : "https://wall.alphacoders.com/";
        }

        // Async method to load the actual image when needed with WebP support
        public async Task<BitmapImage> LoadImageAsync()
        {

            using (var httpClient = new System.Net.Http.HttpClient())
            {
                // Add browser-like headers
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                httpClient.DefaultRequestHeaders.Add("Accept", "image/webp,image/apng,image/*,*/*;q=0.8");
                httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
                httpClient.DefaultRequestHeaders.Add("Referer", GetRefererFor(ImageUrl));


                // Download the image data, host-routed: curl is cara's ONLY transport
                // (TLS-fingerprint 403), backiee keeps its proven client, everything
                // else continues through HttpClient
                var imageBytes = IsCaraUrl(ImageUrl)
                    ? await CurlClient.GetByteArrayAsync(ImageUrl)
                    : IsBackieeUrl(ImageUrl)
                        ? await BackieeNetworkClient.GetByteArrayAsync(ImageUrl)
                        : await httpClient.GetByteArrayAsync(ImageUrl);

                // decode + re-encode OFF the UI thread: every await above resumes on
                // the app dispatcher (WinUI SynchronizationContext), and ImageSharp's
                // decode + PNG encode are CPU work - run inline there they froze the
                // UI once per thumbnail, and the Categories page streams ~200 fills
                // per open (reported: "lagging super hard"). resize BEFORE encoding
                // too: DecodePixelWidth=500 throws the full-res PNG away anyway, so
                // the encode is thumb-sized now. sources <= 500 wide are left alone -
                // DecodePixelWidth's upscale path then behaves exactly as before.
                var pngBytes = await Task.Run(async () =>
                {
                    using (var inputStream = new MemoryStream(imageBytes))
                    using (var image = await Image.LoadAsync(inputStream))
                    {
                        if (image.Width > 500)
                        {
                            image.Mutate(x => x.Resize(500, 0));
                        }

                        using (var outputStream = new MemoryStream())
                        {
                            await image.SaveAsPngAsync(outputStream);
                            return outputStream.ToArray();
                        }
                    }
                });

                // bytes -> stream the SAME way BackieeCategory does (proven on
                // 19/19 backiee thumbs): direct WriteAsync into the WinRT stream.
                // the old GetOutputStreamAt/AsStreamForWrite/Flush path threw
                // WIC 0x88982F50 with NO message on 3 of 93 thumbnail loads.
                // BitmapImage is a DependencyObject - created back on the UI
                // thread, while all the CPU work above ran on the pool.
                var bitmap = new BitmapImage();
                bitmap.DecodePixelWidth = 500;

                using (var stream = new InMemoryRandomAccessStream())
                {
                    await stream.WriteAsync(pngBytes.AsBuffer());
                    stream.Seek(0);
                    await bitmap.SetSourceAsync(stream);
                }

                return bitmap;
            }
        }

        // Method to load the full image with WebP support
        public async Task<BitmapImage> LoadFullImageAsync()
        {
            try
            {

                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    // Add browser-like headers
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                    httpClient.DefaultRequestHeaders.Add("Accept", "image/webp,image/apng,image/*,*/*;q=0.8");
                    httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
                    httpClient.DefaultRequestHeaders.Add("Referer", GetRefererFor(FullPhotoUrl));

                    // Download the image data, host-routed: curl is cara's ONLY transport
                    // (TLS-fingerprint 403), backiee keeps its proven client, everything
                    // else continues through HttpClient
                    var imageBytes = IsCaraUrl(FullPhotoUrl)
                        ? await CurlClient.GetByteArrayAsync(FullPhotoUrl)
                        : IsBackieeUrl(FullPhotoUrl)
                            ? await BackieeNetworkClient.GetByteArrayAsync(FullPhotoUrl)
                            : await httpClient.GetByteArrayAsync(FullPhotoUrl);

                    // decode + re-encode OFF the UI thread - same dispatcher freeze as
                    // the thumbnail path (a detail page opened as one big stutter while
                    // the full-size PNG re-encode ran inline). no resize here: the full
                    // image IS the point.
                    var pngBytes = await Task.Run(async () =>
                    {
                        using (var inputStream = new MemoryStream(imageBytes))
                        using (var image = await Image.LoadAsync(inputStream))
                        {
                            using (var outputStream = new MemoryStream())
                            {
                                await image.SaveAsPngAsync(outputStream);
                                return outputStream.ToArray();
                            }
                        }
                    });

                    // same proven stream path as LoadImageAsync (the old
                    // GetOutputStreamAt/AsStreamForWrite/FlushAsync form threw WIC
                    // 0x88982F50 with NO message on 3 of 93 thumbnail loads - it is
                    // the same WIC API here); BitmapImage stays on the UI thread.
                    var bitmap = new BitmapImage();

                    using (var stream = new InMemoryRandomAccessStream())
                    {
                        await stream.WriteAsync(pngBytes.AsBuffer());
                        stream.Seek(0);
                        await bitmap.SetSourceAsync(stream);
                    }

                    return bitmap;
                }
            }
            catch (Exception ex)
            {

                // Return null instead of throwing - let the calling code handle this
                return null;
            }
        }

        public WallpaperItem()
        {
            // Initialize the download command
            DownloadCommand = new RelayCommand(_ =>
            {
                // This would download the wallpaper
                // Not implemented in this placeholder version
            });
        }

        private static bool IsBackieeUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   uri.Host.EndsWith("backiee.com", StringComparison.OrdinalIgnoreCase);
        }

        // cara.app / images.cara.app: .NET's TLS fingerprint gets 403 (bot management),
        // curl.exe passes - those URLs only ever go through CurlClient (no fallback)
        private static bool IsCaraUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   (uri.Host.Equals("cara.app", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.EndsWith(".cara.app", StringComparison.OrdinalIgnoreCase));
        }
    }

    // Simple RelayCommand implementation for the DownloadCommand
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public event EventHandler CanExecuteChanged;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);

        public void Execute(object parameter) => _execute(parameter);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
