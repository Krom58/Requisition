using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Requisition.Models;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System.Numerics;
using Windows.Foundation;
using WinUIFontWeights = Microsoft.UI.Text.FontWeights;   // ✅ เพิ่มบรรทัดนี้

namespace Requisition.Pages
{
    public sealed partial class CostReportPage : Page, INotifyPropertyChanged
    {
        private readonly ProductService _productService;
        private List<Product> _allProducts = new();
        private List<string> _allCategories = new();
        private List<string> _allUnits = new();

        // Selected filter values
        private string? _selectedProductCode;
        private string? _selectedCategory;
        private string? _selectedUnit;

        // Debounce for auto-loading
        private CancellationTokenSource? _debounceCts;

        // Pagination
        private const int PageSize = 10;
        private int _currentPage = 1;
        private int _totalPages = 1;
        private List<object> _allReportItems = new();
        private readonly List<UIElement> _dynamicHeaderElements = new();

        private readonly List<string> _selectedProductCodes = new();

        // Add this field near other private fields at top of the class
        private bool _contentDialogOpen = false;

        // เพิ่ม class ใหม่ที่ top of the file (หลัง using statements)
        public class ColumnDefinitionInfo
        {
            public string HeaderText { get; set; } = "";
            public GridLength Width { get; set; } = GridLength.Auto; // ← เปลี่ยนเป็น Auto
            public double MinWidth { get; set; } = 70;
            public bool IsStatic { get; set; } // true สำหรับ 4 columns แรก
            public Func<dynamic, string>? GetValue { get; set; } // function ดึงค่าจาก data object
            public SolidColorBrush? Foreground { get; set; }
        }

        // เพิ่ม field ใหม่
        private List<ColumnDefinitionInfo> _currentColumnSchema = new();

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public CostReportPage()
        {
            InitializeComponent();
            _productService = new ProductService();
            Loaded += CostReportPage_Loaded;
            PointerPressed += CostReportPage_PointerPressed;
            ProductSearchBox.LostFocus += AutoSuggestBox_LostFocus;
            
            // ✅ เพิ่มบรรทัดนี้
            InitializeDatePickers();
        }

        // ✅ เพิ่ม method ใหม่
        private void InitializeDatePickers()
        {
            // ตั้งค่า CalendarDatePicker ให้ใช้ปฏิทินไทย
            FromDatePickerFull.CalendarIdentifier = Windows.Globalization.CalendarIdentifiers.Thai;
            ToDatePickerFull.CalendarIdentifier = Windows.Globalization.CalendarIdentifiers.Thai;
        }

        private async void CostReportPage_Loaded(object? sender, RoutedEventArgs e)
        {
            await InitializeFiltersAsync();

            // load immediately after filters initialized
            await LoadCostReportAsync();
        }

        private async Task InitializeFiltersAsync()
        {
            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;

            try
            {
                _allProducts = await _productService.GetAllProductsAsync();

                // categories and units (existing code...)
                _allCategories = _allProducts
                    .Select(p => p.Category?.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s)
                    .ToList();

                _allUnits = _allProducts
                    .Select(p => p.Unit?.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s)
                    .ToList();

                // Build year list
                var productYears = _allProducts
    .SelectMany(p =>
    {
        var list = new List<int>();
        
        // ✅ PriceDate is now DateTime? — use Year if present
        if (p.PriceDate.HasValue)
        {
            var year = p.PriceDate.Value.Year;
            // safety: if stored as BE accidentally, convert to AD
            if (year > 2500)
                year -= 543;

            if (year > 1900)
                list.Add(year);
        }
        
        if (p.LastModifiedDate.HasValue) list.Add(p.LastModifiedDate.Value.Year);
        if (p.CreatedDate.HasValue) list.Add(p.CreatedDate.Value.Year);
        return list;
    })
    .Where(y => y > 1900)
    .Distinct();

                var currentYear = DateTime.Now.Year;
                var startYear = currentYear - 5;
                var endYear = currentYear + 5;

                var rangeYears = Enumerable.Range(startYear, endYear - startYear + 1);
                var years = rangeYears
                    .Union(productYears)
                    .Where(y => y > 1900)
                    .Distinct()
                    .OrderBy(y => y)
                    .ToList();

                // Populate both FromYear and ToYear ComboBoxes
                FromYearComboBox.Items.Clear();
                ToYearComboBox.Items.Clear();
                FromMonthYearComboBox.Items.Clear();
                ToMonthYearComboBox.Items.Clear();

                // Add empty option for all
                FromYearComboBox.Items.Add("ไม่ระบุ");
                ToYearComboBox.Items.Add("ไม่ระบุ");
                FromMonthYearComboBox.Items.Add("ไม่ระบุ");
                ToMonthYearComboBox.Items.Add("ไม่ระบุ");

                // Display years in Buddhist Era (BE = AD + 543)
                foreach (var y in years)
                {
                    var be = ThaiDateHelper.GregorianToBuddhistYear(y);
                    FromYearComboBox.Items.Add(be.ToString());
                    ToYearComboBox.Items.Add(be.ToString());
                    FromMonthYearComboBox.Items.Add(be.ToString());
                    ToMonthYearComboBox.Items.Add(be.ToString());
                }

                // Set defaults: FromYear = last year, ToYear = current year
                var beNow = ThaiDateHelper.GregorianToBuddhistYear(DateTime.Now.Year).ToString();
                var beLastYear = ThaiDateHelper.GregorianToBuddhistYear(DateTime.Now.Year - 1).ToString();

                // Set FromYearComboBox to last year
                try
                {
                    var foundFrom = FromYearComboBox.Items.Cast<object?>().Any(i => i?.ToString() == beLastYear);
                    if (foundFrom)
                        FromYearComboBox.SelectedItem = beLastYear;
                    else
                        FromYearComboBox.SelectedIndex = 0;
                }
                catch
                {
                    FromYearComboBox.SelectedIndex = 0;
                }

                // Set ToYearComboBox to current year
                try
                {
                    var foundTo = ToYearComboBox.Items.Cast<object?>().Any(i => i?.ToString() == beNow);
                    if (foundTo)
                        ToYearComboBox.SelectedItem = beNow;
                    else
                        ToYearComboBox.SelectedIndex = 0;
                }
                catch
                {
                    ToYearComboBox.SelectedIndex = 0;
                }

                // ลบส่วนเก่าทั้งหมดที่เกี่ยวกับ YearCompareBox, YearCompareChips, _availableYearsBe, _selectedCompareYears

                // Populate months (existing code...)
                FromMonthComboBox.Items.Clear();
                ToMonthComboBox.Items.Clear();
                var monthNames = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedMonthNames;
                for (int m = 1; m <= 12; m++)
                {
                    var display = m.ToString("D2") + " - " + (monthNames[m - 1] ?? m.ToString());
                    FromMonthComboBox.Items.Add(display);
                    ToMonthComboBox.Items.Add(display);
                }

                // Prepare suggestions for AutoSuggestBoxes
                ProductSearchBox.ItemsSource = _allProducts.Select(p => $"{p.Code} | {p.Name}").ToList();
                CategorySearchBox.ItemsSource = _allCategories;
                UnitSearchBox.ItemsSource = _allUnits;

                // Default to Year mode
                if (RangeModeComboBox != null)
                {
                    RangeModeComboBox.SelectedIndex = 0;
                    var tag = (RangeModeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Year";
                    ShowPanelForRangeMode(tag);
                }
                else
                {
                    ShowPanelForRangeMode("Year");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"ไม่สามารถเตรียมฟิลเตอร์ได้: {ex.Message}");
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private void RangeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RangeModeComboBox?.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
            {
                ShowPanelForRangeMode(tag);

                // ตั้งค่าเริ่มต้นสำหรับโหมด MonthYear
                if (tag == "MonthYear")
                {
                    SetDefaultMonthYearRange();
                }
                // ✅ เพิ่ม: ตั้งค่าเริ่มต้นสำหรับโหมด Date
                else if (tag == "Date")
                {
                    SetDefaultDateRange();
                }

                _currentPage = 1;
                DebounceLoadReport();
            }
        }

        // เพิ่ม method ใหม่สำหรับตั้งค่า default Date mode
        private void SetDefaultDateRange()
        {
            _isUpdatingDatePickers = true;
            try
            {
                var today = DateTime.Today;

                // ตั้งค่า ToDate = วันนี้
                ToDatePickerFull.Date = new DateTimeOffset(today);

                // คำนวณ FromDate อัตโนมัติ
                DateTime fromDate;
                if (today.Day >= 1 && today.Day <= 15)
                {
                    fromDate = new DateTime(today.Year, today.Month, 1);
                }
                else
                {
                    fromDate = new DateTime(today.Year, today.Month, 16);
                }
                FromDatePickerFull.Date = new DateTimeOffset(fromDate);
            }
            finally
            {
                _isUpdatingDatePickers = false;
            }
        }

        // เพิ่ม method ใหม่หลัง SetDefaultDateRange()
        private void SetDefaultMonthYearRange()
        {
            var today = DateTime.Today;
            var lastMonth = today.AddMonths(-1);

            // ตั้งค่า "จาก" เป็นเดือนที่แล้ว
            FromMonthComboBox.SelectedIndex = lastMonth.Month - 1; // 0-based index

            // หา BE year ที่ตรงกับ lastMonth
            var lastMonthBE = (lastMonth.Year + 543).ToString();
            for (int i = 0; i < FromMonthYearComboBox.Items.Count; i++)
            {
                if (FromMonthYearComboBox.Items[i]?.ToString() == lastMonthBE)
                {
                    FromMonthYearComboBox.SelectedIndex = i;
                    break;
                }
            }

            // ตั้งค่า "ถึง" เป็นเดือนปัจจุบัน
            ToMonthComboBox.SelectedIndex = today.Month - 1; // 0-based index

            // หา BE year ที่ตรงกับ today
            var todayBE = (today.Year + 543).ToString();
            for (int i = 0; i < ToMonthYearComboBox.Items.Count; i++)
            {
                if (ToMonthYearComboBox.Items[i]?.ToString() == todayBE)
                {
                    ToMonthYearComboBox.SelectedIndex = i;
                    break;
                }
            }
        }

        private void ShowPanelForRangeMode(string modeTag)
        {
            // Defensive null checks to avoid NRE if XAML parts are not yet available.
            if (YearPanel != null)
                YearPanel.Visibility = modeTag == "Year" ? Visibility.Visible : Visibility.Collapsed;

            if (MonthYearPanel != null)
                MonthYearPanel.Visibility = modeTag == "MonthYear" ? Visibility.Visible : Visibility.Collapsed;

            if (DatePanel != null)
                DatePanel.Visibility = modeTag == "Date" ? Visibility.Visible : Visibility.Collapsed;
        }
        // --- existing AutoSuggest handlers (now trigger auto-load) ---
        private void ProductSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            // เช็คว่าเป็นการพิมพ์จริงหรือไม่
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

            var text = sender.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(text))
            {
                // ✅ แก้ไข: กรองรายการที่เลือกแล้วออกเสมอ
                sender.ItemsSource = _allProducts
                    .Where(p => !_selectedProductCodes.Contains(p.Code))
                    .Select(p => $"{p.Code} | {p.Name}")
                    .ToList();
                return;
            }

            // ✅ แก้ไข: กรองรายการที่เลือกแล้วออกในขณะค้นหาด้วย
            var suggestions = _allProducts
                .Where(p => !_selectedProductCodes.Contains(p.Code)) // ตัดรายการที่เลือกแล้ว
                .Where(p => p.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                            p.Code.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Select(p => $"{p.Code} | {p.Name}")
                .Take(30)
                .ToList();

            sender.ItemsSource = suggestions;
        }

        private async void ProductSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            string? chosen = args.ChosenSuggestion?.ToString() ?? sender.Text?.Trim();

            if (string.IsNullOrWhiteSpace(chosen))
            {
                sender.Text = string.Empty;
                return;
            }

            // แยก code จาก "CODE | Name"
            var parts = chosen.Split('|', 2);
            var code = parts.Length > 0 ? parts[0].Trim() : chosen.Trim();

            // หา product ที่ตรงกัน
            var match = _allProducts.FirstOrDefault(p =>
                string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));

