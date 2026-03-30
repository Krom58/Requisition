using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Requisition.Helpers;
using Requisition.Models.Reports;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Requisition.Controls;
using Windows.Graphics.Printing;

namespace Requisition.Pages
{
    public sealed partial class KitchenPeopleReportPage : Page
    {
        private readonly CombinedTransferService _combinedService;
        private List<KitchenPeopleReportItem> _items = new();
        private List<DateGroup> _dateGroups = new();
        private PrintHelper? _printHelper;

        public KitchenPeopleReportPage()
        {
            InitializeComponent();
            _combinedService = new CombinedTransferService();

            // Set default dates
            StartDatePicker.Date = new DateTimeOffset(DateTime.Today.AddMonths(-1));
            EndDatePicker.Date = new DateTimeOffset(DateTime.Today);

            Loaded += KitchenPeopleReportPage_Loaded;
        }

        private async void KitchenPeopleReportPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadReportAsync();
        }

        // Simple container for per-day groups
        private class DateGroup
        {
            public DateTime Date { get; set; }
            public string DateDisplay { get; set; } = string.Empty;
            public List<KitchenPeopleReportItem> Items { get; set; } = new();
        }

        private async Task LoadReportAsync()
        {
            try
            {
                LoadingRing.IsActive = true;
                LoadingRing.Visibility = Visibility.Visible;
                ReportGroupsControl.Visibility = Visibility.Visible;
                EmptyStatePanel.Visibility = Visibility.Collapsed;

                DateTime? startDate = StartDatePicker.Date?.DateTime;
                DateTime? endDate = EndDatePicker.Date?.DateTime;

                if (!startDate.HasValue || !endDate.HasValue)
                {
                    EmptyStatePanel.Visibility = Visibility.Visible;
                    ReportGroupsControl.ItemsSource = null;
                    return;
                }

                if (startDate.Value.Date > endDate.Value.Date)
                {
                    await DialogHelper.ShowErrorAsync("ข้อผิดพลาด", "วันที่เริ่มต้นต้องน้อยกว่าหรือเท่ากับวันที่สิ้นสุด");
                    EmptyStatePanel.Visibility = Visibility.Visible;
                    ReportGroupsControl.ItemsSource = null;
                    return;
                }

                // Update date range display (top-right only)
                DateRangeText.Text = $"📅 {ThaiDateHelper.ToThaiDateShort(startDate.Value)} - {ThaiDateHelper.ToThaiDateShort(endDate.Value)}";

                // Load combined transfers (we still need the combined -> transfer mapping)
                var combinedDetails = await _combinedService.GetAllCombinedTransfersWithDateAsync();

                if (combinedDetails == null || combinedDetails.Count == 0)
                {
                    EmptyStatePanel.Visibility = Visibility.Visible;
                    ReportGroupsControl.ItemsSource = null;
                    _items = new List<KitchenPeopleReportItem>();
                    _dateGroups = new List<DateGroup>();
                    return;
                }

                // get transfer ids referenced by all combined records (we will filter transfers by UsageDate below)
                var transferIds = new HashSet<int>();
                foreach (var combined in combinedDetails)
                {
                    var ids = await _combinedService.GetTransferIdsByCombinedIdAsync(combined.Id);
                    foreach (var id in ids)
                        transferIds.Add(id);
                }

                if (transferIds.Count == 0)
                {
                    EmptyStatePanel.Visibility = Visibility.Visible;
                    ReportGroupsControl.ItemsSource = null;
                    _items = new List<KitchenPeopleReportItem>();
                    _dateGroups = new List<DateGroup>();
                    return;
                }

                // Load transfers and filter by UsageDate (use UsageDate instead of CreatedDate)
                var transferService = new TransferService();
                var allTransfers = await transferService.GetAllTransfersAsync();

                var filteredTransfers = allTransfers
                    .Where(t => transferIds.Contains(t.Id)
                                && !t.IsDeleted
                                && t.Status == Models.TransferStatus.Completed
                                && t.UsageDate.HasValue
                                && t.UsageDate.Value.Date >= startDate.Value.Date
                                && t.UsageDate.Value.Date <= endDate.Value.Date)
                    .DistinctBy(t => t.Id)
                    .ToList();

                if (filteredTransfers.Count == 0)
                {
                    EmptyStatePanel.Visibility = Visibility.Visible;
                    ReportGroupsControl.ItemsSource = null;
                    _items = new List<KitchenPeopleReportItem>();
                    _dateGroups = new List<DateGroup>();
                    return;
                }

                // 1) Build overall aggregation (per kitchen) from transfers filtered by UsageDate
                var overallGroups = filteredTransfers
                    .GroupBy(t => t.KitchenDisplay ?? (t.KitchenId.HasValue ? $"Kitchen #{t.KitchenId}" : "ไม่ระบุห้องครัว"))
                    .Select(g =>
                    {
                        var expectedSum = g.Sum(t => t.ExpectedPeople);
                        var actualSum = g.Sum(t => t.ActualPeople ?? 0);
                        var transfersCount = g.Count();
                        double percent = expectedSum > 0 ? Math.Round((double)actualSum / expectedSum * 100.0, 2) : 0.0;

                        return new KitchenPeopleReportItem
                        {
                            KitchenDisplay = g.Key,
                            TotalExpectedPeople = expectedSum,
                            TotalActualPeople = actualSum,
                            TransfersCount = transfersCount,
                            PercentActualOfExpected = percent
                        };
                    })
                    .OrderByDescending(x => x.TotalExpectedPeople)
                    .ToList();

                _items = overallGroups;

                // 2) Build per-day groups where each group's items are kitchen aggregates for that UsageDate
                _dateGroups = filteredTransfers
                    .GroupBy(t => t.UsageDate!.Value.Date)
                    .OrderBy(g => g.Key)
                    .Select(g =>
                    {
                        var dateGroup = new DateGroup
                        {
                            Date = g.Key,
                            DateDisplay = ThaiDateHelper.ToThaiDateShort(g.Key)
                        };

                        var kitchenAggs = g
                            .GroupBy(t => t.KitchenDisplay ?? (t.KitchenId.HasValue ? $"Kitchen #{t.KitchenId}" : "ไม่ระบุห้องครัว"))
                            .Select(kg =>
                            {
                                var expectedSum = kg.Sum(t => t.ExpectedPeople);
                                var actualSum = kg.Sum(t => t.ActualPeople ?? 0);
                                var transfersCount = kg.Count();
                                double percent = expectedSum > 0 ? Math.Round((double)actualSum / expectedSum * 100.0, 2) : 0.0;

                                return new KitchenPeopleReportItem
                                {
                                    KitchenDisplay = kg.Key,
                                    TotalExpectedPeople = expectedSum,
                                    TotalActualPeople = actualSum,
                                    TransfersCount = transfersCount,
                                    PercentActualOfExpected = percent
                                };
                            })
                            .OrderByDescending(x => x.TotalExpectedPeople)
                            .ToList();

                        dateGroup.Items = kitchenAggs;
                        return dateGroup;
                    })
                    .ToList();

                // Bind groups to UI
                if (_dateGroups.Count > 0)
                {
                    ReportGroupsControl.ItemsSource = _dateGroups;
                    ReportGroupsControl.Visibility = Visibility.Visible;
                    EmptyStatePanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ReportGroupsControl.ItemsSource = null;
                    ReportGroupsControl.Visibility = Visibility.Collapsed;
                    EmptyStatePanel.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                var dlg = new ContentDialog
                {
                    Title = "เกิดข้อผิดพลาด",
                    Content = $"ไม่สามารถโหลดรายงานได้: {ex.Message}",
                    CloseButtonText = "ปิด",
                    XamlRoot = XamlRoot
                };
                await dlg.ShowAsync();
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateSummary()
        {
            if (_items == null || _items.Count == 0)
                return;

            var totalTransfers = _items.Sum(x => x.TransfersCount);
            var totalExpected = _items.Sum(x => x.TotalExpectedPeople);
            var totalActual = _items.Sum(x => x.TotalActualPeople);
            var totalDiff = totalActual - totalExpected;
            var totalPercent = totalExpected > 0 ? Math.Round((double)totalActual / totalExpected * 100.0, 2) : 0.0;
        }

        private async void DateFilter_Changed(object sender, object e)
        {
            await LoadReportAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadReportAsync();
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            // ถ้าไม่มีข้อมูลทั้งแบบกลุ่มตามวันและแบบรวม ให้แจ้งผู้ใช้
            var hasDateGroups = _dateGroups != null && _dateGroups.Count > 0;
            var hasOverall = _items != null && _items.Count > 0;
            if (!hasDateGroups && !hasOverall)
            {
                var dlgEmpty = new ContentDialog
                {
                    Title = "ไม่มีข้อมูล",
                    Content = "ไม่มีข้อมูลสำหรับส่งออก",
                    CloseButtonText = "ปิด",
                    XamlRoot = XamlRoot
                };
                await dlgEmpty.ShowAsync();
                return;
            }

            try
            {
                var sb = new StringBuilder();

                DateTime? startDate = StartDatePicker.Date?.DateTime;
                DateTime? endDate = EndDatePicker.Date?.DateTime;
                sb.AppendLine($"# รายงานจำนวนคนตามห้องครัว (ตามวันที่ใช้งาน)");
                if (startDate.HasValue && endDate.HasValue)
                    sb.AppendLine($"# ช่วงเวลา: {ThaiDateHelper.ToThaiDateShort(startDate.Value)} - {ThaiDateHelper.ToThaiDateShort(endDate.Value)}");
                sb.AppendLine();

                // Header with Date column first
                sb.AppendLine("วันที่,ห้องครัว,จำนวนใบ,จำนวนคาดหวัง,จำนวนจริง,ส่วนต่าง,% (จริง/คาดหวัง)");

                // Export per-day groups whenมี _dateGroups
                if (hasDateGroups)
                {
                    foreach (var dg in _dateGroups!)
                    {
                        var dateText = ThaiDateHelper.ToThaiDateShort(dg.Date);
                        foreach (var it in dg.Items)
                        {
                            var kitchen = it.KitchenDisplay?.Replace("\"", "\"\"") ?? "";
                            if (kitchen.Contains(',') || kitchen.Contains('"'))
                                kitchen = $"\"{kitchen}\"";

                            sb.AppendLine($"{dateText},{kitchen},{it.TransfersCount},{it.TotalExpectedPeople},{it.TotalActualPeople},{it.Difference},{it.PercentActualOfExpected:N2}");
                        }
                    }
                }
                else // fallback: export overall aggregates (no per-day)
                {
                    foreach (var it in _items!)
                    {
                        var kitchen = it.KitchenDisplay?.Replace("\"", "\"\"") ?? "";
                        if (kitchen.Contains(',') || kitchen.Contains('"'))
                            kitchen = $"\"{kitchen}\"";

                        // วันที่ไม่ระบุในภาพรวม -> ใส่ '-'
                        sb.AppendLine($"-,{kitchen},{it.TransfersCount},{it.TotalExpectedPeople},{it.TotalActualPeople},{it.Difference},{it.PercentActualOfExpected:N2}");
                    }
                }

                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var filename = $"KitchenPeopleReport_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                var path = Path.Combine(desktop, filename);

                // เขียน UTF8 พร้อม BOM เพื่อให้ Excel แสดงภาษาไทยได้ถูกต้อง
                await File.WriteAllTextAsync(path, sb.ToString(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                var dlg = new ContentDialog
                {
                    Title = "ส่งออกสำเร็จ",
                    Content = $"บันทึกไฟล์ CSV ไปยังเดสก์ท็อป: {filename}",
                    CloseButtonText = "ปิด",
                    XamlRoot = XamlRoot
                };
                await dlg.ShowAsync();
            }
            catch (Exception ex)
            {
                var dlg = new ContentDialog
                {
                    Title = "เกิดข้อผิดพลาด",
                    Content = $"ไม่สามารถส่งออกไฟล์ได้: {ex.Message}",
                    CloseButtonText = "ปิด",
                    XamlRoot = XamlRoot
                };
                await dlg.ShowAsync();
            }
        }

        private async void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            if ((_dateGroups == null || _dateGroups.Count == 0) && (_items == null || _items.Count == 0))
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
                // 🔧 แปลง _dateGroups ให้เป็น flat list สำหรับพิมพ์
                var flattenedItems = new List<KitchenPeopleReportItem>();
                
                if (_dateGroups != null && _dateGroups.Count > 0)
                {
                    foreach (var dateGroup in _dateGroups)
                    {
                        foreach (var item in dateGroup.Items)
                        {
                            flattenedItems.Add(new KitchenPeopleReportItem
                            {
                                DateDisplay = dateGroup.DateDisplay,
                                KitchenDisplay = item.KitchenDisplay,
                                TotalExpectedPeople = item.TotalExpectedPeople,
                                TotalActualPeople = item.TotalActualPeople,
                                TransfersCount = item.TransfersCount,
                                PercentActualOfExpected = item.PercentActualOfExpected
                            });
                        }
                    }
                }

                Debug.WriteLine($"📊 Creating print view with {flattenedItems.Count} flattened items");
                
                var printView = new PrintableReportView();
                
                DateTime? startDate = StartDatePicker.Date?.DateTime;
                DateTime? endDate = EndDatePicker.Date?.DateTime;
                
                printView.SetData(flattenedItems, startDate!.Value, endDate!.Value);

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