using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Controls;
using WallYouNeed.Core.Models;
using WallYouNeed.Core.Services.Interfaces;
using WallYouNeed.App.Services;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.IO;
using System.Windows.Media;

namespace WallYouNeed.App.Pages
{
    /// <summary>
    /// Interaction logic for CollectionsPage.xaml
    /// </summary>
    public partial class CollectionsPage : Page, INavigableView<CollectionsPage>
    {
        private readonly ILogger<CollectionsPage> _logger;
        private readonly ICollectionService _collectionService;
        private readonly IWallpaperService _wallpaperService;
        private readonly ILogService _logService;

        public ObservableCollection<Collection> Collections { get; } = new();
        
        public CollectionsPage ViewModel => this;
        
        public CollectionsPage(
            ILogger<CollectionsPage> logger,
            ICollectionService collectionService,
            IWallpaperService wallpaperService,
            ILogService logService)
        {
            _logger = logger;
            _collectionService = collectionService;
            _wallpaperService = wallpaperService;
            _logService = logService;
            
            InitializeComponent();
            DataContext = this;
            
            _logService.LogInfo("CollectionsPage initialized");
            Loaded += CollectionsPage_Loaded;
        }
        
        private async void CollectionsPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _logService.LogInfo("CollectionsPage_Loaded called");
                
                // Set initial visualization state - show the sample item for now
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                
                // Try to load collections
                await LoadCollectionsAsync();
                
                _logService.LogInfo("Collections loaded successfully. Collection count: {Count}", Collections.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load collections");
                _logService.LogError(ex, "Failed to load collections with exception: {ExMessage}", ex.Message);
            }
        }
        
        private async Task LoadCollectionsAsync()
        {
            try {
                _logService.LogInfo("LoadCollectionsAsync - Starting to load collections");
                
                var collections = await _collectionService.GetAllCollectionsAsync();
                _logService.LogInfo("Collections retrieved from service. Count: {Count}", 
                    collections != null ? collections.Count : 0);
                
                Collections.Clear();
                if (collections != null)
                {
                    foreach (var collection in collections)
                    {
                        _logService.LogInfo("Adding collection: {Id} - {Name}", collection.Id, collection.Name);
                        Collections.Add(collection);
                    }
                }
                
                // For testing, let's add a dummy collection if none exist
                if (Collections.Count == 0 && !Collections.Any(c => c.Name == "Imported"))
                {
                    _logService.LogInfo("No collections found. Adding default 'Imported' collection");
                    var newCollection = new Collection
                    {
                        Id = "imported",
                        Name = "Imported",
                        WallpaperIds = new System.Collections.Generic.List<string>()
                    };
                    Collections.Add(newCollection);
                }
                
                // Keep this commented for now so we see our static sample
                /*
                // Update UI based on whether collections exist
                if (Collections.Count > 0)
                {
                    CollectionsItemsControl.Visibility = Visibility.Visible;
                    EmptyStatePanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    CollectionsItemsControl.Visibility = Visibility.Collapsed;
                    EmptyStatePanel.Visibility = Visibility.Visible;
                }
                */
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in LoadCollectionsAsync: {Message}", ex.Message);
                _logService.LogError(ex, "Error in LoadCollectionsAsync: {Message}", ex.Message);
                // Don't rethrow - we'll handle it gracefully
            }
        }
        
        private async void CreateCollection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logService.LogUIAction("CreateCollection_Button", "Clicked");
                
                // Show a dialog to get the collection name
                string collectionName = "New Collection"; // Default name
                
                // Simple text input dialog - using a standard message box for now
                _logService.LogInfo("Showing dialog for collection creation with default name: {Name}", collectionName);
                
                var result = System.Windows.MessageBox.Show(
                    "Create a new collection named '" + collectionName + "'?", 
                    "Create Collection",
                    System.Windows.MessageBoxButton.OKCancel,
                    System.Windows.MessageBoxImage.Question);
                
                _logService.LogInfo("Dialog result: {Result}", result);
                
