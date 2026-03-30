using Microsoft.Data.SqlClient;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Requisition.Helpers;
using Requisition.Models;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;

namespace Requisition.Pages
{
    public sealed partial class ProductEditPage : Page
    {
        private readonly ProductService _productService;
        private Product? _currentProduct;
        private string? _productCode;
        private bool _isEditMode;

        // สำหรับ Pagination
        private List<ProductModificationHistory> _allHistories = new();
        private List<ProductModificationHistory> _filteredHistories = new();
        private int _currentPage = 1;
        private const int _pageSize = 5;

        // ⚠️ เพิ่ม: ป้องกันเปิด Dialog ซ้ำ
        private bool _isDialogOpen = false;
        private DispatcherQueue? _dispatcherQueue;  // ⚠️ เพิ่ม

        private ObservableCollection<ProductActionLog> _productActionLogs = new();

        public ProductEditPage()
        {
            InitializeComponent();
            _productService = new ProductService();
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            // ⬇️ เพิ่มส่วนนี้
            SetupNumberFormatters();
        }

        // ⬇️ เพิ่ม method ใหม่
        private void SetupNumberFormatters()
        {
            var decimalFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter
            {
                IntegerDigits = 1,
                FractionDigits = 4,
                IsDecimalPointAlwaysDisplayed = true
            };

            PriceNumberBox.NumberFormatter = decimalFormatter;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is string productCode && !string.IsNullOrEmpty(productCode))
            {
                _productCode = productCode;
                _isEditMode = true;
                await LoadProductAsync(productCode);
            }
            else
            {
                _isEditMode = false;
                TitleText.Text = "เพิ่มสินค้าใหม่";
                await LoadCategoriesAsync();
            }
        }

        private async Task LoadProductAsync(string productCode)
        {
            try
            {
                _currentProduct = await _productService.GetProductByCodeAsync(productCode);

                if (_currentProduct == null)
                {
                    await ShowErrorDialogAsync("ไม่พบข้อมูล", "ไม่พบข้อมูลสินค้าที่ต้องการแก้ไข");
                    Frame.GoBack();
                    return;
                }

                TitleText.Text = "แก้ไขสินค้า";
                DeleteButton.Visibility = Visibility.Visible;

                ProductCodeTextBox.Text = _currentProduct.Code;
                ProductCodeTextBox.IsReadOnly = true;
                ProductNameTextBox.Text = _currentProduct.Name;
                PriceNumberBox.Value = _currentProduct.Price.HasValue ? (double)_currentProduct.Price.Value : 0;
                UnitTextBox.Text = _currentProduct.Unit ?? "";
                RemarksTextBox.Text = _currentProduct.Remarks ?? "";

                await LoadCategoriesAsync();
                CategoryComboBox.Text = _currentProduct.Category ?? "";

                if (_currentProduct.ModificationCount > 0)
                {
                    ModificationInfoPanel.Visibility = Visibility.Visible;
                    ModificationCountText.Text = _currentProduct.ModificationCount.ToString();
                    LastModifiedDateText.Text = ThaiDateHelper.ToThaiDateTimeFullOrDefault(_currentProduct.LastModifiedDate, "N/A");
                    LastModifiedByText.Text = "ระบบ";
                }

                // แสดงราคาเฉลี่ย
                await LoadAveragePriceAsync(productCode);

                // โหลดประวัติ
                await LoadHistoryAsync(productCode);

                // ปรับ UI ตามสถานะ IsActive (ถ้าไม่ active ให้เป็น read-only)
                UpdateUIForActiveState();
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync("เกิดข้อผิดพลาด", $"ไม่สามารถโหลดข้อมูลได้: {ex.Message}");
            }
        }

        private void UpdateUIForActiveState()
        {
            // ถ้ายังไม่มีข้อมูล ให้ไม่ทำอะไร
            if (_currentProduct == null)
                return;

            bool isActive = _currentProduct.IsActive;

            PriceNumberBox.IsEnabled = isActive;
            RemarksTextBox.IsEnabled = isActive;

            // Save/Delete ปิดถ้า inactive
            SaveButton.IsEnabled = isActive;
            DeleteButton.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;

            // แสดงสถานะบน Title / UI ชัดเจน
            if (!isActive)
            {
                TitleText.Text = "ดูข้อมูลสินค้า (อ่านอย่างเดียว)";
            }
            else
            {
                TitleText.Text = _isEditMode ? "แก้ไขสินค้า" : "เพิ่มสินค้าใหม่";
            }

            // ถ้ inactive ให้ซ่อนปุ่มแก้ไขในส่วน History ด้วย
            // (UpdateHistoryDisplay จะอ่านค่า _currentProduct.IsActive เพื่อไม่สร้างปุ่มแก้ไข)
            UpdateHistoryDisplay();
        }

        private async Task LoadCategoriesAsync()
        {
            var allProducts = await _productService.GetAllProductsAsync();
            var categories = new System.Collections.Generic.HashSet<string>();

            foreach (var product in allProducts)
            {
                if (!string.IsNullOrWhiteSpace(product.Category))
                {
                    categories.Add(product.Category);
                }
            }

            CategoryComboBox.Items.Clear();
            foreach (var category in categories)
            {
                CategoryComboBox.Items.Add(category);
            }
        }

        private async Task LoadAveragePriceAsync(string productCode)
        {
            try
            {
                var today = DateTime.Now;
                var startDate = today.AddMonths(-12);

                using var connection = new SqlConnection(ConfigurationHelper.GetConnectionString());
                await connection.OpenAsync();

                var query = @"
                    WITH CombinedPrices AS (
                        SELECT 
                            Price,
                            PriceDate AS PriceDateTime
                        FROM PriceHistory
                        WHERE ProductCode = @ProductCode
                        
                        UNION ALL
                        
                        SELECT 
                            TRY_CAST(JSON_VALUE(NewValues, '$.Price') AS DECIMAL(18, 4)) AS Price,
                            ModifiedDate AS PriceDateTime
                        FROM ProductModificationHistory
                        WHERE ProductCode = @ProductCode
                          AND NewValues IS NOT NULL
                          AND JSON_VALUE(NewValues, '$.Price') IS NOT NULL
                    )
                    SELECT 
                        (SELECT TOP 1 Price 
                         FROM CombinedPrices 
                         ORDER BY PriceDateTime DESC) AS CurrentPrice,
                        
                        AVG(Price) AS AveragePrice,
                        
                        COUNT(*) AS RecordCount
                    FROM CombinedPrices
                    WHERE PriceDateTime >= @StartDate
                      AND PriceDateTime <= @EndDate
                      AND Price IS NOT NULL";

                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@ProductCode", productCode);
                command.Parameters.AddWithValue("@StartDate", startDate);
                command.Parameters.AddWithValue("@EndDate", today);

                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    var currentPrice = reader.IsDBNull(0) ? 0 : reader.GetDecimal(0);
                    var averagePrice = reader.IsDBNull(1) ? 0 : reader.GetDecimal(1);
                    var recordCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);

                    if (recordCount > 0 && currentPrice > 0)
                    {
                        AveragePricePanel.Visibility = Visibility.Visible;
                        CurrentPriceText.Text = $"{FormatPrice(currentPrice)} ฿";
                        AveragePriceText.Text = $"{FormatPrice(averagePrice)} ฿";

                        decimal changePercentage = 0;
                        if (averagePrice > 0)
                        {
                            changePercentage = ((currentPrice - averagePrice) / averagePrice) * 100;
                        }

                        if (changePercentage > 0)
                        {
                            ChangePercentageIcon.Text = "📈";
                            ChangePercentageText.Text = $"+{FormatPercentage(changePercentage)}%";
                            ChangePercentageText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                        }
                        else if (changePercentage < 0)
                        {
                            ChangePercentageIcon.Text = "📉";
                            ChangePercentageText.Text = $"{FormatPercentage(changePercentage)}%";
                            ChangePercentageText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
                        }
                        else
                        {
                            ChangePercentageIcon.Text = "➡️";
                            ChangePercentageText.Text = "0.00%";
                            ChangePercentageText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                        }

                        var trendText = changePercentage switch
                        {
                            > 0 => "สูงกว่า",
                            < 0 => "ต่ำกว่า",
                            _ => "เท่ากับ"
                        };

                        var fromDate = ThaiDateHelper.ToThaiDateShort(startDate);
                        var toDate = ThaiDateHelper.ToThaiDateShort(today);

                        AveragePriceNoteText.Text = recordCount == 1
                            ? $"ราคาปัจจุบัน{trendText}ราคาเฉลี่ย {FormatPercentage(Math.Abs(changePercentage))}% (มี {recordCount} รายการในช่วง {fromDate} - {toDate})"
                            : $"ราคาปัจจุบัน{trendText}ราคาเฉลี่ย {FormatPercentage(Math.Abs(changePercentage))}% (คำนวณจากทั้งหมด {recordCount} รายการในช่วง 12 เดือนที่ผ่านมา)";
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating average price: {ex.Message}");
            }
        }

