using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Requisition.Services;
using Requisition.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using System.Diagnostics;
using Requisition.Models.Reports;
using Requisition.Controls;
using Requisition.Helpers;

namespace Requisition.Pages
{
    public sealed partial class InactiveProductsReportPage : Page
    {
        private readonly ProductService _productService;
        private List<InactiveProductItem> _allItems = new();
        private List<InactiveProductItem> _filteredItems = new();
        private PrintHelper? _printHelper;

        public InactiveProductsReportPage()
        {
            InitializeComponent();
            _productService = new ProductService();
            Loaded += InactiveProductsReportPage_Loaded;
        }

        private async void InactiveProductsReportPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            try
            {
                LoadingRing.IsActive = true;
                ReportListView.Visibility = Visibility.Collapsed;
                EmptyStatePanel.Visibility = Visibility.Collapsed;

                // load all products including inactive
                var products = await _product_service_GetProductsAsyncWrapper();

                // only keep inactive products (IsActive == false)
                var inactiveProducts = products.Where(p => !p.IsActive).ToList();

                // preload categories
                PopulateCategories(inactiveProducts);

                // build view items including latest disable reason
                var items = new List<InactiveProductItem>();
                foreach (var p in inactiveProducts)
                {
                    var logs = await _productService.GetProductActionLogAsync(p.Code);
                    var lastLog = logs
                                  .Where(l => string.Equals(l.ActionType, "Disable", StringComparison.OrdinalIgnoreCase))
                                  .OrderByDescending(l => l.PerformedAt ?? DateTime.MinValue)
                                  .FirstOrDefault();

                    items.Add(new InactiveProductItem
                    {
                        ProductCode = p.Code,
                        ProductName = p.Name,
                        Category = p.Category ?? "",
                        Unit = p.Unit ?? "",
                        ShortRemarks = p.Remarks ?? "",
                        DisabledReason = lastLog?.Reason ?? "-",
                        DisabledBy = lastLog?.PerformedBy ?? "-",
                        DisabledAt = lastLog?.PerformedAt,
                        OriginalProduct = p
                    });
                }

                _allItems = items.OrderBy(i => i.ProductCode).ToList();
                _filteredItems = _allItems.ToList();

                ApplyFilterToView();
            }
            catch (Exception ex)
            {
                var dlg = new ContentDialog
                {
                    Title = "เกิดข้อผิดพลาด",
                    Content = $"ไม่สามารถโหลดรายงานได้: {ex.Message}",
                    CloseButtonText = "ปิด",
                    XamlRoot = this.XamlRoot
                };
                await dlg.ShowAsync();
            }
            finally
            {
                LoadingRing.IsActive = false;
            }
        }

        // wrapper in case ProductService method name or behavior differs in different contexts
        private Task<List<Product>> _product_service_GetProductsAsyncWrapper()
        {
            return _productService.GetProductsAsync(includeInactive: true);
        }

