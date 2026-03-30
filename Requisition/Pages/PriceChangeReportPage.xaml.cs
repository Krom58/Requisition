using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Requisition.Helpers;
using Requisition.Models.Reports;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Requisition.Controls;

namespace Requisition.Pages
{
    public sealed partial class PriceChangeReportPage : Page
    {
        private readonly ProductService _productService;
        private List<PriceChangeReportItem> _items = new();

        private const int PageSize = 10;
        private int _currentPage = 1;
        private int _totalPages = 1;

        // ⚠️ เพิ่ม flag ป้องกัน recursive loading
        private bool _isLoading = false;

        // เพิ่ม field สำหรับ PrintHelper
        private PrintHelper? _printHelper;

        public PriceChangeReportPage()
        {
            InitializeComponent();
            _productService = new ProductService();

            // Set default dates (last 30 days)
            StartDatePicker.Date = new DateTimeOffset(DateTime.Today.AddDays(-30));
            EndDatePicker.Date = new DateTimeOffset(DateTime.Today);

            Loaded += PriceChangeReportPage_Loaded;
        }

        private async void PriceChangeReportPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadReportAsync();
        }

        // Safely toggle the loading ring (guard when XAML field is null)
        private void SetLoading(bool active)
        {
            var ring = this.FindName("LoadingRing") as ProgressRing;
            if (ring != null)
            {
                ring.IsActive = active;
                ring.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdatePaginationControls()
        {
            var pageInfo = this.FindName("PageInfoTextBlock") as TextBlock;
            var prevBtn = this.FindName("PrevPageButton") as Button;
            var nextBtn = this.FindName("NextPageButton") as Button;
            if (pageInfo != null)
                pageInfo.Text = $"หน้า {_currentPage} จาก {_totalPages}";
            if (prevBtn != null)
                prevBtn.IsEnabled = _currentPage > 1;
            if (nextBtn != null)
                nextBtn.IsEnabled = _currentPage < _totalPages;
        }

        private void ApplyPagination()
        {
            var listView = this.FindName("ReportListView") as ListView;
            if (listView == null) return;

            var totalItems = _items.Count;
            _totalPages = totalItems == 0 ? 1 : (int)Math.Ceiling(totalItems / (double)PageSize);
            _currentPage = Math.Clamp(_currentPage, 1, _totalPages);

            var skip = (_currentPage - 1) * PageSize;
            var pageItems = _items.Skip(skip).Take(PageSize).ToList();

            // Force replace ItemsSource to avoid showing full collection
            listView.ItemsSource = null;
            listView.ItemsSource = pageItems;

            // Ensure layout updated on UI thread
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
            {
                listView.UpdateLayout();
            });

            UpdatePaginationControls();

            var paginationPanel = this.FindName("PaginationPanel") as StackPanel;
            if (paginationPanel != null)
                paginationPanel.Visibility = _totalPages > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task LoadReportAsync()
        {
            // ⚠️ ป้องกัน recursive call
            if (_isLoading) return;

            try
            {
                _isLoading = true;
                SetLoading(true);

                var reportList = this.FindName("ReportListView") as ListView;
                var emptyPanel = this.FindName("EmptyStatePanel") as StackPanel;
                if (reportList != null) reportList.Visibility = Visibility.Collapsed;
                if (emptyPanel != null) emptyPanel.Visibility = Visibility.Collapsed;

                DateTime? startDate = (this.FindName("StartDatePicker") as CalendarDatePicker)?.Date?.DateTime;
                DateTime? endDate = (this.FindName("EndDatePicker") as CalendarDatePicker)?.Date?.DateTime;

                // Use safe reads for controls that may not be initialized
                double threshold = (this.FindName("ThresholdBox") as NumberBox)?.Value ?? 10;
                var statusFilter = (this.FindName("StatusCombo") as ComboBox)?.SelectedItem is ComboBoxItem si
                    ? (si.Tag?.ToString() ?? "All")
                    : "All";

                if (!startDate.HasValue || !endDate.HasValue)
                {
                    if (emptyPanel != null) emptyPanel.Visibility = Visibility.Visible;
                    return;
                }

                if (startDate.Value.Date > endDate.Value.Date)
                {
                    await DialogHelper.ShowErrorAsync("ข้อผิดพลาด", "วันที่เริ่มต้นต้องน้อยกว่าหรือเท่ากับวันที่สิ้นสุด", this.XamlRoot);
                    if (emptyPanel != null) emptyPanel.Visibility = Visibility.Visible;
                    return;
                }

                // ดึงประวัติราคาทั้งหมด
                var priceChanges = await _productService.GetPriceChangesAsync(startDate.Value, endDate.Value);

                // ⚠️ เติมรายการประเภทใน CategoryCombo โดย unsubscribe event ชั่วคราว
                var categoryCombo = this.FindName("CategoryCombo") as ComboBox;
                if (categoryCombo != null)
                {
                    // Unsubscribe SelectionChanged ชั่วคราว
                    categoryCombo.SelectionChanged -= FilterChanged;

                    var distinctCats = priceChanges
                        .Select(p => p.Category)
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(c => c)
                        .ToList();

                    var currentSelection = (categoryCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";

                    categoryCombo.Items.Clear();
                    categoryCombo.Items.Add(new ComboBoxItem { Content = "ทั้งหมด", Tag = "All" });
                    foreach (var cat in distinctCats)
                    {
                        categoryCombo.Items.Add(new ComboBoxItem { Content = cat, Tag = cat });
                    }

                    // กู้คืน selection เดิม
                    var matchItem = categoryCombo.Items.Cast<ComboBoxItem>()
                        .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), currentSelection, StringComparison.OrdinalIgnoreCase));
                    
                    categoryCombo.SelectedItem = matchItem ?? categoryCombo.Items[0];

                    // Subscribe event กลับ
                    categoryCombo.SelectionChanged += FilterChanged;
                }

                // กรองตามเกณฑ์ %
                var filtered = priceChanges
                    .Where(p => Math.Abs(p.PercentChange) >= (decimal)threshold)
                    .ToList();

                // กรองตามสถานะ
                if (statusFilter == "Active")
                    filtered = filtered.Where(p => p.IsActive).ToList();
                else if (statusFilter == "Inactive")
                    filtered = filtered.Where(p => !p.IsActive).ToList();

                // กรองตามการค้นหา (ชื่อ/รหัส/ประเภท)
                var searchText = (this.FindName("SearchBox") as TextBox)?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(searchText))
                {
                    var st = searchText.Trim();
                    filtered = filtered.Where(p =>
                        (!string.IsNullOrEmpty(p.ProductName) && p.ProductName.Contains(st, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrEmpty(p.ProductCode) && p.ProductCode.Contains(st, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrEmpty(p.Category) && p.Category.Contains(st, StringComparison.OrdinalIgnoreCase))
                    ).ToList();
                }

                // กรองตามประเภทที่เลือก
                if (this.FindName("CategoryCombo") is ComboBox cc && cc.SelectedItem is ComboBoxItem citem)
                {
                    var selectedTag = (citem.Tag?.ToString() ?? "All");
                    if (!string.Equals(selectedTag, "All", StringComparison.OrdinalIgnoreCase))
                    {
                        filtered = filtered.Where(p => string.Equals(p.Category ?? "", selectedTag, StringComparison.OrdinalIgnoreCase)).ToList();
                    }
                }

                _items = filtered.OrderByDescending(p => Math.Abs(p.PercentChange)).ToList();

                // reset to first page and apply pagination (replaces ItemsSource with page slice)
                _currentPage = 1;
                ApplyPagination();

                // show empty or list depending on items count
                if (_items.Count == 0)
                {
                    if (emptyPanel != null) emptyPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    if (reportList != null)
                    {
                        reportList.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception ex)
            {
                await DialogHelper.ShowErrorAsync("เกิดข้อผิดพลาด", $"ไม่สามารถโหลดรายงานได้: {ex.Message}", this.XamlRoot);
            }
            finally
            {
                SetLoading(false);
                _isLoading = false; // ⚠️ ปลดล็อก
            }
        }

        private async void FilterChanged(object sender, object e)
        {
            // ⚠️ ไม่ทำอะไรถ้ากำลังโหลดอยู่
            if (_isLoading) return;

            _currentPage = 1;
            await LoadReportAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _currentPage = 1;
            await LoadReportAsync();
        }

        private async void DisableProduct_Click(object sender, RoutedEventArgs e)
        {
            // Updated UX: ask for a reason (same pattern as ProductListPage)
            if (sender is Button btn && btn.Tag is string productCode)
            {
                var dialog = new ContentDialog
                {
                    Title = "ปิดการใช้งานสินค้า",
                    PrimaryButtonText = "ยืนยัน",
                    CloseButtonText = "ยกเลิก",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                var reasonBox = new TextBox
                {
                    Header = "เหตุผล",
                    PlaceholderText = "กรุณากรอกเหตุผลสำหรับการปิดใช้งาน",
                    MinWidth = 300
                };

                dialog.Content = reasonBox;

                // enable primary only when reason is provided
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
                        var success = await _productService.DisableProductNowAsync(
                            productCode,
                            reasonBox.Text,
                            Environment.UserName);

                        if (success)
                        {
                            await DialogHelper.ShowSuccessAsync("สำเร็จ", $"ปิดการใช้งานสินค้า {productCode} เรียบร้อยแล้ว", this.XamlRoot);
                            _currentPage = 1;
                            await LoadReportAsync(); // refresh list so item disappears or shows new status
                        }
                        else
                        {
                            await DialogHelper.ShowErrorAsync("ข้อผิดพลาด", "ไม่สามารถปิดการใช้งานสินค้าได้", this.XamlRoot);
                        }
                    }
                    catch (Exception ex)
                    {
                        await DialogHelper.ShowErrorAsync("เกิดข้อผิดพลาด", $"ไม่สามารถปิดการใช้งานสินค้าได้: {ex.Message}", this.XamlRoot);
                    }
                }
            }
        }

        private void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                ApplyPagination();
            }
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages)
            {
                _currentPage++;
                ApplyPagination();
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_items == null || _items.Count == 0)
            {
                await DialogHelper.ShowErrorAsync("ไม่มีข้อมูล", "ไม่มีข้อมูลสำหรับส่งออก", this.XamlRoot);
                return;
            }

