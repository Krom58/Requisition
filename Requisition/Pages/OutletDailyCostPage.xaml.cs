using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Requisition.Models.Reports;
using Requisition.Services;

namespace Requisition.Pages
{
    public sealed partial class OutletDailyCostPage : Page
    {
        private readonly CombinedTransferService _service;
        private readonly ProductService _productService;
        private List<OutletDailyCostItem> _items = new();

        // debounce timer for filter changes
        private Timer? _filterChangeTimer;
        private const int FilterDebounceMs = 500;

        public OutletDailyCostPage()
        {
            InitializeComponent();
            _service = new CombinedTransferService();
            _productService = new ProductService();
            Loaded += OutletDailyCostPage_Loaded;
        }

        private async void OutletDailyCostPage_Loaded(object sender, RoutedEventArgs e)
        {
            PopulateMonthsYears();
            await LoadOutletsAsync();
            await RefreshAsync();
        }

        private void PopulateMonthsYears()
        {
            MonthCombo.Items.Clear();
            for (int m = 1; m <= 12; m++)
            {
                var ci = new ComboBoxItem { Content = new DateTime(2000, m, 1).ToString("MMMM"), Tag = m };
                if (m == DateTime.Now.Month) ci.IsSelected = true;
                MonthCombo.Items.Add(ci);
            }

            YearCombo.Items.Clear();
            int current = DateTime.Now.Year;
            for (int y = current - 5; y <= current; y++)
            {
                var ci = new ComboBoxItem { Content = y.ToString(), Tag = y };
                if (y == current) ci.IsSelected = true;
                YearCombo.Items.Add(ci);
            }
        }

        private async Task LoadOutletsAsync()
        {
            try
            {
                var outlets = await _service.GetActiveOutletsAsync();
                OutletCombo.Items.Clear();

                foreach (var o in outlets)
                    OutletCombo.Items.Add(new ComboBoxItem { Content = o.Name, Tag = o.Id });

                if (OutletCombo.Items.Count > 0)
                    OutletCombo.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("โหลด outlet ผิดพลาด: " + ex.Message);
            }
        }

        // wired to SelectionChanged for MonthCombo, YearCombo, OutletCombo
        private void Filter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            StartFilterTimer();
        }