        private void PopulateCategories(List<Product> products)
        {
            var cats = products.Select(p => p.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList();
            CategoryCombo.Items.Clear();
            CategoryCombo.Items.Add(new ComboBoxItem { Content = "ทั้งหมด", Tag = "All" });
            foreach (var c in cats)
            {
                CategoryCombo.Items.Add(new ComboBoxItem { Content = c, Tag = c });
            }
            CategoryCombo.SelectedIndex = 0;
        }

        private void ApplyFilterToView()
        {
            // Safe reads for controls that may be not yet initialized
            var search = (SearchBox?.Text ?? "").Trim();
            string selectedCategory = "All";
            if (CategoryCombo?.SelectedItem is ComboBoxItem ci)
                selectedCategory = ci.Tag?.ToString() ?? "All";

            var filtered = _allItems.Where(it =>
            {
                var matchesSearch = string.IsNullOrWhiteSpace(search) ||
                                    it.ProductCode.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                    (it.ProductName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);

                var matchesCategory = string.Equals(selectedCategory, "All", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(it.Category ?? "", selectedCategory, StringComparison.OrdinalIgnoreCase);

                return matchesSearch && matchesCategory;
            }).ToList();

            // keep filtered list for export/print
            _filteredItems = filtered;

            // If ReportListView is not yet created (null), defer the UI update to the Dispatcher
            if (ReportListView == null || EmptyStatePanel == null)
            {
                var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                if (dq != null)
                {
                    dq.TryEnqueue(() =>
                    {
                        // double-check in dispatcher context
                        if (ReportListView != null && EmptyStatePanel != null)
                        {
                            ReportListView.ItemsSource = filtered;
                            ReportListView.Visibility = filtered.Any() ? Visibility.Visible : Visibility.Collapsed;
                            EmptyStatePanel.Visibility = filtered.Any() ? Visibility.Collapsed : Visibility.Visible;
                        }
                    });
                }
                return;
            }

            // Normal update when UI elements are ready
            ReportListView.ItemsSource = filtered;
            ReportListView.Visibility = filtered.Any() ? Visibility.Visible : Visibility.Collapsed;
            EmptyStatePanel.Visibility = filtered.Any() ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilterToView();
        }

        private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilterToView();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadAsync();
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

                var reasonBox = new TextBox { Header = "เหตุผลการเปิดใช้งาน", PlaceholderText = "กรุณากรอกเหตุผล", MinWidth = 300 };
                dialog.Content = reasonBox;

                void UpdatePrimary() => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(reasonBox.Text);
                reasonBox.TextChanged += (s, e) => UpdatePrimary();
                UpdatePrimary();

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    try
                    {
                        var success = await _productService.EnableProductAsync(code, reasonBox.Text, Environment.UserName);
                        if (success)
                        {
                            var ok = new ContentDialog { Title = "สำเร็จ", Content = "เปิดใช้งานสินค้าเรียบร้อยแล้ว", CloseButtonText = "ปิด", XamlRoot = this.XamlRoot };
                            await ok.ShowAsync();
                            await LoadAsync();
                        }
                        else
                        {
                            var err = new ContentDialog { Title = "ผิดพลาด", Content = "ไม่สามารถเปิดใช้งานสินค้าได้", CloseButtonText = "ปิด", XamlRoot = this.XamlRoot };
                            await err.ShowAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        var err = new ContentDialog { Title = "ข้อผิดพลาด", Content = ex.Message, CloseButtonText = "ปิด", XamlRoot = this.XamlRoot };
                        await err.ShowAsync();
                    }
                }
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            // ส่งออก CSV ของสินค้าที่ถูกยกเลิก (filtered view)
            if (_filteredItems == null || !_filteredItems.Any())
            {
                var dlg = new ContentDialog { Title = "ไม่มีข้อมูล", Content = "ไม่มีรายการสำหรับส่งออก", CloseButtonText = "ปิด", XamlRoot = this.XamlRoot };
                await dlg.ShowAsync();
                return;
            }

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("ProductCode,ProductName,Category,Unit,DisabledReason,DisabledBy,DisabledAt");

                foreach (var it in _filteredItems)
                {
                    string Escape(string s)
                    {
                        if (string.IsNullOrEmpty(s)) return "";
                        var esc = s.Replace("\"", "\"\"");
                        if (esc.Contains(',') || esc.Contains('\n') || esc.Contains('"'))
                            return $"\"{esc}\"";
                        return esc;
                    }

                    var line = string.Join(",",
                        Escape(it.ProductCode),
                        Escape(it.ProductName),
                        Escape(it.Category),
                        Escape(it.Unit),
                        Escape(it.DisabledReason),
                        Escape(it.DisabledBy),
                        Escape(it.DisabledAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""));

                    sb.AppendLine(line);
                }

                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var filename = $"InactiveProducts_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                var path = System.IO.Path.Combine(desktop, filename);
                await System.IO.File.WriteAllTextAsync(path, sb.ToString(), System.Text.Encoding.UTF8);

                var ok = new ContentDialog { Title = "ส่งออกเสร็จ", Content = $"ไฟล์ถูกบันทึกที่เดสก์ท็อป: {filename}", CloseButtonText = "ปิด", XamlRoot = this.XamlRoot };
                await ok.ShowAsync();
            }
            catch (Exception ex)
            {
                var err = new ContentDialog { Title = "ผิดพลาด", Content = ex.Message, CloseButtonText = "ปิด", XamlRoot = this.XamlRoot };
                await err.ShowAsync();
            }
        }

        private async void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            var itemsToPrint = _filteredItems ?? new List<InactiveProductItem>();
            if (!itemsToPrint.Any())
            {
                var dlg = new ContentDialog { Title = "ไม่มีข้อมูล", Content = "ไม่มีรายการสำหรับพิมพ์", CloseButtonText = "ปิด", XamlRoot = this.XamlRoot };
                await dlg.ShowAsync();
                return;
            }

            // Map view items to report model
            var reportItems = itemsToPrint.Select(it => new InactiveProductReportItem
            {
                ProductCode = it.ProductCode,
                ProductName = it.ProductName,
                Category = it.Category,
                Unit = it.Unit,
                DisabledReason = it.DisabledReason,
                DisabledBy = it.DisabledBy,
                DisabledAt = it.DisabledAt
            }).ToList();

            try
            {
                var printView = new PrintableReportView();
                // Use printedAt as now (single-date report); itemsPerPage tuned to 28-30
                printView.SetData(reportItems, DateTime.Now, itemsPerPage: 30);

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
                        XamlRoot = this.XamlRoot
                    };
                    await dlg.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Print exception: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                var dlg = new ContentDialog
                {
                    Title = "เกิดข้อผิดพลาด",
                    Content = $"ไม่สามารถพิมพ์ได้: {ex.Message}",
                    CloseButtonText = "ปิด",
                    XamlRoot = this.XamlRoot
                };
                await dlg.ShowAsync();
            }
            finally
            {
                _printHelper?.Dispose();
                _printHelper = null;
            }
        }

        // small view-model for the page
        private class InactiveProductItem
        {
            public string ProductCode { get; set; } = "";
            public string ProductName { get; set; } = "";
            public string Category { get; set; } = "";
            public string Unit { get; set; } = "";
            public string ShortRemarks { get; set; } = "";
            public string DisabledReason { get; set; } = "";
            public string DisabledBy { get; set; } = "";
            public DateTime? DisabledAt { get; set; }
            public Product? OriginalProduct { get; set; }

            public string DisabledAtDisplay => DisabledAt.HasValue ? DisabledAt.Value.ToString("dd/MM/yyyy HH:mm") : "-";
        }
    }
}
