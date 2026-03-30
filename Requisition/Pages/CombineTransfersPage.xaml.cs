using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Requisition.Models;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Requisition.Pages
{
    public sealed partial class CombineTransfersPage : Page
    {
        private readonly TransferService _transferService;
        private readonly CostPerHeadService _costPerHeadService;
        private readonly CombinedTransferService _combinedService;

        private ObservableCollection<TransferViewModel> _transfers = new();
        private HashSet<int> _selectedIds = new();

        // history viewmodels
        private ObservableCollection<CombinedHistoryViewModel> _combinedHistory = new();

        // dialog gate used by ShowError/ShowSuccess helpers
        private int _dialogOpen = 0;

        public CombineTransfersPage()
        {
            this.InitializeComponent();

            _transferService = new TransferService();
            _costPerHead_service_safe_init();
            _costPerHeadService = new CostPerHeadService();
            _combinedService = new CombinedTransferService();

            Loaded += CombineTransfersPage_Loaded;
        }

        private void _costPerHead_service_safe_init()
        {
            // no-op placeholder to match pattern in project (keeps analyzer happy)
        }

        private async void CombineTransfersPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadTransfersAsync();
            await LoadCombinedHistoryAsync();
        }

        private async Task LoadTransfersAsync()
        {
            try
            {
                TransfersListView.ItemsSource = null;
                _transfers.Clear();

                var all = await _transferService.GetAllTransfersAsync();

                // Load Outlets to map price per head
                var outlets = await _costPerHeadService.GetAllAsync();
                var outletMap = outlets.ToDictionary(k => k.Id, k => k);

                // Only show Completed transfers (per requirement)
                var completed = all.Where(t => t.Status == TransferStatus.Completed && !t.IsDeleted).OrderByDescending(t => t.CreatedDate).ToList();

                foreach (var t in completed)
                {
                    decimal outletPrice = 0;
                    if (t.OutletId.HasValue && outletMap.TryGetValue(t.OutletId.Value, out var k))
                        outletPrice = k.PricePerHead.GetValueOrDefault(0m);

                    var vm = new TransferViewModel
                    {
                        Id = t.Id,
                        TransferNo = t.TransferNo,
                        OutletDisplay = t.OutletDisplay,
                        CreatedDateDisplay = t.CreatedDate.ToString("dd/MM/yyyy HH:mm"),
                        UsageDateDisplay = t.UsageDate?.ToString("dd/MM/yyyy") ?? "ไม่ระบุ",
                        CostPerPerson = t.CostPerActualPerson > 0 ? t.CostPerActualPerson : t.CostPerPerson,
                        CostPerPersonDisplay = t.CostPerActualPerson > 0 ? t.CostPerActualPersonDisplay : t.CostPerPersonDisplay,
                        OutletPricePerHead = outletPrice,
                        TotalQuantity = t.TotalQuantity,
                        TotalCost = t.TotalCost,
                        IsSelectable = true // completed => selectable
                    };

                    _transfers.Add(vm);
                }

                TransfersListView.ItemsSource = _transfers;
                AvailableCountText.Text = $"แสดง { _transfers.Count } ใบ (สถานะ: จบงาน)";

                UpdateAggregateDisplay();
            }
            catch (Exception ex)
            {
                AvailableCountText.Text = $"โหลดไม่สำเร็จ: {ex.Message}";
            }
        }

        // Called when a checkbox is checked
        private void TransferCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is int id)
            {
                var vm = _transfers.FirstOrDefault(x => x.Id == id);
                if (vm == null) return;

                // Add to selected set and disable ability to select again
                _selectedIds.Add(id);
                vm.IsSelectable = false;
                vm.IsSelected = true;

                UpdateAggregateDisplay();
            }
        }

        private void TransferCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is int id)
            {
                var vm = _transfers.FirstOrDefault(x => x.Id == id);
                if (vm == null) return;

                // Requirement: once selected cannot be selected again. Revert uncheck to keep it selected.
                vm.IsSelected = true; // revert to checked
            }
        }

        private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear only in-memory selections (and re-enable ability to select again)
            _selectedIds.Clear();
            foreach (var vm in _transfers)
            {
                vm.IsSelectable = true;
                vm.IsSelected = false;
            }
            UpdateAggregateDisplay();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame != null && Frame.CanGoBack) Frame.GoBack();
        }

        private void UpdateAggregateDisplay()
        {
            var selected = _transfers.Where(t => t.IsSelected || _selectedIds.Contains(t.Id)).ToList();

            int count = selected.Count;
            decimal totalQty = selected.Sum(s => s.TotalQuantity);
            decimal totalCost = selected.Sum(s => s.TotalCost);

            // Best-effort derive people per transfer from CostPerPerson if available.
            int totalPeople = 0;
            foreach (var v in selected)
            {
                if (v.CostPerPerson > 0 && v.TotalCost > 0)
                {
                    try
                    {
                        int p = (int)Math.Round((double)(v.TotalCost / v.CostPerPerson));
                        totalPeople += Math.Max(0, p);
                    }
                    catch { }
                }
            }

            decimal combinedCostPerHead = totalPeople > 0 ? totalCost / totalPeople : 0m;

            SelectedCountText.Text = count.ToString();
            TotalQuantityText.Text = $"{totalQty:N4}";
            TotalCostText.Text = $"{totalCost:N4} ฿";
            CombinedCostPerHeadText.Text = $"{combinedCostPerHead:N4} ฿/คน";

            var distinctOutletPrices = selected.Select(s => s.OutletPricePerHead).Distinct().ToList();
            if (selected.Count > 0 && distinctOutletPrices.Count == 1 && distinctOutletPrices[0] > 0)
            {
                var kprice = distinctOutletPrices[0];
                if (combinedCostPerHead > kprice)
                {
                    CombinedWarningText.Text = $"เกินงบต่อหัวของOutlet: {kprice:N4} ฿/คน";
                    CombinedWarningText.Visibility = Visibility.Visible;
                    CombinedCostPerHeadText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
                }
                else
                {
                    CombinedWarningText.Text = string.Empty;
                    CombinedWarningText.Visibility = Visibility.Collapsed;
                    CombinedCostPerHeadText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
                }
            }
            else
            {
                CombinedWarningText.Text = string.Empty;
                CombinedWarningText.Visibility = Visibility.Collapsed;
                CombinedCostPerHeadText.Foreground = (SolidColorBrush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            }
        }

        // Combine button handler
        private async void CombineButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = _transfers.Where(t => t.IsSelected || _selectedIds.Contains(t.Id)).ToList();
            if (selected.Count == 0)
            {
                await ShowErrorDialog("ข้อผิดพลาด", "กรุณาเลือกใบ Transfer อย่างน้อย 1 ใบ");
                return;
            }

            // Compute totals
            decimal totalQty = selected.Sum(s => s.TotalQuantity);
            decimal totalCost = selected.Sum(s => s.TotalCost);

            // Ask for actual people (optional) and reason (required)
            var peopleBox = new Microsoft.UI.Xaml.Controls.NumberBox
            {
                Header = "จำนวนคนรวม (ถ้ามี)",
                Value = 0,
                Minimum = 0,
                Width = 200
            };

            var reasonBox = new TextBox
            {
                Header = "เหตุผล/หมายเหตุ (บังคับ)",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 120,
                MinWidth = 400
            };

            var preview = new TextBlock
            {
                Text = $"รวม {selected.Count} ใบ\nจำนวนวัตถุดิบรวม: {totalQty:N4}\nยอดรวม: {totalCost:N4} ฿",
                TextWrapping = TextWrapping.Wrap
            };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(preview);
            panel.Children.Add(peopleBox);
            panel.Children.Add(reasonBox);

            var dialog = new ContentDialog
            {
                Title = "ยืนยันการบันทึกการรวมใบ Transfer",
                Content = panel,
                PrimaryButtonText = "ยืนยันบันทึก",
                CloseButtonText = "ยกเลิก",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            dialog.PrimaryButtonClick += (s, args) =>
            {
                if (string.IsNullOrWhiteSpace(reasonBox.Text))
                {
                    args.Cancel = true; // prevent dialog from closing
                }
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            int peopleValue = (int)Math.Round(peopleBox.Value);
            string createdBy = Environment.UserName;
            string reason = reasonBox.Text.Trim();

            try
            {
                ShowLoading(true);

                // Call service
                var ids = selected.Select(s => s.Id);
                int combinedId = await _combined_service_safe_call_Create(ids, peopleValue, totalQty, totalCost, createdBy, reason);

                ShowLoading(false);
                await ShowSuccessDialog("สำเร็จ", $"บันทึกการรวมเรียบร้อย (Id={combinedId})");

                // After creating combined record:
                // - reload combined history
                // - optionally mark selected items non-selectable (we already did when checked)
                await LoadCombinedHistoryAsync();
            }
            catch (Exception ex)
            {
                ShowLoading(false);
                await ShowErrorDialog("ล้มเหลว", ex.Message);
            }
        }

        // wrapper to call CreateCombinedTransferAsync safely (keeps pattern consistent)
        private async Task<int> _combined_service_safe_call_Create(IEnumerable<int> ids, int totalPeople, decimal totalQty, decimal totalCost, string createdBy, string notes)
        {
            try
            {
                return await _combinedService.CreateCombinedTransferAsync(ids, totalPeople, totalQty, totalCost, createdBy, notes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateCombinedTransferAsync failed: {ex.Message}");
                throw;
            }
        }

        // Load combined history list
        private async Task LoadCombinedHistoryAsync()
        {
            try
            {
                _combinedHistory.Clear();
                CombinedHistoryListView.ItemsSource = null;

                var rows = await _combinedService.GetAllCombinedAsync();

                foreach (var r in rows)
                {
                    _combinedHistory.Add(new CombinedHistoryViewModel
                    {
                        Id = r.Id,
                        CombinedNo = r.CombinedNo,
                        CreatedBy = r.CreatedBy!,
                        CreatedDate = r.CreatedDate,
                        CombinedCount = r.CombinedCount,
                        TotalQuantity = r.TotalQuantity,
                        TotalCost = r.TotalCost
                    });
                }

                CombinedHistoryListView.ItemsSource = _combinedHistory;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadCombinedHistoryAsync failed: {ex.Message}");
            }
        }

        private async void CombinedHistoryListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is CombinedHistoryViewModel vm)
            {
                try
                {
                    var sources = await _combinedService.GetCombinedSourcesAsync(vm.Id);

                    CombinedDetailsPanel.Visibility = Visibility.Visible;
                    CombinedSourcesListView.ItemsSource = sources;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"GetCombinedSourcesAsync failed: {ex.Message}");
                }
            }
        }

        // Show/hide loading indicator
        private void ShowLoading(bool isLoading)
        {
            // If LoadingRing is not in the visual tree (defensive), skip
            try
            {
                if (LoadingRing != null)
                {
                    LoadingRing.IsActive = isLoading;
                    LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch { }
        }

        /// <summary>
        /// Non-async wrappers used by caller code
        /// </summary>
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

    // Simple INotifyPropertyChanged base (small helper) - already namespace scope
    public class BindableBase : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