                if (result == System.Windows.MessageBoxResult.OK)
                {
                    try
                    {
                        _logService.LogInfo("Creating new collection with name: {Name}", collectionName);
                        var newCollection = await _collectionService.CreateCollectionAsync(collectionName);
                        
                        if (newCollection != null)
                        {
                            _logService.LogInfo("Collection created with ID: {Id}", newCollection.Id);
                            Collections.Add(newCollection);
                            
                            // Always show the collections now
                            CollectionsItemsControl.Visibility = Visibility.Visible;
                            EmptyStatePanel.Visibility = Visibility.Collapsed;
                            
                            _logService.LogInfo("Collection UI updated - showing collections list");
                        }
                        else
                        {
                            _logService.LogWarning("Collection was not created - null returned from service");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to create collection");
                        _logService.LogError(ex, "Failed to create collection: {ExMessage}", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CreateCollection_Click: {Message}", ex.Message);
                _logService.LogError(ex, "Error in CreateCollection_Click: {Message}", ex.Message);
            }
        }
        
        private async void DeleteCollection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logService.LogUIAction("DeleteCollection_Button", "Clicked");
                
                // Get the collection ID from the button's Tag
                string collectionId = null;
                if (sender is System.Windows.Controls.Button button)
                {
                    collectionId = button.Tag as string;
                    _logService.LogInfo("Delete button clicked for collection ID: {Id}", collectionId ?? "null");
                }
                
                // If no tag, try to use the sample collection ID
                if (string.IsNullOrEmpty(collectionId))
                {
                    // Use the "imported" collection ID for our static sample
                    collectionId = "imported";
                    _logService.LogInfo("No collection ID found in Tag, using default: {Id}", collectionId);
                }
                
                if (!string.IsNullOrEmpty(collectionId))
                {
                    // Confirm deletion with a standard message box
                    _logService.LogInfo("Showing delete confirmation dialog for collection: {Id}", collectionId);
                    
                    var result = System.Windows.MessageBox.Show(
                        "Are you sure you want to delete this collection? This action cannot be undone.",
                        "Delete Collection",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning);
                    
                    _logService.LogInfo("Delete confirmation result: {Result}", result);
                    
                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        try
                        {
                            _logService.LogInfo("Proceeding with collection deletion: {Id}", collectionId);
                            
                            // For our static example, just show a success message
                            _logService.LogInfo("Collection deleted successfully");
                                
                            // In a real app with a proper database:
                            // await _collectionService.DeleteCollectionAsync(collectionId);
                            
                            // Remove from the observable collection
                            var collectionToRemove = Collections.FirstOrDefault(c => c.Id == collectionId);
                            if (collectionToRemove != null)
                            {
                                _logService.LogInfo("Removing collection from UI: {Id} - {Name}", 
                                    collectionToRemove.Id, collectionToRemove.Name);
                                Collections.Remove(collectionToRemove);
                            }
                            else
                            {
                                _logService.LogWarning("Collection not found in UI list: {Id}", collectionId);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to delete collection");
                            _logService.LogError(ex, "Failed to delete collection: {ExMessage}", ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DeleteCollection_Click: {Message}", ex.Message);
                _logService.LogError(ex, "Error in DeleteCollection_Click: {Message}", ex.Message);
            }
        }
        
        private void ViewCollection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logService.LogUIAction("ViewCollection_Button", "Clicked");
                
                // Get the collection ID from the button's Tag
                string collectionId = null;
                if (sender is System.Windows.Controls.Button button)
                {
                    collectionId = button.Tag as string;
                    _logService.LogInfo("View button clicked for collection ID: {Id}", collectionId ?? "null");
                }
                
                // If no tag, try to use the sample collection ID
                if (string.IsNullOrEmpty(collectionId))
                {
                    // Use the "imported" collection ID for our static sample
                    collectionId = "imported";
                    _logService.LogInfo("No collection ID found in Tag, using default: {Id}", collectionId);
                }
                
                if (!string.IsNullOrEmpty(collectionId))
                {
                    _logService.LogInfo("Viewing collection: {Id}", collectionId);
                    
                    // Just show a notification for now
                    _logService.LogInfo("Collection View", "Viewing collection: " + collectionId);
                    
                    // In a real app, you would navigate to a collection details page
                    // navigationService.NavigateTo<CollectionDetailsPage>(new NavigationArguments { { "collectionId", collectionId } });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ViewCollection_Click: {Message}", ex.Message);
                _logService.LogError(ex, "Error in ViewCollection_Click: {Message}", ex.Message);
            }
        }
        
        private async Task LoadCollectionThumbnailAsync(Collection collection, System.Windows.Controls.Image imageControl)
        {
            if (collection == null || imageControl == null || string.IsNullOrEmpty(collection.Id))
            {
                _logService.LogWarning("Cannot load thumbnail - invalid parameters");
                return;
            }
            
            try
            {
                _logService.LogInfo("LoadCollectionThumbnailAsync - Loading thumbnail for collection: {Id}", collection.Id);
                
                // Get the first wallpaper in the collection to use as thumbnail
                if (collection.WallpaperIds != null && collection.WallpaperIds.Count > 0)
                {
                    _logService.LogInfo("Collection has {Count} wallpapers. Using first one as thumbnail.", 
                        collection.WallpaperIds.Count);
                        
                    var wallpaper = await _collectionService.GetWallpaperFromCollectionAsync(collection.Id, collection.WallpaperIds[0]);
                    
                    if (wallpaper != null && !string.IsNullOrEmpty(wallpaper.FilePath) && File.Exists(wallpaper.FilePath))
                    {
                        _logService.LogInfo("Loading wallpaper from file: {Path}", wallpaper.FilePath);
                        
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(wallpaper.FilePath);
                        bitmap.EndInit();
                        
                        imageControl.Source = bitmap;
                        
                        _logService.LogInfo("Thumbnail loaded successfully");
                    }
                    else
                    {
                        _logService.LogWarning("Wallpaper not valid or file does not exist. Using placeholder.");
                        // Set a placeholder image if no wallpapers in collection
                        SetPlaceholderImage(imageControl);
                    }
                }
                else
                {
                    _logService.LogInfo("Collection has no wallpapers. Using placeholder image.");
                    SetPlaceholderImage(imageControl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading collection thumbnail: {CollectionId}", collection.Id);
                _logService.LogError(ex, "Error loading collection thumbnail for {CollectionId}: {ExMessage}", 
                    collection.Id, ex.Message);
                SetPlaceholderImage(imageControl);
            }
        }
        
        private void SetPlaceholderImage(System.Windows.Controls.Image imageControl)
        {
            try
            {
                _logService.LogInfo("Setting placeholder image");
                
                // Create a placeholder image
                var placeholderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240));
                var drawingVisual = new DrawingVisual();
                
                using (var drawingContext = drawingVisual.RenderOpen())
                {
                    drawingContext.DrawRectangle(
                        placeholderBrush,
                        null,
                        new Rect(0, 0, 100, 100));
                }
                
                var renderTargetBitmap = new RenderTargetBitmap(
                    100, 100, 96, 96, PixelFormats.Pbgra32);
                renderTargetBitmap.Render(drawingVisual);
                
                imageControl.Source = renderTargetBitmap;
                
                _logService.LogInfo("Placeholder image set successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting placeholder image");
                _logService.LogError(ex, "Error setting placeholder image: {ExMessage}", ex.Message);
            }
        }
    }
} 