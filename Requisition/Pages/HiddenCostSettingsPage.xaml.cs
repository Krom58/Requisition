using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Requisition.Helpers;
using Requisition.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Requisition.Pages
{
    public sealed partial class HiddenCostSettingsPage : Page
    {
        private readonly HiddenCostService _service;
        public ObservableCollection<HiddenCostHistoryItem> HistoryItems { get; } = new();

        public HiddenCostSettingsPage()
        {
            InitializeComponent();
            _service = new HiddenCostService();
            Loaded += HiddenCostSettingsPage_Loaded;
        }

        private async void HiddenCostSettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                // โหลดค่าปัจจุบัน
                var current = await _service.GetCurrentPercentageAsync();
                PercentageInput.Value = (double)current;

                // โหลดประวัติ
                var history = await _service.GetHistoryAsync();
                HistoryItems.Clear();
                foreach (var item in history)
                {
                    HistoryItems.Add(new HiddenCostHistoryItem
                    {
                        OldPercentage = item.OldPercentage,
                        NewPercentage = item.NewPercentage,
                        ActionBy = item.ActionBy ?? "ไม่ระบุ",
                        ActionDate = item.ActionDate
                    });
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"เกิดข้อผิดพลาดในการโหลดข้อมูล: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (PercentageInput.Value == double.NaN)
                {
                    ShowMessage("กรุณากรอกค่าเปอร์เซ็นต์", InfoBarSeverity.Warning);
                    return;
                }

                var percentage = (decimal)PercentageInput.Value;

                // แสดง Confirmation Dialog
                var confirmed = await DialogHelper.ShowConfirmAsync(
                    "ยืนยันการบันทึก",
                    $"คุณต้องการบันทึกค่าต้นทุนแฝงเป็น {percentage:0}% ใช่หรือไม่?",
                    XamlRoot);

                if (!confirmed)
                {
                    return;
                }

                // ดึงชื่อผู้ใช้งานปัจจุบัน
                var currentUser = Environment.UserName;

                await _service.SavePercentageAsync(percentage, currentUser);

                ShowMessage("บันทึกค่าต้นทุนแฝงสำเร็จ", InfoBarSeverity.Success);

                // โหลดข้อมูลใหม่
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                await DialogHelper.ShowErrorAsync(
                    "เกิดข้อผิดพลาด",
                    $"ไม่สามารถบันทึกข้อมูลได้: {ex.Message}",
                    XamlRoot);
            }
        }

        private void ShowMessage(string message, InfoBarSeverity severity)
        {
            MessageBar.Message = message;
            MessageBar.Severity = severity;
            MessageBar.IsOpen = true;
        }
    }

    // DTO สำหรับแสดงในประวัติ
    public class HiddenCostHistoryItem
    {
        public decimal? OldPercentage { get; set; }
        public decimal NewPercentage { get; set; }
        public string ActionBy { get; set; } = string.Empty;
        public DateTime ActionDate { get; set; }

        public string OldPercentageDisplay => OldPercentage.HasValue ? $"{OldPercentage.Value:0}%" : "ไม่มี";
        public string NewPercentageDisplay => $"{NewPercentage:0}%";
        public string ActionDateDisplay => ActionDate.ToString("dd/MM/yyyy HH:mm:ss");
    }
}
