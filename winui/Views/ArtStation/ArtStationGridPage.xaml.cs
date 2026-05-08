using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Aura.Models;
using Aura.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Aura.Views.ArtStation
{
    public sealed partial class ArtStationGridPage : Page
    {
        private readonly ObservableCollection<WallpaperItem> _projects = new();
        private readonly ArtStationService _artStationService = new();
        private int _currentPage = 1;
        private bool _isLoading;
        private bool _hasMoreProjects = true;
        private string _currentSorting = "trending";

        public ArtStationGridPage()
        {
            InitializeComponent();
            ProjectsGridView.ItemsSource = _projects;
        }

        protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (_projects.Count == 0)
            {
                await LoadProjectsAsync();
            }
        }

        private async void SortingButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            var sorting = button.Tag?.ToString() ?? "trending";
            if (sorting == _currentSorting && _projects.Count > 0)
            {
                return;
            }

            _currentSorting = sorting;
            _currentPage = 1;
            _hasMoreProjects = true;
            _projects.Clear();

            UpdateSortingButtonStyles();
            PageTitleTextBlock.Text = _currentSorting == "latest" ? "ArtStation latest" : "ArtStation trending";
            await LoadProjectsAsync();
        }

        private async Task LoadProjectsAsync()
        {
            if (_isLoading || !_hasMoreProjects)
            {
                return;
            }

            try
            {
                _isLoading = true;
                LoadingProgressBar.Visibility = Visibility.Visible;
                StatusInfoBar.IsOpen = false;

                var newProjects = await _artStationService.GetProjectsAsync(_currentSorting, _currentPage);
                if (newProjects.Count == 0)
                {
                    _hasMoreProjects = false;
                    if (_projects.Count == 0)
                    {
                        ShowStatus("No ArtStation projects were returned. Please try again later.", InfoBarSeverity.Warning);
                    }
                    return;
                }

                foreach (var project in newProjects)
                {
                    _projects.Add(project);
                }

                _currentPage++;
            }
            catch (Exception ex)
            {
                ShowStatus($"ArtStation failed to load: {ex.Message}", InfoBarSeverity.Error);
            }
            finally
            {
                _isLoading = false;
                LoadingProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private async void MainScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer &&
                !e.IsIntermediate &&
                scrollViewer.ScrollableHeight > 0 &&
                scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 300)
            {
                await LoadProjectsAsync();
            }
        }

        private async void ProjectsGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not WallpaperItem project)
            {
                return;
            }

            try
            {
                LoadingProgressBar.Visibility = Visibility.Visible;
                var detailedProject = await _artStationService.GetProjectDetailsAsync(project);
                Frame.Navigate(typeof(ArtStationDetailPage), detailedProject);
            }
            catch (Exception ex)
            {
                ShowStatus($"Could not open this ArtStation project: {ex.Message}", InfoBarSeverity.Error);
            }
            finally
            {
                LoadingProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void ProjectsGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                return;
            }

            if (args.Phase == 0)
            {
                args.RegisterUpdateCallback(ShowProjectImage);
                args.Handled = true;
            }
        }

        private async void ShowProjectImage(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.Item is not WallpaperItem project)
            {
                return;
            }

            var templateRoot = args.ItemContainer.ContentTemplateRoot as Grid;
            var image = templateRoot?.FindName("ItemImage") as Image;
            if (image == null)
            {
                return;
            }

            var requestKey = $"{project.Id}_{args.ItemContainer.GetHashCode()}";
            image.Tag = requestKey;

            try
            {
                var bitmap = await project.LoadImageAsync();
                if (image.Tag?.ToString() == requestKey && bitmap != null)
                {
                    image.Source = bitmap;
                }
            }
            catch
            {
                if (image.Tag?.ToString() == requestKey)
                {
                    image.Source = new BitmapImage(new Uri("ms-appx:///Assets/placeholder-wallpaper-1000.png"));
                }
            }
        }

        private void ProjectsWrapGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is not ItemsWrapGrid wrapGrid)
            {
                return;
            }

            var availableWidth = e.NewSize.Width;
            var columns = Math.Max(1, Math.Min(6, (int)(availableWidth / 300)));
            var finalWidth = Math.Max(200, (availableWidth - ((columns - 1) * 8)) / columns);

            wrapGrid.ItemWidth = finalWidth;
            wrapGrid.ItemHeight = finalWidth * 9 / 16;
            wrapGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        private void UpdateSortingButtonStyles()
        {
            ResetSortingButton(TrendingButton);
            ResetSortingButton(LatestButton);

            var selectedButton = _currentSorting == "latest" ? LatestButton : TrendingButton;
            selectedButton.Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            selectedButton.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
        }

        private static void ResetSortingButton(Button button)
        {
            button.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
            button.Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        }

        private void ShowStatus(string message, InfoBarSeverity severity)
        {
            StatusInfoBar.Message = message;
            StatusInfoBar.Severity = severity;
            StatusInfoBar.IsOpen = true;
        }
    }
}
