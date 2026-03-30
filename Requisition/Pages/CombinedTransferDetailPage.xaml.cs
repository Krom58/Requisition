using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Requisition.Helpers;
using Requisition.Models;
using Requisition.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Requisition.Pages
{
    public sealed partial class CombinedTransferDetailPage : Page
    {
        private readonly CombinedTransferService _service;
        private int _combinedId;

        // dialog gate used by ShowError/ShowSuccess helpers
        private int _dialogOpen = 0;

        // 🔥 เพิ่มตัวแปรเก็บวันที่ทั้งหมด
        private List<DateTime> _allUsageDates = new List<DateTime>();

        public CombinedTransferDetailPage()
        {
            InitializeComponent();
            _service = new CombinedTransferService();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is int combinedId)
            {
                _combinedId = combinedId;
                await LoadDataAsync();
            }
        }

        private async Task LoadDataAsync()
        {
            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;

                var detail = await _service.GetCombinedTransferDetailAsync(_combinedId);
                if (detail == null)
                {
                    await DialogHelper.ShowErrorAsync("ไม่พบข้อมูล", "ไม่พบรายละเอียดใบรวม Transfer");
                    Frame.GoBack();
                    return;
                }

                // แสดงข้อมูลหลัก
                CombinedNoText.Text = detail.CombinedNo;
                CreatedDateText.Text = detail.CreatedDate.ToString("dd/MM/yyyy HH:mm");
                CreatedByText.Text = $"สร้างโดย: {detail.CreatedBy ?? "ไม่ระบุ"}";
                
                TransferCountText.Text = $"{detail.TransferCount} ใบ";
                
                // จำนวนคน (พร้อม warning)
                if (detail.HasInconsistentPeople)
                {
                    var min = detail.AllPeopleCounts.Min();
                    var max = detail.AllPeopleCounts.Max();
                    PeopleCountText.Text = $"{min}-{max} คน";
                    PeopleCountText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                    PeopleWarningIcon.Visibility = Visibility.Visible;
                    ToolTipService.SetToolTip(PeopleWarningIcon, 
                        "⚠️ จำนวนคนในแต่ละใบไม่เท่ากัน: " + string.Join(", ", detail.AllPeopleCounts.Select(p => $"{p} คน")));
                }
                else if (detail.ConsistentPeopleCount.HasValue)
                {
                    PeopleCountText.Text = $"{detail.ConsistentPeopleCount} คน";
                    PeopleCountText.ClearValue(TextBlock.ForegroundProperty);
                    PeopleWarningIcon.Visibility = Visibility.Collapsed;
                }
                else
                {
                    PeopleCountText.Text = "-";
                    PeopleCountText.ClearValue(TextBlock.ForegroundProperty);
                    PeopleWarningIcon.Visibility = Visibility.Collapsed;
                }
                
                TotalCostText.Text = $"{detail.TotalCost:N4} ฿";
                CostPerHeadText.Text = detail.CostPerHead.HasValue ? $"{detail.CostPerHead:N4} ฿/คน" : "-";
                IndividualCostsText.Text = detail.IndividualCostsDisplay;
                OutletBudgetsText.Text = detail.OutletBudgetsDisplay;
                
                // ผู้มาใช้จริง
                if (detail.TotalActualPeople.HasValue)
                {
                    ActualPeopleText.Text = $"{detail.TotalActualPeople} คน";
                    ActualPeopleText.ClearValue(TextBlock.ForegroundProperty);
                    
                    if (detail.HasInconsistentActualPeople)
                    {
                        ActualPeopleText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                        ActualPeopleWarningIcon.Visibility = Visibility.Visible;
                        ToolTipService.SetToolTip(ActualPeopleWarningIcon, 
                            "⚠️ จำนวนผู้มาใช้จริงในแต่ละใบไม่เท่ากัน: " + 
                            string.Join(", ", detail.AllActualPeopleCounts.Select(p => $"{p} คน")));
                    }
                    else
                    {
                        ActualPeopleWarningIcon.Visibility = Visibility.Collapsed;
                    }
                    
                    if (detail.ActualCostPerHead.HasValue)
                    {
                        ActualCostPerHeadText.Text = $"{detail.ActualCostPerHead:N4} ฿/คน";
                        
                        if (detail.MaxOutletBudget.HasValue && detail.ActualCostPerHead > detail.MaxOutletBudget)
                        {
                            ActualCostPerHeadText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                            ToolTipService.SetToolTip(ActualCostPerHeadText, 
                                $"⚠️ เกินงบ Outlet ({detail.MaxOutletBudget:N4}฿/คน)");
                        }
                        else
                        {
                            ActualCostPerHeadText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
                            if (detail.MaxOutletBudget.HasValue)
                            {
                                ToolTipService.SetToolTip(ActualCostPerHeadText, 
                                    $"✅ อยู่ในงบ Outlet ({detail.MaxOutletBudget:N4}฿/คน)");
                            }
                        }
                    }
                    else
                    {
                        ActualCostPerHeadText.Text = "-";
                    }
                }
                else if (detail.HasInconsistentActualPeople)
                {
                    var min = detail.AllActualPeopleCounts.Min();
                    var max = detail.AllActualPeopleCounts.Max();
                    ActualPeopleText.Text = $"{min}-{max} คน";
                    ActualPeopleText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                    ActualPeopleWarningIcon.Visibility = Visibility.Visible;
                    ActualCostPerHeadText.Text = "ไม่สามารถคำนวณได้";
                    ToolTipService.SetToolTip(ActualPeopleWarningIcon, 
                        "⚠️ จำนวนผู้มาใช้จริงในแต่ละใบไม่เท่ากัน: " + 
                        string.Join(", ", detail.AllActualPeopleCounts.Select(p => $"{p} คน")));
                }
                else
                {
                    ActualPeopleText.Text = "ยังไม่ระบุ";
                    ActualCostPerHeadText.Text = "-";
                    ActualPeopleWarningIcon.Visibility = Visibility.Collapsed;
                }

                // 🔥 เรียกฟังก์ชันตรวจสอบวันที่
                ProcessUsageDates(detail.Transfers);

                // แสดง Reason
                if (!string.IsNullOrWhiteSpace(detail.Reason))
                {
                    ReasonSection.Visibility = Visibility.Visible;
                    ReasonText.Text = detail.Reason;
                }

                // ต้นทุนแฝง (Expected)
                if (detail.ConsistentPeopleCount.HasValue && detail.CostPerHead.HasValue)
                {
                    decimal hiddenCostPct = detail.Transfers.FirstOrDefault()?.HiddenCostPercentage ?? 0m;
                    HiddenCostPercentageText.Text = $"{hiddenCostPct:N4}";
                    
                    decimal costPerPerson = detail.CostPerHead.Value;
                    decimal hiddenAmount = Math.Round(costPerPerson * (hiddenCostPct / 100m), 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข
                    decimal totalWithHidden = Math.Round(costPerPerson + hiddenAmount, 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข
                    
                    HiddenCostAmountText.Text = hiddenCostPct > 0m ? $"≈ {hiddenAmount:N4} ฿/คน" : "ไม่มีต้นทุนแฝง";
                    TotalCostWithHiddenText.Text = $"{totalWithHidden:N4} ฿/คน";
                    TotalCostBreakdownText.Text = $"{costPerPerson:N4} + {hiddenCostPct:N4}% ({hiddenAmount:N4} ฿)";
                    
                    if (detail.MaxOutletBudget.HasValue)
                    {
                        TotalCostTargetText.Text = $"งบต่อคน : {detail.MaxOutletBudget.Value:N4} ฿";
                        TotalCostTargetText.Visibility = Visibility.Visible;
                    }
                }

                // ต้นทุนแฝง (Actual)
                if (detail.TotalActualPeople.HasValue && detail.TotalActualPeople > 0 && detail.ActualCostPerHead.HasValue)
                {
                    ActualHiddenCostRow.Visibility = Visibility.Visible;
                    
                    decimal hiddenCostPct = detail.Transfers.FirstOrDefault()?.HiddenCostPercentage ?? 0m;
                    ActualHiddenCostPercentageText.Text = $"{hiddenCostPct:N4}";
                    
                    decimal actualCostPerPerson = detail.ActualCostPerHead.Value;
                    decimal actualHiddenAmount = Math.Round(actualCostPerPerson * (hiddenCostPct / 100m), 4, MidpointRounding.AwayFromZero);
                    decimal totalActualWithHidden = Math.Round(actualCostPerPerson + actualHiddenAmount, 4, MidpointRounding.AwayFromZero);
                    
                    ActualHiddenCostAmountText.Text = hiddenCostPct > 0m ? $"≈ {actualHiddenAmount:N4} ฿/คน" : "ไม่มีต้นทุนแฝง";
                    TotalActualCostWithHiddenText.Text = $"{totalActualWithHidden:N4} ฿/คน";
                    TotalActualCostBreakdownText.Text = $"{actualCostPerPerson:N4} + {hiddenCostPct:N4}% ({actualHiddenAmount:N4} ฿)";
                    
                    if (detail.MaxOutletBudget.HasValue)
                    {
                        TotalActualCostTargetText.Text = $"งบต่อคน : {detail.MaxOutletBudget.Value:N4} ฿";
                        TotalActualCostTargetText.Visibility = Visibility.Visible;
                        
                        if (totalActualWithHidden > detail.MaxOutletBudget.Value)
                        {
                            var exceed = Math.Round(totalActualWithHidden - detail.MaxOutletBudget.Value, 4, MidpointRounding.AwayFromZero); // ✅ เพิ่ม
                            TotalActualCostWarningText.Text = $"⚠️ เกินงบ {exceed:N4} ฿/คน";
                            TotalActualCostWarningText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                            TotalActualCostWarningText.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            var remain = Math.Round(detail.MaxOutletBudget.Value - totalActualWithHidden, 4, MidpointRounding.AwayFromZero); // ✅ เพิ่ม
                            TotalActualCostWarningText.Text = $"✓ เหลืองบ {remain:N4} ฿/คน";
                            TotalActualCostWarningText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
                            TotalActualCostWarningText.Visibility = Visibility.Visible;
                        }
                    }
                }
                else
                {
                    ActualHiddenCostRow.Visibility = Visibility.Collapsed;
                }

                // แสดง Transfer List และ Items
                TransfersListView.ItemsSource = detail.Transfers;
                ItemsListView.ItemsSource = detail.CombinedItems;
            }
            catch (Exception ex)
            {
                await DialogHelper.ShowErrorAsync("เกิดข้อผิดพลาด", $"ไม่สามารถโหลดรายละเอียด: {ex.Message}");
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 🔥 ตรวจสอบและแสดงวันที่ใช้ในใบ Transfer
        /// </summary>
        private void ProcessUsageDates(List<TransferSummaryViewModel> transfers)
        {
            if (transfers == null || transfers.Count == 0)
            {
                UsageDateText.Text = "-";
                UsageDateCountText.Text = "";
                UsageDateWarningIcon.Visibility = Visibility.Collapsed;
                ViewUsageDatesButton.Visibility = Visibility.Collapsed;
                return;
            }

            // ดึงวันที่ทั้งหมด (เฉพาะส่วนวันที่ ไม่สนใจเวลา)
            _allUsageDates = transfers
                .Where(t => t.UsageDate.HasValue)
                .Select(t => t.UsageDate!.Value.Date)
                .ToList();

            if (_allUsageDates.Count == 0)
            {
                UsageDateText.Text = "ไม่มีข้อมูลวันที่";
                UsageDateCountText.Text = "";
                UsageDateWarningIcon.Visibility = Visibility.Collapsed;
                ViewUsageDatesButton.Visibility = Visibility.Collapsed;
                return;
            }

            // หาวันที่ที่ไม่ซ้ำกัน
            var uniqueDates = _allUsageDates.Distinct().OrderBy(d => d).ToList();

            if (uniqueDates.Count == 1)
            {
                // ✅ วันที่ตรงกันทั้งหมด
                UsageDateText.Text = uniqueDates[0].ToString("dd/MM/yyyy");
                UsageDateCountText.Text = $"({_allUsageDates.Count} ใบ)";
                UsageDateWarningIcon.Visibility = Visibility.Collapsed;
                ViewUsageDatesButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                // ⚠️ วันที่ไม่ตรงกัน
                UsageDateText.Text = $"{uniqueDates.Count} วันที่";
                UsageDateCountText.Text = $"({uniqueDates[0]:dd/MM} - {uniqueDates[uniqueDates.Count - 1]:dd/MM})";
                UsageDateWarningIcon.Visibility = Visibility.Visible;
                ViewUsageDatesButton.Visibility = Visibility.Visible;
                
                // ตั้งค่า Tooltip
                var tooltip = "⚠️ พบวันที่ใช้ที่แตกต่างกัน:\n" + 
                              string.Join("\n", uniqueDates.Select(d => $"• {d:dd/MM/yyyy} ({_allUsageDates.Count(x => x == d)} ใบ)"));
                ToolTipService.SetToolTip(UsageDateWarningIcon, tooltip);
            }
        }

        /// <summary>
        /// 🔥 แสดง Dialog รายละเอียดวันที่ทั้งหมด (เมื่อวันที่ไม่ตรงกัน)
        /// </summary>
        private async void ViewUsageDatesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_allUsageDates == null || _allUsageDates.Count == 0)
                return;

            var uniqueDates = _allUsageDates.Distinct().OrderBy(d => d).ToList();
            var message = "วันที่ใช้ในใบ Transfer:\n\n";
            
            foreach (var date in uniqueDates)
            {
                var count = _allUsageDates.Count(d => d == date);
                message += $"📅 {date:dd/MM/yyyy} - {count} ใบ\n";
            }

            var dialog = new ContentDialog
            {
                Title = "รายละเอียดวันที่ใช้",
                Content = message,
                CloseButtonText = "ปิด",
                XamlRoot = this.XamlRoot
            };
            
            await dialog.ShowAsync();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private Task ShowErrorDialog(string title, string message) => ShowErrorDialogAsync(title, message);
        private Task ShowSuccessDialog(string title, string message) => ShowSuccessDialogAsync(title, message);

        private async Task ShowErrorDialogAsync(string title, string message)
        {
            if (Interlocked.CompareExchange(ref _dialogOpen, 1, 0) == 1)
                return;

            try
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    CloseButtonText = "ตกลง",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _dialogOpen, 0);
            }
        }

        private async Task ShowSuccessDialogAsync(string title, string message)
        {
            if (Interlocked.CompareExchange(ref _dialogOpen, 1, 0) == 1)
                return;

            try
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    CloseButtonText = "ตกลง",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _dialogOpen, 0);
            }
        }
    }
}
