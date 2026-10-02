using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Aura.Models;
using Aura.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace Aura.Views.Backiee
{
    public sealed partial class CategoriesPage : Page
    {
        private const string CategoriesUrl = "https://backiee.com/categories";

        private readonly ObservableCollection<BackieeCategory> _categories;
        private bool _isLoading;

        public CategoriesPage()
        {
            this.InitializeComponent();
            _categories = new ObservableCollection<BackieeCategory>();
            CategoriesGridView.ItemsSource = _categories;
            Loaded += CategoriesPage_Loaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
        }

        private async void CategoriesPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadCategoriesAsync();
        }

        private async Task LoadCategoriesAsync()
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;
            _categories.Clear();
            ErrorTextBlock.Visibility = Visibility.Collapsed;
            LoadingProgressBar.Visibility = Visibility.Visible;

            try
            {
                var html = await BackieeNetworkClient.GetStringAsync(CategoriesUrl);
                var parsed = BackieeHtmlParser.ParseCategories(html);

                if (parsed.Count == 0)
                {
                    ShowError("backiee returned no categories - the /categories markup may have changed.");
                    return;
                }

                foreach (var category in parsed)
                {
                    _categories.Add(category);
                }

                await LoadThumbnailsAsync(parsed);
            }
            catch (Exception ex)
            {
                ShowError($"Couldn't load categories: {ex.Message}");
            }
            finally
            {
                LoadingProgressBar.Visibility = Visibility.Collapsed;
                _isLoading = false;
            }
        }

        private async Task LoadThumbnailsAsync(List<BackieeCategory> categories)
        {
            int failures = 0;

            var tasks = new List<Task>();
            foreach (var category in categories)
            {
                tasks.Add(LoadThumbnailAsync(category, () => failures++));
            }

            await Task.WhenAll(tasks);

            if (categories.Count > 0 && failures == categories.Count)
            {
                ShowError("Every category thumbnail failed to load - backiee is likely blocking image requests.");
            }
        }

        private async Task LoadThumbnailAsync(BackieeCategory category, Action onFailure)
        {
            try
            {
                category.ImageSource = await category.LoadImageAsync();
            }
            catch
            {
                onFailure();
            }
        }

        private void ShowError(string message)
        {
            ErrorTextBlock.Text = message;
            ErrorTextBlock.Visibility = Visibility.Visible;
        }

        private void CategoriesGridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is BackieeCategory category)
            {
                this.Frame.Navigate(typeof(CategoryWallpapersPage), category);
            }
        }

        private void CategoriesWrapGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is ItemsWrapGrid wrapGrid)
            {
                double availableWidth = e.NewSize.Width;

                double desiredItemWidth = 260;
                double itemMargin = 8;

                int columnsCount = Math.Max(1, (int)(availableWidth / desiredItemWidth));
                columnsCount = Math.Min(columnsCount, 6);
                wrapGrid.MaximumRowsOrColumns = columnsCount;

                double totalMarginWidth = (columnsCount - 1) * itemMargin;
                double newItemWidth = (availableWidth - totalMarginWidth) / columnsCount;
                double finalWidth = Math.Max(200, newItemWidth);

                double aspectRatio = 16.0 / 9.0;
                double imageHeight = finalWidth / aspectRatio;

                wrapGrid.ItemWidth = finalWidth;
                wrapGrid.ItemHeight = imageHeight + 44;

                wrapGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
        }
    }
}
