using Microsoft.Data.SqlClient;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Navigation;
using Requisition.Helpers;
using Requisition.Models;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Requisition.Pages
{
    public sealed partial class ProductListPage : Page
    {
        private readonly ProductService _productService;
        private List<Product> _allProducts;
        private List<Product> _filteredProducts;

        // เก็บสถานะการค้นหา
        private static string _lastSearchText = string.Empty;
        private static string _lastSelectedCategory = string.Empty;

        public ProductListPage()
        {
            InitializeComponent();
            _productService = new ProductService();
            _allProducts = new List<Product>();
            _filteredProducts = new List<Product>();
            Loaded += ProductListPage_Loaded;
        }

        private async void ProductListPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadProductsAsync();
            await LoadCategoriesAsync();
            
            // กู้คืนสถานะการค้นหา
            RestoreFilterState();
        }

        private async Task LoadProductsAsync()
        {
            LoadingRing.IsActive = true;
            ProductListView.Visibility = Visibility.Collapsed;

            try
            {
                // Admin list: include inactive so user can see/enable them
                _allProducts = await _productService.GetProductsAsync(includeInactive: true);
                _filteredProducts = _allProducts.ToList();
                ProductListView.ItemsSource = _filteredProducts;

                // Update UI for disabled state per product
                foreach (var p in _allProducts)
                {
                    // If product is disabled until a future date, find its ListViewItem and set the delete button to disabled
                    if (p.DisabledUntil.HasValue && p.DisabledUntil.Value.Date > DateTime.Today)
                    {
                        // We'll rely on binding of IsEnabled in the ItemTemplate if available; otherwise, UI update is minimal here.
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync("เกิดข้อผิดพลาด", $"ไม่สามารถโหลดข้อมูลได้: {ex.Message}");
            }
            finally
            {
                LoadingRing.IsActive = false;
                ProductListView.Visibility = Visibility.Visible;
            }
        }

        // Made non-async because it contains no await; return Task.CompletedTask so callers can still await it.
        private Task LoadCategoriesAsync()
        {
            var categories = _allProducts
                .Select(p => p.Category)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            CategoryFilter.Items.Clear();
            CategoryFilter.Items.Add(new ComboBoxItem { Content = "ทั้งหมด", Tag = "" });
            
            foreach (var category in categories)
            {
                CategoryFilter.Items.Add(new ComboBoxItem { Content = category, Tag = category });
            }

            CategoryFilter.SelectedIndex = 0;

            return Task.CompletedTask;
        }

        private void RestoreFilterState()
        {
            // กู้คืนข้อความค้นหา
            if (!string.IsNullOrEmpty(_lastSearchText))
            {
                SearchBox.Text = _lastSearchText;
            }

            // กู้คืนหมวดหมู่ที่เลือก
            if (!string.IsNullOrEmpty(_lastSelectedCategory))
            {
                for (int i = 0; i < CategoryFilter.Items.Count; i++)
                {
                    if (CategoryFilter.Items[i] is ComboBoxItem item && 
                        item.Tag?.ToString() == _lastSelectedCategory)
                    {
                        CategoryFilter.SelectedIndex = i;
                        break;
                    }
                }
            }

            // ใช้ Filter
            ApplyFilters();
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            _lastSearchText = sender.Text; // บันทึกสถานะ
            ApplyFilters();
        }

        private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryFilter.SelectedItem is ComboBoxItem item)
            {
                _lastSelectedCategory = item.Tag?.ToString() ?? ""; // บันทึกสถานะ
            }
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            var searchText = SearchBox.Text.ToLower();
            var selectedCategory = "";

            if (CategoryFilter.SelectedItem is ComboBoxItem item)
            {
                selectedCategory = item.Tag?.ToString() ?? "";
            }

            _filteredProducts = _allProducts.Where(p =>
            {
                var matchesSearch = string.IsNullOrWhiteSpace(searchText) ||
                                  p.Code.ToLower().Contains(searchText) ||
                                  p.Name.ToLower().Contains(searchText);

                var matchesCategory = string.IsNullOrWhiteSpace(selectedCategory) ||
                                    p.Category == selectedCategory;

                return matchesSearch && matchesCategory;
            }).ToList();

            ProductListView.ItemsSource = _filteredProducts;
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadProductsAsync();
            await LoadCategoriesAsync();
            RestoreFilterState(); // เพิ่ม: กู้คืนสถานะหลัง Refresh
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string productCode)
            {
                Frame.Navigate(typeof(ProductEditPage), productCode);
            }
        }

        private async void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string productCode)
            {
                await ShowCompactHistoryDialogAsync(productCode);
            }
        }
        private async Task ShowCompactHistoryDialogAsync(string productCode)
        {
            try
            {
                var product = _allProducts.FirstOrDefault(p => p.Code == productCode);
                var allHistories = await _productService.GetCombinedProductHistoryAsync(productCode);

                if (allHistories.Count == 0)
                {
                    await ShowErrorDialogAsync("ไม่มีประวัติ", "สินค้านี้ยังไม่มีประวัติการเปลี่ยนแปลง");
                    return;
                }

                var dialog = new ContentDialog
                {
                    Title = $"📜 ประวัติสินค้า: {product?.Name}",
                    CloseButtonText = "ปิด",
                    XamlRoot = Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                var dialogContent = new CompactHistoryDialogContent(allHistories, productCode);
                dialog.Content = dialogContent;

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync("เกิดข้อผิดพลาด", $"ไม่สามารถโหลดประวัติได้: {ex.Message}");
            }
        }

        private async Task ShowErrorDialogAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "ตกลง",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async Task ShowSuccessDialogAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "ตกลง",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
        // Add the following method into the ProductService class (near other update/delete methods)
        private async void DisableButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string code)
            {
                var dialog = new ContentDialog
                {
                    Title = "ปิดใช้งานสินค้า",
                    PrimaryButtonText = "ยืนยัน",
                    CloseButtonText = "ยกเลิก",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                var reasonBox = new TextBox { Header = "เหตุผล", PlaceholderText = "กรุณากรอกเหตุผล", MinWidth = 250 };

                dialog.Content = reasonBox;

                // เปิด/ปิดปุ่มยืนยันตาม validation แบบเรียลไทม์ (เหตุผลอย่างเดียว)
                void UpdatePrimaryEnabled()
                {
                    dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(reasonBox.Text);
                }

                reasonBox.TextChanged += (s, args) => UpdatePrimaryEnabled();

                UpdatePrimaryEnabled();

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    try
                    {
                        // Disable immediately (no disabled-until)
                        await _productService.DisableProductNowAsync(
                            code,
                            reasonBox.Text,
                            Environment.UserName
                        );
                        await LoadProductsAsync();
                        await ShowSuccessDialogAsync("สำเร็จ", "ปิดการใช้งานสินค้าเรียบร้อยแล้ว");
                    }
                    catch (Exception ex)
                    {
                        await ShowErrorDialogAsync("เกิดข้อผิดพลาด", $"ไม่สามารถปิดใช้งานสินค้าได้: {ex.Message}");
                    }
                }
            }
        }

        private async void EnableButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string code)
            {
                var dialog = new ContentDialog
                {
                    Title = "เปิดใช้งานสินค้า",
                    PrimaryButtonText = "ยืนยัน",
                    CloseButtonText = "ยกเลิก",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                var reasonBox = new TextBox { Header = "เหตุผล", PlaceholderText = "กรุณากรอกเหตุผล", MinWidth = 250 };

                dialog.Content = reasonBox;

                // เปิด/ปิดปุ่มยืนยันตาม validation แบบเรียลไทม์
                void UpdatePrimaryEnabled()
                {
                    dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(reasonBox.Text);
                }

                reasonBox.TextChanged += (s, args) => UpdatePrimaryEnabled();
                UpdatePrimaryEnabled();

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    try
                    {
                        await _productService.EnableProductAsync(
                            code,
                            reasonBox.Text,
                            Environment.UserName
                        );
                        await LoadProductsAsync();
                        await ShowSuccessDialogAsync("สำเร็จ", "เปิดใช้งานสินค้าเรียบร้อยแล้ว");
                    }
                    catch (Exception ex)
                    {
                        await ShowErrorDialogAsync("เกิดข้อผิดพลาด", $"ไม่สามารถเปิดใช้งานสินค้าได้: {ex.Message}");
                    }
                }
            }
        }
    }

    public sealed class CompactHistoryDialogContent : UserControl
    {
        private List<ProductModificationHistory> _allHistories;
        private List<ProductModificationHistory> _filteredHistories;
        private int _currentPage = 1;
        private const int _pageSize = 5;

        private ScrollViewer _scrollViewer = null!;
        private StackPanel _historyPanel = null!;
        private Button _firstPageButton = null!;
        private Button _previousPageButton = null!;
        private Button _nextPageButton = null!;
        private Button _lastPageButton = null!;
        private TextBlock _pageInfoText = null!;

        public CompactHistoryDialogContent(List<ProductModificationHistory> histories, string productCode)
        {
            // keep original list for filtering; caller already provided combined histories
            _allHistories = histories;

            // Filter out "disable" actions so they are not shown in the dialog.
            // We match common English/Thai keywords appearing in ChangesDescription.
            _filteredHistories = _allHistories
                .Where(h =>
                {
                    if (string.IsNullOrWhiteSpace(h.ChangesDescription))
                        return true; // keep if no description (likely price history)
                    var desc = h.ChangesDescription.ToLowerInvariant();
                    // exclude if description contains disable-related keywords (English / Thai)
                    if (desc.Contains("disable") || desc.Contains("disabled") || desc.Contains("ปิด"))
                        return false;
                    return true;
                })
                .ToList();

            BuildUI();
            UpdateHistoryDisplay();
        }

        private void BuildUI()
        {
            var mainPanel = new StackPanel { Spacing = 16, Padding = new Thickness(16), MinWidth = 500, MaxWidth = 600 };

            _scrollViewer = new ScrollViewer
            {
                MaxHeight = 400,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            _historyPanel = new StackPanel { Spacing = 12 };
            _scrollViewer.Content = _historyPanel;
            mainPanel.Children.Add(_scrollViewer);

            var paginationGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            paginationGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            paginationGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            paginationGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _firstPageButton = new Button { Content = "⏮️", Padding = new Thickness(8), IsEnabled = false };
            _previousPageButton = new Button { Content = "◀️", Padding = new Thickness(8), IsEnabled = false };
            _firstPageButton.Click += FirstPage_Click;
            _previousPageButton.Click += PreviousPage_Click;
            leftPanel.Children.Add(_firstPageButton);
            leftPanel.Children.Add(_previousPageButton);
            Grid.SetColumn(leftPanel, 0);

            _pageInfoText = new TextBlock
            {
                Text = "หน้า 1 จาก 1",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13
            };
            Grid.SetColumn(_pageInfoText, 1);

            var rightPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _nextPageButton = new Button { Content = "▶️", Padding = new Thickness(8), IsEnabled = false };
            _lastPageButton = new Button { Content = "⏭️", Padding = new Thickness(8), IsEnabled = false };
            _nextPageButton.Click += NextPage_Click;
            _lastPageButton.Click += LastPage_Click;
            rightPanel.Children.Add(_nextPageButton);
            rightPanel.Children.Add(_lastPageButton);
            Grid.SetColumn(rightPanel, 2);

            paginationGrid.Children.Add(leftPanel);
            paginationGrid.Children.Add(_pageInfoText);
            paginationGrid.Children.Add(rightPanel);
            mainPanel.Children.Add(paginationGrid);

            Content = mainPanel;
        }

        // Key used for chronological comparisons: prefer PriceDate, otherwise ModifiedDate
        private DateTime DateKey(ProductModificationHistory h) => h.PriceDate ?? h.ModifiedDate;

        private void UpdateHistoryDisplay()
        {
            _historyPanel.Children.Clear();

            var totalPages = (int)Math.Ceiling(_filteredHistories.Count / (double)_pageSize);
            if (totalPages == 0) totalPages = 1;

            // sortedAscending is used for "previous" lookups (earlier -> later)
            var sortedAscending = _filteredHistories.OrderBy(h => DateKey(h)).ToList();

            // For display we want newest first
            var sortedDescending = sortedAscending.OrderByDescending(h => DateKey(h)).ToList();

            var skip = (_currentPage - 1) * _pageSize;
            var pagedHistories = sortedDescending.Skip(skip).Take(_pageSize).ToList();

            foreach (var history in pagedHistories)
            {
                var card = CreateHistoryCard(history, sortedAscending);
                _historyPanel.Children.Add(card);
            }

            _pageInfoText.Text = $"หน้า {_currentPage} จาก {totalPages} (แสดง {pagedHistories.Count} จาก {_filteredHistories.Count} รายการ)";

            _firstPageButton.IsEnabled = _currentPage > 1;
            _previousPageButton.IsEnabled = _currentPage > 1;
            _nextPageButton.IsEnabled = _currentPage < totalPages;
            _lastPageButton.IsEnabled = _currentPage < totalPages;
        }

        private Border CreateHistoryCard(ProductModificationHistory history, List<ProductModificationHistory> sortedHistories)
        {
            var card = new Border
            {
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1)
            };

            if (history.IsModifiedToday && history.EditCount > 0)
            {
                card.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(25, 255, 165, 0));
            }

            var panel = new StackPanel { Spacing = 8 };

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

            var dateText = new TextBlock
            {
                Text = history.ModifiedDate.ToString("dd/MM/yyyy HH:mm"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 14
            };
            headerPanel.Children.Add(dateText);

            if (history.IsModifiedToday && history.EditCount > 0)
            {
                var editBadge = new Border
                {
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(6, 2, 6, 2)
                };
                editBadge.Child = new TextBlock
                {
                    Text = $"🔶 แก้ไข {history.EditCount}x",
                    FontSize = 10,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                };
                Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(editBadge,
                    $"แก้ไขในวันนี้แล้ว {history.EditCount} ครั้ง");
                headerPanel.Children.Add(editBadge);
            }

            var sourceBadge = new Border
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    history.ModificationSource == "Excel Import" ? Microsoft.UI.Colors.DodgerBlue : Microsoft.UI.Colors.Green
                ),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2)
            };
            sourceBadge.Child = new TextBlock
            {
                Text = FormatSourceDisplay(history.ModificationSource),
                FontSize = 11,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
            };
            headerPanel.Children.Add(sourceBadge);

            panel.Children.Add(headerPanel);

            var pricePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

            var oldPrice = GetPreviousPriceForDisplay(history, sortedHistories);
            var newPrice = ExtractPriceFromJson(history.NewValues)?.ToString("N4") ?? "-";

            pricePanel.Children.Add(new TextBlock { Text = "ราคา: ", FontSize = 13, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray) });
            pricePanel.Children.Add(new TextBlock { Text = oldPrice, FontSize = 13 });
            pricePanel.Children.Add(new TextBlock { Text = "→", FontSize = 13 });
            pricePanel.Children.Add(new TextBlock { Text = newPrice, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            pricePanel.Children.Add(new TextBlock { Text = "฿", FontSize = 13 });

            panel.Children.Add(pricePanel);

            var changeText = CalculatePriceChange(history, sortedHistories);
            var changePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            changePanel.Children.Add(new TextBlock { Text = "การเปลี่ยนแปลง: ", FontSize = 13, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray) });
            changePanel.Children.Add(new TextBlock { Text = changeText, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(changePanel);

            var modifiedByPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            modifiedByPanel.Children.Add(new TextBlock { Text = "โดย: ", FontSize = 12, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray) });
            modifiedByPanel.Children.Add(new TextBlock { Text = history.ModifiedBy ?? "ไม่ระบุ", FontSize = 12 });
            panel.Children.Add(modifiedByPanel);

            card.Child = panel;
            return card;
        }

        private string GetPreviousPriceForDisplay(ProductModificationHistory current, List<ProductModificationHistory> sortedHistories)
        {
            // Find the most recent earlier history entry (by DateKey) that contains a price in NewValues
            var currentKey = DateKey(current);

            var previousHistory = sortedHistories
                .Where(h => DateKey(h) < currentKey)
                .OrderByDescending(h => DateKey(h))
                .FirstOrDefault();

            // Walk backwards until we find a price value if necessary
            while (previousHistory != null && ExtractPriceFromJson(previousHistory.NewValues) == null)
            {
                var prevKey = DateKey(previousHistory);
                previousHistory = sortedHistories
                    .Where(h => DateKey(h) < prevKey)
                    .OrderByDescending(h => DateKey(h))
                    .FirstOrDefault();
            }

            if (previousHistory == null) return "-";

            var previousPrice = ExtractPriceFromJson(previousHistory.NewValues);
            return previousPrice?.ToString("N4") ?? "-";
        }

        private string CalculatePriceChange(ProductModificationHistory current, List<ProductModificationHistory> sortedHistories)
        {
            var currentPrice = ExtractPriceFromJson(current.NewValues);
            if (!currentPrice.HasValue) return "-";

            var currentKey = DateKey(current);

            // find most recent earlier entry (by DateKey) that may contain a price
            var previousHistory = sortedHistories
                .Where(h => DateKey(h) < currentKey)
                .OrderByDescending(h => DateKey(h))
                .FirstOrDefault();

            // walk back until we find a price value
            while (previousHistory != null && ExtractPriceFromJson(previousHistory.NewValues) == null)
            {
                var prevKey = DateKey(previousHistory);
                previousHistory = sortedHistories
                    .Where(h => DateKey(h) < prevKey)
                    .OrderByDescending(h => DateKey(h))
                    .FirstOrDefault();
            }

            if (previousHistory == null) return "🆕 ใหม่";

            var previousPrice = ExtractPriceFromJson(previousHistory.NewValues);
            if (!previousPrice.HasValue) return "🆕 ใหม่";

            // avoid division by zero
            if (previousPrice.Value == 0m)
            {
                // show infinity indicator for percentage change from zero
                var icon = currentPrice.Value > 0 ? "📈" : (currentPrice.Value < 0 ? "📉" : "➡️");
                return $"{icon} ∞%";
            }

            var diffPercent = (currentPrice.Value - previousPrice.Value) / previousPrice.Value * 100m;
            var iconSign = diffPercent > 0 ? "📈" : (diffPercent < 0 ? "📉" : "➡️");
            var sign = diffPercent > 0 ? "+" : "";

            return $"{iconSign} {sign}{diffPercent:N4}%";
        }

        private string FormatSourceDisplay(string? source)
        {
            return source switch
            {
                "Manual Edit" => "✏️ แก้ไขเอง",
                "Excel Import" => "📊 Import",
                _ => source ?? "N/A"
            };
        }

        private decimal? ExtractPriceFromJson(string? jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString)) return null;

            try
            {
                var jsonDoc = JsonDocument.Parse(jsonString);
                if (jsonDoc.RootElement.TryGetProperty("Price", out var priceElement))
                {
                    if (priceElement.ValueKind == JsonValueKind.Number)
                        return priceElement.GetDecimal();
                }
            }
            catch { }

            return null;
        }

        private void FirstPage_Click(object sender, RoutedEventArgs e)
        {
            _currentPage = 1;
            UpdateHistoryDisplay();
        }

        private void PreviousPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                UpdateHistoryDisplay();
            }
        }

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            var totalPages = (int)Math.Ceiling(_filteredHistories.Count / (double)_pageSize);
            if (_currentPage < totalPages)
            {
                _currentPage++;
                UpdateHistoryDisplay();
            }
        }

        private void LastPage_Click(object sender, RoutedEventArgs e)
        {
            var totalPages = (int)Math.Ceiling(_filteredHistories.Count / (double)_pageSize);
            _currentPage = totalPages;
            UpdateHistoryDisplay();
        }
    }
}