            try
            {
                var sb = new StringBuilder();

                DateTime? startDate = (this.FindName("StartDatePicker") as CalendarDatePicker)?.Date?.DateTime;
                DateTime? endDate = (this.FindName("EndDatePicker") as CalendarDatePicker)?.Date?.DateTime;
                var threshold = (this.FindName("ThresholdBox") as NumberBox)?.Value ?? 10;
                sb.AppendLine($"# รายงานการเปลี่ยนแปลงราคา (เกิน {threshold}%)");
                if (startDate.HasValue && endDate.HasValue)
                    sb.AppendLine($"# ช่วงเวลา: {ThaiDateHelper.ToThaiDateShort(startDate.Value)} - {ThaiDateHelper.ToThaiDateShort(endDate.Value)}");
                sb.AppendLine();

                sb.AppendLine("ProductCode,ProductName,Category,Unit,OldPrice,NewPrice,PriceChange,PercentChange,OldPriceDate,NewPriceDate,IsActive");

                // ⚠️ แก้ไข: ใช้ InvariantCulture และ F4/F2 แทน N4/N2 เพื่อป้องกัน thousands separator
                foreach (var item in _items)
                {
                    var fields = new[]
                    {
                        EscapeCsv(item.ProductCode),
                        EscapeCsv(item.ProductName),
                        EscapeCsv(item.Category),
                        EscapeCsv(item.Unit),
                        item.OldPrice.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                        item.NewPrice.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                        item.PriceChange.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                        item.PercentChange.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                        EscapeCsv(item.OldPriceDateDisplay),
                        EscapeCsv(item.NewPriceDateDisplay),
                        EscapeCsv(item.IsActive ? "ใช้งาน" : "ไม่ใช้งาน")
                    };

                    sb.AppendLine(string.Join(",", fields));
                }

                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var filename = $"PriceChangeReport_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                var path = Path.Combine(desktop, filename);

                await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);

