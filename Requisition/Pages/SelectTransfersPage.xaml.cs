using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Requisition.Helpers;
using Requisition.Models;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Requisition.Pages
{
    public sealed partial class SelectTransfersPage : Page
    {
        private readonly CombinedTransferService _service;
        private List<SelectableTransferViewModel> _allTransfers = new();
        private HashSet<int> _selectedTransferIds = new(); // 🔥 เก็บ ID ที่เลือกไว้
        private bool _isEditMode = false;
        private int _editingCombinedId = 0;
        private System.Threading.Timer? _searchTimer;

        public SelectTransfersPage()
        {
            InitializeComponent();
            _service = new CombinedTransferService();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // ถ้ามี parameter แสดงว่าเป็นโหมดแก้ไข
            if (e.Parameter is int combinedId)
            {
                _isEditMode = true;
                _editingCombinedId = combinedId;
            }

            await LoadFiltersAsync();
            await LoadDataAsync();
        }

        /// <summary>
        /// โหลดข้อมูล Outlets และ Kitchens สำหรับ ComboBox
        /// </summary>
        private async Task LoadFiltersAsync()
        {
            try
            {
                // โหลด Outlets
                var outlets = await _service.GetActiveOutletsAsync();
                OutletFilterComboBox.ItemsSource = outlets;

                // โหลด Kitchens
                var kitchens = await _service.GetActiveKitchensAsync();
                KitchenFilterComboBox.ItemsSource = kitchens;
            }
            catch (Exception ex)
            {
                await DialogHelper.ShowErrorAsync("เกิดข้อผิดพลาด", $"ไม่สามารถโหลดตัวกรองได้: {ex.Message}");
            }
        }

        private async Task LoadDataAsync(string? searchTransferNo = null, int? outletId = null, int? kitchenId = null)
        {
            try
            {
                LoadingRing.IsActive = true;
                LoadingRing.Visibility = Visibility.Visible;
                TransfersListView.Visibility = Visibility.Collapsed;
                EmptyStatePanel.Visibility = Visibility.Collapsed;

                // 🔥 เก็บ ID ที่เลือกไว้ก่อนโหลดข้อมูลใหม่
                _selectedTransferIds = _allTransfers
                    .Where(t => t.IsSelected)
                    .Select(t => t.Id)
                    .ToHashSet();

                // Unsubscribe from old list before replacing to avoid duplicated handlers
                if (_allTransfers != null)
                {
                    foreach (var old in _allTransfers)
                    {
                        old.PropertyChanged -= Transfer_PropertyChanged;
                    }
                }

                // Call the overload that accepts outletId and kitchenId directly
                _allTransfers = await _service.GetAvailableTransfersAsync(searchTransferNo, outletId, kitchenId);

                // 🔥 กลับมาเลือกรายการที่เคยเลือกไว้
                foreach (var transfer in _allTransfers)
                {
                    if (_selectedTransferIds.Contains(transfer.Id))
                    {
                        transfer.IsSelected = true;
                    }
                }

                // subscribe for IsSelected changes so summary always stays correct
                foreach (var t in _allTransfers)
                {
                    t.PropertyChanged += Transfer_PropertyChanged;
                }

                // ถ้าเป็นโหมดแก้ไข ให้โหลดการเลือกเดิม
                if (_isEditMode && string.IsNullOrEmpty(searchTransferNo) && !outletId.HasValue && !kitchenId.HasValue)
                {
                    await LoadExistingSelectionsAsync();
                }

                if (_allTransfers.Count == 0)
                {
                    EmptyStatePanel.Visibility = Visibility.Visible;
                }
                else
                {
                    TransfersListView.ItemsSource = _allTransfers;
                    TransfersListView.Visibility = Visibility.Visible;
                }

                AvailableCountText.Text = $"({_allTransfers.Count(t => t.IsSelectable)} ใบ)";
                UpdateSummary();
            }
            catch (Exception ex)
            {
                await DialogHelper.ShowErrorAsync("เกิดข้อผิดพลาด", $"ไม่สามารถโหลดข้อมูลได้: {ex.Message}");
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadExistingSelectionsAsync()
        {
            // โหลดการเลือกเดิมจาก CombinedTransferSources
            var detail = await _service.GetCombinedTransferDetailAsync(_editingCombinedId);
            if (detail != null)
            {
                var selectedIds = detail.Transfers.Select(t => t.Id).ToHashSet();
                
                // 🔥 เก็บไว้ใน _selectedTransferIds ด้วย
                _selectedTransferIds = selectedIds;
                
                foreach (var transfer in _allTransfers)
                {
                    if (selectedIds.Contains(transfer.Id))
                    {
                        transfer.IsSelected = true;
                        transfer.IsAlreadyCombined = false; // ให้เลือกได้
                    }
                }

                ReasonTextBox.Text = detail.Reason ?? "";
            }
        }

        private void TransferCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSummary();
        }

        private void Transfer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SelectableTransferViewModel.IsSelected))
            {
                // ensure UI update occurs on UI thread
                DispatcherQueue.TryEnqueue(() => UpdateSummary());
            }
        }

        private void UpdateSummary()
        {
            var selected = _allTransfers.Where(t => t.IsSelected).ToList();

            SelectedCountText.Text = $"{selected.Count} ใบ";
            
            bool hasMismatch = false;
            var warnings = new List<string>();

            // เช็คจำนวนคนคาดหวัง - ต้องเท่ากันทุกใบ
            if (selected.Count > 0)
            {
                var peopleGroups = selected.GroupBy(t => t.ExpectedPeople).ToList();
                
                if (peopleGroups.Count > 1)
                {
                    // จำนวนคนไม่เท่ากัน - แสดงคำเตือน
                    hasMismatch = true;
                    var peopleList = string.Join(", ", peopleGroups.Select(g => $"{g.Key} คน ({g.Count()} ใบ)"));
                    warnings.Add($"จำนวนคนที่คาดหวังไม่เท่ากัน: {peopleList}");
                    PeopleCountText.Text = "ไม่สม่ำเสมอ";
                    PeopleCountText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                }
                else
                {
                    // จำนวนคนเท่ากัน
                    var peopleCount = peopleGroups[0].Key;
                    PeopleCountText.Text = $"{peopleCount} คน";
                    PeopleCountText.ClearValue(TextBlock.ForegroundProperty);
                }

                // เช็ค Outlet — ต้องเป็น Outlet เดียวกัน
                var outletGroups = selected.GroupBy(t => t.OutletId).ToList();
                if (outletGroups.Count > 1)
                {
                    hasMismatch = true;
                    var outletList = string.Join(", ", outletGroups.Select(g => $"{(g.First().OutletName ?? "-")} ({g.Count()} ใบ)"));
                    warnings.Add($"มาจาก Outlet ต่างกัน: {outletList}");
                }

                // เช็ค UsageDate — ต้องเป็นวันเดียวกัน (date only)
                var dateGroups = selected.GroupBy(t => t.UsageDate?.Date).ToList();
                if (dateGroups.Count > 1)
                {
                    hasMismatch = true;
                    var dateList = string.Join(", ", dateGroups.Select(g => $"{(g.Key.HasValue ? g.Key.Value.ToString("dd/MM/yyyy") : "ไม่ระบุ")} ({g.Count()} ใบ)"));
                    warnings.Add($"วันที่ใช้ไม่ตรงกัน: {dateList}");
                }

                // เช็ค ActualPeople (ผู้มาใช้จริง) — ต้องเท่ากันทุกใบ
                var actualPeopleGroups = selected.GroupBy(t => t.ActualPeople).ToList();
                // treat null and value difference as mismatch
                if (actualPeopleGroups.Count > 1)
                {
                    hasMismatch = true;
                    var actualList = string.Join(", ", actualPeopleGroups.Select(g => $"{(g.Key.HasValue ? $"{g.Key} คน" : "ยังไม่ระบุ")} ({g.Count()} ใบ)"));
                    warnings.Add($"จำนวนผู้มาใช้จริงไม่เท่ากัน/ไม่ครบ: {actualList}");
                }

                // ยอดรวม
                var totalCost = selected.Sum(t => t.TotalCost);
                TotalCostText.Text = $"{totalCost:N4} ฿";

                // ราคา/หัว รวม (รวม CostPerPerson จากทุกใบ)
                var totalCostPerHead = selected
                    .Where(t => t.CostPerPerson.HasValue)
                    .Sum(t => t.CostPerPerson!.Value);
                TotalCostPerHeadText.Text = $"{totalCostPerHead:N4} ฿/คน";

                // ราคาต่อหัวของ Outlet (ดึงจาก Outlet โดยตรง)
                var outletPrices = selected
                    .Where(t => t.OutletPricePerHead.HasValue)
                    .Select(t => (t.OutletName, t.OutletPricePerHead!.Value))
                    .Distinct()
                    .ToList();

                if (outletPrices.Count == 1)
                {
                    // Outlet เดียวกันทั้งหมด - แสดงราคาต่อหัว
                    var pricePerHead = outletPrices[0].Item2;
                    OutletPricePerHeadText.Text = $"{pricePerHead:N4} ฿/คน";
                    
                    // เปรียบเทียบกับราคา/หัว รวม
                    if (selected.All(t => t.CostPerPerson.HasValue))
                    {
                        var avgCostPerHead = selected.Average(t => t.CostPerPerson!.Value);
                        OutletPricePerHeadText.Foreground = avgCostPerHead > pricePerHead
                            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red)
                            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green);
                    }
                    else
                    {
                        OutletPricePerHeadText.ClearValue(TextBlock.ForegroundProperty);
                    }
                }
                else if (outletPrices.Count > 1)
                {
                    // หลาย Outlet - แสดงรายการราคา
                    var priceList = string.Join(", ", outletPrices.Select(p => $"{p.Item2:N0}฿"));
                    OutletPricePerHeadText.Text = $"หลาย Outlet: {priceList}";
                    OutletPricePerHeadText.ClearValue(TextBlock.ForegroundProperty);
                }
                else
                {
                    // ไม่มีข้อมูลราคา Outlet
                    OutletPricePerHeadText.Text = "-";
                    OutletPricePerHeadText.ClearValue(TextBlock.ForegroundProperty);
                }
            }
            else
            {
                // ไม่มีใบที่เลือก - รีเซ็ตค่า
                PeopleWarningBorder.Visibility = Visibility.Collapsed;
                PeopleCountText.Text = "0 คน";
                PeopleCountText.ClearValue(TextBlock.ForegroundProperty);
                TotalCostText.Text = "0.00 ฿";
                TotalCostPerHeadText.Text = "0.00 ฿/คน";
                OutletPricePerHeadText.Text = "-";
                OutletPricePerHeadText.ClearValue(TextBlock.ForegroundProperty);
            }

            // show combined warnings if any
            if (hasMismatch)
            {
                PeopleWarningBorder.Visibility = Visibility.Visible;
                PeopleWarningText.Text = "⚠️ ไม่สามารถรวมใบได้เนื่องจาก:\n" + string.Join("\n", warnings);
            }
            else
            {
                PeopleWarningBorder.Visibility = Visibility.Collapsed;
            }

            // ✅ เปิดปุ่มเฉพาะเมื่อมีรายการเลือก และ ทุกเงื่อนไขสอดคล้องกัน
            CreateButton.IsEnabled = selected.Count > 0 && !hasMismatch;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Debounce search - รอ 500ms หลังจากพิมพ์เสร็จ
            _searchTimer?.Dispose();
            _searchTimer = new System.Threading.Timer(_ =>
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await ApplyFiltersAsync();
                });
            }, null, 500, System.Threading.Timeout.Infinite);
        }

        private async void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // เรียกฟิลเตอร์ทันทีเมื่อเลือก ComboBox
            await ApplyFiltersAsync();
        }

        private async Task ApplyFiltersAsync()
        {
            var transferNo = string.IsNullOrWhiteSpace(SearchTransferNoBox.Text) 
                ? null 
                : SearchTransferNoBox.Text.Trim();

            var outletId = (OutletFilterComboBox.SelectedItem as Outlet)?.Id;
            var kitchenId = (KitchenFilterComboBox.SelectedItem as Kitchen)?.Id;

            await LoadDataAsync(transferNo, outletId, kitchenId);
        }

        private async void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchTransferNoBox.Text = "";
            OutletFilterComboBox.SelectedItem = null;
            KitchenFilterComboBox.SelectedItem = null;
            await LoadDataAsync();
        }

        private async void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = _allTransfers.Where(t => t.IsSelected).ToList();

            if (selected.Count == 0)
            {
                await DialogHelper.ShowErrorAsync("ไม่สามารถดำเนินการได้", "กรุณาเลือกใบ Transfer อย่างน้อย 1 ใบ");
                return;
            }

            // VALIDATE: ExpectedPeople consistency
            var peopleGroups = selected.GroupBy(t => t.ExpectedPeople).ToList();
            if (peopleGroups.Count > 1)
            {
                var peopleList = string.Join(", ", peopleGroups.Select(g => $"{g.Key} คน ({g.Count()} ใบ)"));
                await DialogHelper.ShowErrorAsync(
                    "ไม่สามารถสร้างใบรวมได้", 
                    $"จำนวนคนที่คาดหวังในแต่ละใบต้องเท่ากัน\n\nพบ: {peopleList}\n\nกรุณาเลือกเฉพาะใบที่มีจำนวนคนเท่ากัน");
                return;
            }

            // VALIDATE: Outlet consistency
            var outletGroups = selected.GroupBy(t => t.OutletId).ToList();
            if (outletGroups.Count > 1)
            {
                var outletList = string.Join(", ", outletGroups.Select(g => $"{(g.First().OutletName ?? "-")} ({g.Count()} ใบ)"));
                await DialogHelper.ShowErrorAsync(
                    "ไม่สามารถสร้างใบรวมได้",
                    $"ใบที่เลือกมาจาก Outlet ต่างกัน: {outletList}\n\nกรุณาเลือกเฉพาะใบจาก Outlet เดียวกัน");
                return;
            }

            // VALIDATE: UsageDate consistency (date only)
            var dateGroups = selected.GroupBy(t => t.UsageDate?.Date).ToList();
            if (dateGroups.Count > 1)
            {
                var dateList = string.Join(", ", dateGroups.Select(g => $"{(g.Key.HasValue ? g.Key.Value.ToString("dd/MM/yyyy") : "ไม่ระบุ")} ({g.Count()} ใบ)"));
                await DialogHelper.ShowErrorAsync(
                    "ไม่สามารถสร้างใบรวมได้",
                    $"วันที่ใช้ไม่ตรงกัน: {dateList}\n\nกรุณาเลือกเฉพาะใบที่วันที่ใช้เป็นวันเดียวกัน");
                return;
            }

            // VALIDATE: ActualPeople consistency
            var actualGroups = selected.GroupBy(t => t.ActualPeople).ToList();
            if (actualGroups.Count > 1)
            {
                var actualList = string.Join(", ", actualGroups.Select(g => $"{(g.Key.HasValue ? $"{g.Key} คน" : "ยังไม่ระบุ")} ({g.Count()} ใบ)"));
                await DialogHelper.ShowErrorAsync(
                    "ไม่สามารถสร้างใบรวมได้",
                    $"จำนวนผู้มาใช้จริงในแต่ละใบไม่เท่ากัน/มีบางใบยังไม่ระบุ: {actualList}\n\nกรุณาเลือกเฉพาะใบที่จำนวนผู้มาใช้จริงสอดคล้องกัน");
                return;
            }

            try
            {
                LoadingRing.IsActive = true;
                LoadingRing.Visibility = Visibility.Visible;

                var username = await UserHelper.GetCurrentUsernameAsync();
                var reason = string.IsNullOrWhiteSpace(ReasonTextBox.Text) ? null : ReasonTextBox.Text;
                var selectedIds = selected.Select(t => t.Id).ToList();

                if (_isEditMode)
                {
                    // โหมดแก้ไข
                    var updateResult = await _service.UpdateCombinedTransferSourcesAsync(
                        _editingCombinedId,
                        selectedIds,
                        username,
                        reason);

                    if (updateResult.success)
                    {
                        await DialogHelper.ShowSuccessAsync("สำเร็จ", "แก้ไขการเลือกใบเรียบร้อยแล้ว");
                        Frame.Navigate(typeof(CombinedTransferDetailPage), _editingCombinedId);
                    }
                    else
                    {
                        await DialogHelper.ShowErrorAsync("เกิดข้อผิดพลาด", updateResult.error);
                    }
                }
                else
                {
                    // โหมดสร้างใหม่
                    var createResult = await _service.CreateCombinedTransferAsync(
                        selectedIds,
                        reason,
                        username);

                    if (createResult.success)
                    {
                        await DialogHelper.ShowSuccessAsync("สำเร็จ", $"สร้างใบรวม {createResult.combinedNo} เรียบร้อยแล้ว");
                        Frame.Navigate(typeof(CombinedTransferDetailPage), createResult.combinedId);
                    }
                    else
                    {
                        await DialogHelper.ShowErrorAsync("เกิดข้อผิดพลาด", createResult.error);
                    }
                }
            }
            catch (Exception ex)
            {
                await DialogHelper.ShowErrorAsync("เกิดข้อผิดพลาด", ex.Message);
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }
    }
}