            if (match != null && !_selectedProductCodes.Contains(match.Code))
            {
                // เพิ่มเข้าไปในรายการที่เลือก
                _selectedProductCodes.Add(match.Code);

                // ✅ อัปเดต chips
                UpdateSelectedProductsUI();

                // ✅ โหลดรายงาน
                _currentPage = 1;
                await LoadCostReportAsync();
            }

            // ✅ ล้างค่าเสมอ
            sender.Text = string.Empty;
            sender.IsSuggestionListOpen = false;
        }
        private void CategorySearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            var text = sender.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                sender.ItemsSource = _allCategories;
                _selectedCategory = null;
                _currentPage = 1;
                DebounceLoadReport();
                return;
            }

            var suggestions = _allCategories
                .Where(c => c.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Take(30)
                .ToList();

            sender.ItemsSource = suggestions;
            _currentPage = 1;
            DebounceLoadReport();
        }

        private void CategorySearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            var chosen = args.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(chosen)) return;

            _selectedCategory = chosen;
            sender.Text = chosen;
            _currentPage = 1;
            DebounceLoadReport();
        }

        private void CategorySearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            var text = args.ChosenSuggestion?.ToString() ?? sender.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                _selectedCategory = text.Trim();
            }
            _currentPage = 1;
            DebounceLoadReport();
        }

        private void UnitSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            var text = sender.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                sender.ItemsSource = _allUnits;
                _selectedUnit = null;
                _currentPage = 1;
                DebounceLoadReport();
                return;
            }

            var suggestions = _allUnits
                .Where(u => u.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Take(30)
                .ToList();

            sender.ItemsSource = suggestions;
            _currentPage = 1;
            DebounceLoadReport();
        }

        private void UnitSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            var chosen = args.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(chosen)) return;

            _selectedUnit = chosen;
            sender.Text = chosen;
            _currentPage = 1;
            DebounceLoadReport();
        }

        private void UnitSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            var text = args.ChosenSuggestion?.ToString() ?? sender.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                _selectedUnit = text.Trim();
            }
            _currentPage = 1;
            DebounceLoadReport();
        }

        // Generic selection/date changed handlers
        private void FilterSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentPage = 1;
            DebounceLoadReport();
        }

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            ProductSearchBox.Text = string.Empty;
            CategorySearchBox.Text = string.Empty;
            UnitSearchBox.Text = string.Empty;

            FromYearComboBox.SelectedIndex = 0;
            ToYearComboBox.SelectedIndex = 0; // ← เพิ่มบรรทัดนี้
            FromMonthComboBox.SelectedIndex = -1;
            ToMonthComboBox.SelectedIndex = -1;
            FromMonthYearComboBox.SelectedIndex = 0;
            ToMonthYearComboBox.SelectedIndex = 0;
            FromDatePickerFull.Date = null;
            ToDatePickerFull.Date = null;

            // clear selected products
            _selectedProductCodes.Clear();
            UpdateSelectedProductsUI();

            _selectedProductCode = null;
            _selectedCategory = null;
            _selectedUnit = null;

            _currentPage = 1;
            DebounceLoadReport();
        }

        // Debounce helper
        private void DebounceLoadReport(int delayMs = 350)
        {
            try
            {
                _debounceCts?.Cancel();
                _debounceCts = new CancellationTokenSource();
                var token = _debounceCts.Token;

                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(delayMs, token);
                        if (token.IsCancellationRequested) return;

                        _ = DispatcherQueue.TryEnqueue(() => _ = LoadCostReportAsync());
                    }
                    catch (TaskCanceledException) { }
                }, token);
            }
            catch
            {
                // ignore
            }
        }

        // ✅ แก้ไข: เพิ่ม flag เพื่อป้องกันการ update chart ซ้ำ
        private bool _isLoadingReport = false;

        private async Task LoadCostReportAsync()
        {
            // ✅ ป้องกันการโหลดซ้ำ
            if (_isLoadingReport) return;
            _isLoadingReport = true;

            LoadingRing.Visibility = Visibility.Visible;
            LoadingRing.IsActive = true;
            ReportItemsControl.ItemsSource = null;
            ReportItemsControl.Items.Clear();
            EmptyText.Visibility = Visibility.Collapsed;

            try
            {
                if (_allProducts == null || !_allProducts.Any())
                    _allProducts = await _product_service_fallback_GetAllProductsAsync();

                var filtered = _allProducts.AsEnumerable();

                // Product / Category / Unit filters unchanged...
                if (_selectedProductCodes != null && _selectedProductCodes.Any())
                {
                    filtered = filtered.Where(p => _selectedProductCodes.Contains(p.Code));
                }
                else if (!string.IsNullOrEmpty(_selectedProductCode))
                {
                    filtered = filtered.Where(p => string.Equals(p.Code, _selectedProductCode, StringComparison.OrdinalIgnoreCase));
                }
                else if (!string.IsNullOrWhiteSpace(ProductSearchBox.Text))
                {
                    var q = ProductSearchBox.Text.Trim();
                    filtered = filtered.Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Code.Contains(q, StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(_selectedCategory))
                {
                    filtered = filtered.Where(p => !string.IsNullOrWhiteSpace(p.Category) && p.Category!.IndexOf(_selectedCategory, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                else if (!string.IsNullOrWhiteSpace(CategorySearchBox.Text))
                {
                    var q = CategorySearchBox.Text.Trim();
                    filtered = filtered.Where(p => !string.IsNullOrWhiteSpace(p.Category) && p.Category!.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (!string.IsNullOrWhiteSpace(_selectedUnit))
                {
                    filtered = filtered.Where(p => !string.IsNullOrWhiteSpace(p.Unit) && string.Equals(p.Unit, _selectedUnit, StringComparison.OrdinalIgnoreCase));
                }
                else if (!string.IsNullOrWhiteSpace(UnitSearchBox.Text))
                {
                    var q = UnitSearchBox.Text.Trim();
                    filtered = filtered.Where(p => !string.IsNullOrWhiteSpace(p.Unit) && p.Unit!.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                // Time range handling and requiredYearsForYearMode (same as prior step)
                DateTime? rangeStart = null;
                DateTime? rangeEnd = null;
                var modeTag = (RangeModeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Year";
                List<int>? requiredYearsForYearMode = null;
                int? selectedYear = null;
                List<int> compareYears = new();
                List<(int Month, int Year, string DisplayName)>? monthColumns = null; // เพิ่มตัวแปรนี้
                List<DateTime>? dateColumns = null;

                if (modeTag == "Date")
                {
                    if (FromDatePickerFull.Date.HasValue) rangeStart = FromDatePickerFull.Date.Value.DateTime.Date;
                    if (ToDatePickerFull.Date.HasValue) rangeEnd = ToDatePickerFull.Date!.Value.DateTime.Date.AddDays(1).AddSeconds(-1);

                    // สร้างรายการวันที่
                    if (rangeStart.HasValue && rangeEnd.HasValue)
                    {
                        dateColumns = GenerateDateColumns(rangeStart.Value, rangeEnd.Value);
                    }
                }
                else if (modeTag == "MonthYear")
                {
                    if (FromMonthYearComboBox.SelectedIndex > 0 && FromMonthComboBox.SelectedIndex >= 0)
                    {
                        if (int.TryParse(FromMonthYearComboBox.SelectedItem?.ToString(), out var beFromYear))
                            if (beFromYear - 543 > 1900)
                                rangeStart = new DateTime(beFromYear - 543, FromMonthComboBox.SelectedIndex + 1, 1);
                    }

                    if (ToMonthYearComboBox.SelectedIndex > 0 && ToMonthComboBox.SelectedIndex >= 0)
                    {
                        if (int.TryParse(ToMonthYearComboBox.SelectedItem?.ToString(), out var beToYear))
                            if (beToYear - 543 > 1900)
                                rangeEnd = new DateTime(beToYear - 543, ToMonthComboBox.SelectedIndex + 1, 1).AddMonths(1).AddSeconds(-1);
                    }

                    // สร้างรายการเดือน
                    if (rangeStart.HasValue && rangeEnd.HasValue)
                    {
                        monthColumns = GenerateMonthColumns(
                            rangeStart.Value.Month,
                            rangeStart.Value.Year,
                            rangeEnd.Value.Month,
                            rangeEnd.Value.Year
                        );
                    }
                }
                else // Year mode
                {
                    int? fromYear = null;
                    int? toYear = null;

                    // Get FromYear
                    if (FromYearComboBox.SelectedIndex > 0 && int.TryParse(FromYearComboBox.SelectedItem?.ToString(), out var beFrom))
                    {
                        fromYear = beFrom - 543;
                        if (fromYear <= 1900) fromYear = null;
                    }

                    // Get ToYear
                    if (ToYearComboBox.SelectedIndex > 0 && int.TryParse(ToYearComboBox.SelectedItem?.ToString(), out var beTo))
                    {
                        toYear = beTo - 543;
                        if (toYear <= 1900) toYear = null;
                    }

                    // ถ้าเลือกทั้งสองปี ให้สร้าง range
                    if (fromYear.HasValue && toYear.HasValue)
                    {
                        // ถ้า fromYear > toYear ให้สลับ
                        if (fromYear.Value > toYear.Value)
                        {
                            (fromYear, toYear) = (toYear, fromYear);
                        }

                        selectedYear = toYear; // ใช้ปีสุดท้ายเป็น selectedYear
                        rangeStart = new DateTime(fromYear.Value, 1, 1);
                        rangeEnd = new DateTime(toYear.Value, 12, 31).AddDays(1).AddSeconds(-1);

                        // สร้างรายการปีทั้งหมดในช่วง
                        compareYears = Enumerable.Range(fromYear.Value, toYear.Value - fromYear.Value + 1)
                            .OrderBy(y => y)
                            .ToList();

                        requiredYearsForYearMode = compareYears.ToList();
                    }
                    else if (fromYear.HasValue) // มีแค่ FromYear
                    {
                        selectedYear = fromYear;
                        rangeStart = new DateTime(fromYear.Value, 1, 1);
                        rangeEnd = new DateTime(fromYear.Value, 12, 31).AddDays(1).AddSeconds(-1);
                        compareYears = new List<int> { fromYear.Value };
                        requiredYearsForYearMode = compareYears.ToList();
                    }
                    else if (toYear.HasValue) // มีแค่ ToYear
                    {
                        selectedYear = toYear;
                        rangeStart = new DateTime(toYear.Value, 1, 1);
                        rangeEnd = new DateTime(toYear.Value, 12, 31).AddDays(1).AddSeconds(-1);
                        compareYears = new List<int> { toYear.Value };
                        requiredYearsForYearMode = compareYears.ToList();
                    }
                }

                UpdateHeaderGrid(compareYears, modeTag, selectedYear, monthColumns, dateColumns);

                // Build view-model items
                var productList = filtered.ToList();

                var maxConcurrency = 12;
                using var sem = new SemaphoreSlim(maxConcurrency);

                var tasks = productList.Select(async p =>
                {
                    await sem.WaitAsync();
                    try
                    {
                        // Year-mode presence check
                        if (modeTag == "Year" && requiredYearsForYearMode != null && requiredYearsForYearMode.Count > 0)
                        {
                            foreach (var y in requiredYearsForYearMode)
                            {
                                var has = await _productService.HasPriceInYearAsync(p.Code, y);
                                if (!has) return (object?)null; // exclude product
                            }
                        }

                        // fetch latest price (existing code)
                        decimal? latestPrice = null;
                        DateTime? latestPriceDate = null;

                        if (rangeStart.HasValue && rangeEnd.HasValue)
                        {
                            var s = rangeStart.Value.Date;
                            var e = rangeEnd.Value;
                            if (e < s) (s, e) = (e, s);

                            var latestRec = await _productService.GetLatestPriceInRangeAsync(p.Code, s, e);
                            if (latestRec.Price.HasValue)
                            {
                                latestPrice = latestRec.Price;
                                latestPriceDate = latestRec.PriceDate;
                            }
                            else
                            {
                                latestPrice = await _product_service_fallback_GetAveragePriceAsync(p.Code, s, e);
                                latestPriceDate = e;
                            }
                        }
                        else
                        {
                            var rec = await _productService.GetLatestPriceRecordAsync(p.Code);
                            latestPrice = rec.Price;
                            latestPriceDate = rec.PriceDate;
                            if (!latestPrice.HasValue) latestPrice = p.Price;
                        }

                        var compareValues = new List<string>();
                        decimal? earliestPriceForCAGR = null;
                        int? earliestYearForCAGR = null;

                        if (modeTag == "Year" && compareYears != null && compareYears.Count > 0)
                        {
                            // เรียงปีจากน้อยไปมาก
                            var sortedYears = compareYears.OrderBy(y => y).ToList();

                            // ดึงราคาสำหรับแต่ละปีในช่วง
                            foreach (var cy in sortedYears)
                            {
                                var rec = await _productService.GetLatestPriceForProductInYearAsync(p.Code, cy);
                                if (rec.HasValue)
                                    compareValues.Add($"{rec.Value:N4} ฿");
                                else
                                    compareValues.Add("-");
                            }

                            // ตั้งค่า earliest year (ปีแรกในช่วง)
                            if (sortedYears.Count > 0)
                            {
                                earliestYearForCAGR = sortedYears.First();
                                var pEarliest = await _productService.GetLatestPriceForProductInYearAsync(p.Code, earliestYearForCAGR.Value);
                                if (pEarliest.HasValue)
                                {
                                    earliestPriceForCAGR = pEarliest.Value;

                                    // ตั้งค่า latestPrice เป็นปีสุดท้าย
                                    var pLatest = await _product_service_fallback_GetAveragePriceAsync(p.Code, new DateTime(sortedYears.Last(),1,1), new DateTime(sortedYears.Last(),12,31));
                                    if (pLatest.HasValue)
                                        latestPrice = pLatest.Value;
                                }
                            }
                        }
                        else if (modeTag == "MonthYear" && monthColumns != null && monthColumns.Count > 0)
                        {
                            // ดึงราคาสำหรับแต่ละเดือน
                            foreach (var (month, year, _) in monthColumns)
                            {
                                var monthPrice = await _productService.GetLatestPriceForProductInMonthAsync(p.Code, month, year);
                                if (monthPrice.HasValue)
                                    compareValues.Add($"{monthPrice.Value:N4} ฿");
                                else
                                    compareValues.Add("-");
                            }

                            // คำนวณการเปลี่ยนแปลง (เดือนแรก vs เดือนสุดท้าย)
                            if (monthColumns.Count >= 2)
                            {
                                var firstMonth = monthColumns.First();
                                var lastMonth = monthColumns.Last();

                                var firstPrice = await _productService.GetLatestPriceForProductInMonthAsync(
                                    p.Code, firstMonth.Month, firstMonth.Year);
                                var lastPrice = await _productService.GetLatestPriceForProductInMonthAsync(
                                    p.Code, lastMonth.Month, lastMonth.Year);

                                if (firstPrice.HasValue && lastPrice.HasValue)
                                {
                                    earliestPriceForCAGR = firstPrice.Value;
                                    earliestYearForCAGR = firstMonth.Year;
                                    latestPrice = lastPrice.Value;
                                }
                            }
                        }
                        else if (modeTag == "Date" && dateColumns != null && dateColumns.Count > 0)
                        {
                            // ดึงราคาสำหรับแต่ละวัน
                            foreach (var date in dateColumns)
                            {
                                var datePrice = await _productService.GetLatestPriceForProductOnDateAsync(p.Code, date);
                                if (datePrice.HasValue)
                                    compareValues.Add($"{datePrice.Value:N4} ฿");
                                else
                                    compareValues.Add("-");
                            }
                        }

                        // คำนวณ Avg และ CAGR
                        string avgIncreaseText = "-";
                        string cagrText = "-";

                        if (modeTag == "MonthYear" && monthColumns != null && monthColumns.Count >= 2)
                        {
                            // หาเดือนแรกและเดือนสุดท้ายที่มีข้อมูลจริง
                            decimal? actualFirstPrice = null;
                            decimal? actualLastPrice = null;
                            int? firstMonthIndex = null;
                            int? lastMonthIndex = null;

                            // หาเดือนแรกที่มีข้อมูล
                            for (int i = 0; i < monthColumns.Count; i++)
                            {
                                var (month, year, _) = monthColumns[i];
                                var price = await _productService.GetLatestPriceForProductInMonthAsync(p.Code, month, year);
                                if (price.HasValue)
                                {
                                    actualFirstPrice = price;
                                    firstMonthIndex = i;
                                    break;
                                }
                            }

                            // หาเดือนสุดท้ายที่มีข้อมูล
                            for (int i = monthColumns.Count - 1; i >= 0; i--)
                            {
                                var (month, year, _) = monthColumns[i];
                                var price = await _productService.GetLatestPriceForProductInMonthAsync(p.Code, month, year);
                                if (price.HasValue)
                                {
                                    actualLastPrice = price;
                                    lastMonthIndex = i;
                                    break;
                                }
                            }

                            // คำนวณเฉพาะเมื่อมีข้อมูลทั้งสองจุด
                            if (actualFirstPrice.HasValue && actualLastPrice.HasValue &&
                                firstMonthIndex.HasValue && lastMonthIndex.HasValue &&
                                lastMonthIndex > firstMonthIndex)
                            {
                                var firstMonthData = monthColumns[firstMonthIndex.Value];
                                var lastMonthData = monthColumns[lastMonthIndex.Value];

                                // คำนวณจำนวนเดือนระหว่าง
                                var monthsBetween = ((lastMonthData.Year - firstMonthData.Year) * 12) +
                                                   (lastMonthData.Month - firstMonthData.Month);

                                if (monthsBetween > 0)
                                {
                                    // average absolute increase per month
                                    var avgIncrease = (actualLastPrice.Value - actualFirstPrice.Value) / monthsBetween;
                                    avgIncreaseText = $"{avgIncrease:N4} ฿/mo";

                                    // CAGR percent (แปลงเป็นรายปี)
                                    if (actualFirstPrice.Value > 0m)
                                    {
                                        var ratio = actualLastPrice.Value / actualFirstPrice.Value;
                                        var periodsPerYear = 12.0 / monthsBetween;
                                        var cagr = Math.Pow((double)ratio, periodsPerYear) - 1.0;
                                        cagrText = $"{(cagr >= 0 ? "+" : "")}{cagr * 100.0:N4}%";
                                    }
                                    else
                                    {
                                        cagrText = "∞";
                                    }
                                }
                            }
                        }
                        else if (modeTag == "Date" && dateColumns != null && dateColumns.Count > 0)
                        {
                            // ✅ โหมดวัน: ไม่เอาวันที่เป็น "-" มาคิด โดยหาแค่จุดแรก/จุดสุดท้ายที่มีราคา
                            decimal? firstPrice = null;
                            decimal? lastPrice = null;
                            DateTime? firstDateWithPrice = null;
                            DateTime? lastDateWithPrice = null;

                            // หา "วันแรกที่มีราคา"
                            foreach (var date in dateColumns.OrderBy(d => d))
                            {
                                var price = await _productService.GetLatestPriceForProductOnDateAsync(p.Code, date);
                                if (price.HasValue)
                                {
                                    firstPrice = price.Value;
                                    firstDateWithPrice = date;
                                    break;
                                }
                            }

                            // หา "วันสุดท้ายที่มีราคา"
                            foreach (var date in dateColumns.OrderByDescending(d => d))
                            {
                                var price = await _productService.GetLatestPriceForProductOnDateAsync(p.Code, date);
                                if (price.HasValue)
                                {
                                    lastPrice = price.Value;
                                    lastDateWithPrice = date;
                                    break;
                                }
                            }

                            if (firstPrice.HasValue && lastPrice.HasValue &&
                                firstDateWithPrice.HasValue && lastDateWithPrice.HasValue &&
                                lastDateWithPrice > firstDateWithPrice)
                            {
                                var daysBetween = (lastDateWithPrice.Value - firstDateWithPrice.Value).TotalDays;

                                if (daysBetween > 0)
                                {
                                    // average absolute increase per day
                                    var avgIncrease = (lastPrice.Value - firstPrice.Value) / (decimal)daysBetween;
                                    avgIncreaseText = $"{avgIncrease:N4} ฿/day";

                                    // CAGR แปลง period เป็นปี (daysBetween/365)
                                    if (firstPrice.Value > 0m)
                                    {
                                        var ratio = (double)(lastPrice.Value / firstPrice.Value);
                                        var years = daysBetween / 365.0;
                                        var cagr = Math.Pow(ratio, 1.0 / years) - 1.0;
                                        cagrText = $"{(cagr >= 0 ? "+" : "")}{cagr * 100.0:N4}%";
                                    }
                                    else
                                    {
                                        cagrText = "∞";
                                    }
                                }
                            }
                        }
                        else if (modeTag == "Year" && compareYears != null && compareYears.Count > 1)
                        {
                            var sortedYears = compareYears.OrderBy(y => y).ToList();

                            // หาปีแรกและปีสุดท้ายที่มีข้อมูลจริง
                            decimal? actualFirstPrice = null;
                            decimal? actualLastPrice = null;
                            int? firstYearWithData = null;
                            int? lastYearWithData = null;

                            // หาปีแรกที่มีข้อมูล
                            foreach (var y in sortedYears)
                            {
                                var price = await _productService.GetLatestPriceForProductInYearAsync(p.Code, y);
                                if (price.HasValue)
                                {
                                    actualFirstPrice = price;
                                    firstYearWithData = y;
                                    break;
                                }
                            }

                            // หาปีสุดท้ายที่มีข้อมูล
                            for (int i = sortedYears.Count - 1; i >= 0; i--)
                            {
                                var price = await _product_service_fallback_GetAveragePriceAsync(p.Code, new DateTime(sortedYears[i],1,1), new DateTime(sortedYears[i],12,31));
                                if (price.HasValue)
                                {
                                    actualLastPrice = price;
                                    lastYearWithData = sortedYears[i];
                                    break;
                                }
                            }

                            // คำนวณเฉพาะเมื่อมีข้อมูลทั้งสองปี
                            if (actualFirstPrice.HasValue && actualLastPrice.HasValue &&
                                firstYearWithData.HasValue && lastYearWithData.HasValue &&
                                lastYearWithData > firstYearWithData)
                            {
                                var yearsBetween = lastYearWithData.Value - firstYearWithData.Value;

                                if (yearsBetween > 0)
                                {
                                    // average absolute increase per year
                                    var avgIncrease = (actualLastPrice.Value - actualFirstPrice.Value) / yearsBetween;
                                    avgIncreaseText = $"{avgIncrease:N4} ฿/yr";

                                    // CAGR percent
                                    if (actualFirstPrice.Value > 0m)
                                    {
                                        var ratio = actualLastPrice.Value / actualFirstPrice.Value;
                                        var cagr = Math.Pow((double)ratio, 1.0 / yearsBetween) - 1.0;
                                        cagrText = $"{(cagr >= 0 ? "+" : "")}{cagr * 100.0:N4}%";
                                    }
                                    else
                                    {
                                        cagrText = "∞";
                                    }
                                }
                            }
                        }

                        // Also keep existing previous/ diff / pct for non-Year mode compatibility (simple placeholders here)
                        string prevText = "-";
                        string diffText = "-";
                        string pctText = "-";

                        // Build the result object including dynamic compare values and summaries
                        return (object?)new
                        {
                            ProductCode = p.Code,
                            ProductName = p.Name,
                            Category = p.Category ?? "",
                            Unit = p.Unit ?? "",
                            LatestPrice = latestPrice.HasValue ? $"{latestPrice.Value:N4} ฿" : "-",
                            CompareYearValues = compareValues, // List<string>
                            AvgIncreasePerYear = avgIncreaseText,
                            CAGRPercent = cagrText,
                            ModeTag = modeTag, // ← เพิ่มบรรทัดนี้
                            // keep legacy fields for compatibility
                            PreviousPrice = prevText,
                            Difference = diffText,
                            PercentChange = pctText
                        };
                    }
                    finally
                    {
                        sem.Release();
                    }
                });

                var resultArray = await Task.WhenAll(tasks);

                // Filter out excluded products (nulls)
                var nonNull = resultArray.Where(r => r != null).Select(r => r!).Cast<object>().ToList();
                var ordered = nonNull.OrderBy(i => (i as dynamic).ProductCode).ToList();

                _allReportItems = ordered.Cast<object>().ToList();

                _totalPages = Math.Max(1, (int)Math.Ceiling(_allReportItems.Count / (double)PageSize));
                if (_currentPage > _totalPages) _currentPage = _totalPages;

                // ✅ เก็บ parameters ไว้ใช้ตอนเปลี่ยนหน้า
                _lastChartParameters = (modeTag, compareYears, monthColumns, dateColumns);

                // ✅ เรียก ApplyPaginationAndShow แต่ไม่ให้มันอัปเดตกราฟ
                ApplyPaginationAndShow(updateChart: false);

                // ✅ อัปเดตกราฟเพียงครั้งเดียวที่นี่
                var skip = (_currentPage - 1) * PageSize;
                var currentPageProducts = productList.Skip(skip).Take(PageSize).ToList();
                await UpdateChartAsync(currentPageProducts, modeTag, compareYears, monthColumns, dateColumns);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"ไม่สามารถโหลดรายงานได้: {ex.Message}");
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
                _isLoadingReport = false; // ✅ reset flag
            }

            async Task<List<Product>> _product_service_fallback_GetAllProductsAsync()
            {
                try
                {
                    return await _productService.GetAllProductsAsync();
                }
                catch
                {
                    return new List<Product>();
                }
            }

            async Task<decimal?> _product_service_fallback_GetAveragePriceAsync(string code, DateTime s, DateTime e)
            {
                try
                {
                    return await _productService.GetAveragePriceAsync(code, s, e);
                }
                catch
                {
                    return null;
                }
            }
        }

        // ✅ แก้ไข: เพิ่ม parameter updateChart
        private void ApplyPaginationAndShow(bool updateChart = true)
        {
            if (_allReportItems == null || !_allReportItems.Any())
            {
                ReportItemsControl.ItemsSource = null;
                ReportItemsControl.Items.Clear();
                EmptyText.Visibility = Visibility.Visible;
                PageInfoTextBlock.Text = "หน้า 0 / 0";
                PrevPageButton.IsEnabled = false;
                NextPageButton.IsEnabled = false;

                // ✅ ล้างกราฟด้วย
                _chartData.Clear();
                _chartLabels.Clear();
                CostChart?.Invalidate();
                return;
            }

            _totalPages = Math.Max(1, (int)Math.Ceiling(_allReportItems.Count / (double)PageSize));
            if (_currentPage < 1) _currentPage = 1;
            if (_currentPage > _totalPages) _currentPage = _totalPages;

            var skip = (_currentPage - 1) * PageSize;
            var pageItems = _allReportItems.Skip(skip).Take(PageSize).ToList();

            RenderPageItems(pageItems);

            EmptyText.Visibility = pageItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            PageInfoTextBlock.Text = $"หน้า {_currentPage} / {_totalPages} ({_allReportItems.Count} รายการ)";

            PrevPageButton.IsEnabled = _currentPage > 1;
            NextPageButton.IsEnabled = _currentPage < _totalPages;

            // ✅ อัปเดตกราฟเฉพาะเมื่อ updateChart = true
            if (updateChart)
            {
                UpdateChartForCurrentPage();
            }
        }
        private (
    string ModeTag,
    List<int> CompareYears,
    List<(int Month, int Year, string DisplayName)>? MonthColumns,
    List<DateTime>? DateColumns
) _lastChartParameters;
        private void UpdateChartForCurrentPage()
        {
            // ดึงรายการในหน้าปัจจุบัน
            var skip = (_currentPage - 1) * PageSize;
            var currentPageItems = _allReportItems.Skip(skip).Take(PageSize).ToList();

            // แปลง dynamic objects เป็น Product list
            var productCodes = currentPageItems
                .Select(item => ((dynamic)item).ProductCode as string)
                .Where(code => !string.IsNullOrEmpty(code))
                .ToList();

            var currentPageProducts = _allProducts
                .Where(p => productCodes.Contains(p.Code))
                .ToList();

            // ใช้ข้อมูล mode/columns จาก _lastChartParameters
            _ = UpdateChartAsync(
                currentPageProducts,
                _lastChartParameters.ModeTag,
                _lastChartParameters.CompareYears,
                _lastChartParameters.MonthColumns,
                _lastChartParameters.DateColumns
            );
        }
        private void RenderPageItems(List<object> pageItems)
        {
            ReportItemsControl.Items.Clear();

            foreach (var it in pageItems)
            {
                dynamic d = it;
                var row = new Grid
                {
                    Padding = new Thickness(0, 4, 0, 4), // padding แนวตั้งเท่านั้น
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

                // Copy column definitions from schema (ใช้ Width และ MinWidth เดียวกัน)
                for (int i = 0; i < _currentColumnSchema.Count; i++)
                {
                    var colDef = _currentColumnSchema[i];
                    row.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = colDef.Width,
                        MinWidth = colDef.MinWidth
                    });

                    string cellValue = "-";
                    try
                    {
                        cellValue = colDef.GetValue?.Invoke(d) ?? "-";
                    }
                    catch { }

                    var tb = new TextBlock
                    {
                        Text = cellValue,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        Padding = new Thickness(12, 6, 12, 6), // ← ใช้ padding เดียวกับ header
                        TextWrapping = TextWrapping.NoWrap
                    };

                    // Apply TextTrimming for name column only
                    if (i == 1)
                        tb.TextTrimming = TextTrimming.CharacterEllipsis;

                    if (colDef.Foreground != null)
                        tb.Foreground = colDef.Foreground;

                    Grid.SetColumn(tb, i);
                    row.Children.Add(tb);
                }

                ReportItemsControl.Items.Add(row);
            }
        }

        private DateTime? GetRepresentativeDate(Product p)
        {
            // ✅ PriceDate is DateTime? — use directly
            if (p.PriceDate.HasValue) return p.PriceDate.Value;
            
            if (p.LastModifiedDate.HasValue) return p.LastModifiedDate.Value;
            if (p.CreatedDate.HasValue) return p.CreatedDate.Value;
            return null;
        }

        // Added missing pagination click handlers

        private void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage <= 1) return;
            _currentPage--;
            ApplyPaginationAndShow();
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage >= _totalPages) return;
            _currentPage++;
            ApplyPaginationAndShow();
        }

        // --- New helper: update header grid columns and header texts dynamically ---
        private void UpdateHeaderGrid(
    List<int> compareYears,
    string modeTag,
    int? selectedYear,
    List<(int Month, int Year, string DisplayName)>? monthColumns = null,
    List<DateTime>? dateColumns = null) // ← เพิ่ม parameter นี้
        {
            _currentColumnSchema.Clear();

            // Static columns with Auto width
            _currentColumnSchema.Add(new ColumnDefinitionInfo
            {
                HeaderText = "รหัสวัตถุดิบ",
                Width = GridLength.Auto,
                MinWidth = 100, // เพิ่ม MinWidth เพื่อป้องกันแคบเกินไป
                IsStatic = true,
                GetValue = d => d.ProductCode ?? ""
            });

            _currentColumnSchema.Add(new ColumnDefinitionInfo
            {
                HeaderText = "ชื่อวัตถุดิบ",
                Width = GridLength.Auto,
                MinWidth = 200, // ชื่อควรกว้างกว่า
                IsStatic = true,
                GetValue = d => d.ProductName ?? ""
            });

            _currentColumnSchema.Add(new ColumnDefinitionInfo
            {
                HeaderText = "หมวดหมู่",
                Width = GridLength.Auto,
                MinWidth = 120,
                IsStatic = true,
                GetValue = d => d.Category ?? ""
            });

            _currentColumnSchema.Add(new ColumnDefinitionInfo
            {
                HeaderText = "หน่วย",
                Width = GridLength.Auto,
                MinWidth = 80,
                IsStatic = true,
                GetValue = d => d.Unit ?? "",
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray)
            });

            // Dynamic columns (Year mode)
            if (modeTag == "Year" && compareYears != null && compareYears.Count > 0)
            {
                var sortedYears = compareYears.OrderBy(y => y).ToList();
                int yearIndex = 0;

                foreach (var year in sortedYears)
                {
                    int capturedIndex = yearIndex;
                    _currentColumnSchema.Add(new ColumnDefinitionInfo
                    {
                        HeaderText = $"ปี {year + 543}",
                        Width = GridLength.Auto, // ← เปลี่ยนเป็น Auto
                        MinWidth = 100,
                        IsStatic = false,
                        GetValue = d =>
                        {
                            var list = d.CompareYearValues as List<string>;
                            return (list != null && capturedIndex < list.Count) ? list[capturedIndex] : "-";
                        }
                    });
                    yearIndex++;
                }

                if (sortedYears.Count > 1)
                {
                    _currentColumnSchema.Add(new ColumnDefinitionInfo
                    {
                        HeaderText = "Avg/yr",
                        Width = GridLength.Auto,
                        MinWidth = 100,
                        IsStatic = false,
                        GetValue = d => d.AvgIncreasePerYear ?? "-",
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkGreen)
                    });

                    _currentColumnSchema.Add(new ColumnDefinitionInfo
                    {
                        HeaderText = "% CAGR",
                        Width = GridLength.Auto,
                        MinWidth = 100,
                        IsStatic = false,
                        GetValue = d => d.CAGRPercent ?? "-",
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkBlue)
                    });
                }
            }
            else if (modeTag == "MonthYear" && monthColumns != null && monthColumns.Count > 0)
            {
                int monthIndex = 0;
                foreach (var (month, year, displayName) in monthColumns)
                {
                    int capturedIndex = monthIndex;
                    _currentColumnSchema.Add(new ColumnDefinitionInfo
                    {
                        HeaderText = displayName,
                        Width = GridLength.Auto,
                        MinWidth = 100,
                        IsStatic = false,
                        GetValue = d =>
                        {
                            var list = d.CompareYearValues as List<string>;
                            return (list != null && capturedIndex < list.Count) ? list[capturedIndex] : "-";
                        }
                    });
                    monthIndex++;
                }

                if (monthColumns.Count > 1)
                {
                    _currentColumnSchema.Add(new ColumnDefinitionInfo
                    {
                        HeaderText = "Avg/mo",
                        Width = GridLength.Auto,
                        MinWidth = 100,
                        IsStatic = false,
                        GetValue = d => d.AvgIncreasePerYear ?? "-",
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkGreen)
                    });

                    _currentColumnSchema.Add(new ColumnDefinitionInfo
                    {
                        HeaderText = "% CAGR",
                        Width = GridLength.Auto,
                        MinWidth = 100,
                        IsStatic = false,
                        GetValue = d => d.CAGRPercent ?? "-",
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkBlue)
                    });
                }
            }
            else if (modeTag == "Date" && dateColumns != null && dateColumns.Count > 0)
            {
                int dateIndex = 0;
                foreach (var date in dateColumns)
                {
                    int capturedIndex = dateIndex;
                    _currentColumnSchema.Add(new ColumnDefinitionInfo
                    {
                        HeaderText = $"{date:dd/MM/yy}",
                        Width = GridLength.Auto,
                        MinWidth = 100,
                        IsStatic = false,
                        GetValue = d =>
                        {
                            var list = d.CompareYearValues as List<string>;
                            return (list != null && capturedIndex < list.Count) ? list[capturedIndex] : "-";
                        }
                    });
                    dateIndex++;
                }

                if (dateColumns.Count > 1)
                {
                    _currentColumnSchema.Add(new ColumnDefinitionInfo
                    {
                        HeaderText = "Avg/day",
                        Width = GridLength.Auto,
                        MinWidth = 100,
                        IsStatic = false,
                        GetValue = d => d.AvgIncreasePerYear ?? "-",
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkGreen)
                    });

                    _currentColumnSchema.Add(new ColumnDefinitionInfo
                    {
                        HeaderText = "% CAGR",
                        Width = GridLength.Auto,
                        MinWidth = 100,
                        IsStatic = false,
                        GetValue = d => d.CAGRPercent ?? "-",
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkBlue)
                    });
                }
            }

            // Rebuild header using schema
            RebuildHeaderFromSchema();
        }

        private void RebuildHeaderFromSchema()
        {
            foreach (var e in _dynamicHeaderElements)
            {
                HeaderGrid.Children.Remove(e);
            }
            _dynamicHeaderElements.Clear();
            HeaderGrid.ColumnDefinitions.Clear();

            for (int i = 0; i < _currentColumnSchema.Count; i++)
            {
                var colDef = _currentColumnSchema[i];

                // สร้าง ColumnDefinition
                var colDefinition = new ColumnDefinition
                {
                    Width = colDef.Width,
                    MinWidth = colDef.MinWidth
                };
                HeaderGrid.ColumnDefinitions.Add(colDefinition);

                // สร้าง Header TextBlock
                var tb = new TextBlock
                {
                    Text = colDef.HeaderText,
                    FontWeight = FontWeights.SemiBold,
                    Padding = new Thickness(12, 8, 12, 8), // ← เพิ่ม padding
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap // ป้องกันขึ้นบรรทัดใหม่
                };

                if (colDef.Foreground != null)
                    tb.Foreground = colDef.Foreground;

                Grid.SetColumn(tb, i);
                HeaderGrid.Children.Add(tb);

                if (!colDef.IsStatic)
                    _dynamicHeaderElements.Add(tb);
            }
        }

        // Add this helper method inside the CostReportPage class (e.g. near other helpers)
        private int? ParseBeToAdSafe(string? be)
        {
            if (string.IsNullOrWhiteSpace(be)) return null;
            if (int.TryParse(be, out var bev))
            {
                var ad = bev - 543;
                if (ad > 1900) return ad;
            }
            return null;
        }

        private void RemoveSelectedProduct_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string display)
            {
                // display format is "CODE | Name" — extract code
                var parts = display.Split('|', 2);
                var code = parts.Length > 0 ? parts[0].Trim() : null;
                if (!string.IsNullOrWhiteSpace(code) && _selectedProductCodes.Contains(code))
                {
                    _selectedProductCodes.Remove(code);
                    UpdateSelectedProductsUI();
                    _currentPage = 1;
                    DebounceLoadReport();
                }
            }
        }
        private void UpdateSelectedProductsUI()
        {
            var chips = _selectedProductCodes
                .Select(c => GetProductDisplay(c))
                .ToList();

            ProductChips.ItemsSource = chips;

            // ✅ อัปเดต suggestion list ให้ตัดรายการที่เลือกแล้วออก
            ProductSearchBox.ItemsSource = _allProducts
                .Where(p => !_selectedProductCodes.Contains(p.Code))
                .Select(p => $"{p.Code} | {p.Name}")
                .ToList();
        }

        private string GetProductDisplay(string code)
        {
            var p = _allProducts.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
            return p != null ? $"{p.Code} | {p.Name}" : code;
        }

        // Called when the Page receives a pointer press.
        // If the click is outside any AutoSuggestBox, close their suggestion lists.
        private void CostReportPage_PointerPressed(object? sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            try
            {
                var src = e.OriginalSource as DependencyObject;
                if (src == null) return;

                // If the click is within an AutoSuggestBox (or its children), do nothing.
                if (IsInsideAutoSuggest(src)) return;

                // Otherwise close both suggestion lists.
                try { ProductSearchBox.IsSuggestionListOpen = false; } catch { }
            }
            catch
            {
                // ignore any unexpected VisualTree inspection errors
            }
        }

        // When an AutoSuggestBox loses focus, close its suggestion list to avoid it staying open.
        private void AutoSuggestBox_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (sender is AutoSuggestBox box)
            {
                try { box.IsSuggestionListOpen = false; } catch { }
            }
        }

        // Helper: walk up visual tree to detect if 'd' is inside an AutoSuggestBox.
        private bool IsInsideAutoSuggest(DependencyObject? d)
        {
            while (d != null)
            {
                if (d is AutoSuggestBox) return true;
                d = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        // Replace existing ShowErrorAsync method with this implementation
        private async Task ShowErrorAsync(string message)
        {
            // Prevent multiple dialogs opening concurrently
            if (_contentDialogOpen) return;

            _contentDialogOpen = true;
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "ข้อผิดพลาด",
                    Content = message,
                    CloseButtonText = "ตกลง",
                    XamlRoot = Content.XamlRoot
                };

                // Ensure ShowAsync runs on UI thread. If we are not on UI thread, marshal and await via TaskCompletionSource.
                if (DispatcherQueue.HasThreadAccess)
                {
                    await dialog.ShowAsync();
                }
                else
                {
                    var tcs = new TaskCompletionSource<object?>();
                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        try
                        {
                            await dialog.ShowAsync();
                            tcs.SetResult(null);
                        }
                        catch (Exception ex)
                        {
                            tcs.SetException(ex);
                        }
                    });
                    await tcs.Task;
                }
            }
            finally
            {
                _contentDialogOpen = false;
            }
        }

        // เพิ่ม method ใหม่สำหรับสร้างรายการเดือน
        private List<(int Month, int Year, string DisplayName)> GenerateMonthColumns(int fromMonth, int fromYear, int toMonth, int toYear)
        {
            var columns = new List<(int Month, int Year, string DisplayName)>();

            var currentDate = new DateTime(fromYear, fromMonth, 1);
            var endDate = new DateTime(toYear, toMonth, 1);

            int maxMonths = 36;
            int monthCount = 0;

            while (currentDate <= endDate && monthCount < maxMonths)
            {
                string[] thaiMonthsShort = {
            "ม.ค.", "ก.พ.", "มี.ค.", "เม.ย.", "พ.ค.", "มิ.ย.",
            "ก.ค.", "ส.ค.", "ก.ย.", "ต.ค.", "พ.ย.", "ธ.ค."
        };

                int buddhistYear = currentDate.Year + 543;

                // ✅ แก้ไข: แสดงแค่ "เดือน ปี" (ไม่มีวัน)
                // แสดงเป็น "ม.ค. 2568" แทน "12 - ธ.ค. 68"
                string displayName = ThaiDateHelper.ToThaiMonthYear(currentDate);

                columns.Add((currentDate.Month, currentDate.Year, displayName));

                currentDate = currentDate.AddMonths(1);
                monthCount++;
            }

            return columns;
        }
        // เพิ่ม method ใหม่สำหรับสร้างรายการวันที่
        private List<DateTime> GenerateDateColumns(DateTime fromDate, DateTime toDate)
        {
            var columns = new List<DateTime>();

            // ป้องกัน infinite loop และจำกัดจำนวนวันสูงสุด
            int maxDays = 90; // จำกัดไม่เกิน 90 วัน (ประมาณ 3 เดือน)
            var currentDate = fromDate.Date;
            var endDate = toDate.Date;
            int dayCount = 0;

            // วนลูปสร้างคอลัมน์ทีละวัน
            while (currentDate <= endDate && dayCount < maxDays)
            {
                columns.Add(currentDate);
                currentDate = currentDate.AddDays(1);
                dayCount++;
            }

            return columns;
        }

        // เพิ่ม field เพื่อป้องกัน infinite loop
        private bool _isUpdatingDatePickers = false;

        private void ToDatePickerFull_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            if (_isUpdatingDatePickers) return;

            if (args.NewDate.HasValue)
            {
                _isUpdatingDatePickers = true;
                try
                {
                    var selectedDate = args.NewDate.Value.DateTime;
                    var day = selectedDate.Day;

                    // คำนวณ FromDate ตามกฎ (ถ้ายังไม่ได้เลือกเอง)
                    DateTime fromDate;
                    if (day >= 1 && day <= 15)
                    {
                        fromDate = new DateTime(selectedDate.Year, selectedDate.Month, 1);
                    }
                    else
                    {
                        fromDate = new DateTime(selectedDate.Year, selectedDate.Month, 16);
                    }

                    // ตั้งค่า FromDate (แต่ user ยังสามารถแก้ไขได้)
                    FromDatePickerFull.Date = new DateTimeOffset(fromDate);
                }
                finally
                {
                    _isUpdatingDatePickers = false;
                }
            }
            else
            {
                _isUpdatingDatePickers = true;
                try
                {
                    FromDatePickerFull.Date = null;
                }
                finally
                {
                    _isUpdatingDatePickers = false;
                }
            }

            _currentPage = 1;
            DebounceLoadReport();
        }
        private void FromDatePickerFull_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            if (_isUpdatingDatePickers) return;

            // ✅ ให้ user แก้ไขได้ และ reload report
            _currentPage = 1;
            DebounceLoadReport();
        }

        // ✅ เพิ่ม fields สำหรับกราฟ (เพิ่มหลัง _selectedProductCodes และก่อน _contentDialogOpen)
        private List<ChartSeriesData> _chartData = new();
        private List<string> _chartLabels = new();
        private Point? _tooltipPosition = null;
        private ChartTooltipData? _tooltipData = null;

        // ✅ เพิ่ม nested classes เหล่านี้ภายใน CostReportPage class (ก่อน Constructor หรือหลัง ColumnDefinitionInfo)
        public class ChartSeriesData
        {
            public string ProductCode { get; set; } = "";
            public string ProductName { get; set; } = "";
            public List<double?> Values { get; set; } = new();
            public Windows.UI.Color Color { get; set; }
        }

        public class ChartTooltipData
        {
            public string Label { get; set; } = "";
            public List<(string ProductName, double? Value, Windows.UI.Color Color)> Items { get; set; } = new();
        }

        // ✅ เพิ่ม method นี้ก่อนวงเล็บปีกกาปิดสุดท้ายของ class (หลัง FromDatePickerFull_DateChanged)
        public async Task UpdateChartAsync(
    List<Product> products,
    string modeTag,
    List<int> compareYears,
    List<(int Month, int Year, string DisplayName)>? monthColumns,
    List<DateTime>? dateColumns)
        {
            // ✅ Clear ข้อมูลเก่าทั้งหมด
            _chartData.Clear();
            _chartLabels.Clear();
            _tooltipPosition = null;
            _tooltipData = null;

            // ✅ เพิ่มสีให้ครอบคลุม 10 รายการขึ้นไป
            var colors = new[]
            {
        Windows.UI.Color.FromArgb(255, 33, 150, 243),   // Blue
        Windows.UI.Color.FromArgb(255, 244, 67, 54),    // Red
        Windows.UI.Color.FromArgb(255, 76, 175, 80),    // Green
        Windows.UI.Color.FromArgb(255, 255, 152, 0),    // Orange
        Windows.UI.Color.FromArgb(255, 156, 39, 176),   // Purple
        Windows.UI.Color.FromArgb(255, 121, 85, 72),    // Brown
        Windows.UI.Color.FromArgb(255, 233, 30, 99),    // Pink
        Windows.UI.Color.FromArgb(255, 0, 188, 212),    // Cyan
        Windows.UI.Color.FromArgb(255, 255, 193, 7),    // Amber
        Windows.UI.Color.FromArgb(255, 96, 125, 139),   // Blue Grey
        Windows.UI.Color.FromArgb(255, 205, 220, 57),   // Lime
        Windows.UI.Color.FromArgb(255, 63, 81, 181)     // Indigo
    };

            // สร้าง labels สำหรับแกน X
            if (modeTag == "Year" && compareYears != null && compareYears.Any())
            {
                _chartLabels = compareYears.OrderBy(y => y).Select(y => (y + 543).ToString()).ToList();
            }
            else if (modeTag == "MonthYear" && monthColumns != null && monthColumns.Any())
            {
                _chartLabels = monthColumns.Select(m => m.DisplayName).ToList();
            }
            else if (modeTag == "Date" && dateColumns != null && dateColumns.Any())
            {
                _chartLabels = dateColumns.Select(d => d.ToString("dd/MM")).ToList();
            }

            // ✅ ตรวจสอบว่ามี labels หรือไม่ ก่อนดำเนินการต่อ
            if (!_chartLabels.Any())
            {
                CostChart?.Invalidate();
                return;
            }

            // ✅ เปลี่ยนจาก Take(8) เป็น Take(10)
            int colorIndex = 0;
            var processedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var product in products.Take(10) // ← เปลี่ยนจาก 8 เป็น 10
)
            {
                if (!processedCodes.Add(product.Code))
                    continue;

        var values = new List<double?>();

        if (modeTag == "Year" && compareYears != null && compareYears.Any())
        {
            var sortedYears = compareYears.OrderBy(y => y).ToList();
            foreach (var year in sortedYears)
            {
                var price = await _productService.GetLatestPriceForProductInYearAsync(product.Code, year);
                values.Add(price.HasValue ? (double)price.Value : null);
            }
        }
        else if (modeTag == "MonthYear" && monthColumns != null && monthColumns.Any())
        {
            foreach (var (month, year, _) in monthColumns)
            {
                var price = await _productService.GetLatestPriceForProductInMonthAsync(product.Code, month, year);
                values.Add(price.HasValue ? (double)price.Value : null);
            }
        }
        else if (modeTag == "Date" && dateColumns != null && dateColumns.Any())
        {
            foreach (var date in dateColumns)
            {
                var price = await _productService.GetLatestPriceForProductOnDateAsync(product.Code, date);
                values.Add(price.HasValue ? (double)price.Value : null);
            }
        }

        // เพิ่มเฉพาะสินค้าที่มีข้อมูลอย่างน้อย 1 จุด
        if (values.Any(v => v.HasValue))
        {
            _chartData.Add(new ChartSeriesData
            {
                ProductCode = product.Code,
                ProductName = product.Name,
                Values = values,
                Color = colors[colorIndex % colors.Length]
            });

            colorIndex++;
        }
    }

    // บังคับให้กราฟวาดใหม่
    CostChart?.Invalidate();
}
        // ✅ ขั้นตอนที่ 4: Event Handler สำหรับวาดกราฟ (ปรับปรุง)
        private void CostChart_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            var ds = args.DrawingSession;
            var size = sender.Size;

            // ถ้าไม่มีข้อมูล ให้แสดงข้อความ
            if (_chartData == null || !_chartData.Any() || _chartLabels == null || !_chartLabels.Any())
            {
                ds.Clear(Microsoft.UI.Colors.White);
                ds.DrawText(
                    "ไม่มีข้อมูลสำหรับแสดงกราฟ",
                    new Vector2((float)size.Width / 2, (float)size.Height / 2),
                    Microsoft.UI.Colors.Gray,
                    new CanvasTextFormat
                    {
                        FontSize = 16,
                        HorizontalAlignment = CanvasHorizontalAlignment.Center,
                        VerticalAlignment = CanvasVerticalAlignment.Center,
                        FontFamily = "Segoe UI"
                    }
                );
                return;
            }

            // กำหนดขอบเขตของกราฟ
            float leftMargin = 60;
            float rightMargin = 150; // สำหรับ legend
            float topMargin = 40;
            float bottomMargin = 60;

            float chartWidth = (float)size.Width - leftMargin - rightMargin;
            float chartHeight = (float)size.Height - topMargin - bottomMargin;

            // พื้นหลังสีขาว
            ds.Clear(Microsoft.UI.Colors.White);

            // หาค่า min/max สำหรับ Y axis
            var allValues = _chartData.SelectMany(s => s.Values.Where(v => v.HasValue).Select(v => v!.Value)).ToList();
            if (!allValues.Any()) return;

            double minValue = allValues.Min();
            double maxValue = allValues.Max();
            double range = maxValue - minValue;

            // เพิ่ม padding 10%
            double padding = range * 0.1;
            minValue -= padding;
            maxValue += padding;
            if (minValue < 0) minValue = 0;

            // ✅ แก้ไข: วาดป้าย Y-axis ("ราคา") แบบหมุน 90 องศา
            using (ds.CreateLayer(1.0f))
            {
                var centerY = topMargin + chartHeight / 2;
                var labelX = 15f;

                // สร้าง transform matrix สำหรับหมุน -90 องศา (หมุนทวนเข็มนาฬิกา)
                var transform = Matrix3x2.CreateRotation((float)(-Math.PI / 2), new Vector2(labelX, centerY));
                ds.Transform = transform;

                ds.DrawText(
                    "ราคา (฿)",
                    new Vector2(labelX, centerY),
                    Microsoft.UI.Colors.Black,
                    new CanvasTextFormat
                    {
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        HorizontalAlignment = CanvasHorizontalAlignment.Center,
                        VerticalAlignment = CanvasVerticalAlignment.Center,
                        FontFamily = "Segoe UI"
                    }
                );

                // Reset transform
                ds.Transform = Matrix3x2.Identity;
            }

            // วาด grid lines และ Y axis labels
            int gridLines = 5;
            for (int i = 0; i <= gridLines; i++)
            {
                float y = topMargin + (chartHeight * i / gridLines);
                double value = maxValue - (range * i / gridLines);

                // Grid line
                ds.DrawLine(
                    leftMargin, y,
                    leftMargin + chartWidth, y,
                    Windows.UI.Color.FromArgb(50, 200, 200, 200),
                    1
                );

                // Y axis label
                ds.DrawText(
                    $"{value:N0}",
                    new Vector2(leftMargin - 5, y),
                    Microsoft.UI.Colors.Gray,
                    new CanvasTextFormat
                    {
                        FontSize = 11,
                        HorizontalAlignment = CanvasHorizontalAlignment.Right,
                        VerticalAlignment = CanvasVerticalAlignment.Center,
                        FontFamily = "Segoe UI"
                    }
                );
            }

            // วาดแกน X และ Y
            ds.DrawLine(leftMargin, topMargin, leftMargin, topMargin + chartHeight, Microsoft.UI.Colors.Black, 2); // Y axis
            ds.DrawLine(leftMargin, topMargin + chartHeight, leftMargin + chartWidth, topMargin + chartHeight, Microsoft.UI.Colors.Black, 2); // X axis

            // ✅ หาชื่อแกน X ตามโหมด
            string xAxisLabel;
            var modeTag = (RangeModeComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Year";

            if (modeTag == "Year")
            {
                xAxisLabel = "ปี (พ.ศ.)";
            }
            else if (modeTag == "MonthYear")
            {
                xAxisLabel = "เดือน/ปี";
            }
            else if (modeTag == "Date")
            {
                xAxisLabel = "วัน/เดือน";
            }
            else
            {
                xAxisLabel = "ช่วงเวลา";
            }

            // วาดป้าย X-axis
            ds.DrawText(
                xAxisLabel,
                new Vector2(leftMargin + chartWidth / 2, topMargin + chartHeight + 45),
                Microsoft.UI.Colors.Black,
                new CanvasTextFormat
                {
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    HorizontalAlignment = CanvasHorizontalAlignment.Center,
                    VerticalAlignment = CanvasVerticalAlignment.Center,
                    FontFamily = "Segoe UI"
                }
            );

            // ✅ แก้ไข: วาด X axis labels โดยป้องกันการซ้อนกัน
            float xStep = chartWidth / (_chartLabels.Count - 1);
            
            // คำนวณว่าควรแสดง label ทุกกี่จุด (ถ้ามีจุดเยอะเกินไป)
            int labelSkip = 1;
            float estimatedLabelWidth = 40; // ความกว้างโดยประมาณของแต่ละ label
            
            if (_chartLabels.Count > 1)
            {
                float availableSpacePerLabel = chartWidth / _chartLabels.Count;
                if (availableSpacePerLabel < estimatedLabelWidth)
                {
                    labelSkip = (int)Math.Ceiling(estimatedLabelWidth / availableSpacePerLabel);
                }
            }

            // วาด labels โดยข้ามตามที่คำนวณได้
            for (int i = 0; i < _chartLabels.Count; i++)
            {
                // แสดง label เฉพาะจุดแรก, จุดสุดท้าย, และจุดที่ตรงตาม skip interval
                bool shouldShowLabel = (i == 0) || 
                                      (i == _chartLabels.Count - 1) || 
                                      (i % labelSkip == 0);

                if (shouldShowLabel)
                {
                    float x = leftMargin + (i * xStep);
                    float y = topMargin + chartHeight;

                    // ✅ ถ้ามี label เยอะมาก (> 15) ให้หมุนเอียง 45 องศา
                    if (_chartLabels.Count > 15)
                    {
                        using (ds.CreateLayer(1.0f))
                        {
                            var transform = Matrix3x2.CreateRotation((float)(-Math.PI / 4), new Vector2(x, y + 5));
                            ds.Transform = transform;

                            ds.DrawText(
                                _chartLabels[i],
                                new Vector2(x, y + 5),
                                Microsoft.UI.Colors.Gray,
                                new CanvasTextFormat
                                {
                                    FontSize = 10,
                                    HorizontalAlignment = CanvasHorizontalAlignment.Right,
                                    VerticalAlignment = CanvasVerticalAlignment.Top,
                                    FontFamily = "Segoe UI"
                                }
                            );

                            ds.Transform = Matrix3x2.Identity;
                        }
                    }
                    else
                    {
                        // แสดงแนวตั้งปกติ
                        ds.DrawText(
                            _chartLabels[i],
                            new Vector2(x, y + 5),
                            Microsoft.UI.Colors.Gray,
                            new CanvasTextFormat
                            {
                                FontSize = 11,
                                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                                VerticalAlignment = CanvasVerticalAlignment.Top,
                                FontFamily = "Segoe UI"
                            }
                        );
                    }
                }
            }

            // วาดเส้นกราฟแต่ละ series
            foreach (var series in _chartData)
            {
                var points = new List<Vector2>();

                for (int i = 0; i < series.Values.Count && i < _chartLabels.Count; i++)
                {
                    if (series.Values[i].HasValue)
                    {
                        float x = leftMargin + (i * xStep);
                        float y = topMargin + chartHeight - (float)((series.Values[i]!.Value - minValue) / (maxValue - minValue) * chartHeight);
                        points.Add(new Vector2(x, y));
                    }
                }

                // วาดเส้นเชื่อมจุด
                for (int i = 0; i < points.Count - 1; i++)
                {
                    ds.DrawLine(points[i], points[i + 1], series.Color, 2.5f);
                }

                // วาดจุดข้อมูล
                foreach (var point in points)
                {
                    ds.FillCircle(point, 4, series.Color);
                    ds.DrawCircle(point, 4, Microsoft.UI.Colors.White, 1.5f);
                }
            }

            // วาด Legend (ขวามือ)
            float legendX = leftMargin + chartWidth + 20;
            float legendY = topMargin;
            float legendLineHeight = 25;

            ds.DrawText(
                "สินค้า:",
                new Vector2(legendX, legendY),
                Microsoft.UI.Colors.Black,
                new CanvasTextFormat
                {
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontFamily = "Segoe UI"
                }
            );

            legendY += 25;

            foreach (var series in _chartData)
            {
                // สี่เหลี่ยมสี
                ds.FillRectangle(legendX, legendY + 2, 15, 15, series.Color);
                ds.DrawRectangle(legendX, legendY + 2, 15, 15, Microsoft.UI.Colors.Gray, 1);

                // ชื่อสินค้า (ตัดให้สั้น)
                string displayName = series.ProductName.Length > 15
                    ? series.ProductName.Substring(0, 15) + "..."
                    : series.ProductName;

                ds.DrawText(
                    displayName,
                    new Vector2(legendX + 20, legendY),
                    Microsoft.UI.Colors.Black,
                    new CanvasTextFormat
                    {
                        FontSize = 11,
                        VerticalAlignment = CanvasVerticalAlignment.Top,
                        FontFamily = "Segoe UI"
                    }
                );

                legendY += legendLineHeight;
            }

            // วาด Tooltip (ถ้ามี)
            if (_tooltipPosition.HasValue && _tooltipData != null && _tooltipData.Items.Any())
            {
                DrawTooltip(ds, _tooltipPosition.Value);
            }
        }

        // Helper method สำหรับวาด Tooltip
        private void DrawTooltip(CanvasDrawingSession ds, Point position)
        {
            if (_tooltipData == null || !_tooltipData.Items.Any()) return;

            float tooltipWidth = 180;
            float tooltipHeight = 30 + (_tooltipData.Items.Count * 22);
            float padding = 8;

            float x = (float)position.X + 15;
            float y = (float)position.Y - tooltipHeight / 2;

            // ปรับตำแหน่งให้อยู่ในขอบเขต
            if (x + tooltipWidth > CostChart.ActualWidth - 10)
                x = (float)position.X - tooltipWidth - 15;
            if (y < 10) y = 10;
            if (y + tooltipHeight > CostChart.ActualHeight - 10)
                y = (float)CostChart.ActualHeight - tooltipHeight - 10;

            // พื้นหลัง tooltip
            ds.FillRectangle(x, y, tooltipWidth, tooltipHeight, Windows.UI.Color.FromArgb(240, 255, 255, 255));
            ds.DrawRectangle(x, y, tooltipWidth, tooltipHeight, Microsoft.UI.Colors.Gray, 1);

            // Header
            ds.DrawText(
                _tooltipData.Label,
                new Vector2(x + padding, y + padding),
                Microsoft.UI.Colors.Black,
                new CanvasTextFormat
                {
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontFamily = "Segoe UI"
                }
            );

            // รายการสินค้า
            float itemY = y + padding + 20;
            foreach (var item in _tooltipData.Items)
            {
                if (item.Value.HasValue)
                {
                    // สี่เหลี่ยมสี
                    ds.FillRectangle(x + padding, itemY + 2, 10, 10, item.Color);

                    // ชื่อและราคา
                    string text = $"{item.ProductName}: {item.Value.Value:N4} ฿";
                    if (text.Length > 25) text = text.Substring(0, 25) + "...";

                    ds.DrawText(
                        text,
                        new Vector2(x + padding + 15, itemY),
                        Microsoft.UI.Colors.Black,
                        new CanvasTextFormat
                        {
                            FontSize = 10,
                            FontFamily = "Segoe UI"
                        }
                    );
                }

                itemY += 18;
            }
        }

        // ✅ ขั้นตอนที่ 5: Pointer Event Handlers สำหรับ Tooltip
        private void CostChart_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_chartData == null || !_chartData.Any() || _chartLabels == null || !_chartLabels.Any())
                return;

            var point = e.GetCurrentPoint(CostChart);
            var position = point.Position;

            // กำหนดขอบเขตของกราฟ (ตรงกับ CostChart_Draw)
            float leftMargin = 60;
            float rightMargin = 150;
            float topMargin = 40;
            float bottomMargin = 60;

            float chartWidth = (float)CostChart.ActualWidth - leftMargin - rightMargin;
            float chartHeight = (float)CostChart.ActualHeight - topMargin - bottomMargin;

            // ตรวจสอบว่า pointer อยู่ในพื้นที่กราฟหรือไม่
            if (position.X < leftMargin || position.X > leftMargin + chartWidth ||
                position.Y < topMargin || position.Y > topMargin + chartHeight)
            {
                // นอกพื้นที่กราฟ - ซ่อน tooltip
                if (_tooltipPosition.HasValue)
                {
                    _tooltipPosition = null;
                    _tooltipData = null;
                    CostChart?.Invalidate();
                }
                return;
            }

            // คำนวณ index ของจุดข้อมูลที่ใกล้ที่สุด
            float relativeX = (float)position.X - leftMargin;
            float xStep = chartWidth / (_chartLabels.Count - 1);
            int nearestIndex = (int)Math.Round(relativeX / xStep);

            // ตรวจสอบ index ให้อยู่ในขอบเขต
            if (nearestIndex < 0 || nearestIndex >= _chartLabels.Count)
                return;

            // สร้างข้อมูล tooltip
            var tooltipItems = new List<(string ProductName, double? Value, Windows.UI.Color Color)>();

            foreach (var series in _chartData)
            {
                if (nearestIndex < series.Values.Count)
                {
                    tooltipItems.Add((series.ProductName, series.Values[nearestIndex], series.Color));
                }
            }

            // ถ้ามีข้อมูลอย่างน้อย 1 รายการที่มีค่า ให้แสดง tooltip
            if (tooltipItems.Any(item => item.Value.HasValue))
            {
                _tooltipPosition = position;
                _tooltipData = new ChartTooltipData
                {
                    Label = _chartLabels[nearestIndex],
                    Items = tooltipItems
                };

                CostChart?.Invalidate();
            }
        }

        private void CostChart_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // ซ่อน tooltip เมื่อ pointer ออกจากกราฟ
            if (_tooltipPosition.HasValue)
            {
                _tooltipPosition = null;
                _tooltipData = null;
                CostChart?.Invalidate();
            }
        }
    }
}
