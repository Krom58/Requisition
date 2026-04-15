using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using Requisition.Models.Reports;
using Requisition.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Requisition.Pages;

public sealed partial class OutletCostComparisonPage : Page
{
    private readonly CombinedTransferService _service;
    private List<OutletCostComparisonItem> _items = new();
    private Timer? _dateChangeTimer;
    private const int DateChangeDebounceMs = 500;

    public OutletCostComparisonPage()
    {
        InitializeComponent();
        _service = new CombinedTransferService();
        Loaded += OutletCostComparisonPage_Loaded;
    }

    private async void OutletCostComparisonPage_Loaded(object sender, RoutedEventArgs e)
    {
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
                OutletCombo.Items.Add(new ComboBoxItem { Content = o.Name, Tag = o.Id });
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("ไม่สามารถโหลด outlet ได้: " + ex.Message);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            SetUiBusy(true);
            if (StartDatePicker?.Date == null || EndDatePicker?.Date == null) { await ShowErrorAsync("กรุณาเลือกวันที่"); return; }

            var start = StartDatePicker.Date.DateTime.Date;
            var end = EndDatePicker.Date.DateTime.Date;
            if (start > end) { await ShowErrorAsync("วันที่เริ่มต้น ต้องไม่มากกว่าวันที่สิ้นสุด"); return; }

            int? outletId = null;
            if (OutletCombo.SelectedItem is ComboBoxItem ci && ci.Tag is int id) outletId = id;

            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;

            _items = await _service.GetOutletCostComparisonAsync(start, end, outletId);

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

        var grouped = _items.OrderBy(r => r.UsageDate).ThenBy(r => r.OutletName)
            .GroupBy(r => (r.UsageDate.Date, r.OutletId, r.OutletName));

        foreach (var grp in grouped)
        {
            // ✅ ดึงข้อมูลจำนวนคนจาก item แรกของกลุ่ม
            var firstItem = grp.First();
            var expectedPeople = firstItem.ExpectedPeople;
            var actualPeople = firstItem.TotalPeople;

            // ✅ สร้าง header พร้อมข้อมูลจำนวนคน
            var headerText = $"วันที่ {grp.Key.Date:dd/MM/yyyy} — ";
            headerText += $"Outlet: {grp.Key.OutletName}";
            if (expectedPeople.HasValue)
            {
                headerText += $" (จำนวนคนคาดการณ์: {expectedPeople:N0} คน) — ";
            }
            else
            {
                headerText += "จำนวนคนคาดการณ์: ไม่สอดคล้อง — ";
            }
            
            if (actualPeople.HasValue)
            {
                headerText += $" (จำนวนคนมาใช้จริง: {actualPeople:N0} คน)";
            }
            else
            {
                headerText += " (จำนวนคนมาใช้จริง: ไม่สอดคล้อง)";
            }

            var header = new TextBlock
            {
                Text = headerText,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ResultsStack.Children.Add(header);

            var grid = new Grid { Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            // columns: no, item, unit, price, estimated(q/cost), add, return, actual(q/cost), diff, diff%
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); // no
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); // item (wider)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // unit
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // price
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) }); // est qty/cost
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // add
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // return
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) }); // actual qty/cost
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // diff
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // diff %

            void AddHeader(string t, int c)
            {
                var tb = new TextBlock { Text = t, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Margin = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
                Grid.SetRow(tb, 0); Grid.SetColumn(tb, c); grid.Children.Add(tb);
            }

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddHeader("ลำดับ", 0);
            AddHeader("รายการ", 1);
            AddHeader("หน่วย", 2);
            AddHeader("ราคา", 3);
            AddHeader("ประมาณการ (จำนวน/฿)", 4);
            AddHeader("เพิ่ม", 5);
            AddHeader("คืน", 6);
            AddHeader("ใช้จริง (จำนวน/฿)", 7);
            AddHeader("ผลต่าง ฿", 8);
            AddHeader("ผลต่าง %", 9);

            int row = 1;
            var rows = grp.ToList();
            foreach (var item in rows)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                void AddCell(string text, int r, int c, HorizontalAlignment ha = HorizontalAlignment.Center)
                {
                    var tb = new TextBlock { Text = text, Margin = new Thickness(4), HorizontalAlignment = ha };
                    Grid.SetRow(tb, r); Grid.SetColumn(tb, c); grid.Children.Add(tb);
                }

                AddCell(row.ToString(), row, 0);
                AddCell(item.ProductName, row, 1, HorizontalAlignment.Left);
                AddCell(item.Unit, row, 2);
                AddCell(item.UnitPrice.ToString("N4"), row, 3);
                AddCell($"{item.EstimatedQuantity:N4} / {item.EstimatedCost:N4}", row, 4);
                AddCell(item.AddedQuantity != 0 ? item.AddedQuantity.ToString("N4") : "-", row, 5);
                AddCell(item.ReturnedQuantity != 0 ? item.ReturnedQuantity.ToString("N4") : "-", row, 6);
                AddCell($"{item.ActualQuantity:N4} / {item.ActualCost:N4}", row, 7);
                AddCell(item.CostDifference.ToString("N4"), row, 8);
                AddCell(item.PercentDifference.HasValue ? item.PercentDifference.Value.ToString("N2") + " %" : "-", row, 9);

                row++;
            }

            // footer (group totals)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var footerTotalEstQty = rows.Sum(x => x.EstimatedQuantity);
            var footerTotalEstCost = rows.Sum(x => x.EstimatedCost);
            var footerTotalAdded = rows.Sum(x => x.AddedQuantity);
            var footerTotalReturned = rows.Sum(x => x.ReturnedQuantity);
            var footerTotalActualQty = rows.Sum(x => x.ActualQuantity);
            var footerTotalActualCost = rows.Sum(x => x.ActualCost);
            var footerTotalDiff = footerTotalActualCost - footerTotalEstCost;
            var footerPercent = footerTotalEstCost != 0m ? Math.Round(footerTotalDiff / footerTotalEstCost * 100m, 2) : (decimal?)null;

            var footerText = new TextBlock
            {
                Text = $"รวม: ประมาณการ {footerTotalEstQty:N4}/{footerTotalEstCost:N4} — เพิ่ม {footerTotalAdded:N4} — คืน {footerTotalReturned:N4} — ใช้จริง {footerTotalActualQty:N4}/{footerTotalActualCost:N4} — ผลต่าง {footerTotalDiff:N4} — %: {(footerPercent.HasValue ? footerPercent.Value.ToString("N2") + " %" : "-")}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetRow(footerText, row); Grid.SetColumn(footerText, 0);
            Grid.SetColumnSpan(footerText, 10);
            grid.Children.Add(footerText);

            ResultsStack.Children.Add(grid);
        }

        if (!grouped.Any())
            ResultsStack.Children.Add(new TextBlock { Text = "ไม่มีข้อมูลสำหรับเงื่อนไขที่เลือก", Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray), HorizontalAlignment = HorizontalAlignment.Center });
            }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items == null || !_items.Any()) { await ShowErrorAsync("ไม่มีข้อมูลสำหรับส่งออก"); return; }

        try
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("OutletCostComparison");

            int r = 1;
            // headers definition (use this length for merges)
            var headers = new[] { "ลำดับ", "รายการ", "หน่วย", "ราคา", "ประมาณการ (จำนวน)", "ประมาณการ (฿)", "เพิ่ม", "คืน", "ใช้จริง (จำนวน)", "ใช้จริง (฿)", "ผลต่าง (฿)", "ผลต่าง (%)" };
            int colCount = headers.Length;

            // Title
            ws.Cells[r, 1].Value = "รายงานเปรียบเทียบต้นทุนจริงกับต้นทุนประมาณการ (ตาม Outlet)";
            ws.Cells[r, 1, r, colCount].Merge = true;
            ws.Cells[r, 1].Style.Font.Bold = true;
            ws.Cells[r, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            r += 2;

            // Filters info (dates and outlet) — split into two halves
            var start = StartDatePicker?.Date.DateTime.Date;
            var end = EndDatePicker?.Date.DateTime.Date;
            var outletName = (OutletCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ทั้งหมด";

            int leftSpan = colCount / 2; // 6 when 12 columns
            int rightStart = leftSpan + 1;

            ws.Cells[r, 1].Value = $"วันที่ {start:dd/MM/yyyy} - {end:dd/MM/yyyy}";
            ws.Cells[r, 1, r, leftSpan].Merge = true;
            ws.Cells[r, leftSpan + 1].Value = $"Outlet: {outletName}";
            ws.Cells[r, rightStart, r, colCount].Merge = true;
            r += 2;

            // Groups
            var grouped = _items.OrderBy(i => i.UsageDate).ThenBy(i => i.OutletName)
                            .GroupBy(i => (i.UsageDate.Date, i.OutletId, i.OutletName));
            foreach (var grp in grouped)
            {
                // group header (merged)
                var grpKey = grp.Key;
                var groupPeople = grp.Select(i => i.TotalPeople).Where(p => p.HasValue).Select(p => p!.Value).FirstOrDefault();
                ws.Cells[r, 1].Value = $"วันที่ {grpKey.Date:dd/MM/yyyy} — Outlet: {grpKey.OutletName} (จำนวนคน: {(groupPeople > 0 ? groupPeople.ToString() : "-")})";
                ws.Cells[r, 1, r, colCount].Merge = true;
                ws.Cells[r, 1].Style.Font.Bold = true;
                ws.Cells[r, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                r++;

                // repeat column header for this group (prevents headers missing on new pages / after many rows)
                for (int c = 1; c <= colCount; c++) ws.Cells[r, c].Value = headers[c - 1];
                using (var hdr = ws.Cells[r, 1, r, colCount])
                {
                    hdr.Style.Font.Bold = true;
                    hdr.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    hdr.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                }
                r++;

                int idx = 1;
                foreach (var it in grp)
                {
                    ws.Cells[r, 1].Value = idx;
                    ws.Cells[r, 2].Value = it.ProductName;
                    ws.Cells[r, 3].Value = it.Unit;
                    ws.Cells[r, 4].Value = it.UnitPrice;
                    ws.Cells[r, 5].Value = it.EstimatedQuantity;
                    ws.Cells[r, 6].Value = it.EstimatedCost;
                    ws.Cells[r, 7].Value = it.AddedQuantity;
                    ws.Cells[r, 8].Value = it.ReturnedQuantity;
                    ws.Cells[r, 9].Value = it.ActualQuantity;
                    ws.Cells[r, 10].Value = it.ActualCost;
                    ws.Cells[r, 11].Value = it.CostDifference;
                    ws.Cells[r, 12].Value = it.PercentDifference.HasValue ? it.PercentDifference.Value / 100m : (decimal?)null;

                    // formatting
                    ws.Cells[r, 4].Style.Numberformat.Format = "#,##0.0000";
                    ws.Cells[r, 5].Style.Numberformat.Format = "#,##0.0000";
                    ws.Cells[r, 6].Style.Numberformat.Format = "#,##0.0000";
                    ws.Cells[r, 7].Style.Numberformat.Format = "#,##0.0000";
                    ws.Cells[r, 8].Style.Numberformat.Format = "#,##0.0000";
                    ws.Cells[r, 9].Style.Numberformat.Format = "#,##0.0000";
                    ws.Cells[r, 10].Style.Numberformat.Format = "#,##0.0000";
                    ws.Cells[r, 11].Style.Numberformat.Format = "#,##0.0000";
                    ws.Cells[r, 12].Style.Numberformat.Format = "0.00%";

                    r++; idx++;
                }

                // group footer totals
                var rows = grp.ToList();
                ws.Cells[r, 1].Value = "รวม";
                ws.Cells[r, 1].Style.Font.Bold = true;
                ws.Cells[r, 5].Value = rows.Sum(x => x.EstimatedQuantity);
                ws.Cells[r, 6].Value = rows.Sum(x => x.EstimatedCost);
                ws.Cells[r, 7].Value = rows.Sum(x => x.AddedQuantity);
                ws.Cells[r, 8].Value = rows.Sum(x => x.ReturnedQuantity);
                ws.Cells[r, 9].Value = rows.Sum(x => x.ActualQuantity);
                ws.Cells[r, 10].Value = rows.Sum(x => x.ActualCost);
                ws.Cells[r, 11].Value = rows.Sum(x => x.CostDifference);
                var estSum = rows.Sum(x => x.EstimatedCost);
                // ✅ แก้ไข: หารด้วย 100 เพื่อแปลงเป็นทศนิยม (เช่น 15% = 0.15)
                ws.Cells[r, 12].Value = estSum != 0m ? (rows.Sum(x => x.CostDifference)) / estSum : (decimal?)null;

                // ✅ แก้ไข: formatting for footer - ใช้ 0.00% สำหรับคอลัมน์ที่ 12
                ws.Cells[r, 5, r, 11].Style.Numberformat.Format = "#,##0.0000";
                ws.Cells[r, 12].Style.Numberformat.Format = "0.00%"; // ✅ เปลี่ยนเป็น %
                using (var rng = ws.Cells[r, 1, r, colCount]) { rng.Style.Font.Bold = true; rng.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center; }

                r += 2;
            }

            // columns widths (adjust as needed)
            for (int c = 1; c <= colCount; c++)
            {
                if (c == 2) ws.Column(c).Width = 40; // product name wider
                else if (c == 1) ws.Column(c).Width = 6;
                else ws.Column(c).Width = 12;
            }

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var filename = $"OutletCostComparison_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
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

    private void SetUiBusy(bool busy)
    {
        RefreshButton.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy;
        StartDatePicker.IsEnabled = !busy;
        EndDatePicker.IsEnabled = !busy;
        OutletCombo.IsEnabled = !busy;
    }

    private void DatePicker_DateChanged(object? sender, DatePickerValueChangedEventArgs e)
    {
        _dateChangeTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _dateChangeTimer?.Dispose();
        _dateChangeTimer = new Timer(_ =>
        {
            DispatcherQueue.TryEnqueue(async () => await RefreshAsync());
        }, null, DateChangeDebounceMs, Timeout.Infinite);
    }
}