        private async Task LoadHistoryAsync(string productCode)
        {
            try
            {
                // Load ProductActionLog for the Expander (include disable actions)
                var actionLogs = await _product_service_GetProductActionLogAsyncWrapper(productCode);

                if (actionLogs != null && actionLogs.Count > 0)
                {
                    ProductHistoryExpander.Visibility = Visibility.Visible;

                    // keep all action logs (including Disable) and order newest-first
                    var orderedLogs = actionLogs
                        .OrderByDescending(l => l.PerformedAt ?? DateTime.MinValue)
                        .ToList();

                    ProductHistoryListView.ItemsSource = orderedLogs;
                }
                else
                {
                    ProductHistoryExpander.Visibility = Visibility.Collapsed;
                    ProductHistoryListView.ItemsSource = null;
                }

                // Combined history (price + modification). Keep all entries (do NOT filter out disable)
                _allHistories = await _productService.GetCombinedProductHistoryAsync(productCode) ?? new List<ProductModificationHistory>();

                // Normalize PriceDate => date-only for consistent ordering
                foreach (var h in _allHistories)
                {
                    if (h.PriceDate.HasValue)
                        h.PriceDate = h.PriceDate.Value.Date;
                    else if (!string.IsNullOrWhiteSpace(h.NewValues))
                    {
                        try
                        {
                            using var jd = JsonDocument.Parse(h.NewValues);
                            if (jd.RootElement.TryGetProperty("PriceDate", out var pdEl) && pdEl.ValueKind == JsonValueKind.String)
                            {
                                if (DateTime.TryParse(pdEl.GetString(), out var pd))
                                    h.PriceDate = pd.Date;
                            }
                        }
                        catch
                        {
                            // ignore parse errors
                        }
                    }
                }

                // DO NOT filter out disable entries here — include everything
                _filteredHistories = new List<ProductModificationHistory>(_allHistories);

                if (_filteredHistories.Count > 0)
                {
                    HistoryPanel.Visibility = Visibility.Visible;
                    _currentPage = 1;
                    UpdateHistoryDisplay();
                }
                else
                {
                    HistoryPanel.Visibility = Visibility.Collapsed;
                    _filteredHistories = new List<ProductModificationHistory>();
                    UpdateHistoryDisplay();
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync("เกิดข้อผิดพลาด", $"ไม่สามารถโหลดประวัติได้: {ex.Message}");
            }
        }

        // wrapper helpers to avoid large diffs when copy-pasting: they just call original methods
        private Task<List<ProductActionLog>> _product_service_GetProductActionLogAsyncWrapper(string productCode) => _productService.GetProductActionLogAsync(productCode);
        private int _all_histories_count_wrapper(List<ProductModificationHistory> list) => list?.Count ?? 0;

        private async void UpdateHistoryDisplay()
        {
            var totalPages = (int)Math.Ceiling(_filteredHistories.Count / (double)_pageSize);
            if (totalPages == 0) totalPages = 1;

            var skip = (_currentPage - 1) * _pageSize;
            var pagedHistories = _filteredHistories.Skip(skip).Take(_pageSize).ToList();

            System.Diagnostics.Debug.WriteLine($"\n📋 UpdateHistoryDisplay: Page {_currentPage}/{totalPages}");
            foreach (var h in pagedHistories)
            {
                System.Diagnostics.Debug.WriteLine($"  - History #{h.Id} | SourceTable: {h.SourceTable} | ModifiedDate: {h.ModifiedDate:yyyy-MM-dd HH:mm:ss} | PriceDate: {h.PriceDate?.ToString("yyyy-MM-dd") ?? "NULL"}");
            }

            // Sort for calculation by effective date (PriceDate if present, otherwise ModifiedDate.Date), ascending
            var sortedForCalculation = _filteredHistories
                .OrderBy(h => h.PriceDate?.Date ?? h.ModifiedDate.Date)
                .ThenBy(h => h.ModifiedDate)
                .ToList();

            var priceHistoryMap = new Dictionary<int, int?>();
            var pmhMap = new Dictionary<int, int?>();

            foreach (var h in pagedHistories)
            {
                int? mappedPriceHistoryId = null;
                int? mappedPmhId = null;

                // Effective date = PriceDate (date-only) if available, otherwise ModifiedDate.Date
                var effectiveDate = (h.PriceDate?.Date) ?? h.ModifiedDate.Date;

                if (h.SourceTable == "PriceHistory" && h.SourceId.HasValue)
                {
                    mappedPriceHistoryId = h.SourceId;
                }
                else
                {
                    // keep PMH id for edit actions when source is ModificationHistory or unknown
                    if (h.SourceTable == "ModificationHistory")
                        mappedPmhId = h.Id;

                    var price = ExtractPriceFromJson(h.NewValues);
                    if (price.HasValue)
                    {
                        try
                        {
                            // Match by date-only + price
                            mappedPriceHistoryId = await FindMatchingPriceHistoryIdAsync(h.ProductCode, effectiveDate, price.Value);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ Error mapping history {h.Id} to price history: {ex.Message}");
                        }

                        // if source wasn't explicit set to ModificationHistory, still keep pmh ref
                        if (mappedPmhId == null)
                            mappedPmhId = h.Id;
                    }
                }

                priceHistoryMap[h.Id] = mappedPriceHistoryId;
                pmhMap[h.Id] = mappedPmhId;
            }

            var displayItems = pagedHistories.Select(h =>
            {
                var phId = priceHistoryMap.GetValueOrDefault(h.Id);
                var pmhId = pmhMap.GetValueOrDefault(h.Id);

                // Effective date for display (date-only)
                var effDate = (h.PriceDate?.Date) ?? h.ModifiedDate.Date;
                var effectiveDateStr = effDate.ToString("dd/MM/yyyy");

                var currentPrice = ExtractPriceFromJson(h.NewValues);
                var previousPrice = GetPreviousPriceForDisplay(h, sortedForCalculation);

                bool hasEditHistory = !string.IsNullOrEmpty(h.EditHistory);

                System.Diagnostics.Debug.WriteLine($"  → DisplayItem #{h.Id} | EffectiveDate: {effectiveDateStr} | ModifiedDate: {h.ModifiedDate:dd/MM/yyyy HH:mm:ss}");

                return new HistoryDisplayItem
                {
                    HistoryId = h.Id,
                    PriceHistoryId = phId,
                    ModifiedDate = ThaiDateHelper.ToThaiDateShort(h.ModifiedDate) + " " + h.ModifiedDate.ToString("HH:mm:ss"),
                    EffectiveDate = effectiveDateStr,
                    OldPrice = previousPrice,
                    NewPrice = FormatPrice(currentPrice),
                    PriceChange = CalculatePriceChange(h, sortedForCalculation),
                    Source = FormatSourceDisplay(h.ModificationSource),
                    ModifiedBy = h.ModifiedBy ?? "ไม่ระบุ",
                    CanEdit = phId.HasValue || pmhId.HasValue,
                    EditCount = hasEditHistory ? (JsonSerializer.Deserialize<List<PriceEditHistory>>(h.EditHistory!)?.Count ?? 0) : 0,
                    IsModifiedToday = h.IsModifiedToday,
                    HasEditHistory = hasEditHistory,
                    OriginalDate = h.ModifiedDate,
                    OriginalOldPrice = decimal.TryParse(previousPrice, out var oldP) ? oldP : 0,
                    OriginalNewPrice = currentPrice ?? 0,
                    OriginalModifiedBy = h.ModifiedBy ?? "ไม่ระบุ"
                };
            }).ToList();

            HistoryListView.Items.Clear();

            foreach (var item in displayItems)
            {
                // (UI building code unchanged)
                var border = new Border
                {
                    BorderBrush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(12, 10, 12, 10),
                    MinHeight = 56,
                    Background = item.HasEditHistory
                        ? new SolidColorBrush(ColorHelper.FromArgb(25, 255, 165, 0))
                        : new SolidColorBrush(Colors.Transparent)
                };

                var grid = new Grid();
                // ⬇️ เพิ่มคอลัมน์ที่ 8 (แยกวันที่ออกจากกัน)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // วันที่ของราคา
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // วันที่บันทึก
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // ราคาเก่า
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // ราคาใหม่
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // การเปลี่ยนแปลง
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // แหล่งที่มา
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // ผู้ทำรายการ

                // ⬇️ Column 0: วันที่ของราคา (EffectiveDate)
                var effectiveDatePanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 2,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                if (!string.IsNullOrEmpty(item.EffectiveDate))
                {
                    var effectiveDateText = new TextBlock
                    {
                        Text = "📅 " + item.EffectiveDate,
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        TextAlignment = TextAlignment.Center
                    };
                    effectiveDatePanel.Children.Add(effectiveDateText);
                }
                else
                {
                    // แสดง "-" ถ้าไม่มีวันที่ของราคา
                    effectiveDatePanel.Children.Add(new TextBlock
                    {
                        Text = "-",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Colors.Gray),
                        TextAlignment = TextAlignment.Center
                    });
                }

                Grid.SetColumn(effectiveDatePanel, 0);
                grid.Children.Add(effectiveDatePanel);

                // ⬇️ Column 1: วันที่บันทึก (ModifiedDate + ไอคอนแก้ไข)
                var modifiedDatePanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 2,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var modifiedDateText = new TextBlock
                {
                    Text = item.ModifiedDate,
                    FontSize = 11,
                    TextAlignment = TextAlignment.Center
                };
                modifiedDatePanel.Children.Add(modifiedDateText);

                // ⚠️ แสดงไอคอนถ้ามีการแก้ไข
                if (item.HasEditHistory)
                {
                    var editIconPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 4,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    var editIcon = new FontIcon
                    {
                        Glyph = "\uE70F",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.Orange)
                    };
                    editIconPanel.Children.Add(editIcon);
                    editIconPanel.Children.Add(new TextBlock
                    {
                        Text = $"แก้ไข {item.EditCount}x",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Colors.Orange)
                    });
                    ToolTipService.SetToolTip(editIconPanel, "คลิกปุ่ม 📜 เพื่อดูรายละเอียด");
                    modifiedDatePanel.Children.Add(editIconPanel);
                }

