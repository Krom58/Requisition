using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Requisition.Helpers;
using Requisition.Models.Reports;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace Requisition.Pages
{
    public sealed partial class UsageReportPage : Page
    {
        private readonly TransferService _transferService;
        private readonly ProductService _productService;

        private List<MaterialUsageReportItem> _materialItems = new();
        private List<CostUsageReportItem> _costItems = new();

        // lookup ProductCode -> Category
        private readonly Dictionary<string, string?> _productCategoryByCode = new(StringComparer.OrdinalIgnoreCase);

        private const int PageSize = 10;
        private int _currentPage = 1;
        private int _totalPages = 1;

        private PrintHelper? _printHelper;
        private bool _isInitializing = true;

        public UsageReportPage()
        {
            InitializeComponent();
            _transferService = new TransferService();
            _productService = new ProductService();

            _isInitializing = true;

            StartDatePicker.Date = new DateTimeOffset(DateTime.Today.AddMonths(-1));
            EndDatePicker.Date = new DateTimeOffset(DateTime.Today);

            ReportTypeCombo.SelectedIndex = 0;
            PeriodTypeCombo.SelectedIndex = 0;

            Loaded += UsageReportPage_Loaded;
        }

        private async void UsageReportPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadCategoriesAsync();
            await LoadKitchensAsync();
            
            _isInitializing = false;
            
            await LoadReportAsync();
        }

        // ✅ แก้ไข: ลบการโหลด UnitCost ออก
        private async Task LoadCategoriesAsync()
        {
            try
            {
                var products = await _productService.GetProductsAsync(includeInactive: true);
                var categories = products
                    .Select(p => string.IsNullOrWhiteSpace(p.Category) ? null : p.Category.Trim())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // build map by Code (preferred) and by Name (fallback)
                _productCategoryByCode.Clear();
                
                foreach (var p in products)
                {
                    if (!string.IsNullOrWhiteSpace(p.Code))
                    {
                        _productCategoryByCode[p.Code!] = string.IsNullOrWhiteSpace(p.Category) ? null : p.Category?.Trim();
                    }
                    // also store by Name to improve matching when Code missing
                    if (!string.IsNullOrWhiteSpace(p.Name) && !_productCategoryByCode.ContainsKey(p.Name!))
                    {
                        _productCategoryByCode[p.Name!] = string.IsNullOrWhiteSpace(p.Category) ? null : p.Category?.Trim();
                    }
                }

                if (CategoryCombo != null)
                {
                    CategoryCombo.Items.Clear();
                    CategoryCombo.Items.Add(new ComboBoxItem { Content = "ทั้งหมด", Tag = "" });
                    foreach (var c in categories)
                    {
                        CategoryCombo.Items.Add(new ComboBoxItem { Content = c, Tag = c });
                    }
                    CategoryCombo.SelectedIndex = 0;
                }
            }
            catch
            {
                // ignore errors loading categories — report still works without category filter
            }
        }

        private async Task LoadKitchensAsync()
        {
            try
            {
                var transfers = await _transferService.GetAllTransfersAsync();
                var kitchens = transfers
                    .Where(t => !string.IsNullOrWhiteSpace(t.KitchenDisplay))
                    .Select(t => t.KitchenDisplay!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (KitchenCombo != null)
                {
                    KitchenCombo.Items.Clear();
                    KitchenCombo.Items.Add(new ComboBoxItem { Content = "(ทั้งหมด)", Tag = "" });
                    foreach (var kitchen in kitchens)
                    {
                        KitchenCombo.Items.Add(new ComboBoxItem { Content = kitchen, Tag = kitchen });
                    }
                    KitchenCombo.SelectedIndex = 0;
                }
            }
            catch
            {
                // ignore errors loading kitchens
            }
        }

        private void SetLoading(bool active)
        {
            if (LoadingRing != null)
            {
                LoadingRing.IsActive = active;
                LoadingRing.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async Task LoadReportAsync()
        {
            try
            {
                SetLoading(true);

                if (MaterialReportListView != null)
                    MaterialReportListView.Visibility = Visibility.Collapsed;
                if (CostReportListView != null)
                    CostReportListView.Visibility = Visibility.Collapsed;
                if (EmptyStatePanel != null)
                    EmptyStatePanel.Visibility = Visibility.Collapsed;
                if (PaginationPanel != null)
                    PaginationPanel.Visibility = Visibility.Collapsed;

                if (ReportTypeCombo == null || PeriodTypeCombo == null)
                {
                    if (EmptyStatePanel != null) EmptyStatePanel.Visibility = Visibility.Visible;
                    return;
                }

                var reportType = (ReportTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Material";
                var periodType = (PeriodTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Daily";

                DateTime? startDate = StartDatePicker?.Date?.DateTime;
                DateTime? endDate = EndDatePicker?.Date?.DateTime;

                if (!startDate.HasValue || !endDate.HasValue)
                {
                    if (EmptyStatePanel != null) EmptyStatePanel.Visibility = Visibility.Visible;
                    return;
                }

                if (startDate.Value > endDate.Value)
                {
                    await DialogHelper.ShowErrorAsync("ข้อผิดพลาด", "วันที่เริ่มต้นต้องน้อยกว่าหรือเท่ากับวันที่สิ้นสุด");
                    if (EmptyStatePanel != null) EmptyStatePanel.Visibility = Visibility.Visible;
                    return;
                }

                var transfers = await _transferService.GetAllTransfersAsync();
                var filteredTransfers = transfers
                    .Where(t => !t.IsDeleted && t.Status == Models.TransferStatus.Completed)
                    .Where(t => t.UsageDate.HasValue && t.UsageDate.Value.Date >= startDate.Value.Date && t.UsageDate.Value.Date <= endDate.Value.Date)
                    .ToList();

                if (reportType == "Material")
                {
                    await LoadMaterialReportAsync(filteredTransfers, periodType);
                }
                else
                {
                    await LoadCostReportAsync(filteredTransfers, periodType);
                }
            }
            catch (Exception ex)
            {
                await DialogHelper.ShowErrorAsync("เกิดข้อผิดพลาด", $"ไม่สามารถโหลดรายงานได้: {ex.Message}");
            }
            finally
            {
                SetLoading(false);
            }
        }

        // ✅ แก้ไข LoadMaterialReportAsync - เพิ่มต้นทุนแฝงและใช้ UsageDate
        private async Task LoadMaterialReportAsync(List<Models.Transfer> transfers, string periodType)
        {
            var groups = new List<MaterialUsageReportItem>();

            foreach (var transfer in transfers)
            {
                var kitchenDisplay = transfer.KitchenDisplay ?? (transfer.KitchenId.HasValue ? $"Kitchen #{transfer.KitchenId}" : "ไม่ระบุ");

                // ✅ ตรวจสอบว่ามี UsageDate หรือไม่
                if (!transfer.UsageDate.HasValue)
                    continue; // ข้ามถ้าไม่มีวันที่ใช้งาน

                foreach (var item in transfer.Items)
                {
                    // ✅ ใช้ UsageDate แทน CreatedDate
                    var periodKey = GetPeriodKey(transfer.UsageDate.Value, periodType);

                    // resolve category
                    string? category = null;
                    if (!string.IsNullOrWhiteSpace(item.ProductCode) && _productCategoryByCode.TryGetValue(item.ProductCode!, out var c1))
                        category = c1;
                    else if (!string.IsNullOrWhiteSpace(item.ProductName) && _productCategoryByCode.TryGetValue(item.ProductName!, out var c2))
                        category = c2;

                    // ✅ คำนวณต้นทุนรวม (ราคาต่อหน่วย + ต้นทุนแฝง)
                    decimal unitCost = item.UnitPrice ?? 0m;
                    
                    // ✅ เพิ่มต้นทุนแฝง (Hidden Cost) ถ้ามี
                    if (transfer.HiddenCostPercentage.HasValue && transfer.HiddenCostPercentage.Value > 0)
                    {
                        decimal hiddenCostMultiplier = 1m + (transfer.HiddenCostPercentage.Value / 100m);
                        unitCost = unitCost * hiddenCostMultiplier;
                    }

                    var existing = groups.FirstOrDefault(g => 
                        g.ProductName == item.ProductName && 
                        g.PeriodKey == periodKey && 
                        string.Equals(g.Unit, item.Unit, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(g.KitchenDisplay, kitchenDisplay, StringComparison.OrdinalIgnoreCase));
                    
                    if (existing != null)
                    {
                        // ✅ ปรับปรุง: ใช้ค่าเฉลี่ยถ่วงน้ำหนักสำหรับราคาต่อหน่วย
                        decimal newTotalQuantity = existing.TotalQuantity + item.TotalIssuedQuantity;
                        decimal weightedCost = ((existing.UnitCost * existing.TotalQuantity) + (unitCost * item.TotalIssuedQuantity)) / newTotalQuantity;
                        
                        existing.TotalQuantity = newTotalQuantity;
                        existing.UnitCost = weightedCost;
                    }
                    else
                    {
                        groups.Add(new MaterialUsageReportItem
                        {
                            ProductName = item.ProductName ?? "ไม่ระบุ",
                            Unit = item.Unit ?? "",
                            PeriodKey = periodKey,
                            PeriodDisplay = GetPeriodDisplay(transfer.UsageDate!.Value, periodType),
                            TotalQuantity = item.TotalIssuedQuantity,
                            Category = category ?? string.Empty,
                            KitchenDisplay = kitchenDisplay,
                            UnitCost = unitCost // ✅ ใช้ราคาที่มีต้นทุนแฝงแล้ว
                        });
                    }
                }
            }

            var nameFilter = (SearchNameBox?.Text ?? string.Empty).Trim();
            var unitFilter = (SearchUnitBox?.Text ?? string.Empty).Trim();
            var selectedCategory = (CategoryCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
            var selectedKitchen = (KitchenCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;

            var filtered = groups
                .OrderBy(g => g.PeriodKey)
                .ThenBy(g => g.ProductName)
                .ToList()
                .Where(g =>
                {
                    if (!string.IsNullOrEmpty(nameFilter) && !g.ProductName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (!string.IsNullOrEmpty(unitFilter) && !g.Unit.Contains(unitFilter, StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (!string.IsNullOrEmpty(selectedCategory) && !string.Equals(selectedCategory, "", StringComparison.Ordinal) &&
                        !string.Equals(g.Category ?? "", selectedCategory, StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (!string.IsNullOrEmpty(selectedKitchen) && !string.Equals(selectedKitchen, "", StringComparison.Ordinal) &&
                        !string.Equals(g.KitchenDisplay ?? "", selectedKitchen, StringComparison.OrdinalIgnoreCase))
                        return false;
                    return true;
                })
                .ToList();

            _materialItems = filtered;
            _currentPage = 1;
            UpdateMaterialPagination();

            if (_materialItems.Count == 0)
            {
                if (EmptyStatePanel != null) EmptyStatePanel.Visibility = Visibility.Visible;
            }
            else
            {
                if (MaterialReportListView != null) MaterialReportListView.Visibility = Visibility.Visible;
                if (PaginationPanel != null) PaginationPanel.Visibility = _totalPages > 1 ? Visibility.Visible : Visibility.Collapsed;
            }

            await Task.CompletedTask;
        }

        // ✅ แก้ไข LoadCostReportAsync - ดึง HiddenCostPercentage จาก Transfer
        private async Task LoadCostReportAsync(List<Models.Transfer> transfers, string periodType)
        {
            // Filter transfers that have UsageDate
            var validTransfers = transfers.Where(t => t.UsageDate.HasValue).ToList();

            foreach (var t in validTransfers)
            {
                System.Diagnostics.Debug.WriteLine($"Transfer: {t.TransferNo}, HiddenCostPercentage: {t.HiddenCostPercentage?.ToString() ?? "NULL"}");
            }

            var groups = validTransfers
                .GroupBy(t => new
                {
                    PeriodKey = GetPeriodKey(t.UsageDate!.Value, periodType),
                    Kitchen = t.KitchenDisplay ?? (t.KitchenId.HasValue ? $"Kitchen #{t.KitchenId}" : "ไม่ระบุ")
                })
                .Select(g =>
                {
                    var mealCount = g.Count();
                    var avgPeople = (int)Math.Round(g.Average(t => t.ActualPeople ?? t.ExpectedPeople));
                    
                    // 1) base total cost (ไม่รวมต้นทุนแฝง)
                    var baseTotalCost = g.Sum(t => t.TotalCost);

                    // 2) weighted HiddenCostPercentage (เฉลี่ยถ่วงน้ำหนัก)
                    var weightedHiddenPercentage = 0m;
                    if (baseTotalCost > 0)
                    {
                        weightedHiddenPercentage = g.Sum(t =>
                        {
                            var transferWeight = t.TotalCost / baseTotalCost;
                            return (t.HiddenCostPercentage ?? 0m) * transferWeight;
                        });
                    }

                    // 3) NEW calculation:
                    // ยอดแฝง = ยอดต้นทุน + (ยอดต้นทุน * แฝง%)
                    var extraHiddenAmount = baseTotalCost * (weightedHiddenPercentage / 100m);
                    var hiddenTotal = baseTotalCost + extraHiddenAmount;

                    // 4) per-head values
                    var costPerHead = avgPeople > 0 ? baseTotalCost / avgPeople : 0;
                    var hiddenCostPerHead = avgPeople > 0 ? hiddenTotal / avgPeople : 0;

                    var firstDate = g.OrderBy(t => t.UsageDate).First().UsageDate!.Value;

                    return new CostUsageReportItem
                    {
                        PeriodKey = g.Key.PeriodKey,
                        PeriodDisplay = GetPeriodDisplay(firstDate, periodType),
                        KitchenDisplay = g.Key.Kitchen,
                        PeoplePerMeal = avgPeople,
                        MealCount = mealCount,
                        // keep TotalCost as base (not including hidden)
                        TotalCost = baseTotalCost,
                        CostPerHead = costPerHead,
                        HiddenCostPercentage = weightedHiddenPercentage,
                        // HiddenCostAmount now stores the TOTAL including the extra (ยอดต้นทุน + ยอดแฝง)
                        HiddenCostAmount = hiddenTotal,
                        HiddenCostPerHead = hiddenCostPerHead
                    };
                })
                .OrderBy(g => g.PeriodKey)
                .ThenBy(g => g.KitchenDisplay)
                .ToList();

            foreach (var item in groups)
            {
                System.Diagnostics.Debug.WriteLine($"Period: {item.PeriodDisplay}, Kitchen: {item.KitchenDisplay}");
                System.Diagnostics.Debug.WriteLine($"  TotalCost: {item.TotalCost:N4}");
                System.Diagnostics.Debug.WriteLine($"  HiddenCostPercentage: {item.HiddenCostPercentage:N2}%"); // ✅ ตรวจสอบค่านี้
                System.Diagnostics.Debug.WriteLine($"  HiddenCostAmount: {item.HiddenCostAmount:N4}");
                System.Diagnostics.Debug.WriteLine($"  HiddenCostPerHead: {item.HiddenCostPerHead:N4}");
            }

            var selectedKitchen = (KitchenCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
            
            var filtered = groups
                .Where(g =>
                {
                    if (!string.IsNullOrEmpty(selectedKitchen) && !string.Equals(selectedKitchen, "", StringComparison.Ordinal) &&
                        !string.Equals(g.KitchenDisplay ?? "", selectedKitchen, StringComparison.OrdinalIgnoreCase))
                        return false;
                    return true;
                })
                .ToList();

            _costItems = filtered;
            _currentPage = 1;
            UpdateCostPagination();

            if (_costItems.Count == 0)
            {
                if (EmptyStatePanel != null) EmptyStatePanel.Visibility = Visibility.Visible;
            }
            else
            {
                if (CostReportListView != null) CostReportListView.Visibility = Visibility.Visible;
                if (PaginationPanel != null) PaginationPanel.Visibility = _totalPages > 1 ? Visibility.Visible : Visibility.Collapsed;
            }

            await Task.CompletedTask;
        }

        private void UpdateMaterialPagination()
        {
            var totalItems = _materialItems.Count;
            _totalPages = totalItems == 0 ? 1 : (int)Math.Ceiling(totalItems / (double)PageSize);
            _currentPage = Math.Clamp(_currentPage, 1, _totalPages);

            var skip = (_currentPage - 1) * PageSize;
            var pageItems = _materialItems.Skip(skip).Take(PageSize).ToList();

            if (MaterialReportListView != null) MaterialReportListView.ItemsSource = pageItems;
            UpdatePaginationControls();
        }

        private void UpdateCostPagination()
        {
            var totalItems = _costItems.Count;
            _totalPages = totalItems == 0 ? 1 : (int)Math.Ceiling(totalItems / (double)PageSize);
            _currentPage = Math.Clamp(_currentPage, 1, _totalPages);

            var skip = (_currentPage - 1) * PageSize;
            var pageItems = _costItems.Skip(skip).Take(PageSize).ToList();

            if (CostReportListView != null) CostReportListView.ItemsSource = pageItems;
            UpdatePaginationControls();
        }

        private void UpdatePaginationControls()
        {
            if (PageInfoTextBlock != null) PageInfoTextBlock.Text = $"หน้า {_currentPage} จาก {_totalPages}";
            if (PrevPageButton != null) PrevPageButton.IsEnabled = _currentPage > 1;
            if (NextPageButton != null) NextPageButton.IsEnabled = _currentPage < _totalPages;
        }

        private string GetPeriodKey(DateTime date, string periodType)
        {
            return periodType switch
            {
                "Daily" => date.ToString("yyyy-MM-dd"),
                "Monthly" => date.ToString("yyyy-MM"),
                "Yearly" => date.ToString("yyyy"),
                _ => date.ToString("yyyy-MM-dd")
            };
        }

        private string GetPeriodDisplay(DateTime date, string periodType)
        {
            return periodType switch
            {
                "Daily" => ThaiDateHelper.ToThaiDateShort(date),
                "Monthly" => $"{ThaiDateHelper.GetThaiMonthName(date.Month)} {date.Year + 543}",
                "Yearly" => $"ปี {date.Year + 543}",
                _ => ThaiDateHelper.ToThaiDateShort(date)
            };
        }

        private async void FilterChanged(object sender, object e)
        {
            if (_isInitializing) return;

            _currentPage = 1;
            await LoadReportAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadReportAsync();
        }

        private void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                var reportType = (ReportTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Material";
                if (reportType == "Material")
                    UpdateMaterialPagination();
                else
                    UpdateCostPagination();
            }
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages)
            {
                _currentPage++;
                var reportType = (ReportTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Material";
                if (reportType == "Material")
                    UpdateMaterialPagination();
                else
                    UpdateCostPagination();
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var reportType = (ReportTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Material";

            if (reportType == "Material" && _materialItems.Count == 0)
            {
                await DialogHelper.ShowErrorAsync("ไม่มีข้อมูล", "ไม่มีข้อมูลสำหรับส่งออก");
                return;
            }

            if (reportType == "Cost" && _costItems.Count == 0)
            {
                await DialogHelper.ShowErrorAsync("ไม่มีข้อมูล", "ไม่มีข้อมูลสำหรับส่งออก");
                return;
            }

            try
            {
                var sb = new StringBuilder();

                sb.AppendLine("# รายงานการใช้งาน");
                sb.AppendLine($"# ประเภทรายงาน: {(ReportTypeCombo.SelectedItem as ComboBoxItem)?.Content}");
                sb.AppendLine($"# ช่วงเวลา: {(PeriodTypeCombo.SelectedItem as ComboBoxItem)?.Content}");
                
                DateTime? startDate = StartDatePicker?.Date?.DateTime;
                DateTime? endDate = EndDatePicker?.Date?.DateTime;
                if (startDate.HasValue && endDate.HasValue)
                {
                    sb.AppendLine($"# วันที่: {ThaiDateHelper.ToThaiDateShort(startDate.Value)} - {ThaiDateHelper.ToThaiDateShort(endDate.Value)}");
                }

                var nameFilter = SearchNameBox?.Text?.Trim();
                if (!string.IsNullOrEmpty(nameFilter))
                {
                    sb.AppendLine($"# กรองชื่อสินค้า: {nameFilter}");
                }

                var unitFilter = SearchUnitBox?.Text?.Trim();
                if (!string.IsNullOrEmpty(unitFilter))
                {
                    sb.AppendLine($"# กรองหน่วย: {unitFilter}");
                }

                var selectedCategory = (CategoryCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (!string.IsNullOrEmpty(selectedCategory) && selectedCategory != "ทั้งหมด")
                {
                    sb.AppendLine($"# กรองประเภท: {selectedCategory}");
                }

                var selectedKitchen = (KitchenCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (!string.IsNullOrEmpty(selectedKitchen) && selectedKitchen != "(ทั้งหมด)")
                {
                    sb.AppendLine($"# กรองห้องครัว: {selectedKitchen}");
                }

                sb.AppendLine($"# สร้างเมื่อ: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
                sb.AppendLine($"# จำนวนรายการ: {(reportType == "Material" ? _materialItems.Count : _costItems.Count)} รายการ");
                sb.AppendLine();

                if (reportType == "Material")
                {
                    sb.AppendLine("วัตถุดิบ,ประเภท,ห้องครัว,ช่วงเวลา,จำนวนใช้,หน่วย,ต้นทุน/หน่วย,ต้นทุนรวม");
                    
                    foreach (var item in _materialItems)
                    {
                        sb.AppendLine($"{EscapeCsv(item.ProductName)},{EscapeCsv(item.Category)},{EscapeCsv(item.KitchenDisplay)},{EscapeCsv(item.PeriodDisplay)},{item.TotalQuantity:F4},{EscapeCsv(item.Unit)},{item.UnitCost:F2},{item.TotalCost:F2}");
                    }
                }
                else
                {
                    // ✅ เพิ่มคอลัมน์ต้นทุนแฝง
                    sb.AppendLine("ช่วงเวลา,ห้องครัว,คน/ครั้ง,จำนวนครั้ง,ยอดต้นทุน,ต้นทุน/หัว,ต้นทุนแฝง%,ยอดต้นทุนแฝง,ต้นทุนแฝง/หัว");
                    
                    foreach (var item in _costItems)
                    {
                        sb.AppendLine($"{EscapeCsv(item.PeriodDisplay)},{EscapeCsv(item.KitchenDisplay)},{item.PeoplePerMeal},{item.MealCount},{item.TotalCost:F4},{item.CostPerHead:F4},{item.HiddenCostPercentage:F4},{item.HiddenCostAmount:F4},{item.HiddenCostPerHead:F4}");
                    }
                }

                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var filename = $"UsageReport_{reportType}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                var path = Path.Combine(desktop, filename);

                var utf8WithBom = new UTF8Encoding(true);
                await File.WriteAllTextAsync(path, sb.ToString(), utf8WithBom);

                await DialogHelper.ShowSuccessAsync("ส่งออกสำเร็จ", $"บันทึกไฟล์ CSV ไปยังเดสก์ท็อป:\n{filename}\n\nจำนวน: {(reportType == "Material" ? _materialItems.Count : _costItems.Count)} รายการ");
            }
            catch (Exception ex)
            {
                await DialogHelper.ShowErrorAsync("เกิดข้อผิดพลาด", $"ไม่สามารถส่งออกไฟล์ได้: {ex.Message}");
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

        private async void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            var reportType = (ReportTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Material";

            if (reportType == "Material" && _materialItems.Count == 0)
            {
                await DialogHelper.ShowErrorAsync("ไม่มีข้อมูล", "ไม่มีข้อมูลสำหรับพิมพ์");
                return;
            }

            if (reportType == "Cost" && _costItems.Count == 0)
            {
                await DialogHelper.ShowErrorAsync("ไม่มีข้อมูล", "ไม่มีข้อมูลสำหรับพิมพ์");
                return;
            }

            try
            {
                var printView = new Controls.PrintableUsageReportView();

                DateTime? startDate = StartDatePicker?.Date?.DateTime;
                DateTime? endDate = EndDatePicker?.Date?.DateTime;

                if (!startDate.HasValue || !endDate.HasValue)
                {
                    await DialogHelper.ShowErrorAsync("ข้อผิดพลาด", "กรุณาเลือกวันที่");
                    return;
                }

                var reportTypeName = (ReportTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                var periodTypeName = (PeriodTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

                if (reportType == "Material")
                {
                    var filters = new Dictionary<string, string>();

                    var nameFilter = SearchNameBox?.Text?.Trim();
                    if (!string.IsNullOrEmpty(nameFilter))
                    {
                        filters["ชื่อสินค้า"] = nameFilter;
                    }

                    var unitFilter = SearchUnitBox?.Text?.Trim();
                    if (!string.IsNullOrEmpty(unitFilter))
                    {
                        filters["หน่วย"] = unitFilter;
                    }

                    var selectedCategory = (CategoryCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
                    if (!string.IsNullOrEmpty(selectedCategory) && selectedCategory != "ทั้งหมด")
                    {
                        filters["ประเภท"] = selectedCategory;
                    }

                    var selectedKitchen = (KitchenCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString();
                    if (!string.IsNullOrEmpty(selectedKitchen) && selectedKitchen != "(ทั้งหมด)")
                    {
                        filters["ห้องครัว"] = selectedKitchen;
                    }

                    printView.SetMaterialData(
                        _materialItems,
                        reportTypeName,
                        periodTypeName,
                        startDate.Value,
                        endDate.Value,
                        filters
                    );
                }
                else
                {
                    printView.SetCostData(
                        _costItems,
                        reportTypeName,
                        periodTypeName,
                        startDate.Value,
                        endDate.Value
                    );
                }

                _printHelper = new PrintHelper(App.Window);
                bool success = await _printHelper.ShowPrintUIAsync(printView);

                if (!success)
                {
                    var dlg = new ContentDialog
                    {
                        Title = "ไม่สามารถพิมพ์ได้",
                        Content = "เกิดข้อผิดพลาดในการเปิดหน้าต่างพิมพ์",
                        CloseButtonText = "ปิด",
                        XamlRoot = XamlRoot
                    };
                    await dlg.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                var dlg = new ContentDialog
                {
                    Title = "เกิดข้อผิดพลาด",
                    Content = $"ไม่สามารถพิมพ์ได้: {ex.Message}",
                    CloseButtonText = "ปิด",
                    XamlRoot = XamlRoot
                };
                await dlg.ShowAsync();
            }
            finally
            {
                _printHelper?.Dispose();
                _printHelper = null;
            }
        }
    }
}
