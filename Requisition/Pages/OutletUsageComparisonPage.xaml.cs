using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Requisition.Models.Reports;
using Requisition.Services;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using OfficeOpenXml;
using OfficeOpenXml.Style;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Requisition.Pages;

public sealed partial class OutletUsageComparisonPage : Page
{
    private readonly CombinedTransferService _service;
    private List<OutletUsageReportItem> _items = new();

    // new: debounce timer for date changes
    private Timer? _dateChangeTimer;
    private const int DateChangeDebounceMs = 500;

    public OutletUsageComparisonPage()
    {
        InitializeComponent();
        _service = new CombinedTransferService();
        Loaded += OutletUsageComparisonPage_Loaded;
    }

    private async void OutletUsageComparisonPage_Loaded(object sender, RoutedEventArgs e)
    {
        // defaults: last 7 days
        EndDatePicker.Date = DateTimeOffset.Now;
        StartDatePicker.Date = DateTimeOffset.Now.AddDays(-6);
        await LoadOutletsAsync();
        await RefreshAsync();
    }

    private async Task LoadOutletsAsync()
    {
        try
        {
            var outlets = await _service.GetActiveOutletsAsync();
            OutletCombo.Items.Clear();
            OutletCombo.Items.Add(new ComboBoxItem { Content = "ทั้งหมด", Tag = null, IsSelected = true });
            foreach (var o in outlets)
            {
                OutletCombo.Items.Add(new ComboBoxItem { Content = o.Name, Tag = o.Id });
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("ไม่สามารถโหลด outlet ได้: " + ex.Message);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        // Validate date pickers
        try
        {
            // disable UI while loading
            SetUiBusy(true);

            // basic date validation
            if (StartDatePicker?.Date == null || EndDatePicker?.Date == null)
            {
                await ShowErrorAsync("กรุณาเลือกวันที่เริ่มต้นและวันที่สิ้นสุด");
                return;
            }

            var start = StartDatePicker.Date.DateTime.Date;
            var end = EndDatePicker.Date.DateTime.Date;

            if (start > end)
            {
                await ShowErrorAsync("วันที่เริ่มต้น ต้องไม่มากกว่าวันที่สิ้นสุด");
                return;
            }

            int? outletId = null;
            if (OutletCombo.SelectedItem is ComboBoxItem ci && ci.Tag is int id) outletId = id;

            // show loader
            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;

            // fetch
            _items = await _service.GetUsageByCategoryAsync(start, end, outletId);

            // render
            RenderResults();
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

    private void RenderResults()
    {
        ResultsStack.Children.Clear();

        // Group by date then outlet
        var grouped = _items
            .OrderBy(r => r.UsageDate)
            .ThenBy(r => r.OutletName)
            .GroupBy(r => (r.UsageDate.Date, r.OutletId, r.OutletName));

        foreach (var grp in grouped)
        {
            // decide group-level people display:
            var groupRows = grp.ToList();

            // If any row marks ActualPeopleInconsistent => group is inconsistent (show message)
            bool groupActualInconsistent = groupRows.Any(r => r.ActualPeopleInconsistent);

            // If any row has ConsistentActualPeople -> use that as group count (they should all be same)
            int? groupConsistentActual = groupRows.Select(r => r.ConsistentActualPeople).Where(p => p.HasValue).Select(p => p!.Value).FirstOrDefault();

            string peopleDisplay;
            if (groupConsistentActual.HasValue && groupConsistentActual.Value > 0)
                peopleDisplay = groupConsistentActual.Value.ToString();
            else if (groupActualInconsistent)
                peopleDisplay = "จำนวนคนจริง: ไม่ตรงกัน";
            else
                peopleDisplay = "-";

            var header = new TextBlock
            {
                Text = $"วันที่ {grp.Key.Date:dd/MM/yyyy} — Outlet: {grp.Key.OutletName} (จำนวนคน: {peopleDisplay})",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center // center header
            };
            ResultsStack.Children.Add(header);

            // create grid table header
            var grid = new Grid { Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };

            // Column layout: first column small (index), rest equal star widths
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); // no
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // category
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // qty
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // cost/head (calculated)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // % 
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // total cost

            // header row
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            void AddHeader(string text, int col)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Margin = new Thickness(4),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                Grid.SetRow(tb, 0); Grid.SetColumn(tb, col);
                grid.Children.Add(tb);
            }
            AddHeader("ลำดับ", 0);
            AddHeader("ประเภท", 1);
            AddHeader("ปริมาณการใช้", 2);
            AddHeader("ต้นทุน/หัว (฿)", 3);
            AddHeader("%", 4);
            AddHeader("รวม (฿)", 5);

            // rows
            int row = 1;
            var rows = grp.ToList();

            // determine group-level people for cost-per-head calculation
            // Use ConsistentActualPeople only when available; otherwise treat as not available
            int? groupTotalPeople = groupConsistentActual;
            bool hasGroupPeople = groupTotalPeople.HasValue && groupTotalPeople.Value > 0;

            foreach (var item in rows)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                void AddCell(string text, int r, int c, HorizontalAlignment align = HorizontalAlignment.Center, TextAlignment textAlign = TextAlignment.Center)
                {
                    var tb = new TextBlock
                    {
                        Text = text,
                        Margin = new Thickness(4),
                        TextWrapping = TextWrapping.NoWrap,
                        HorizontalAlignment = align,
                        TextAlignment = textAlign
                    };
                    Grid.SetRow(tb, r); Grid.SetColumn(tb, c);
                    grid.Children.Add(tb);
                }

                // numeric formatting
                var qtyText = item.TotalQuantity.ToString("N4", System.Globalization.CultureInfo.InvariantCulture);
                var totalCostText = item.TotalCost.ToString("N4", System.Globalization.CultureInfo.InvariantCulture);

                // cost per head per row: calculate from each row's TotalCost divided by groupTotalPeople
                string costPerHeadText;
                if (hasGroupPeople)
                {
                    var cph = item.TotalCost / groupTotalPeople.Value;
                    costPerHeadText = cph.ToString("N4", System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    costPerHeadText = "-";
                }

                AddCell(row.ToString(), row, 0);
                AddCell(item.Category, row, 1, HorizontalAlignment.Center, TextAlignment.Center);
                AddCell(qtyText, row, 2, HorizontalAlignment.Center, TextAlignment.Center);
                AddCell(costPerHeadText, row, 3, HorizontalAlignment.Center, TextAlignment.Center);
                AddCell(item.PercentOfGroup.ToString("N2", System.Globalization.CultureInfo.InvariantCulture) + " %", row, 4, HorizontalAlignment.Center, TextAlignment.Center);
                AddCell(totalCostText, row, 5, HorizontalAlignment.Center, TextAlignment.Center);

                row++;
            }

            // summary footer
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var footerTotalCost = rows.Sum(x => x.TotalCost);
            var footerTotalQty = rows.Sum(x => x.TotalQuantity);

            // compute footer cost-per-head (group level) and percent sum
            string footerCostPerHeadText = "-";
            if (hasGroupPeople)
            {
                var footerCph = footerTotalCost / groupTotalPeople.Value;
                footerCostPerHeadText = footerCph.ToString("N4", System.Globalization.CultureInfo.InvariantCulture);
            }
            var footerPercentSum = rows.Sum(x => x.PercentOfGroup);

            var footerText = new TextBlock
            {
                Text = $"รวม: ปริมาณ {footerTotalQty:N4} — ต้นทุนรวม {footerTotalCost:N4} ฿ — ต้นทุนต่อหัว: {footerCostPerHeadText} ฿/คน — %รวม: {footerPercentSum:N2} %",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            Grid.SetRow(footerText, row); Grid.SetColumn(footerText, 0);
            Grid.SetColumnSpan(footerText, 6);
            grid.Children.Add(footerText);

            ResultsStack.Children.Add(grid);
        }

        if (!grouped.Any())
        {
            ResultsStack.Children.Add(new TextBlock { Text = "ไม่มีข้อมูลสำหรับเงื่อนไขที่เลือก", Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray), HorizontalAlignment = HorizontalAlignment.Center });
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items == null || !_items.Any())
        {
            await ShowErrorAsync("ไม่มีข้อมูลสำหรับส่งออก");
            return;
        }

        try
        {
            // EPPlus license (non-commercial / adjust if needed)
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("OutletUsage");

            int row = 1;
            // Title
            ws.Cells[row, 1].Value = "รายงานเปรียบเทียบเปอร์เซ็นต์การใช้สินค้า (ตาม Outlet)";
            ws.Cells[row, 1, row, 6].Merge = true;
            ws.Cells[row, 1].Style.Font.Bold = true;
            ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            row += 2;

            // Filters info (dates and outlet)
            var start = StartDatePicker?.Date.DateTime.Date;
            var end = EndDatePicker?.Date.DateTime.Date;
            var outletName = (OutletCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ทั้งหมด";
            ws.Cells[row, 1].Value = $"วันที่ {start:dd/MM/yyyy} - {end:dd/MM/yyyy}";
            ws.Cells[row, 1, row, 3].Merge = true;
            ws.Cells[row, 4].Value = $"Outlet: {outletName}";
            ws.Cells[row, 4, row, 6].Merge = true;
            row += 2;

            // Columns header
            ws.Cells[row, 1].Value = "ลำดับ";
            ws.Cells[row, 2].Value = "ประเภท";
            ws.Cells[row, 3].Value = "ปริมาณการใช้";
            ws.Cells[row, 4].Value = "ต้นทุน/หัว (฿)";
            ws.Cells[row, 5].Value = "%";
            ws.Cells[row, 6].Value = "รวม (฿)";

            using (var hdr = ws.Cells[row, 1, row, 6])
            {
                hdr.Style.Font.Bold = true;
                hdr.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                hdr.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            }
            row++;

            // Group by (UsageDate, Outlet)
            var grouped = _items
                .OrderBy(r => r.UsageDate)
                .ThenBy(r => r.OutletName)
                .GroupBy(r => (r.UsageDate.Date, r.OutletId, r.OutletName));

            foreach (var grp in grouped)
            {
                var grpKey = grp.Key;
                // check consistent actual across group
                var groupConsistentActual = grp.Select(r => r.ConsistentActualPeople).Where(p => p.HasValue).Select(p => p!.Value).FirstOrDefault();
                var groupHasInconsistentActual = grp.Any(r => r.ActualPeopleInconsistent);

                string peopleLabel;
                if (groupConsistentActual > 0)
                    peopleLabel = groupConsistentActual.ToString();
                else if (groupHasInconsistentActual)
                    peopleLabel = "ไม่ตรงกัน";
                else
                    peopleLabel = "-";

                ws.Cells[row, 1].Value = $"วันที่ {grpKey.Date:dd/MM/yyyy} — Outlet: {grpKey.OutletName} (จำนวนคน: {peopleLabel})";
                ws.Cells[row, 1, row, 6].Merge = true;
                ws.Cells[row, 1].Style.Font.Bold = true;
                ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                row++;

                var rows = grp.ToList();
                int index = 1;
                decimal footerTotalQty = 0m;
                decimal footerTotalCost = 0m;
                decimal footerPercentSum = 0m;

                // group-level people for calculations
                int? groupTotalPeople = groupConsistentActual > 0 ? (int?)groupConsistentActual : null;

                foreach (var item in rows)
                {
                    ws.Cells[row, 1].Value = index;
                    ws.Cells[row, 2].Value = item.Category;
                    ws.Cells[row, 3].Value = item.TotalQuantity;
                    // cost per head per row = item.TotalCost / groupTotalPeople (only when available)
                    if (groupTotalPeople.HasValue && groupTotalPeople.Value > 0)
                        ws.Cells[row, 4].Value = item.TotalCost / groupTotalPeople.Value;
                    else
                        ws.Cells[row, 4].Value = null;

                    // PercentOfGroup in model is e.g. 1.68 => write fraction = 0.0168
                    ws.Cells[row, 5].Value = item.PercentOfGroup / 100m;
                    ws.Cells[row, 6].Value = item.TotalCost;

                    // formatting
                    ws.Cells[row, 3].Style.Numberformat.Format = "#,##0.0000";
                    ws.Cells[row, 4].Style.Numberformat.Format = "#,##0.0000";
                    ws.Cells[row, 5].Style.Numberformat.Format = "0.00%";
                    ws.Cells[row, 6].Style.Numberformat.Format = "#,##0.0000";

                    // align
                    ws.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    ws.Cells[row, 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
                    ws.Cells[row, 3, row, 6].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                    footerTotalQty += item.TotalQuantity;
                    footerTotalCost += item.TotalCost;
                    footerPercentSum += item.PercentOfGroup;

                    row++;
                    index++;
                }

                // Footer row per group
                ws.Cells[row, 1].Value = "รวม";
                ws.Cells[row, 1].Style.Font.Bold = true;
                ws.Cells[row, 2].Value = ""; // leave
                ws.Cells[row, 3].Value = footerTotalQty;
                // group-level cost-per-head
                if (groupTotalPeople > 0)
                    ws.Cells[row, 4].Value = footerTotalCost / groupTotalPeople;
                else
                    ws.Cells[row, 4].Value = null;
                ws.Cells[row, 5].Value = footerPercentSum / 100m; // write fraction for percent format
                ws.Cells[row, 6].Value = footerTotalCost;

                ws.Cells[row, 3].Style.Numberformat.Format = "#,##0.0000";
                ws.Cells[row, 4].Style.Numberformat.Format = "#,##0.0000";
                ws.Cells[row, 5].Style.Numberformat.Format = "0.00%";
                ws.Cells[row, 6].Style.Numberformat.Format = "#,##0.0000";

                using (var rng = ws.Cells[row, 1, row, 6])
                {
                    rng.Style.Font.Bold = true;
                    rng.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                }

                row += 2; // blank line after each group
            }

            // AutoFit and set some column widths (category wider)
            ws.Column(1).Width = 8; // index
            ws.Column(2).Width = 30; // category
            ws.Column(3).Width = 15;
            ws.Column(4).Width = 18;
            ws.Column(5).Width = 10;
            ws.Column(6).Width = 18;

            // Save to desktop
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var filename = $"OutletUsageReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var path = System.IO.Path.Combine(desktop, filename);

            await System.IO.File.WriteAllBytesAsync(path, package.GetAsByteArray());

            var dlg = new ContentDialog { Title = "ส่งออกเสร็จ", Content = $"ไฟล์ Excel ถูกบันทึกที่เดสก์ท็อป: {filename}", CloseButtonText = "ปิด", XamlRoot = this.XamlRoot };
            await dlg.ShowAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("ส่งออกล้มเหลว: " + ex.Message);
        }
    }

    private Task ShowErrorAsync(string message)
    {
        var dlg = new ContentDialog { Title = "ข้อผิดพลาด", Content = message, CloseButtonText = "ปิด", XamlRoot = this.XamlRoot };
        return dlg.ShowAsync().AsTask();
    }

    // helper to disable/enable UI during fetch
    private void SetUiBusy(bool busy)
    {
        RefreshButton.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy;
        StartDatePicker.IsEnabled = !busy;
        EndDatePicker.IsEnabled = !busy;
        OutletCombo.IsEnabled = !busy;
    }

    // new handler for DatePicker.DateChanged (debounced -> calls RefreshAsync)
    private void DatePicker_DateChanged(object? sender, DatePickerValueChangedEventArgs e)
    {
        // restart debounce timer
        _dateChangeTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _dateChangeTimer?.Dispose();
        _dateChangeTimer = new Timer(_ =>
        {
            // marshal back to UI thread
            DispatcherQueue.TryEnqueue(async () =>
            {
                await RefreshAsync();
            });
        }, null, DateChangeDebounceMs, Timeout.Infinite);
    }
}