                await DialogHelper.ShowSuccessAsync("ส่งออกสำเร็จ", $"บันทึกไฟล์ CSV ไปยังเดสก์ท็อป:\n{filename}", this.XamlRoot);
            }
            catch (Exception ex)
            {
                await DialogHelper.ShowErrorAsync("เกิดข้อผิดพลาด", $"ไม่สามารถส่งออกไฟล์ได้: {ex.Message}", this.XamlRoot);
            }
        }

        private string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var escaped = value.Replace("\"", "\"\"");
            if (escaped.Contains(',') || escaped.Contains('"') || escaped.Contains('\n'))
                return $"\"{escaped}\"";
            return escaped;
        }

        // เพิ่ม method PrintButton_Click
        private async void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            if (_items == null || _items.Count == 0)
            {
                var dlgEmpty = new ContentDialog
                {
                    Title = "ไม่มีข้อมูล",
                    Content = "ไม่มีข้อมูลสำหรับพิมพ์",
                    CloseButtonText = "ปิด",
                    XamlRoot = XamlRoot
                };
                await dlgEmpty.ShowAsync();
                return;
            }

            try
            {
                Debug.WriteLine($"📊 Creating print view with {_items.Count} items");
                
                // สร้าง PrintableReportView สำหรับรายงานการเปลี่ยนแปลงราคา
                var printView = new PrintableReportView();
                
                DateTime? startDate = StartDatePicker.Date?.DateTime;
                DateTime? endDate = EndDatePicker.Date?.DateTime;
                
                // ส่งข้อมูลไปยัง PrintableReportView
                printView.SetData(_items, startDate!.Value, endDate!.Value);

                Debug.WriteLine("🖨️ Initializing PrintHelper");

                _printHelper = new PrintHelper(App.Window);
                bool success = await _printHelper.ShowPrintUIAsync(printView);

                Debug.WriteLine($"Print result: {(success ? "✅ Success" : "❌ Failed")}");

                if (!success)
                {
                    var dlg = new ContentDialog
                    {
                        Title = "ไม่สามารถพิมพ์ได้",
                        Content = "เกิดข้อผิดพลาดในการเปิดหน้าต่างพิมพ์\n\nโปรดตรวจสอบ Output window สำหรับรายละเอียด",
                        CloseButtonText = "ปิด",
                        XamlRoot = XamlRoot
                    };
                    await dlg.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Print exception: {ex.Message}");
                Debug.WriteLine($"Stack: {ex.StackTrace}");
                
                var dlg = new ContentDialog
                {
                    Title = "เกิดข้อผิดพลาด",
                    Content = $"ไม่สามารถพิมพ์ได้\n\nError: {ex.Message}\n\nType: {ex.GetType().Name}",
                    CloseButtonText = "ปิด",
                    XamlRoot = XamlRoot
                };
                await dlg.ShowAsync();
            }
            finally
            {
                _printHelper?.Dispose();
            }
        }
    }
}