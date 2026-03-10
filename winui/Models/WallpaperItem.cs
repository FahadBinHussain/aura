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
using System.ComponentModel;
using System.Runtime.CompilerServices;

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

        // Async method to load the actual image when needed with WebP support
        public async Task<BitmapImage> LoadImageAsync()
        {
            System.Diagnostics.Debug.WriteLine($"LoadImageAsync: Starting to load image from URL: {ImageUrl}");

            using (var httpClient = new System.Net.Http.HttpClient())
            {
                // Add browser-like headers
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                httpClient.DefaultRequestHeaders.Add("Accept", "image/webp,image/apng,image/*,*/*;q=0.8");
                httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
                httpClient.DefaultRequestHeaders.Add("Referer", "https://wall.alphacoders.com/");

                System.Diagnostics.Debug.WriteLine($"LoadImageAsync: Making HTTP request to {ImageUrl}");

                // Download the image data
                var imageBytes = await httpClient.GetByteArrayAsync(ImageUrl);
                System.Diagnostics.Debug.WriteLine($"LoadImageAsync: Downloaded {imageBytes.Length} bytes for {ImageUrl}");

                // Convert WebP to PNG using ImageSharp
                using (var inputStream = new MemoryStream(imageBytes))
                {
                    System.Diagnostics.Debug.WriteLine($"LoadImageAsync: Converting image format for {ImageUrl}");

                    // Load the image using ImageSharp (supports WebP)
                    using (var image = await Image.LoadAsync(inputStream))
                    {
                        using (var outputStream = new MemoryStream())
                        {
                            // Convert to PNG
                            await image.SaveAsPngAsync(outputStream);
                            outputStream.Position = 0;

                            System.Diagnostics.Debug.WriteLine($"LoadImageAsync: Converted to PNG, size: {outputStream.Length} bytes");

                            // Create BitmapImage from PNG data
                            var bitmap = new BitmapImage();
                            bitmap.DecodePixelWidth = 500;

                            // Convert to IRandomAccessStream
                            var randomAccessStream = new InMemoryRandomAccessStream();
                            var raOutputStream = randomAccessStream.GetOutputStreamAt(0);
                            await outputStream.CopyToAsync(raOutputStream.AsStreamForWrite());
                            await raOutputStream.FlushAsync();

                            // Set bitmap source
                            await bitmap.SetSourceAsync(randomAccessStream);
                            System.Diagnostics.Debug.WriteLine($"LoadImageAsync: Successfully created bitmap for {ImageUrl}");

                            return bitmap;
                        }
                    }
                }
            }
        }

        // Method to load the full image with WebP support
        public async Task<BitmapImage> LoadFullImageAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Loading full image from URL: {FullPhotoUrl}");

                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    // Add browser-like headers
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                    httpClient.DefaultRequestHeaders.Add("Accept", "image/webp,image/apng,image/*,*/*;q=0.8");
                    httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
                    httpClient.DefaultRequestHeaders.Add("Referer", "https://wall.alphacoders.com/");

                    // Download the image data
                    var imageBytes = await httpClient.GetByteArrayAsync(FullPhotoUrl);
                    System.Diagnostics.Debug.WriteLine($"Downloaded {imageBytes.Length} bytes for full image");

                    // Convert WebP to PNG using ImageSharp
                    using (var inputStream = new MemoryStream(imageBytes))
                    {
                        // Load the image using ImageSharp (supports WebP)
                        using (var image = await Image.LoadAsync(inputStream))
                        {
                            using (var outputStream = new MemoryStream())
                            {
                                // Convert to PNG
                                await image.SaveAsPngAsync(outputStream);
                                outputStream.Position = 0;
                                System.Diagnostics.Debug.WriteLine($"Converted full image to PNG, size: {outputStream.Length} bytes");

                                // Create BitmapImage from PNG data
                                var bitmap = new BitmapImage();

                                // Convert to IRandomAccessStream
                                var randomAccessStream = new InMemoryRandomAccessStream();
                                var raOutputStream = randomAccessStream.GetOutputStreamAt(0);
                                await outputStream.CopyToAsync(raOutputStream.AsStreamForWrite());
                                await raOutputStream.FlushAsync();

                                // Set bitmap source
                                await bitmap.SetSourceAsync(randomAccessStream);
                                System.Diagnostics.Debug.WriteLine($"Successfully created full image bitmap");

                                return bitmap;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading full image from {FullPhotoUrl}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

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
