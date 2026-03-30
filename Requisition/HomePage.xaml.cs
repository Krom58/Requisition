using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Requisition.Helpers;
using Requisition.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Requisition.Pages
{
    public class ActivityItem
    {
        public string Icon { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string TimeAgo { get; set; } = "";
    }

    public sealed partial class HomePage : Page, INotifyPropertyChanged
    {
        private readonly TransferService _transferService;
        private readonly ProductService _productService;
        private readonly CombinedTransferService _combinedService;

        public HomePage()
        {
            InitializeComponent();
            _transferService = new TransferService();
            _productService = new ProductService();
            _combinedService = new CombinedTransferService();
            
            RecentActivities = new ObservableCollection<ActivityItem>();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await LoadDashboardDataAsync();
        }

        private async Task LoadDashboardDataAsync()
        {
            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;

                // โหลดข้อมูลสถิติ
                var transfers = await _transferService.GetAllTransfersAsync();
                var products = await _productService.GetAllProductsAsync();

                var today = DateTime.Today;
                TodayTransfersCount = transfers.Count(t => t.CreatedDate.Date == today).ToString();
                PendingTransfersCount = transfers.Count(t => !t.ActualPeople.HasValue).ToString();
                TotalProductsCount = products.Count.ToString();
                
                var thisMonth = transfers.Where(t => t.CreatedDate.Year == today.Year && 
                                                     t.CreatedDate.Month == today.Month);
                var monthCost = thisMonth.Sum(t => t.TotalCost);
                MonthCostDisplay = $"{monthCost:N2} ฿";

                // โหลดกิจกรรมล่าสุด
                await LoadRecentActivitiesAsync();
            }
            catch (Exception ex)
            {
                await DialogHelper.ShowErrorAsync(
                    "เกิดข้อผิดพลาด", 
                    $"ไม่สามารถโหลดข้อมูลหน้าแรก: {ex.Message}");
                
                // ตั้งค่าเริ่มต้นถ้าโหลดข้อมูลไม่ได้
                TodayTransfersCount = "0";
                PendingTransfersCount = "0";
                TotalProductsCount = "0";
                MonthCostDisplay = "0 ฿";
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadRecentActivitiesAsync()
        {
            RecentActivities.Clear();
            
            var transfers = await _transferService.GetAllTransfersAsync();
            var recent = transfers.OrderByDescending(t => t.CreatedDate).Take(10);
            
            foreach (var transfer in recent)
            {
                RecentActivities.Add(new ActivityItem
                {
                    Icon = "\uE8F1",
                    Title = $"ใบ Transfer #{transfer.TransferNo}",
                    Description = $"ห้องครัว: {transfer.KitchenName} | {transfer.TotalCost:N2} ฿",
                    TimeAgo = GetTimeAgo(transfer.CreatedDate)
                });
            }
        }

        private string GetTimeAgo(DateTime date)
        {
            var span = DateTime.Now - date;
            if (span.TotalDays >= 1) return $"{(int)span.TotalDays} วันที่แล้ว";
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours} ชั่วโมงที่แล้ว";
            if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes} นาทีที่แล้ว";
            return "เมื่อสักครู่";
        }

        // Properties
        private string _todayTransfersCount = "0";
        public string TodayTransfersCount
        {
            get => _todayTransfersCount;
            set { _todayTransfersCount = value; OnPropertyChanged(); }
        }

        private string _pendingTransfersCount = "0";
        public string PendingTransfersCount
        {
            get => _pendingTransfersCount;
            set { _pendingTransfersCount = value; OnPropertyChanged(); }
        }

        private string _totalProductsCount = "0";
        public string TotalProductsCount
        {
            get => _totalProductsCount;
            set { _totalProductsCount = value; OnPropertyChanged(); }
        }

        private string _monthCostDisplay = "0 ฿";
        public string MonthCostDisplay
        {
            get => _monthCostDisplay;
            set { _monthCostDisplay = value; OnPropertyChanged(); }
        }

        public string CurrentDateDisplay => DateTime.Now.ToString("วันdddd ที่ dd MMMM yyyy", 
            new System.Globalization.CultureInfo("th-TH"));

        public ObservableCollection<ActivityItem> RecentActivities { get; }

        // Event Handlers
        private void CreateTransferButton_Click(object sender, RoutedEventArgs e)
        {
            (App.Window as MainWindow)?.SelectNavByTag("Transfer");
        }

        private void ImportExcelButton_Click(object sender, RoutedEventArgs e)
        {
            (App.Window as MainWindow)?.SelectNavByTag("ImportExcel");
        }

        private void CombineTransfersButton_Click(object sender, RoutedEventArgs e)
        {
            (App.Window as MainWindow)?.SelectNavByTag("CombineTransfers");
        }

        private void ViewReportButton_Click(object sender, RoutedEventArgs e)
        {
            (App.Window as MainWindow)?.SelectNavByTag("CostReport");
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}