                Grid.SetColumn(modifiedDatePanel, 1);
                grid.Children.Add(modifiedDatePanel);

                // Column 2: ราคาเก่า
                var oldPriceText = new TextBlock
                {
                    Text = item.OldPrice,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 13
                };
                Grid.SetColumn(oldPriceText, 2);
                grid.Children.Add(oldPriceText);

                // Column 3: ราคาใหม่
                var newPriceText = new TextBlock
                {
                    Text = item.NewPrice,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                };
                Grid.SetColumn(newPriceText, 3);
                grid.Children.Add(newPriceText);

                // Column 4: การเปลี่ยนแปลง
                var changeText = new TextBlock
                {
                    Text = item.PriceChange,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 13
                };
                Grid.SetColumn(changeText, 4);
                grid.Children.Add(changeText);

                // Column 5: แหล่งที่มา
                var sourceBorder = new Border
                {
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0, 120, 212)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var sourceText = new TextBlock
                {
                    Text = item.Source,
                    FontSize = 11,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
                };
                sourceBorder.Child = sourceText;
                Grid.SetColumn(sourceBorder, 5);
                grid.Children.Add(sourceBorder);

                // Column 6: ผู้ทำรายการ
                var modifiedByText = new TextBlock
                {
                    Text = item.ModifiedBy,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 13,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)
                };
                Grid.SetColumn(modifiedByText, 6);
                grid.Children.Add(modifiedByText);

                // Column 7: ปุ่มจัดการ (แก้ไข + ดูประวัติ)
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                string? tagValue = null;
                if (item.PriceHistoryId.HasValue)
                    tagValue = $"PH:{item.PriceHistoryId.Value}";
                else if (item.CanEdit)
                    tagValue = $"PMH:{item.HistoryId}";

                if (item.HasEditHistory)
                {
                    var viewHistoryButton = new Button
                    {
                        Content = "📜",
                        Tag = item,
                        Padding = new Thickness(8),
                        MinWidth = 36,
                        MinHeight = 36
                    };
                    ToolTipService.SetToolTip(viewHistoryButton, $"ดูประวัติการแก้ไข ({item.EditCount} ครั้ง)");
                    viewHistoryButton.Click += ViewHistoryButton_Click;
                    buttonPanel.Children.Add(viewHistoryButton);
                }

                if (buttonPanel.Children.Count > 0)
                {
                    Grid.SetColumn(buttonPanel, 7);
                    grid.Children.Add(buttonPanel);
                }

                border.Child = grid;
                HistoryListView.Items.Add(border);
            }

            PageInfoText.Text = $"หน้า {_currentPage} จาก {totalPages} (แสดง {displayItems.Count} จาก {_filteredHistories.Count} รายการ)";
            FirstPageButton.IsEnabled = _currentPage > 1;
            PreviousPageButton.IsEnabled = _currentPage > 1;
            NextPageButton.IsEnabled = _currentPage < totalPages;
            LastPageButton.IsEnabled = _currentPage < totalPages;
        }

        private void ViewHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is HistoryDisplayItem item)
            {
                if (!_isDialogOpen)
                {
                    ShowEditHistoryDialog(item);
                }
            }
        }
        // Replace FindMatchingPriceHistoryIdAsync: compare by date-only and numeric price
        private async Task<int?> FindMatchingPriceHistoryIdAsync(string productCode, DateTime priceDateOnly, decimal price)
        {
            try
            {
                using var connection = new SqlConnection(ConfigurationHelper.GetConnectionString());
                await connection.OpenAsync();

                var query = @"
            SELECT TOP 1 Id
            FROM PriceHistory
            WHERE ProductCode = @ProductCode
              AND CONVERT(date, PriceDate) = @PriceDate
              AND Price = @Price
            ORDER BY Id DESC";

                using var cmd = new SqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@ProductCode", productCode);
                cmd.Parameters.AddWithValue("@PriceDate", priceDateOnly.Date);
                var p = cmd.Parameters.Add("@Price", System.Data.SqlDbType.Decimal);
                p.Precision = 18;
                p.Scale = 4;
                p.Value = price;

                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error finding matching PriceHistory: {ex.Message}");
            }

            return null;
        }

        private async void EditHistoryPrice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;
            if (button.Tag == null) return;

            var tagStr = button.Tag.ToString() ?? string.Empty;
            if (!tagStr.Contains(":")) return;

            var parts = tagStr.Split(':', 2);
            var type = parts[0];
            if (!int.TryParse(parts[1], out int id)) return;

            button.IsEnabled = false;
            try
            {
                decimal? currentPrice = null;
                DateTime? originalDate = null;

                if (type == "PH")
                {
                    var priceInfo = await GetPriceInfoFromPriceHistoryAsync(id);
                    currentPrice = priceInfo?.Price;
                    originalDate = priceInfo?.PriceDate;
                }
                else if (type == "PMH")
                {
                    var history = _allHistories.FirstOrDefault(h => h.Id == id);
                    if (history != null)
                    {
                        currentPrice = ExtractPriceFromJson(history.NewValues);
                        originalDate = history.ModifiedDate;
                    }
                }

                if (!currentPrice.HasValue)
                {
                    await ShowErrorDialogAsync("ข้อผิดพลาด", "ไม่พบราคาปัจจุบัน");
                    return;
                }

                var numberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter
                {
                    IntegerDigits = 1,
                    FractionDigits = 4,
                    IsDecimalPointAlwaysDisplayed = true
                };

                var numberBox = new NumberBox
                {
                    Header = "ราคาใหม่ (บาท)",
                    Value = (double)Math.Round(currentPrice.Value, 4),
                    Minimum = 0,
                    Maximum = 999999999,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                    Width = 300,
                    NumberFormatter = numberFormatter
                };

                var dialog = new ContentDialog
                {
                    Title = "แก้ไขราคาในประวัติ",
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock { Text = $"ราคาปัจจุบัน: {currentPrice.Value:N4} บาท", FontSize = 13, FontWeight = FontWeights.SemiBold },
                            numberBox,
                            new TextBlock
                            {
                                Text = "หมายเหตุ: การแก้ไขจะอัปเดตราคาในประวัติโดยตรง และบันทึก Audit Trail",
                                FontSize = 12,
                                Foreground = new SolidColorBrush(Colors.Gray),
                                TextWrapping = TextWrapping.Wrap
                            }
                        }
                    },
                    PrimaryButtonText = "บันทึก",  // ⚠️ เหลือปุ่มเดียว
                    CloseButtonText = "ยกเลิก",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    var newPrice = (decimal)numberBox.Value;
                    if (newPrice == currentPrice.Value)
                    {
                        await ShowErrorDialogAsync("ไม่มีการเปลี่ยนแปลง", "ราคาใหม่เท่ากับราคาเดิม");
                        return;
                    }

                    bool success;
                    if (type == "PH")
                    {
                        success = await UpdatePriceHistoryAsync(id, newPrice, currentPrice.Value);
                    }
                    else
                    {
                        success = await UpdateProductModificationHistoryPriceAsync(id, newPrice, currentPrice.Value);
                    }

                    if (success)
                    {
                        await ShowSuccessDialogAsync("สำเร็จ", "อัปเดตราคาและบันทึกประวัติการแก้ไขเรียบร้อยแล้ว");
                        if (!string.IsNullOrEmpty(_productCode))
                            await LoadProductAsync(_productCode);
                    }
                    else
                    {
                        await ShowErrorDialogAsync("ล้มเหลว", "ไม่สามารถอัปเดตราคาได้");
                    }
                }
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private async Task<(decimal? Price, DateTime? PriceDate)?> GetPriceInfoFromPriceHistoryAsync(int priceHistoryId)
        {
            try
            {
                using var connection = new SqlConnection(ConfigurationHelper.GetConnectionString());
                await connection.OpenAsync();

                var query = "SELECT Price, PriceDate FROM PriceHistory WHERE Id = @Id";
                using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", priceHistoryId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var price = reader.IsDBNull(0) ? (decimal?)null : reader.GetDecimal(0);
                    var priceDate = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);
                    return (price, priceDate);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error getting price info from PriceHistory: {ex.Message}");
            }

            return null;
        }

        private async Task<bool> UpdateProductModificationHistoryPriceAsync(
    int historyId,
    decimal newPrice,
    decimal oldPrice)
        {
            try
            {
                using var connection = new SqlConnection(ConfigurationHelper.GetConnectionString());
                await connection.OpenAsync();
                using var transaction = connection.BeginTransaction();

                try
                {
                    string? productCode = null;
                    DateTime? modifiedDate = null;
                    string? newValuesJson = null;
                    string? editHistoryJson = null;

                    // อ่านข้อมูลเดิม
                    var selectQuery = @"
                SELECT ProductCode, ModifiedDate, NewValues, EditHistory
                FROM ProductModificationHistory 
                WHERE Id = @Id";

                    using (var sel = new SqlCommand(selectQuery, connection, transaction))
                    {
                        sel.Parameters.AddWithValue("@Id", historyId);
                        using var r = await sel.ExecuteReaderAsync();
                        if (await r.ReadAsync())
                        {
                            productCode = r.GetString(0);
                            modifiedDate = r.GetDateTime(1);
                            newValuesJson = r.IsDBNull(2) ? null : r.GetString(2);
                            editHistoryJson = r.IsDBNull(3) ? null : r.GetString(3);
                        }
                        else
                        {
                            transaction.Rollback();
                            return false;
                        }
                    }

                    if (string.IsNullOrEmpty(productCode) || string.IsNullOrEmpty(newValuesJson))
                    {
                        transaction.Rollback();
                        return false;
                    }

                    var currentPrice = ExtractPriceFromJson(newValuesJson);
                    if (!currentPrice.HasValue || decimal.Round(currentPrice.Value, 4) != decimal.Round(oldPrice, 4))
                    {
                        transaction.Rollback();
                        return false;
                    }

                    // สร้าง Edit History
                    var editHistoryList = new List<PriceEditHistory>();
                    if (!string.IsNullOrEmpty(editHistoryJson))
                    {
                        editHistoryList = JsonSerializer.Deserialize<List<PriceEditHistory>>(editHistoryJson) ?? new();
                    }

                    editHistoryList.Add(new PriceEditHistory
                    {
                        EditDate = DateTime.Now,
                        EditBy = Environment.UserName,
                        OldPrice = oldPrice,
                        NewPrice = newPrice
                    });

                    var updatedEditHistoryJson = JsonSerializer.Serialize(editHistoryList);

                    // อัปเดตราคาและ EditHistory
                    var doc = JsonDocument.Parse(newValuesJson);
                    var dict = new Dictionary<string, object?>();
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        if (p.NameEquals("Price"))
                            dict[p.Name] = newPrice;
                        else
                            dict[p.Name] = p.Value.ValueKind switch
                            {
                                JsonValueKind.Number => p.Value.GetDecimal(),
                                JsonValueKind.String => p.Value.GetString(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                JsonValueKind.Null => null,
                                _ => p.Value.ToString()
                            };
                    }
                    var updatedJson = JsonSerializer.Serialize(dict);

                    var updateQuery = @"
                UPDATE ProductModificationHistory 
                SET NewValues = @NewValues,
                    EditHistory = @EditHistory
                WHERE Id = @Id";

                    using (var upd = new SqlCommand(updateQuery, connection, transaction))
                    {
                        upd.Parameters.AddWithValue("@NewValues", updatedJson);
                        upd.Parameters.AddWithValue("@EditHistory", updatedEditHistoryJson);
                        upd.Parameters.AddWithValue("@Id", historyId);
                        var rows = await upd.ExecuteNonQueryAsync();
                        if (rows == 0)
                        {
                            transaction.Rollback();
                            return false;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"✅ Updated PMH Id={historyId} with {editHistoryList.Count} edit(s)");

                    // ✅ บันทึก ProductActionLog
                    await LogPriceEditActionAsync(
                        connection,
                        transaction,
                        productCode,
                        oldPrice,
                        newPrice,
                        Environment.UserName,
                        modificationHistoryId: historyId
                    );

                    // เช็คว่าเป็นรายการล่าสุดหรือไม่ → อัปเดต CurrentPrice
                    bool isLatest = await IsLatestHistoryAsync(connection, transaction, productCode, modifiedDate.Value);
                    if (isLatest)
                    {
                        var updateProductQuery = @"
                    UPDATE Products 
                    SET CurrentPrice = @NewPrice, 
                        LastModifiedDate = GETDATE() 
                    WHERE Code = @ProductCode AND IsActive = 1";

                        using var upProd = new SqlCommand(updateProductQuery, connection, transaction);
                        var pNew = upProd.Parameters.Add("@NewPrice", System.Data.SqlDbType.Decimal);
                        pNew.Precision = 18;
                        pNew.Scale = 4;
                        pNew.Value = newPrice;
                        upProd.Parameters.AddWithValue("@ProductCode", productCode);
                        await upProd.ExecuteNonQueryAsync();

                        System.Diagnostics.Debug.WriteLine($"✅ Updated Products.CurrentPrice = {newPrice:N4}");
                    }

                    transaction.Commit();
                    return true;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    System.Diagnostics.Debug.WriteLine($"❌ Transaction error: {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Connection error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> UpdatePriceHistoryAsync(
    int priceHistoryId,
    decimal newPrice,
    decimal oldPrice)
        {
            try
            {
                using var connection = new SqlConnection(ConfigurationHelper.GetConnectionString());
                await connection.OpenAsync();

                using var transaction = connection.BeginTransaction();
                try
                {
                    string? productCode = null;
                    DateTime? priceDate = null;
                    decimal? currentDbPrice = null;
                    string? editHistoryJson = null;

                    // อ่านข้อมูลเดิม
                    var selectQuery = "SELECT ProductCode, PriceDate, Price, EditHistory FROM PriceHistory WHERE Id = @Id";
                    using (var selectCmd = new SqlCommand(selectQuery, connection, transaction))  // ✅ เพิ่ม transaction
                    {
                        selectCmd.Parameters.AddWithValue("@Id", priceHistoryId);
                        using var reader = await selectCmd.ExecuteReaderAsync();
                        if (await reader.ReadAsync())
                        {
                            productCode = reader.IsDBNull(0) ? null : reader.GetString(0);
                            priceDate = reader.IsDBNull(1) ? null : reader.GetDateTime(1);
                            currentDbPrice = reader.IsDBNull(2) ? null : reader.GetDecimal(2);
                            editHistoryJson = reader.IsDBNull(3) ? null : reader.GetString(3);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ PriceHistory not found: {priceHistoryId}");
                            transaction.Rollback();
                            return false;
                        }
                    }

                    if (string.IsNullOrEmpty(productCode))
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ No ProductCode for PriceHistoryId: {priceHistoryId}");
                        transaction.Rollback();
                        return false;
                    }

                    // Concurrency check
                    if (currentDbPrice.HasValue && decimal.Round(currentDbPrice.Value, 4) != decimal.Round(oldPrice, 4))
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Concurrency conflict: PriceHistoryId={priceHistoryId}");
                        transaction.Rollback();
                        return false;
                    }

                    // สร้าง EditHistory และอัปเดต PriceHistory
                    var editHistoryList = new List<PriceEditHistory>();
                    if (!string.IsNullOrEmpty(editHistoryJson))
                    {
                        editHistoryList = JsonSerializer.Deserialize<List<PriceEditHistory>>(editHistoryJson) ?? new();
                    }

                    editHistoryList.Add(new PriceEditHistory
                    {
                        EditDate = DateTime.Now,
                        EditBy = Environment.UserName,
                        OldPrice = oldPrice,
                        NewPrice = newPrice
                    });

                    var updatedEditHistoryJson = JsonSerializer.Serialize(editHistoryList);

                    var updateQuery = @"
        UPDATE PriceHistory 
        SET Price = @NewPrice,
            EditHistory = @EditHistory
        WHERE Id = @Id";

                    using (var updateCmd = new SqlCommand(updateQuery, connection, transaction))
                    {
                        var pNew = updateCmd.Parameters.Add("@NewPrice", System.Data.SqlDbType.Decimal);
                        pNew.Precision = 18;
                        pNew.Scale = 4;
                        pNew.Value = newPrice;
                        updateCmd.Parameters.AddWithValue("@EditHistory", updatedEditHistoryJson);
                        updateCmd.Parameters.AddWithValue("@Id", priceHistoryId);

                        int rowsAffected = await updateCmd.ExecuteNonQueryAsync();
                        if (rowsAffected == 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ Update affected 0 rows: {priceHistoryId}");
                            transaction.Rollback();
                            return false;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"✅ Updated PriceHistory Id={priceHistoryId} with edit history");

                    // บันทึก ProductActionLog
                    await LogPriceEditActionAsync(
                        connection,
                        transaction,
                        productCode,
                        oldPrice,
                        newPrice,
                        Environment.UserName,
                        priceHistoryId: priceHistoryId
                    );

                    // อัปเดต ProductModificationHistory ที่เกี่ยวข้อง
                    try
                    {
                        var updatePmhSql = @"
    UPDATE ProductModificationHistory
    SET NewValues = JSON_MODIFY(NewValues, '$.Price', @NewPrice)
    WHERE PriceHistoryId = @PriceHistoryId";

                        using var updPmhCmd = new SqlCommand(updatePmhSql, connection, transaction);
                        var pNew2 = updPmhCmd.Parameters.Add("@NewPrice", System.Data.SqlDbType.Decimal);
                        pNew2.Precision = 18;
                        pNew2.Scale = 4;
                        pNew2.Value = newPrice;
                        updPmhCmd.Parameters.AddWithValue("@PriceHistoryId", priceHistoryId);

                        var pmhRows = await updPmhCmd.ExecuteNonQueryAsync();
                        System.Diagnostics.Debug.WriteLine($"ℹ️ Updated {pmhRows} PMH rows for PriceHistoryId={priceHistoryId}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Failed to update related PMH.NewValues: {ex.Message}");
                    }

                    // เช็คว่าเป็นรายการล่าสุดหรือไม่
                    bool isLatest = false;
                    try
                    {
                        if (!priceDate.HasValue)
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ PriceHistory Id={priceHistoryId} has NULL PriceDate - cannot determine if latest");
                            isLatest = false;
                        }
                        else
                        {
                            isLatest = await IsLatestHistoryAsync(connection, transaction, productCode, priceDate.Value);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Error checking latest for PriceHistoryId={priceHistoryId}: {ex.Message}");
                        isLatest = false;
                    }

                    // ถ้าเป็นรายการล่าสุด ให้อัปเดต Products.CurrentPrice
                    if (isLatest)
                    {
                        var updateProductQuery = @"
            UPDATE Products
            SET CurrentPrice = @NewPrice,
                LastModifiedDate = GETDATE()
            WHERE Code = @ProductCode AND IsActive = 1";

                        using var updateProductCmd = new SqlCommand(updateProductQuery, connection, transaction);
                        var pNewProduct = updateProductCmd.Parameters.Add("@NewPrice", System.Data.SqlDbType.Decimal);
                        pNewProduct.Precision = 18;
                        pNewProduct.Scale = 4;
                        pNewProduct.Value = newPrice;
                        updateProductCmd.Parameters.AddWithValue("@ProductCode", productCode);
                        await updateProductCmd.ExecuteNonQueryAsync();

                        System.Diagnostics.Debug.WriteLine($"✅ Updated Products.CurrentPrice = {newPrice:N4} for product {productCode}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"ℹ️ PriceHistory Id={priceHistoryId} updated but not latest — skip Products.CurrentPrice update");
                    }

                    transaction.Commit();
                    return true;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    System.Diagnostics.Debug.WriteLine($"❌ Transaction error: {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Connection error: {ex.Message}");
                return false;
            }
        }

        private string CalculatePriceChange(ProductModificationHistory current, List<ProductModificationHistory> sortedHistories)
        {
            var currentPrice = ExtractPriceFromJson(current.NewValues);
            if (!currentPrice.HasValue || currentPrice.Value == 0)
                return "🆕 ใหม่";

            // sortedHistories is expected ascending by effective date then ModifiedDate
            var idx = sortedHistories.FindIndex(h => h.Id == current.Id);
            ProductModificationHistory? previousHistory = null;
            if (idx > 0)
                previousHistory = sortedHistories[idx - 1];

            var previousPrice = previousHistory != null ? ExtractPriceFromJson(previousHistory.NewValues) : null;

            if (!previousPrice.HasValue || previousPrice.Value == 0)
                return "🆕 ใหม่";

            var percentageChange = ((currentPrice.Value - previousPrice.Value) / previousPrice.Value) * 100;
            var icon = percentageChange > 0 ? "📈" : "📉";
            var sign = percentageChange > 0 ? "+" : "";

            return $"{icon} {sign}{FormatPercentage(percentageChange)}%";
        }

        private string GetPreviousPriceForDisplay(ProductModificationHistory current, List<ProductModificationHistory> sortedHistories)
        {
            // sortedHistories is expected ascending with effective date then ModifiedDate
            var idx = sortedHistories.FindIndex(h => h.Id == current.Id);
            if (idx <= 0)
                return "-";

            var previousHistory = sortedHistories[idx - 1];
            var previousPrice = ExtractPriceFromJson(previousHistory.NewValues);
            return FormatPrice(previousPrice);
        }

        private string FormatSourceDisplay(string? source)
        {
            if (string.IsNullOrEmpty(source))
                return "N/A";

            return source switch
            {
                "Manual Edit" => "✏️ แก้ไขเอง",
                "Excel Import" => "📊 Import Excel",
                _ => source
            };
        }

        private decimal? ExtractPriceFromJson(string? jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
                return null;

            try
            {
                var jsonDoc = JsonDocument.Parse(jsonString);
                if (jsonDoc.RootElement.TryGetProperty("Price", out var priceElement))
                {
                    if (priceElement.ValueKind == JsonValueKind.Number)
                    {
                        var price = priceElement.GetDecimal();
                        return Math.Round(price, 4);  // ⬅️ เพิ่มการปัดเศษ
                    }
                }
            }
            catch
            {
            }

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

        private void DateFilter_Changed(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            ApplyDateFilter();
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            StartDatePicker.Date = null;
            EndDatePicker.Date = null;
            ApplyDateFilter();
        }

        private void ApplyDateFilter()
        {
            _filteredHistories = new List<ProductModificationHistory>(_allHistories);

            if (StartDatePicker.Date.HasValue)
            {
                var startDate = StartDatePicker.Date.Value.DateTime;
                _filteredHistories = _filteredHistories.Where(h => h.ModifiedDate >= startDate).ToList();
            }

            if (EndDatePicker.Date.HasValue)
            {
                var endDate = EndDatePicker.Date.Value.DateTime.AddDays(1).AddSeconds(-1);
                _filteredHistories = _filteredHistories.Where(h => h.ModifiedDate <= endDate).ToList();
            }

            _currentPage = 1;
            UpdateHistoryDisplay();
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateForm())
                return;

            SaveButton.IsEnabled = false;

            try
            {
                // Build product from current UI values
                var product = new Product
                {
                    Code = ProductCodeTextBox.Text.Trim(),
                    Name = ProductNameTextBox.Text.Trim(),
                    Category = string.IsNullOrWhiteSpace(CategoryComboBox.Text) ? null : CategoryComboBox.Text.Trim(),
                    Price = PriceNumberBox.Value > 0 ? (decimal)PriceNumberBox.Value : null,
                    Unit = string.IsNullOrWhiteSpace(UnitTextBox.Text) ? null : UnitTextBox.Text.Trim(),
                    Remarks = string.IsNullOrWhiteSpace(RemarksTextBox.Text) ? null : RemarksTextBox.Text.Trim()
                };

                bool success;
                if (_isEditMode && _currentProduct != null)
                {
                    // In edit mode, skip save if neither price nor remarks has changed
                    var originalPrice = _currentProduct.Price;
                    var originalRemarks = _currentProduct.Remarks ?? string.Empty;
                    var newPrice = product.Price;
                    var newRemarks = product.Remarks ?? string.Empty;

                    bool priceChanged = originalPrice != newPrice;
                    bool remarksChanged = !string.Equals(originalRemarks, newRemarks, StringComparison.Ordinal);

                    if (!priceChanged && !remarksChanged)
                    {
                        await ShowErrorDialogAsync("ไม่มีการเปลี่ยนแปลง", "ราคาและหมายเหตุไม่มีการเปลี่ยนแปลง จึงไม่ทำการบันทึก");
                        return;
                    }

                    success = await _productService.UpdateProductAsync(product, DateTime.UtcNow, "Manual Edit", Environment.UserName);

                    if (success)
                    {
                        await ShowSuccessDialogAsync("สำเร็จ", "บันทึกการแก้ไขเรียบร้อยแล้ว");
                        await LoadProductAsync(product.Code);
                    }
                    else
                    {
                        await ShowErrorDialogAsync("ล้มเหลว", "ไม่สามารถบันทึกข้อมูลได้");
                    }
                }
                else
                {
                    success = await _productService.AddProductAsync(product, "Manual Edit", Environment.UserName);

                    if (success)
                    {
                        await ShowSuccessDialogAsync("สำเร็จ", "เพิ่มสินค้าใหม่เรียบร้อยแล้ว");
                        _productCode = product.Code;
                        _isEditMode = true;
                        await LoadProductAsync(product.Code);
                    }
                    else
                    {
                        await ShowErrorDialogAsync("ล้มเหลว", "ไม่สามารถบันทึกข้อมูลได้");
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync("เกิดข้อผิดพลาด", $"ไม่สามารถบันทึกข้อมูลได้: {ex.Message}");
            }
            finally
            {
                SaveButton.IsEnabled = true;
            }
        }

        private bool ValidateForm()
        {
            if (string.IsNullOrWhiteSpace(ProductCodeTextBox.Text))
            {
                ShowValidationError("กรุณากรอกรหัสสินค้า");
                return false;
            }

            if (string.IsNullOrWhiteSpace(ProductNameTextBox.Text))
            {
                ShowValidationError("กรุณากรอกชื่อสินค้า");
                return false;
            }

            return true;
        }

        private async void ShowValidationError(string message)
        {
            await ShowErrorDialogAsync("ข้อมูลไม่ถูกต้อง", message);
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProduct == null)
            {
                await ShowErrorDialogAsync("ข้อผิดพลาด", "ไม่พบข้อมูลสินค้า");
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "ปิดใช้งานชั่วคราว",
                PrimaryButtonText = "ยืนยัน",
                CloseButtonText = "ยกเลิก",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var reasonBox = new TextBox
            {
                Header = "เหตุผล *",
                PlaceholderText = "กรุณากรอกเหตุผล",
                TextWrapping = TextWrapping.Wrap,
                MinWidth = 300
            };

            var datePicker = new CalendarDatePicker
            {
                Header = "ปิดถึงวันที่ *",
                Date = _currentProduct.DisabledUntil.HasValue ? new DateTimeOffset(_currentProduct.DisabledUntil.Value) : DateTimeOffset.Now.AddDays(1)
            };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = $"สินค้าที่จะปิด: {_currentProduct.Name}", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(reasonBox);
            panel.Children.Add(datePicker);
            dialog.Content = panel;

            void UpdatePrimaryEnabled()
            {
                dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(reasonBox.Text) && datePicker.Date.HasValue;
            }

            reasonBox.TextChanged += (s, args) => UpdatePrimaryEnabled();
            datePicker.DateChanged += (s, args) => UpdatePrimaryEnabled();
            UpdatePrimaryEnabled();

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            // Re-validate just in case
            if (string.IsNullOrWhiteSpace(reasonBox.Text) || !datePicker.Date.HasValue)
            {
                await ShowErrorDialogAsync("ข้อมูลไม่ครบ", "กรุณากรอกเหตุผลและเลือกวันที่ก่อนยืนยัน");
                return;
            }

            try
            {
                DateTime? chosen = datePicker.Date?.Date;
                string? reason = string.IsNullOrWhiteSpace(reasonBox.Text) ? null : reasonBox.Text.Trim();

                var success = await _productService.DisableProductUntilAsync(_currentProduct.Code, chosen, reason, Environment.UserName);
                if (success)
                {
                    var msg = chosen.HasValue
                        ? $"สินค้าถูกปิดใช้งานจนถึง {chosen.Value:yyyy-MM-dd}"
                        : "ยกเลิกการปิดใช้งานสำเร็จ";

                    await ShowSuccessDialogAsync("สำเร็จ", msg);

                    // Navigate back so ProductList can refresh
                    Frame.GoBack();
                }
                else
                {
                    await ShowErrorDialogAsync("ล้มเหลว", "ไม่สามารถปิดใช้งานสินค้าได้");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialogAsync("เกิดข้อผิดพลาด", $"ไม่สามารถปิดใช้งานสินค้าได้: {ex.Message}");
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.GoBack();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.GoBack();
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
        private async Task<List<PriceEditHistory>> GetEditHistoryAsync(int? priceHistoryId, int? modificationHistoryId)
        {
            try
            {
                using var connection = new SqlConnection(ConfigurationHelper.GetConnectionString());
                await connection.OpenAsync();

                // ✅ ถ้ามี PriceHistoryId ให้ดึงจาก PriceHistory ก่อน
                if (priceHistoryId.HasValue)
                {
                    var queryPH = "SELECT EditHistory FROM PriceHistory WHERE Id = @Id";
                    using var commandPH = new SqlCommand(queryPH, connection);
                    commandPH.Parameters.AddWithValue("@Id", priceHistoryId.Value);

                    using var readerPH = await commandPH.ExecuteReaderAsync();
                    if (await readerPH.ReadAsync())
                    {
                        var editHistoryJson = readerPH.IsDBNull(0) ? null : readerPH.GetString(0);
                        if (!string.IsNullOrEmpty(editHistoryJson))
                        {
                            var history = JsonSerializer.Deserialize<List<PriceEditHistory>>(editHistoryJson);
                            if (history != null && history.Count > 0)
                            {
                                System.Diagnostics.Debug.WriteLine($"✅ Found {history.Count} edit records in PriceHistory (Id={priceHistoryId})");
                                return history;
                            }
                        }
                    }
                }

                // ✅ ถ้าไม่มี PriceHistoryId หรือไม่พบข้อมูล ให้ดึงจาก PMH
                if (modificationHistoryId.HasValue)
                {
                    var queryPMH = "SELECT EditHistory FROM ProductModificationHistory WHERE Id = @Id";
                    using var commandPMH = new SqlCommand(queryPMH, connection);
                    commandPMH.Parameters.AddWithValue("@Id", modificationHistoryId.Value);

                    using var readerPMH = await commandPMH.ExecuteReaderAsync();
                    if (await readerPMH.ReadAsync())
                    {
                        var editHistoryJson = readerPMH.IsDBNull(0) ? null : readerPMH.GetString(0);
                        if (!string.IsNullOrEmpty(editHistoryJson))
                        {
                            var history = JsonSerializer.Deserialize<List<PriceEditHistory>>(editHistoryJson);
                            if (history != null && history.Count > 0)
                            {
                                System.Diagnostics.Debug.WriteLine($"✅ Found {history.Count} edit records in PMH (Id={modificationHistoryId})");
                                return history;
                            }
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"⚠️ No edit history found for PriceHistoryId={priceHistoryId}, PMH Id={modificationHistoryId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error getting edit history: {ex.Message}");
            }

            return new List<PriceEditHistory>();
        }
        private async void ShowEditHistoryDialog(HistoryDisplayItem item)
        {
            // ⚠️ ป้องกันเปิด Dialog ซ้ำ
            if (_isDialogOpen) return;

            _isDialogOpen = true;
            try
            {
                // ✅ แก้ไข: ส่งทั้ง PriceHistoryId และ HistoryId
                var editHistory = await GetEditHistoryAsync(item.PriceHistoryId, item.HistoryId);

                if (editHistory == null || editHistory.Count == 0)
                {
                    var noHistoryDialog = new ContentDialog
                    {
                        Title = "ไม่มีประวัติ",
                        Content = new TextBlock
                        {
                            Text = "ไม่พบประวัติการแก้ไข",
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(16)
                        },
                        CloseButtonText = "ตกลง",
                        XamlRoot = Content.XamlRoot
                    };
                    await noHistoryDialog.ShowAsync();
                    return;
                }

                var dialog = new ContentDialog
                {
                    Title = $"📝 ประวัติการแก้ไข (แก้ไข {editHistory.Count} ครั้ง)",
                    CloseButtonText = "ปิด",
                    XamlRoot = Content.XamlRoot
                };

                var content = new StackPanel { Spacing = 12, Padding = new Thickness(16) };

                // แสดงข้อมูลต้นฉบับ
                content.Children.Add(CreateHistoryCard("📌 ต้นฉบับ",
                    item.OriginalDate,
                    item.OriginalOldPrice,
                    item.OriginalNewPrice,
                    item.OriginalModifiedBy,
                    isOriginal: true));

                // แสดงประวัติการแก้ไข
                for (int i = 0; i < editHistory.Count; i++)
                {
                    var edit = editHistory[i];
                    content.Children.Add(CreateHistoryCard($"✏️ แก้ไขครั้งที่ {i + 1}",
                        edit.EditDate,
                        edit.OldPrice,
                        edit.NewPrice,
                        edit.EditBy));
                }

                var scrollViewer = new ScrollViewer
                {
                    MaxHeight = 500,
                    Content = content
                };

                dialog.Content = scrollViewer;
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in ShowEditHistoryDialog: {ex.Message}");

                var errorDialog = new ContentDialog
                {
                    Title = "เกิดข้อผิดพลาด",
                    Content = new TextBlock
                    {
                        Text = $"ไม่สามารถแสดงประวัติได้: {ex.Message}",
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(16)
                    },
                    CloseButtonText = "ตกลง",
                    XamlRoot = Content.XamlRoot
                };
                await errorDialog.ShowAsync();
            }
            finally
            {
                // ⚠️ ปลดล็อก Flag
                _isDialogOpen = false;
            }
        }

        private Border CreateHistoryCard(string title, DateTime date, decimal oldPrice, decimal newPrice, string modifiedBy, bool isOriginal = false)
        {
            var card = new Border
            {
                Background = isOriginal
                    ? new SolidColorBrush(ColorHelper.FromArgb(25, 0, 120, 212))
                    : (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var panel = new StackPanel { Spacing = 8 };

            // Title
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14
            });

            // Date
            panel.Children.Add(new TextBlock
            {
                Text = $"⏰ {ThaiDateHelper.ToThaiDateTimeFull(date)}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.Gray)
            });

            // Price Change
            var pricePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            pricePanel.Children.Add(new TextBlock { Text = $"{oldPrice:N4} ฿", FontSize = 13 });
            pricePanel.Children.Add(new TextBlock { Text = "→", FontSize = 13 });
            pricePanel.Children.Add(new TextBlock { Text = $"{newPrice:N4} ฿", FontSize = 13, FontWeight = FontWeights.Bold });
            panel.Children.Add(pricePanel);

            // Modified By
            panel.Children.Add(new TextBlock
            {
                Text = $"👤 {modifiedBy}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.Gray)
            });

            card.Child = panel;
            return card;
        }

        /// <summary>
        /// เช็คว่ารายการนี้เป็นรายการล่าสุดหรือไม่
        /// </summary>
        // Replace IsLatestHistoryAsync: compare by date-only and use transaction-aware command
        private async Task<bool> IsLatestHistoryAsync(SqlConnection connection, SqlTransaction transaction, string productCode, DateTime modifiedDate)
        {
            try
            {
                // ✅ แก้ไข: ใช้ alias 'DateValue' เพื่อให้ทั้ง 2 subquery return column ชื่อเดียวกัน
                var query = @"
            SELECT COUNT(*)
            FROM (
                SELECT ModifiedDate AS DateValue 
                FROM ProductModificationHistory 
                WHERE ProductCode = @ProductCode
                  AND ModifiedDate IS NOT NULL
                
                UNION ALL
                
                SELECT PriceDate AS DateValue 
                FROM PriceHistory 
                WHERE ProductCode = @ProductCode
                  AND PriceDate IS NOT NULL
            ) AS AllDates
            WHERE CONVERT(date, DateValue) > CONVERT(date, @ModifiedDate)";

                using var cmd = new SqlCommand(query, connection, transaction);
                cmd.Parameters.AddWithValue("@ProductCode", productCode);
                cmd.Parameters.AddWithValue("@ModifiedDate", modifiedDate.Date);

                var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);

                // ✅ เพิ่ม Debug log เพื่อตรวจสอบ
                System.Diagnostics.Debug.WriteLine($"🔍 IsLatestHistoryAsync: ProductCode={productCode}, Date={modifiedDate:yyyy-MM-dd}, Count={count}, IsLatest={count == 0}");

                return count == 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error checking latest history: {ex.Message}");
                return false;
            }
        }

        private class FieldDiff
        {
            public string FieldName { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string OldValue { get; set; } = string.Empty;
            public string NewValue { get; set; } = string.Empty;
        }

        // ===== เพิ่ม Helper Methods (วางไว้ด้านล่าง class) =====
        private string FormatPrice(decimal? value)
        {
            if (!value.HasValue) return "-";
            return Math.Round(value.Value, 4, MidpointRounding.AwayFromZero).ToString("0.0000"); // ✅ แก้ไข
        }

        private string FormatPercentage(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00"); // ✅ แก้ไข
        }

        // เพิ่มใกล้ GetPriceInfoFromPriceHistoryAsync

        private async Task LogPriceEditActionAsync(
    SqlConnection connection,
    SqlTransaction transaction,
    string productCode,
    decimal oldPrice,
    decimal newPrice,
    string editBy,
    int? priceHistoryId = null,
    int? modificationHistoryId = null)
{
    try
    {
        var sql = @"
INSERT INTO ProductActionLog
    (ProductCode, ActionType, EffectiveDate, OldValue, NewValue, Reason,
     PerformedBy, PerformedAt, Source, RelatedId)
VALUES
    (@ProductCode, @ActionType, GETDATE(), @OldValue, @NewValue, @Reason,
     @PerformedBy, GETDATE(), @Source, @RelatedId)";

        using var cmd = new SqlCommand(sql, connection, transaction);
        cmd.Parameters.AddWithValue("@ProductCode", productCode);
        cmd.Parameters.AddWithValue("@ActionType", "Price Edit (History)");
        cmd.Parameters.AddWithValue("@OldValue", $"{oldPrice:N4}");
        cmd.Parameters.AddWithValue("@NewValue", $"{newPrice:N4}");
        cmd.Parameters.AddWithValue("@Reason", $"แก้ไขราคาในประวัติโดย {editBy}");
        cmd.Parameters.AddWithValue("@PerformedBy", editBy);
        cmd.Parameters.AddWithValue("@Source", "Manual Edit (History)");
        
        // ใช้ RelatedId เก็บ PriceHistoryId หรือ PMH Id
        int? relatedId = priceHistoryId ?? modificationHistoryId;
        cmd.Parameters.AddWithValue("@RelatedId", (object?)relatedId ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
        System.Diagnostics.Debug.WriteLine($"✅ Logged ProductActionLog for price edit: {oldPrice:N4} → {newPrice:N4}");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"⚠️ Failed to log ProductActionLog: {ex.Message}");
        // ไม่ throw exception เพราะไม่ต้องการให้ล้ม transaction
    }
}
    }
    // ViewModel สำหรับแสดงในตาราง
    public class HistoryDisplayItem
    {
        public int HistoryId { get; set; }
        public int? PriceHistoryId { get; set; }
        public string ModifiedDate { get; set; } = string.Empty;

        // ⬇️ เพิ่มบรรทัดนี้
        public string? EffectiveDate { get; set; }  // วันที่ของราคา (PriceDate)

        public string OldPrice { get; set; } = string.Empty;
        public string NewPrice { get; set; } = string.Empty;
        public string PriceChange { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string ModifiedBy { get; set; } = string.Empty;
        public bool CanEdit { get; set; }
        public bool HasEditHistory { get; set; }
        public int EditCount { get; set; }
        public bool IsModifiedToday { get; set; }

        // ⚠️ เพิ่ม properties สำหรับแสดงประวัติ
        public DateTime OriginalDate { get; set; }
        public decimal OriginalOldPrice { get; set; }
        public decimal OriginalNewPrice { get; set; }
        public string OriginalModifiedBy { get; set; } = string.Empty;
    }

    public class PriceEditHistory
    {
        public DateTime EditDate { get; set; }
        public string EditBy { get; set; } = string.Empty;
        public decimal OldPrice { get; set; }
        public decimal NewPrice { get; set; }
    }
}