        private void StartFilterTimer()
        {
            _filterChangeTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _filterChangeTimer?.Dispose();
            _filterChangeTimer = new Timer(_ =>
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await RefreshAsync();
                });
            }, null, FilterDebounceMs, Timeout.Infinite);
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

        private async Task RefreshAsync()
        {
            try
            {
                SetUiBusy(true);
                LoadingRing.IsActive = true;
                LoadingRing.Visibility = Visibility.Visible;

                if (!(MonthCombo.SelectedItem is ComboBoxItem mci) || !(YearCombo.SelectedItem is ComboBoxItem yci))
                {
                    await ShowErrorAsync("กรุณาเลือกเดือนและปี");
                    return;
                }

                int month = (int)mci.Tag;
                int year = (int)yci.Tag;

                int? outletId = null;
                if (OutletCombo.SelectedItem is ComboBoxItem oci && oci.Tag is int id)
                {
                    outletId = id;
                }
                else
                {
                    await ShowErrorAsync("กรุณาเลือก Outlet");
                    return;
                }

                // category/unit removed — always pass null
                _items = await _service.GetDailyCostsByABFAsync(year, month, outletId, null);

                RenderResults(year, month);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("โหลดข้อมูลล้มเหลว: " + ex.Message);
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
                SetUiBusy(false);
            }
        }

        private void RenderResults(int year, int month)
        {
            ResultsStack.Children.Clear();

            var groupsByDate = _items.GroupBy(i => i.UsageDate.Date).ToDictionary(g => g.Key, g => g.ToList());

            var header = new TextBlock
            {
                Text = $"ต้นทุนรายวันตามใบรายงาน ABF ของเดือน {month}/{year}",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ResultsStack.Children.Add(header);

            // Build grid table - 8 columns (date, total cost, expected, cost/expected, actual, cost/actual, meat kg/person, egg pcs/person)
            var grid = new Grid { Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            int columnCount = 8;
            for (int i = 0; i < columnCount; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // determine outlet name for header if a single outlet is selected
            string outletHeaderSuffix = "";
            if (OutletCombo.SelectedItem is ComboBoxItem selectedOutlet && selectedOutlet.Content is string outletName && !string.IsNullOrWhiteSpace(outletName))
            {
                outletHeaderSuffix = $" ({outletName})";
            }

            void AddHeader(string text, int col)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Margin = new Thickness(4),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetRow(tb, 0);
                Grid.SetColumn(tb, col);
                grid.Children.Add(tb);
            }

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddHeader("วันที่", 0);
            AddHeader("ต้นทุนรวมต่อวัน (฿)" + outletHeaderSuffix, 1);
            AddHeader("จำนวนผู้มีสิทธิ์ใช้", 2);
            AddHeader("ต้นทุน/ท่าน (ตามสิทธิ์ ฿)", 3);
            AddHeader("จำนวนผู้มาใช้จริง", 4);
            AddHeader("ต้นทุน/ท่านจริง (฿)", 5);
            AddHeader("ปริมาณเนื้อสัตว์ (กก./ท่าน)", 6);
            AddHeader("ปริมาณไข่ (ฟอง/ท่าน)", 7);

            int rows = DateTime.DaysInMonth(year, month);
            
            // ✅ แก้ไข: เปลี่ยนจาก sum of averages เป็น total ÷ total
            decimal totalAccumCost = 0m;
            int totalExpected = 0;
            int totalActual = 0;

            for (int day = 1; day <= rows; day++)
            {
                var date = new DateTime(year, month, day).Date;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                void AddCell(string text, int r, int c, HorizontalAlignment align = HorizontalAlignment.Center)
                {
                    var tb = new TextBlock
                    {
                        Text = text,
                        Margin = new Thickness(4),
                        HorizontalAlignment = align,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.NoWrap,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    Grid.SetRow(tb, r);
                    Grid.SetColumn(tb, c);
                    grid.Children.Add(tb);
                }

                AddCell(date.ToString("dd/MM/yyyy"), day, 0, HorizontalAlignment.Center);

                // compute values that will be shown on this row (derive totals from these displayed values)
                decimal dayCost = 0m;
                int dayExpected = 0;
                int dayActual = 0;
                decimal dayMeatQty = 0m;
                decimal dayEggQty = 0m;

                if (groupsByDate.TryGetValue(date, out var itemsForDate))
                {
                    if (itemsForDate.Count == 1)
                    {
                        var item = itemsForDate[0];

                        // apply HiddenCostPercentage once for the single item (if present)
                        decimal adjustedCost = item.TotalCost;
                        if (item.HiddenCostPercentage.HasValue && item.HiddenCostPercentage.Value > 0m)
                        {
                            var percent = item.HiddenCostPercentage.Value / 100m; // e.g. 15 => 0.15
                            adjustedCost = item.TotalCost + (item.TotalCost * percent);
                        }

                        // per-person meat & egg: use ActualPeople as denominator per request
                        decimal meatPerPerson = item.ActualPeople > 0 ? item.MeatQuantity / item.ActualPeople : 0m;
                        decimal eggPerPerson = item.ActualPeople > 0 ? item.EggQuantity / item.ActualPeople : 0m;

                        AddCell(adjustedCost.ToString("F4"), day, 1, HorizontalAlignment.Center);
                        AddCell(item.ExpectedPeople > 0 ? item.ExpectedPeople.ToString() : "-", day, 2);
                        AddCell(item.ExpectedPeople > 0 ? (adjustedCost / item.ExpectedPeople).ToString("F2") : "-", day, 3);
                        AddCell(item.ActualPeople > 0 ? item.ActualPeople.ToString() : "-", day, 4);
                        AddCell(item.ActualPeople > 0 ? (adjustedCost / item.ActualPeople).ToString("F2") : "-", day, 5);

                        AddCell(item.ActualPeople > 0 ? $"{meatPerPerson:F4}" : "-", day, 6);
                        AddCell(item.ActualPeople > 0 ? $"{eggPerPerson:F4}" : "-", day, 7);

                        // record displayed values for this row
                        dayCost = adjustedCost;
                        dayExpected = item.ExpectedPeople;
                        dayActual = item.ActualPeople;
                        dayMeatQty = item.MeatQuantity;
                        dayEggQty = item.EggQuantity;
                    }
                    else
                    {
                        // aggregated across outlets for that date
                        var dateTotalCost = itemsForDate.Sum(x => x.TotalCost);
                        var dateTotalExpected = itemsForDate.Sum(x => x.ExpectedPeople);
                        var dateTotalActual = itemsForDate.Sum(x => x.ActualPeople);
                        var dateTotalMeat = itemsForDate.Sum(x => x.MeatQuantity);
                        var dateTotalEgg = itemsForDate.Sum(x => x.EggQuantity);
                        var outletNames = string.Join(", ", itemsForDate.Select(x => x.OutletName).Distinct());

                        // Check HiddenCostPercentage consistency among contributing items
                        var hiddenValues = itemsForDate
                            .Where(x => x.HiddenCostPercentage.HasValue)
                            .Select(x => x.HiddenCostPercentage!.Value)
                            .Distinct()
                            .ToList();

                        // If exactly one distinct hidden % > 0, apply it once to the aggregated cost (do NOT sum percentages)
                        decimal adjustedTotalCost = dateTotalCost;
                        if (hiddenValues.Count == 1 && hiddenValues[0] > 0m)
                        {
                            var percent = hiddenValues[0] / 100m;
                            adjustedTotalCost = dateTotalCost + (dateTotalCost * percent);
                        }

                        // per-person meat & egg: use ActualPeople as denominator per request
                        var meatPerPerson = dateTotalActual > 0 ? dateTotalMeat / dateTotalActual : 0m;
                        var eggPerPerson = dateTotalActual > 0 ? dateTotalEgg / dateTotalActual : 0m;

                        AddCell(adjustedTotalCost.ToString("F4") + $" ({outletNames})", day, 1, HorizontalAlignment.Left);
                        AddCell(dateTotalExpected > 0 ? dateTotalExpected.ToString() : "-", day, 2);
                        AddCell(dateTotalExpected > 0 ? (adjustedTotalCost / dateTotalExpected).ToString("F2") : "-", day, 3);
                        AddCell(dateTotalActual > 0 ? dateTotalActual.ToString() : "-", day, 4);
                        AddCell(dateTotalActual > 0 ? (adjustedTotalCost / dateTotalActual).ToString("F2") : "-", day, 5);

                        AddCell(dateTotalActual > 0 ? $"{meatPerPerson:F4}" : "-", day, 6);
                        AddCell(dateTotalActual > 0 ? $"{eggPerPerson:F4}" : "-", day, 7);

                        // record displayed values for this row (use aggregated quantities)
                        dayCost = adjustedTotalCost;
                        dayExpected = dateTotalExpected;
                        dayActual = dateTotalActual;
                        dayMeatQty = dateTotalMeat;
                        dayEggQty = dateTotalEgg;
                    }
                }
                else
                {
                    // no data for the day
                    AddCell("-", day, 1);
                    AddCell("-", day, 2);
                    AddCell("-", day, 3);
                    AddCell("-", day, 4);
                    AddCell("-", day, 5);
                    AddCell("-", day, 6);
                    AddCell("-", day, 7);

                    // leave day* values as zero
                }

                // ✅ แก้ไข: รวมยอดรวมและจำนวนหัวรวม (ไม่ใช่เฉลี่ย)
                totalAccumCost += dayCost;
                totalExpected += dayExpected;
                totalActual += dayActual;
            }

            // ✅ footer: คำนวณต้นทุนต่อหัวจาก total ÷ total
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int footerRow = rows + 1;

            void AddFooterCell(string text, int c)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    Margin = new Thickness(4),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetRow(tb, footerRow);
                Grid.SetColumn(tb, c);
                grid.Children.Add(tb);
            }

            AddFooterCell("รวมสุทธิ", 0);
            AddFooterCell($"{totalAccumCost:F4}", 1);
            AddFooterCell(totalExpected > 0 ? totalExpected.ToString() : "-", 2);

            // ✅ แก้ไข: ต้นทุนต่อหัว (ตามสิทธิ์) = ยอดต้นทุนรวมสุทธิ ÷ จำนวนหัวรวมสุทธิ (Expected)
            if (totalExpected > 0)
                AddFooterCell((totalAccumCost / totalExpected).ToString("F2"), 3);
            else
                AddFooterCell("-", 3);

            AddFooterCell(totalActual > 0 ? totalActual.ToString() : "-", 4);

            // ✅ แก้ไข: ต้นทุนต่อหัว (จริง) = ยอดต้นทุนรวมสุทธิ ÷ จำนวนหัวรวมสุทธิ (Actual)
            if (totalActual > 0)
                AddFooterCell((totalAccumCost / totalActual).ToString("F2"), 5);
            else
                AddFooterCell("-", 5);

            // remove grand totals for meat and egg per request — show dash
            AddFooterCell("-", 6);
            AddFooterCell("-", 7);

            ResultsStack.Children.Add(grid);
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(MonthCombo.SelectedItem is ComboBoxItem mci) || !(YearCombo.SelectedItem is ComboBoxItem yci))
            {
                await ShowErrorAsync("กรุณาเลือกเดือนและปี");
                return;
            }

            int month = (int)mci.Tag;
            int year = (int)yci.Tag;

            // selected outlet name or "ทั้งหมด"
            string outletName = "ทั้งหมด";
            if (OutletCombo.SelectedItem is ComboBoxItem oci && oci.Content is string on && !string.IsNullOrWhiteSpace(on))
                outletName = on;

            // group items by date for quick lookup (may be empty)
            var groupsByDate = (_items ?? new List<OutletDailyCostItem>()).GroupBy(i => i.UsageDate.Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            try
            {
                var sb = new StringBuilder();

                // Title row: place title in column C to approximate centered title in CSV
                string title = $"ต้นทุนรายวันตามใบรายงาน ABF ของเดือน {month}/{year}";
                var titleRow = new string[8];
                titleRow[2] = title;
                sb.AppendLine(string.Join(",", titleRow));

                // Date range (left) and outlet (placed around column F)
                DateTime startDate = new DateTime(year, month, 1);
                DateTime endDate = new DateTime(year, month, DateTime.DaysInMonth(year, month));
                var metaRow = new string[8];
                metaRow[0] = $"วันที่ {startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}";
                metaRow[5] = $"Outlet: {outletName}";
                sb.AppendLine(string.Join(",", metaRow));

                // blank row
                sb.AppendLine();

                // Column header (match on-screen table)
                sb.AppendLine("วันที่,ต้นทุนรวมต่อวัน (฿),จำนวนผู้มีสิทธิ์ใช้,ต้นทุน/ท่าน (ตามสิทธิ์ ฿),จำนวนผู้มาใช้จริง,ต้นทุน/ท่านจริง (฿),ปริมาณเนื้อสัตว์ (กก./ท่าน),ปริมาณไข่ (ฟอง/ท่าน)");

                int days = DateTime.DaysInMonth(year, month);

                // ✅ แก้ไข: เก็บยอดรวมและจำนวนหัวรวม (ไม่ใช่เฉลี่ย)
                decimal totalAccumCost = 0m;
                int totalExpected = 0;
                int totalActual = 0;

                for (int d = 1; d <= days; d++)
                {
                    var date = new DateTime(year, month, d).Date;

                    if (groupsByDate.TryGetValue(date, out var itemsForDate) && itemsForDate.Any())
                    {
                        if (itemsForDate.Count == 1)
                        {
                            var it = itemsForDate[0];
                            decimal adjustedCost = it.TotalCost;
                            if (it.HiddenCostPercentage.HasValue && it.HiddenCostPercentage.Value > 0m)
                                adjustedCost = it.TotalCost + (it.TotalCost * (it.HiddenCostPercentage.Value / 100m));

                            var cpe = it.ExpectedPeople > 0 ? (adjustedCost / it.ExpectedPeople).ToString("F2") : "-";
                            var cpa = it.ActualPeople > 0 ? (adjustedCost / it.ActualPeople).ToString("F2") : "-";

                            // meat/egg per person: use ActualPeople as denominator per request
                            var meatPerPerson = it.ActualPeople > 0 ? (it.MeatQuantity / it.ActualPeople).ToString("F4") : "-";
                            var eggPerPerson = it.ActualPeople > 0 ? (it.EggQuantity / it.ActualPeople).ToString("F4") : "-";

                            // Date format like UI
                            sb.AppendLine(string.Join(",",
                                EscapeCsv(date.ToString("dd/MM/yyyy")),
                                EscapeCsv(adjustedCost.ToString("F4")),
                                (it.ExpectedPeople > 0 ? it.ExpectedPeople.ToString() : "-"),
                                cpe,
                                (it.ActualPeople > 0 ? it.ActualPeople.ToString() : "-"),
                                cpa,
                                meatPerPerson,
                                eggPerPerson));

                            // ✅ รวมยอด
                            totalAccumCost += adjustedCost;
                            totalExpected += it.ExpectedPeople;
                            totalActual += it.ActualPeople;
                        }
                        else
                        {
                            // aggregated across outlets for that date
                            var dateTotalCost = itemsForDate.Sum(x => x.TotalCost);
                            var dateTotalExpected = itemsForDate.Sum(x => x.ExpectedPeople);
                            var dateTotalActual = itemsForDate.Sum(x => x.ActualPeople);
                            var dateTotalMeat = itemsForDate.Sum(x => x.MeatQuantity);
                            var dateTotalEgg = itemsForDate.Sum(x => x.EggQuantity);
                            var outletNames = string.Join(", ", itemsForDate.Select(x => x.OutletName).Distinct());

                            // Check HiddenCostPercentage consistency among contributing items
                            var hiddenValues = itemsForDate
                                .Where(x => x.HiddenCostPercentage.HasValue)
                                .Select(x => x.HiddenCostPercentage!.Value)
                                .Distinct()
                                .ToList();

                            decimal adjustedTotalCost = dateTotalCost;
                            if (hiddenValues.Count == 1 && hiddenValues[0] > 0m)
                            {
                                var percent = hiddenValues[0] / 100m;
                                adjustedTotalCost = dateTotalCost + (dateTotalCost * percent);
                            }

                            var cpe = dateTotalExpected > 0 ? (adjustedTotalCost / dateTotalExpected).ToString("F2") : "-";
                            var cpa = dateTotalActual > 0 ? (adjustedTotalCost / dateTotalActual).ToString("F2") : "-";

                            // meat/egg per person: use ActualPeople as denominator per request
                            var meatPerPerson = dateTotalActual > 0 ? (dateTotalMeat / dateTotalActual).ToString("F4") : "-";
                            var eggPerPerson = dateTotalActual > 0 ? (dateTotalEgg / dateTotalActual).ToString("F4") : "-";

                            // cost cell includes outlet names like on-screen
                            var costCell = $"{adjustedTotalCost.ToString("F4")} ({outletNames})";

                            sb.AppendLine(string.Join(",",
                                EscapeCsv(date.ToString("dd/MM/yyyy")),
                                EscapeCsv(costCell),
                                (dateTotalExpected > 0 ? dateTotalExpected.ToString() : "-"),
                                cpe,
                                (dateTotalActual > 0 ? dateTotalActual.ToString() : "-"),
                                cpa,
                                meatPerPerson,
                                eggPerPerson));

                            // ✅ รวมยอด (ใช้ aggregated values)
                            totalAccumCost += adjustedTotalCost;
                            totalExpected += dateTotalExpected;
                            totalActual += dateTotalActual;
                        }
                    }
                    else
                    {
                        // no data for this day -> output dashes (preserve calendar row)
                        sb.AppendLine(string.Join(",", new[]
                        {
                    EscapeCsv(date.ToString("dd/MM/yyyy")),
                    "-",
                    "-",
                    "-",
                    "-",
                    "-",
                    "-",
                    "-"
                }));
                    }
                }

                // ✅ footer: ยอดรวมสุทธิ ÷ จำนวนหัวรวมสุทธิ
                string cpeFooter = totalExpected > 0 ? (totalAccumCost / totalExpected).ToString("F2") : "-";
                string cpaFooter = totalActual > 0 ? (totalAccumCost / totalActual).ToString("F2") : "-";

                sb.AppendLine(string.Join(",",
                    EscapeCsv("รวมสุทธิ"),
                    EscapeCsv(totalAccumCost.ToString("F4")),
                    (totalExpected > 0 ? totalExpected.ToString() : "-"),
                    cpeFooter,
                    (totalActual > 0 ? totalActual.ToString() : "-"),
                    cpaFooter,
                    "-",
                    "-"));

                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var filename = $"OutletDailyCost_{year}{month:00}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                var path = System.IO.Path.Combine(desktop, filename);
                await System.IO.File.WriteAllTextAsync(path, sb.ToString(), System.Text.Encoding.UTF8);

                var dlg = new ContentDialog { Title = "ส่งออกเสร็จ", Content = $"ไฟล์ถูกบันทึกที่เดสก์ท็อป: {filename}", CloseButtonText = "ปิด", XamlRoot = this.XamlRoot };
                await dlg.ShowAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorAsync("ส่งออกล้มเหลว: " + ex.Message);
            }
        }

        private string EscapeCsv(string? s) => string.IsNullOrEmpty(s) ? "" : s.Contains(',') ? $"\"{s.Replace("\"","\"\"")}\"" : s;

        private Task ShowErrorAsync(string message)
        {
            var dlg = new ContentDialog { Title = "ข้อผิดพลาด", Content = message, CloseButtonText = "ปิด", XamlRoot = this.XamlRoot };
            return dlg.ShowAsync().AsTask();
        }

        private void SetUiBusy(bool busy)
        {
            RefreshButton.IsEnabled = !busy;
            ExportButton.IsEnabled = !busy;
            MonthCombo.IsEnabled = !busy;
            YearCombo.IsEnabled = !busy;
            OutletCombo.IsEnabled = !busy;
        }
    }
}
