using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Requisition.Models;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.UI;

namespace Requisition.Pages
{
    public sealed partial class ManageTransferDetailPage : Page
    {
        private readonly TransferService _transferService;
        private readonly ProductService _productService;
        private readonly CostPerHeadService _costPerHeadService;
        private Models.Transfer? _currentTransfer;
        private int _transferId;
        private bool _isReadOnly;
        // new field near other flags
        private bool _hasUserModifiedExpectedPeople = false;
        // Add alongside other private flags/fields
        private bool _hasUserModifiedDate = false;
        private List<Product> _availableProducts = new();
        private bool _hasPendingChanges = false;
        private List<TransferItem> _itemsToAdd = new();
        private List<TransferItem> _itemsToUpdate = new();
        private List<int> _itemIdsToRemove = new();
        private List<string> _stagedDeleteDescriptions = new();
        private decimal _totalCost = 0;
        // Add this near other private fields (e.g. after _totalCost)
        private TransferSnapshot? _originalSnapshot;
        private DateTimeOffset? _pendingUsageDate = null;
        // replaced _pendingLocation with Outlet id
        private int? _pendingOutletId = null;
        private bool _hasUserModifiedOutlet = false;
        // Add this near other private fields (e.g. after _totalCost)
        private int _nextTempId = -1;
        private List<Requisition.Models.Outlet> _outlets = new();
        // new field to suppress programmatic ValueChanged handling
        private bool _suppressExpectedPeopleValueChanged = false;
        private List<Kitchen> _kitchens = new();
        private int? _pendingKitchenId = null;
        private bool _hasUserModifiedKitchen = false;
        private bool _hasUserModifiedActualPeople = false;
        private bool _hasUserModifiedHiddenCost = false; // ← new flag
        private async Task LoadOutletsAsync()
        {
            try
            {
                OutletCombo.Items.Clear();

                // ใช้ wrapper ปลอดภัยแล้วกรองเฉพาะ outlet ที่ active เท่านั้น
                _outlets = await _costPer_head_safe_call_GetAllAsync();
                if (_outlets == null) _outlets = new List<Requisition.Models.Outlet>();

                // กรองเฉพาะรายการที่ IsActive == true
                var activeOutlets = _outlets.Where(o => o != null && o.IsActive).ToList();

                foreach (var o in activeOutlets)
                {
                    OutletCombo.Items.Add(new ComboBoxItem { Content = o.Name ?? $"#{o.Id}", Tag = o.Id });
                }

                System.Diagnostics.Debug.WriteLine($"LoadOutletsAsync: loaded {activeOutlets.Count} active outlets (total fetched: {_outlets.Count})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadOutletsAsync failed: {ex.Message}");
                _outlets = new List<Requisition.Models.Outlet>();
            }
        }

        // Helper wrapper in case the service call throws (keeps pattern consistent)
        private async Task<List<Requisition.Models.Outlet>> _costPer_head_safe_call_GetAllAsync()
        {
            try
            {
                return await _costPerHeadService.GetAllAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetAllAsync failed: {ex.Message}");
                return new List<Requisition.Models.Outlet>();
            }
        }

        private void UsageDatePicker_DateChanged(object sender, DatePickerValueChangedEventArgs e)
        {
            if (_currentTransfer != null && UsageDatePicker?.Date != null)
            {
                _pendingUsageDate = UsageDatePicker.Date;
                _hasUserModifiedDate = true;
                System.Diagnostics.Debug.WriteLine($"📅 User changed date: {_pendingUsageDate}");
                MarkAsChanged();
            }
            else
            {
                _pendingOutletId = null;
                // Border doesn't have IsEnabled — use hit-testing + opacity for visual/interaction state
                AvailableProductsPanel.IsHitTestVisible = false;
                AvailableProductsPanel.Opacity = 0.5;
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is int transferId)
            {
                _transferId = transferId;
                await LoadTransferAsync();
            }
            else if (e.Parameter is TransferDetailPageParameter param)
            {
                _transferId = param.TransferId;
                // param.IsReadOnly will be ignored except for Completed status.
                await LoadTransferAsync();
            }
        }

        private async void ManageTransferDetailPage_Loaded(object sender, RoutedEventArgs e)
        {
            // OnNavigatedTo already handles load
        }

        private async Task LoadTransferAsync()
        {
            try
            {
                ShowLoading(true);

                var preservedItemsToAdd = new List<TransferItem>(_itemsToAdd);

                _currentTransfer = await _transfer_service_safe_call_GetTransfer(_transferId);

                if (_currentTransfer == null)
                {
                    await ShowErrorDialog("ผิดพลาด", "ไม่พบข้อมูลที่ร้องขอ");
                    Frame.GoBack();
                    return;
                }

                // Load outlets first so selection can be applied
                await LoadOutletsAsync();
                await LoadKitchensAsync();
                await LoadCurrentPricesAsync();

                foreach (var newItem in preservedItemsToAdd)
                {
                    if (!_currentTransfer.Items.Any(i => i.ProductCode == newItem.ProductCode))
                    {
                        _currentTransfer.Items.Add(newItem);
                    }
                }

                _itemsToAdd = preservedItemsToAdd;

                _totalCost = CalculateTotalCost();

                // _isReadOnly applies only when transfer is Completed
                _isReadOnly = _currentTransfer.Status == TransferStatus.Completed;

                UpdateUI();
                await LoadHistoryAsync();
                await LoadAvailableProductsAsync();

                // Capture original state for comparison
                _originalSnapshot = TransferSnapshot.From(_currentTransfer);
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("เกิดข้อผิดพลาด", $"ข้อผิดพลาด: {ex.Message}");
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private async Task<Models.Transfer?> _transfer_service_safe_call_GetTransfer(int transferId)
        {
            try
            {
                return await _transferService.GetTransferByIdAsync(transferId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetTransferByIdAsync failed: {ex.Message}");
                return null;
            }
        }

        private async Task LoadCurrentPricesAsync()
        {
            if (_currentTransfer == null || _currentTransfer.Items.Count == 0) return;

            foreach (var item in _currentTransfer.Items)
            {
                if (item.UnitPrice.HasValue)
                {
                    continue;
                }

                var product = await _productService.GetProductByCodeAsync(item.ProductCode);
                if (product?.Price != null)
                {
                    item.CurrentPrice = product.Price.Value;
                }
            }
        }

        private decimal CalculateTotalCost()
        {
            if (_currentTransfer == null || _currentTransfer.Items.Count == 0)
                return 0;

            return _currentTransfer.Items.Sum(item => item.TotalCost);
        }

        private void UpdateUI(bool skipExpectedPeopleUpdate = false)  // ⬅️ เพิ่ม parameter
        {
            if (_currentTransfer == null) return;

            System.Diagnostics.Debug.WriteLine($"🔄 UpdateUI called (skipExpectedPeopleUpdate={skipExpectedPeopleUpdate})");
            System.Diagnostics.Debug.WriteLine($"  - _hasUserModifiedExpectedPeople: {_hasUserModifiedExpectedPeople}");
            System.Diagnostics.Debug.WriteLine($"  - _currentTransfer.ExpectedPeople: {_currentTransfer.ExpectedPeople}");

            if (_currentTransfer.IsDeleted)
            {
                TitleText.Text = "รายละเอียดใบTransfer (ถูกลบ)";
                StatusText.Text = "ถูกลบ";
                StatusBadge.Background = new SolidColorBrush(Color.FromArgb(255, 196, 43, 28));
                NotesTextBox.Header = "หมายเหตุ (ใบTransfer นี้ถูกลบแล้ว)";
                NotesTextBox.IsReadOnly = true;
                DeleteButton.Visibility = Visibility.Collapsed;
                SaveButton.Visibility = Visibility.Collapsed;
                AvailableProductsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            TransferNoText.Text = _currentTransfer.TransferNo;
            StatusText.Text = _currentTransfer.StatusText;
            UpdateStatusBadge(_currentTransfer.Status);

            CreatedDateText.Text = _currentTransfer.CreatedDate.ToString("dd/MM/yyyy HH:mm");
            CreatedByText.Text = _currentTransfer.CreatedBy ?? "ไม่ทราบผู้สร้าง";

            UsageDatePicker.DateChanged -= UsageDatePicker_DateChanged!;
            OutletCombo.SelectionChanged -= OutletCombo_SelectionChanged;
            // ✅ เพิ่มหลัง OutletCombo.SelectionChanged += ...
            KitchenCombo.SelectionChanged += KitchenCombo_SelectionChanged;
            try
            {
                if (_hasUserModifiedDate && _pendingUsageDate.HasValue)
                {
                    UsageDatePicker.Date = _pendingUsageDate.Value;
                }
                else if (_currentTransfer.UsageDate.HasValue)
                {
                    UsageDatePicker.Date = _currentTransfer.UsageDate.Value;
                    _pendingUsageDate = _currentTransfer.UsageDate.Value;
                }
                else
                {
                    UsageDatePicker.Date = DateTimeOffset.Now;
                    _pendingUsageDate = DateTimeOffset.Now;
                }

                // set Outlet selection
                int? targetOutletId = _hasUserModifiedOutlet ? _pendingOutletId : _currentTransfer.OutletId;

                if (targetOutletId.HasValue)
                {
                    bool found = false;
                    foreach (var item in OutletCombo.Items)
                    {
                        if (item is ComboBoxItem comboItem &&
                            comboItem.Tag is int tag &&
                            tag == targetOutletId.Value)
                        {
                            OutletCombo.SelectedItem = comboItem;
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        OutletCombo.SelectedIndex = -1;
                    }
                }
                else
                {
                    OutletCombo.SelectedIndex = -1;
                }

                // ✅ เพิ่มส่วนนี้: Set KitchenCombo selection
                int? targetKitchenId = _hasUserModifiedKitchen ? _pendingKitchenId : _currentTransfer.KitchenId;

                if (targetKitchenId.HasValue)
                {
                    bool found = false;
                    foreach (var item in KitchenCombo.Items)
                    {
                        if (item is ComboBoxItem comboItem &&
                            comboItem.Tag is int tag &&
                            tag == targetKitchenId.Value)
                        {
                            KitchenCombo.SelectedItem = comboItem;
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        KitchenCombo.SelectedIndex = -1;
                    }
                }
                else
                {
                    KitchenCombo.SelectedIndex = -1;
                }
            }
            finally
            {
                UsageDatePicker.DateChanged += UsageDatePicker_DateChanged!;
                OutletCombo.SelectionChanged += OutletCombo_SelectionChanged;
            }

            // Set NumberBox for expected people (read/write)
            if (!skipExpectedPeopleUpdate)
            {
                ExpectedPeopleBox.Value = _currentTransfer.ExpectedPeople;
            }
            
            // ✅ เพิ่ม: Set HiddenCostPercentageBox โดยตรง (เหมือน ExpectedPeopleBox)
            if (!_hasUserModifiedHiddenCost)
            {
                _suppressHiddenCostValueChanged = true;
                HiddenCostPercentageBox.Value = (double)(_currentTransfer.HiddenCostPercentage ?? 0m);
                _suppressHiddenCostValueChanged = false;
            }
            
            UpdateBudgetDisplay();
            // Ensure hidden-cost UI updates when UI refreshes
            UpdateHiddenCostDisplay();

            ItemCountText.Text = _currentTransfer.ItemCount.ToString();
            TotalQuantityText.Text = _currentTransfer.TotalQuantity.ToString("N4");

            // Enable/disable available-products panel based on Outlet selection and read-only state
            bool outletSelected = _currentTransfer.OutletId.HasValue;
            bool panelEnabled = outletSelected && !_isReadOnly && !_currentTransfer.IsDeleted;
            AvailableProductsPanel.IsHitTestVisible = panelEnabled;
            AvailableProductsPanel.Opacity = panelEnabled ? 1.0 : 0.5;

            // Status UI handling (replace existing Completed / InProgress / other block)
            if (_currentTransfer.Status == TransferStatus.Completed)
            {
                // Completed info
                CompletedInfoCard.Visibility = Visibility.Visible;
                CompletedDateText.Text = _currentTransfer.CompletedDate?.ToString("dd/MM/yyyy HH:mm") ?? "N/A";
                TotalReturnedText.Text = $"คืนรวม: {_currentTransfer.TotalReturnedQuantity?.ToString("N4") ?? "0"} หน่วย";

                // Show Actual people NumberBox and allow editing
                ActualPeopleInfoBorder.Visibility = Visibility.Visible;
                ActualPeopleBox.Value = (double)(_currentTransfer.ActualPeople ?? _currentTransfer.ExpectedPeople);
                ActualPeopleBox.IsEnabled = true;

                // Show Actual cost card and compute
                ActualCostPerPersonCard.Visibility = Visibility.Visible;
                UpdateActualCostDisplay();

                // Show actual-hidden-cost cards
                UpdateActualHiddenCostDisplay();

                ActualCostPerPersonHint.Visibility = Visibility.Collapsed;
            }
            else if (_currentTransfer.Status == TransferStatus.InProgress)
            {
                // Not completed yet — hide completed-only header
                CompletedInfoCard.Visibility = Visibility.Collapsed;

                // Show Actual people panel so manager can preview / adjust
                ActualPeopleInfoBorder.Visibility = Visibility.Visible;
                // If ActualPeople recorded use it; otherwise fall back to ExpectedPeople so calculations are visible
                ActualPeopleBox.Value = (double)(_currentTransfer.ActualPeople ?? _currentTransfer.ExpectedPeople);
                // Allow editing only when page allows edits
                ActualPeopleBox.IsEnabled = IsManageEditable;

                // Show Actual cost card so user can preview cost-per-actual-attendee while in-progress
                ActualCostPerPersonCard.Visibility = Visibility.Visible;
                UpdateActualCostDisplay();

                // Also show actual-hidden-cost cards (they will hide if ActualPeople == 0)
                UpdateActualHiddenCostDisplay();

                ActualCostPerPersonHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Other statuses (Draft etc.)
                CompletedInfoCard.Visibility = Visibility.Collapsed;
                ActualPeopleInfoBorder.Visibility = Visibility.Collapsed;
                ActualPeopleBox.Value = 0;

                // Hide Actual cost card in other statuses
                ActualCostPerPersonCard.Visibility = Visibility.Collapsed;
                ActualCostPerPersonText.Text = string.Empty;
                ActualCostPerPersonHint.Visibility = Visibility.Collapsed;
            }

            if (_currentTransfer.Items.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"🔄 Refreshing ListView: {_currentTransfer.Items.Count} items");

                ItemsListView.ItemsSource = null;
                ItemsListView.Items.Clear();
                ItemsListView.InvalidateMeasure();
                ItemsListView.InvalidateArrange();
                ItemsListView.UpdateLayout();

                var freshList = new System.Collections.ObjectModel.ObservableCollection<TransferItem>();
                foreach (var item in _currentTransfer.Items)
                {
                    freshList.Add(item);
                    System.Diagnostics.Debug.WriteLine($"  ✅ Added: {item.ProductCode} (ID={item.Id})");
                }

                ItemsListView.ItemsSource = freshList;
                ItemsListView.Visibility = Visibility.Visible;
                EmptyItemsPanel.Visibility = Visibility.Collapsed;

                System.Diagnostics.Debug.WriteLine($"✅ ItemsListView bound to {freshList.Count} items");

                _ = Task.Run(async () =>
                {
                    await Task.Delay(200);
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        UpdateButtonVisibility();
                    });
                });
            }
            else
            {
                ItemsListView.ItemsSource = null;
                ItemsListView.Items.Clear();
                ItemsListView.Visibility = Visibility.Collapsed;
                EmptyItemsPanel.Visibility = Visibility.Visible;

                UpdateButtonVisibility();
            }

            NotesTextBox.Text = _currentTransfer.Notes ?? "";

            System.Diagnostics.Debug.WriteLine($"✅ UpdateUI completed");
        }
        private async Task LoadKitchensAsync()
        {
            try
            {
                KitchenCombo.Items.Clear();

                var kitchenService = new KitchenService();
                _kitchens = await kitchenService.GetAllAsync() ?? new List<Kitchen>();

                // กรองเฉพาะ kitchen ที่ยัง active (IsActive == true)
                var activeKitchens = _kitchens.Where(k => k != null && k.IsActive).ToList();

                foreach (var k in activeKitchens)
                {
                    KitchenCombo.Items.Add(new ComboBoxItem { Content = k.Name ?? $"#{k.Id}", Tag = k.Id });
                }

                System.Diagnostics.Debug.WriteLine($"LoadKitchensAsync: loaded {activeKitchens.Count} active kitchens (total fetched: {_kitchens.Count})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadKitchensAsync failed: {ex.Message}");
                _kitchens = new List<Kitchen>();
            }
        }
        private void OutletCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentTransfer != null && OutletCombo.SelectedItem is ComboBoxItem item && item.Tag is int oid)
            {
                _pendingOutletId = oid;
                _hasUserModifiedOutlet = true;
                System.Diagnostics.Debug.WriteLine($"📍 User changed outlet: {_pendingOutletId}");
                MarkAsChanged();

                // Border: enable interaction via hit-testing and restore opacity
                AvailableProductsPanel.IsHitTestVisible = true;
                AvailableProductsPanel.Opacity = 1.0;
            }
            else
            {
                _pendingOutletId = null;
                AvailableProductsPanel.IsHitTestVisible = false;
                AvailableProductsPanel.Opacity = 0.5;
            }
            int? targetKitchenId = _hasUserModifiedKitchen ? _pendingKitchenId : _currentTransfer!.KitchenId;
            if (KitchenCombo != null && targetKitchenId.HasValue)
            {
                var kitchenItem = KitchenCombo.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(i => i.Tag is int kid && kid == targetKitchenId.Value);
                if (kitchenItem != null)
                {
                    KitchenCombo.SelectedItem = kitchenItem;
                }
            }
        }
        private void UpdateBudgetDisplay()
        {
            if (_currentTransfer == null) return;

            // คำนวณต้นทุนต่อคนจากรายการปัจจุบัน
            decimal computedCostPerPerson = (_currentTransfer.ExpectedPeople > 0 && _totalCost > 0)
                ? Math.Round(_totalCost / _currentTransfer.ExpectedPeople, 4, MidpointRounding.AwayFromZero) // ✅ แก้ไข
                : 0m;
            decimal pct = _currentTransfer.HiddenCostPercentage ?? 0m;
            decimal hiddenAmount = Math.Round(computedCostPerPerson * (pct / 100m), 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข

            // ⚠️ ใช้ targetOutletId (รองรับการเปลี่ยนOutletก่อนบันทึก)
            int? targetOutletId = _hasUserModifiedOutlet ? _pendingOutletId : _currentTransfer.OutletId;

            decimal? outletPricePerHead = null;
            DateTime? outletPriceModifiedDate = null;

            // ⚠️ สำคัญ: ใช้ OutletPricePerHeadAtSave เมื่อสถานะเป็น InProgress/Completed
            if (_currentTransfer != null)
            {
                bool preferOutletSnapshot = (_currentTransfer.Status == TransferStatus.InProgress || _currentTransfer.Status == TransferStatus.Completed)
                                     && _currentTransfer.OutletPricePerHeadAtSave.HasValue;

                if (preferOutletSnapshot)
                {
                    // ใช้ snapshot ที่บันทึกไว้ (ไม่เปลี่ยนแปลงตามราคาปัจจุบัน)
                    outletPricePerHead = _currentTransfer.OutletPricePerHeadAtSave;
                    outletPriceModifiedDate = _currentTransfer.OutletPricePerHeadSavedAt;
                }
                else
                {
                    // สถานะ Draft: ดึงราคาปัจจุบันจากร้านค้า
                    if (targetOutletId.HasValue && _outlets != null && _outlets.Count > 0)
                    {
                        var o = _outlets.FirstOrDefault(x => x.Id == targetOutletId.Value);
                        if (o != null)
                        {
                            outletPricePerHead = o.PricePerHead;
                            outletPriceModifiedDate = o.ModifiedDate;
                        }
                    }
                }
            }

            decimal displayCostPerPerson = computedCostPerPerson;

            // แสดงราคาต่อคนปัจจุบัน
            BudgetUsageText.Text = $"{displayCostPerPerson:N4} ฿/คน";

            // แสดงราคาต่อหัวของร้านค้า (หรือ snapshot)
            if (outletPricePerHead.HasValue)
            {
                BudgetTargetText.Text = $"งบต่อคน : {outletPricePerHead.Value:N4} ฿";
                BudgetTargetText.Visibility = Visibility.Visible;
            }
            else
            {
                BudgetTargetText.Text = string.Empty;
                BudgetTargetText.Visibility = Visibility.Collapsed;
            }

            // แสดงที่มาของราคา (กรณีมี snapshot หรือราคาปัจจุบัน)
            if (outletPricePerHead.HasValue && OutletPriceInfoText != null)
            {
                var dateText = outletPriceModifiedDate.HasValue
                    ? outletPriceModifiedDate.Value.ToLocalTime().ToString("dd/MM/yyyy")
                    : "ไม่ระบุวันที่";

                bool preferOutletSnapshot = (_currentTransfer!.Status == TransferStatus.InProgress || _currentTransfer.Status == TransferStatus.Completed)
                                     && _currentTransfer.OutletPricePerHeadAtSave.HasValue;

                var sourceText = preferOutletSnapshot
                    ? $"📌 ราคาที่บันทึกไว้: {outletPricePerHead.Value:N4} ฿/คน (บันทึกวันที่ {dateText})"
                    : $"ราคาของOutlet: {outletPricePerHead.Value:N4} ฿/คน (อัปเดต {dateText})";

                OutletPriceInfoText.Text = sourceText;
                OutletPriceInfoText.Visibility = Visibility.Visible;
            }
            else if (OutletPriceInfoText != null)
            {
                OutletPriceInfoText.Text = string.Empty;
                OutletPriceInfoText.Visibility = Visibility.Collapsed;
            }

            // เปรียบเทียบและเปลี่ยนสี
            if (outletPricePerHead.HasValue)
            {
                if (displayCostPerPerson > outletPricePerHead.Value)
                {
                    // เกินงบ - สีแดง
                    BudgetCard.Background = new SolidColorBrush(Color.FromArgb(40, 220, 38, 28));
                    BudgetCard.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));
                    BudgetUsageText.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));
                    BudgetTargetText.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));

                    BudgetWarningText!.Text = $"⚠️ เกินงบต่อคน: {outletPricePerHead.Value:N4} ฿/คน";
                    BudgetWarningText.Visibility = Visibility.Visible;
                }
                else
                {
                    // ไม่เกินงบ - สีเขียว
                    BudgetCard.Background = new SolidColorBrush(Color.FromArgb(40, 34, 197, 94));
                    BudgetCard.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                    BudgetUsageText.Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                    BudgetTargetText.Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));

                    BudgetWarningText!.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                // ไม่มีราคาOutlet - ใช้สีตามจำนวนเงิน
                BudgetWarningText!.Visibility = Visibility.Collapsed;

                if (displayCostPerPerson >= 500)
                {
                    BudgetCard.Background = new SolidColorBrush(Color.FromArgb(40, 220, 38, 28));
                    BudgetCard.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));
                    BudgetUsageText.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));
                }
                else if (displayCostPerPerson >= 300)
                {
                    BudgetCard.Background = new SolidColorBrush(Color.FromArgb(40, 234, 179, 8));
                    BudgetCard.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 234, 179, 8));
                    BudgetUsageText.Foreground = new SolidColorBrush(Color.FromArgb(255, 234, 179, 8));
                }
                else
                {
                    BudgetCard.Background = new SolidColorBrush(Color.FromArgb(40, 34, 197, 94));
                    BudgetCard.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                    BudgetUsageText.Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                }

                BudgetTargetText.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateStatusBadge(TransferStatus status)
        {
            var color = status switch
            {
                TransferStatus.Draft => Color.FromArgb(255, 156, 163, 175),
                TransferStatus.InProgress => Color.FromArgb(255, 59, 130, 246),
                TransferStatus.Completed => Color.FromArgb(255, 16, 185, 129),
                _ => Colors.Gray
            };

            StatusBadge.Background = new SolidColorBrush(color);
        }

        // Add this DependencyProperty inside the page class (near other fields)
        public bool IsManageEditable
        {
            get => (bool)GetValue(IsManageEditableProperty);
            set => SetValue(IsManageEditableProperty, value);
        }

        public static readonly DependencyProperty IsManageEditableProperty =
            DependencyProperty.Register(
                nameof(IsManageEditable),
                typeof(bool),
                typeof(ManageTransferDetailPage),
                new PropertyMetadata(false));

        private void UpdateButtonVisibility()
        {
            if (_currentTransfer == null) return;

            // Manage page: allow edits in Draft and InProgress (managers can edit InProgress)
            bool canEdit = (_currentTransfer.Status == TransferStatus.Draft
                            || _currentTransfer.Status == TransferStatus.InProgress)
                           && !_isReadOnly;
            bool isInProgress = _currentTransfer.Status == TransferStatus.InProgress;
            bool isCompleted = _currentTransfer.Status == TransferStatus.Completed;

            if (ItemsListView != null)
            {
                ItemsListView.MinWidth = (isInProgress || isCompleted) ? 1400 : 1200;
            }

            System.Diagnostics.Debug.WriteLine($"📍 UpdateButtonVisibility: Status={_currentTransfer.Status}, CanEdit={canEdit}");

            // expose to XAML bindings (DataTemplate uses this)
            IsManageEditable = canEdit;

            // Update header / page-level controls
            if (ActionHeaderText != null)
            {
                ActionHeaderText.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
            }

            // Available products behavior: hide only for Completed or Deleted
            if (_currentTransfer.Status == TransferStatus.Completed || _currentTransfer.IsDeleted)
            {
                AvailableProductsPanel.Visibility = Visibility.Collapsed;
                var grid = AvailableProductsPanel.Parent as Grid;
                if (grid != null && grid.ColumnDefinitions.Count >= 2)
                {
                    grid.ColumnDefinitions[0].Width = new GridLength(0);
                    grid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                }
            }
            else
            {
                AvailableProductsPanel.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;

                var grid = AvailableProductsPanel.Parent as Grid;
                if (grid != null && grid.ColumnDefinitions.Count >= 2)
                {
                    if (canEdit)
                    {
                        grid.ColumnDefinitions[0].Width = new GridLength(480);
                        grid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                    }
                    else
                    {
                        grid.ColumnDefinitions[0].Width = new GridLength(0);
                        grid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                    }
                }

                AvailableProductsListView.IsEnabled = canEdit;
                ProductSearchBox.IsEnabled = canEdit;
                UsageDatePicker.IsEnabled = canEdit;
                OutletCombo.IsEnabled = canEdit;
                NotesTextBox.IsReadOnly = !canEdit;
            }

            // Delete page button
            DeleteButton.Visibility = ((_currentTransfer.Status == TransferStatus.Draft
                                      || _currentTransfer.Status == TransferStatus.InProgress)
                                      && !_isReadOnly)
                ? Visibility.Visible : Visibility.Collapsed;

            CompleteButton.Visibility = Visibility.Collapsed;

            // ✅ ใน UpdateButtonVisibility หาส่วนที่ disable OutletCombo
            //bool canEditDateAndOutlet = _currentTransfer.Status == TransferStatus.Draft && !_isReadOnly;
            //if (UsageDatePicker != null) 
            //    UsageDatePicker.IsEnabled = canEditDateAndOutlet;
            //if (OutletCombo != null) 
            //    OutletCombo.IsEnabled = canEditDateAndOutlet;
            //if (KitchenCombo != null)  // ✅ เพิ่มบรรทัดนี้
            //    KitchenCombo.IsEnabled = canEditDateAndOutlet;
        }

        private void MarkAsChanged()
        {
            _hasPendingChanges = _itemsToAdd.Count > 0 || _itemsToUpdate.Count > 0 || _itemIdsToRemove.Count > 0;
            UpdateButtonVisibility();

            System.Diagnostics.Debug.WriteLine($"📝 Pending changes: {_hasPendingChanges} (New: {_itemsToAdd.Count}, Updated: {_itemsToUpdate.Count}, Removed: {_itemIdsToRemove.Count})");
        }

        private async Task LoadHistoryAsync()
        {
            try
            {
                var history = await _requisition_service_safe_call_GetHistory(_transferId);
                if (history.Count > 0)
                {
                    // Compute Comparison on each RequisitionHistory instance, then bind the list directly.
                    foreach (var h in history)
                    {
                        try
                        {
                            h.Comparison = BuildComparison(h);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to build comparison for history id={h.Id}: {ex.Message}");
                            h.Comparison = null;
                        }
                    }

                    HistoryExpander.Visibility = Visibility.Visible;
                    HistoryListView.ItemsSource = history;
                }
                else
                {
                    HistoryExpander.Visibility = Visibility.Collapsed;
                    HistoryListView.ItemsSource = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load history: {ex.Message}");
            }
        }

        private async Task<List<TransferHistory>> _requisition_service_safe_call_GetHistory(int requisitionId)
        {
            try
            {
                return await _transferService.GetTransferHistoryAsync(requisitionId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetTransferHistoryAsync failed: {ex.Message}");
                return new List<TransferHistory>();
            }
        }

        private async Task LoadAvailableProductsAsync()
        {
            try
            {
                _availableProducts = await _productService.GetAllProductsAsync();
                UpdateAvailableProductsDisplay();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load products: {ex.Message}");
            }
        }

        private void UpdateAvailableProductsDisplay()
        {
            if (_currentTransfer == null)
            {
                // filter out disabled/inactive
                var available = _availableProducts
                    .Where(p => p.IsActive && !p.IsCurrentlyDisabled)
                    .ToList();

                AvailableProductsListView.ItemsSource = available.Select(p => new ProductDisplayItem
                {
                    Product = p,
                    IsAlreadySelected = false
                }).ToList();

                AvailableProductsCountText.Text = $"รายการสินค้า: {available.Count}";
                return;
            }

            var selectedCodes = new HashSet<string>(
                _currentTransfer.Items.Select(i => i.ProductCode),
                StringComparer.OrdinalIgnoreCase
            );

            var displayProducts = _availableProducts
                .Where(p => p.IsActive && !p.IsCurrentlyDisabled)        // <- exclude disabled/inactive
                .Select(p => new ProductDisplayItem
                {
                    Product = p,
                    IsAlreadySelected = selectedCodes.Contains(p.Code ?? string.Empty)
                })
                .ToList();

            AvailableProductsListView.ItemsSource = displayProducts;
            AvailableProductsCountText.Text = $"รายการสินค้า: {displayProducts.Count}";
        }

        private class ProductDisplayItem
        {
            public Product Product { get; set; } = null!;
            public bool IsAlreadySelected { get; set; }
            public bool IsNotAlreadySelected => !IsAlreadySelected;
        }

        #region Add from left pane

        private void ProductSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

            var q = sender.Text?.Trim();

            List<Product> filtered;
            if (string.IsNullOrEmpty(q))
            {
                filtered = _availableProducts;
            }
            else
            {
                filtered = _available_products_fallback(q);
            }

            var selectedCodes = new HashSet<string>(
                _currentTransfer?.Items.Select(i => i.ProductCode) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase
            );

            var displayItems = filtered.Select(p => new ProductDisplayItem
            {
                Product = p,
                IsAlreadySelected = selectedCodes.Contains(p.Code ?? string.Empty)
            }).ToList();

            AvailableProductsListView.ItemsSource = displayItems;
        }

        // small helper used above to keep code compact
        private List<Product> _available_products_fallback(string q)
        {
            return _availableProducts
                .Where(p => p.IsActive && !p.IsCurrentlyDisabled
                            && ((p.Code ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase)
                                || (p.Name ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        private async void AddProductButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Product product) return;

            if (_currentTransfer?.Items.Any(i => string.Equals(i.ProductCode, product.Code, StringComparison.OrdinalIgnoreCase)) == true)
            {
                await ShowErrorDialog("ข้อผิดพลาด", "สินค้าตัวนี้มีในใบTransferแล้ว");
                return;
            }

            var qtyBox = new NumberBox
            {
                Header = "จำนวนเริ่มต้น",
                Value = 1,
                Minimum = 0.01,
                SmallChange = 1,
                LargeChange = 10,
                MinWidth = 200
            };

            var dialog = new ContentDialog
            {
                Title = $"เพิ่มสินค้า: {product.Name}",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{product.Name} ({product.Code})",
                            FontWeight = FontWeights.SemiBold
                        },
                        new TextBlock
                        {
                            Text = $"💰 ราคาปัจจุบัน: {product.Price?.ToString("N4") ?? "ไม่ระบุ"} ฿",
                            FontSize = 13,
                            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange)
                        },
                        qtyBox
                    }
                },
                PrimaryButtonText = "เพิ่ม",
                CloseButtonText = "ยกเลิก",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary && qtyBox.Value > 0)
            {
                int tempId = _nextTempId--;

                var newItem = new TransferItem
                {
                    Id = tempId,
                    TransferId = _transferId,
                    ProductCode = product.Code,
                    ProductName = product.Name,
                    InitialQuantity = (decimal)qtyBox.Value,
                    Unit = product.Unit,
                    UnitPrice = product.Price,
                    PriceDate = DateTime.Now,
                    Notes = null
                };

                _itemsToAdd.Add(newItem);
                _currentTransfer?.Items.Add(newItem);

                _totalCost = CalculateTotalCost();

                System.Diagnostics.Debug.WriteLine($"✅ Added new item: {product.Code} (ID={tempId})");

                MarkAsChanged();
                UpdateUI();
                UpdateAvailableProductsDisplay();
            }
        }

        private async void EditItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn)
            {
                System.Diagnostics.Debug.WriteLine("EditItemButton_Click: sender is not Button");
                return;
            }

            var item = btn.Tag as TransferItem ?? btn.DataContext as TransferItem;
            if (item == null)
            {
                System.Diagnostics.Debug.WriteLine("EditItemButton_Click: DataContext/Tag is null or not RequisitionItem");
                await ShowErrorDialog("ข้อผิดพลาด", "ไม่พบข้อมูลรายการ (DataContext)");
                return;
            }

            if (_currentTransfer == null)
            {
                await ShowErrorDialog("ข้อผิดพลาด", "ไม่พบข้อมูลใบTransfer");
                return;
            }

            bool allowEdit = (_currentTransfer.Status == TransferStatus.Draft
                             || _currentTransfer.Status == TransferStatus.InProgress)
                             && !_isReadOnly;

            if (!allowEdit)
            {
                await ShowErrorDialog("ข้อผิดพลาด", "ไม่สามารถแก้ไขรายการได้");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"🔧 Edit item ID={item.Id} ({item.ProductCode})");

            var qtyBox = new NumberBox
            {
                Header = "จำนวน",
                Value = (double)item.InitialQuantity,
                Minimum = 0.01,
                SmallChange = 1,
                LargeChange = 10
            };

            var dialog = new ContentDialog
            {
                Title = "แก้ไขจำนวน",
                PrimaryButtonText = "บันทึก",
                CloseButtonText = "ยกเลิก",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
            {
                new TextBlock { Text = $"{item.ProductName} ({item.ProductCode})", FontWeight = FontWeights.SemiBold },
                new TextBlock {
                    Text = $"💰 ต้นทุน/หน่วย: {item.UnitPrice?.ToString("N4") ?? "ไม่ระบุ"} ฿",  // ✅ เปลี่ยนจาก CurrentPrice → UnitPrice
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange)
                },
                qtyBox
            }
                }
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            if (qtyBox.Value <= 0)
            {
                await ShowErrorDialog("ข้อผิดพลาด", "จำนวนต้องมากกว่า 0");
                return;
            }

            try
            {
                decimal newQuantity = (decimal)qtyBox.Value;

                if (item.Id > 0)
                {
                    // Persisted item -> stage update locally
                    var staged = _itemsToUpdate.FirstOrDefault(i => i.Id == item.Id);
                    if (staged == null)
                    {
                        // add a shallow copy to track change
                        staged = new TransferItem
                        {
                            Id = item.Id,
                            TransferId = item.TransferId,
                            ProductCode = item.ProductCode,
                            ProductName = item.ProductName,
                            InitialQuantity = newQuantity,
                            Unit = item.Unit,
                            UnitPrice = item.UnitPrice,
                            PriceDate = item.PriceDate,
                            Notes = item.Notes
                        };
                        _itemsToUpdate.Add(staged);
                    }
                    else
                    {
                        staged.InitialQuantity = newQuantity;
                    }

                    // update UI model
                    var target = _currentTransfer.Items.FirstOrDefault(i => i.Id == item.Id);
                    if (target != null)
                    {
                        target.InitialQuantity = newQuantity;
                    }

                    _totalCost = CalculateTotalCost();
                    UpdateUI();

                    MarkAsChanged();

                    await ShowSuccessDialog("สำเร็จ", "แก้ไขรายการ");
                    System.Diagnostics.Debug.WriteLine($"✅ Staged update for item ID={item.Id}");
                }
                else
                {
                    // Local new item -> update in-place
                    item.InitialQuantity = newQuantity;
                    var existing = _itemsToAdd.FirstOrDefault(i => i.Id == item.Id);
                    if (existing != null) existing.InitialQuantity = newQuantity;
                    var itemInCurrent = _currentTransfer.Items.FirstOrDefault(i => i.Id == item.Id);
                    if (itemInCurrent != null) itemInCurrent.InitialQuantity = newQuantity;

                    _totalCost = CalculateTotalCost();
                    UpdateUI();

                    MarkAsChanged();

                    await ShowSuccessDialog("สำเร็จ", "แก้ไขรายการใหม่เรียบร้อยแล้ว");
                    System.Diagnostics.Debug.WriteLine($"✅ Updated local new item ID={item.Id}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EditItem error: {ex}");
                await ShowErrorDialog("เกิดข้อผิดพลาด", $"ไม่สามารถแก้ไขรายการได้: {ex.Message}");
            }
        }

        // Replace DeleteItemButton_Click with this (stage deletes locally)
        private async void DeleteItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn)
            {
                System.Diagnostics.Debug.WriteLine("DeleteItemButton_Click: sender is not Button");
                return;
            }

            var item = btn.Tag as TransferItem ?? btn.DataContext as TransferItem;
            if (item == null)
            {
                System.Diagnostics.Debug.WriteLine("DeleteItemButton_Click: DataContext/Tag is null or not TransferItem");
                await ShowErrorDialog("ข้อผิดพลาด", "ไม่พบข้อมูลรายการ (DataContext)");
                return;
            }

            if (_currentTransfer == null)
            {
                await ShowErrorDialog("ข้อผิดพลาด", "ไม่พบข้อมูลใบTransfer");
                return;
            }

            bool allowDelete = (_currentTransfer.Status == TransferStatus.Draft
                               || _currentTransfer.Status == TransferStatus.InProgress)
                               && !_isReadOnly;

            if (!allowDelete)
            {
                await ShowErrorDialog("ข้อผิดพลาด", "ไม่สามารถลบรายการได้");
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "ยืนยันการลบ",
                Content = $"ต้องการลบรายการ '{item.ProductName}' ใช่หรือไม่?",
                PrimaryButtonText = "ลบ",
                CloseButtonText = "ยกเลิก",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                if (item.Id > 0)
                {
                    // Stage persisted item for removal
                    if (!_itemIdsToRemove.Contains(item.Id))
                        _itemIdsToRemove.Add(item.Id);

                    // record summary for history
                    string delSummary = $"{item.ProductName} ({item.ProductCode}) จำนวน {item.InitialQuantity} {item.Unit}";
                    _stagedDeleteDescriptions.Add(delSummary);

                    // Remove any staged update for same item
                    _itemsToUpdate.RemoveAll(i => i.Id == item.Id);

                    // Remove from UI model
                    _currentTransfer.Items.RemoveAll(i => i.Id == item.Id);

                    _totalCost = CalculateTotalCost();
                    UpdateUI();
                    UpdateAvailableProductsDisplay();

                    MarkAsChanged();

                    await ShowSuccessDialog("สำเร็จ", "ลบรายการ");
                    System.Diagnostics.Debug.WriteLine($"✅ Staged delete for item ID={item.Id}");
                }
                else
                {
                    // Local new item -> remove immediately
                    _itemsToAdd.RemoveAll(i => i.Id == item.Id);
                    _currentTransfer.Items.RemoveAll(i => i.Id == item.Id);

                    _totalCost = CalculateTotalCost();
                    UpdateUI();
                    UpdateAvailableProductsDisplay();

                    MarkAsChanged();

                    await ShowSuccessDialog("สำเร็จ", $"ลบรายการ '{item.ProductName}' เรียบร้อยแล้ว");
                    System.Diagnostics.Debug.WriteLine($"✅ Removed local item ID={item.Id}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DeleteItem error: {ex}");
                await ShowErrorDialog("เกิดข้อผิดพลาด", $"ไม่สามารถลบรายการได้: {ex.Message}");
            }
        }

        #endregion

        #region Save & Delete

        // Replace SaveButton_Click with this updated implementation.
        // Key change: only update OutletPricePerHeadAtSave / OutletPricePerHeadSavedAt when the Outlet was actually changed.
        // If Outlet was not changed, keep the existing snapshot on the Transfer.

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTransfer == null) return;

            // ✅ เพิ่ม Debug log เพื่อตรวจสอบ
            System.Diagnostics.Debug.WriteLine($"💾 Save Check:");
            System.Diagnostics.Debug.WriteLine($"  - _itemsToAdd: {_itemsToAdd.Count}");
            System.Diagnostics.Debug.WriteLine($"  - _itemsToUpdate: {_itemsToUpdate.Count}");
            System.Diagnostics.Debug.WriteLine($"  - _itemIdsToRemove: {_itemIdsToRemove.Count}");
            System.Diagnostics.Debug.WriteLine($"  - _hasUserModifiedDate: {_hasUserModifiedDate}");
            System.Diagnostics.Debug.WriteLine($"  - _hasUserModifiedOutlet: {_hasUserModifiedOutlet}");
            System.Diagnostics.Debug.WriteLine($"  - _hasUserModifiedKitchen: {_hasUserModifiedKitchen}");
            System.Diagnostics.Debug.WriteLine($"  - _hasUserModifiedActualPeople: {_hasUserModifiedActualPeople}");
            System.Diagnostics.Debug.WriteLine($"  - _hasUserModifiedExpectedPeople: {_hasUserModifiedExpectedPeople}");
            System.Diagnostics.Debug.WriteLine($"  - ExpectedPeople: {_currentTransfer.ExpectedPeople} vs Box: {(int)ExpectedPeopleBox.Value}");
            System.Diagnostics.Debug.WriteLine($"  - Notes: '{_currentTransfer.Notes?.Trim() ?? ""}' vs TextBox: '{NotesTextBox.Text?.Trim() ?? ""}'");

            // ✅ เพิ่มการเช็คค่า HiddenCostPercentage
            decimal originalHiddenCost = _originalSnapshot?.HiddenCostPercentage ?? 0m;
            decimal currentHiddenCost = (decimal)HiddenCostPercentageBox.Value;
            bool hiddenCostChanged = originalHiddenCost != currentHiddenCost;

            bool hasChanges = _itemsToAdd.Count > 0 ||
                 _itemsToUpdate.Count > 0 ||
                 _itemIdsToRemove.Count > 0 ||
                 !string.Equals(_currentTransfer.Notes?.Trim(), NotesTextBox.Text?.Trim()) ||
                 _hasUserModifiedDate ||
                 _hasUserModifiedOutlet ||
                 _hasUserModifiedKitchen ||
                 _hasUserModifiedActualPeople ||
                 _hasUserModifiedExpectedPeople ||
                 hiddenCostChanged ||  // ← ใช้ตัวแปรที่เช็คค่าจริง
                 (_currentTransfer.ExpectedPeople != (int)ExpectedPeopleBox.Value);

            System.Diagnostics.Debug.WriteLine($"  - HiddenCost: {originalHiddenCost:N4} → {currentHiddenCost:N4} (changed: {hiddenCostChanged})");
            System.Diagnostics.Debug.WriteLine($"  ➡️ hasChanges: {hasChanges}");

            if (!hasChanges)
            {
                await ShowSuccessDialog("แจ้งเตือน", "ไม่มีการเปลี่ยนแปลงที่ต้องบันทึก");
                return;
            }

            if (_currentTransfer.Items.Count == 0 && _itemsToAdd.Count == 0)
            {
                await ShowErrorDialog("ไม่สามารถบันทึกได้", "ใบTransferต้องมีรายการสินค้าอย่างน้อย 1 รายการ");
                return;
            }

            // Require reason when there are changes
            string? saveReason = null;
            {
                var reasonBox = new TextBox
                {
                    Header = "เหตุผลสำหรับการเปลี่ยนแปลง (บังคับ)",
                    PlaceholderText = "เช่น: ปรับรายการ, แก้จำนวน, เพิ่ม/ลบสินค้า...",
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Height = 120,
                    MinWidth = 400
                };

                var validationText = new TextBlock
                {
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red),
                    FontSize = 13,
                    Visibility = Visibility.Collapsed,
                    TextWrapping = TextWrapping.Wrap
                };

                var dialog = new ContentDialog
                {
                    Title = "ยืนยันการบันทึก - ระบุเหตุผล",
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "คุณกำลังจะบันทึกการเปลี่ยนแปลง กรุณาระบุเหตุผลสำหรับการเปลี่ยนแปลงนี้",
                                FontWeight = FontWeights.SemiBold,
                                TextWrapping = TextWrapping.Wrap
                        },
                        validationText,
                        reasonBox
                    }
                },
                PrimaryButtonText = "ยืนยันบันทึก",
                CloseButtonText = "ยกเลิก",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            dialog.PrimaryButtonClick += (s, args) =>
            {
                var txt = reasonBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(txt))
                {
                    args.Cancel = true;
                    validationText.Text = "กรุณาระบุเหตุผลสำหรับบันทึกการเปลี่ยนแปลง";
                    validationText.Visibility = Visibility.Visible;
                }
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ User cancelled save (reason dialog)");
                return;
            }

            saveReason = reasonBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(saveReason))
            {
                await ShowErrorDialog("ข้อผิดพลาด", "กรุณาระบุเหตุผลสำหรับการบันทึก");
                return;
            }
        }

            try
            {
                ShowLoading(true);

                // 1) Deletes first
                if (_itemIdsToRemove.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"🧹 Removing {_itemIdsToRemove.Count} items (staged)");
                    bool delSuccess = await _transferService.RemoveMultipleItemsAsync(_transferId, _itemIdsToRemove, Environment.UserName);
                    if (!delSuccess)
                    {
                        ShowLoading(false);
                        await ShowErrorDialog("ล้มเหลว", "ไม่สามารถลบรายการที่เลือกได้");
                        return;
                    }
                    System.Diagnostics.Debug.WriteLine("✅ Removed staged items from DB");
                }

                // 2) Updates
                if (_itemsToUpdate.Count > 0)
                {
                    var updatesToSend = _itemsToUpdate.Where(i => i.Id > 0).ToList();
                    if (updatesToSend.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"✏️ Updating {updatesToSend.Count} items (staged)");
                        bool updSuccess = await _transferService.UpdateMultipleItemsAsync(_transferId, updatesToSend, Environment.UserName);
                        if (!updSuccess)
                        {
                            ShowLoading(false);
                            await ShowErrorDialog("ล้มเหลว", "ไม่สามารถอัปเดตรายการได้");
                            return;
                        }
                        System.Diagnostics.Debug.WriteLine("✅ Updated staged items in DB");
                    }
                }

                // 3) Adds
                if (_itemsToAdd.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"➕ Adding {_itemsToAdd.Count} new items");
                    bool addSuccess = await _transferService.AddMultipleItemsAsync(
                        _transferId,
                        _itemsToAdd,
                        Environment.UserName
                    );

                    if (!addSuccess)
                    {
                        ShowLoading(false);
                        await ShowErrorDialog("ล้มเหลว", "ไม่สามารถเพิ่มรายการได้");
                        return;
                    }

                    System.Diagnostics.Debug.WriteLine("✅ Added new items to DB");
                }

                // --- ensure totals are up to date
                _totalCost = CalculateTotalCost();

                // Decide final outlet id (respect pending selection)
                int? finalOutletId = _hasUserModifiedOutlet ? _pendingOutletId : _currentTransfer.OutletId;
                _currentTransfer.OutletId = finalOutletId;

                // ✅ เพิ่มบรรทัดนี้
                int? finalKitchenId = _hasUserModifiedKitchen ? _pendingKitchenId : _currentTransfer.KitchenId;
                _currentTransfer.KitchenId = finalKitchenId;

                // Determine whether outlet was actually changed vs original snapshot
                bool outletChanged;
                if (_originalSnapshot == null)
                {
                    // If no snapshot available, treat as changed only if user actively modified outlet
                    outletChanged = _hasUserModifiedOutlet;
                }
                else
                {
                    outletChanged = _originalSnapshot.OutletId != finalOutletId;
                }

                // Capture outlet price snapshot only if outletChanged == true.
                if (outletChanged)
                {
                    decimal? outletPriceSnapshot = null;
                    if (_currentTransfer.OutletId.HasValue && _outlets != null)
                    {
                        var o = _outlets.FirstOrDefault(x => x.Id == _currentTransfer.OutletId.Value);
                        if (o != null && o.PricePerHead.HasValue)
                        {
                            outletPriceSnapshot = o.PricePerHead.Value;
                        }
                    }

                    _currentTransfer.OutletPricePerHeadAtSave = outletPriceSnapshot;
                    _currentTransfer.OutletPricePerHeadSavedAt = outletPriceSnapshot.HasValue ? DateTime.UtcNow : null;

                    // Non-blocking history entry for snapshot change: use outlet name
                    try
                    {
                        var outletName = GetOutletName(_currentTransfer.OutletId);
                        var newValues = JsonSerializer.Serialize(new
                        {
                            OutletPricePerHeadAtSave = _currentTransfer.OutletPricePerHeadAtSave,
                            OutletId = _currentTransfer.OutletId,
                            OutletName = outletName
                        });

                        await _transferService.AddHistoryEntryAsync(
                            _currentTransfer.Id,
                            "OutletPriceSnapshot",
                            $"บันทึกราคาต่อหัวของOutlet: {(_currentTransfer.OutletPricePerHeadAtSave.HasValue ? _currentTransfer.OutletPricePerHeadAtSave.Value.ToString("N4") + " ฿" : "ไม่ระบุ")} ({outletName})",
                            Environment.UserName,
                            null,
                            newValues
                        );
                    }
                    catch (Exception hx)
                    {
                        System.Diagnostics.Debug.WriteLine($"AddHistoryEntryAsync failed: {hx.Message}");
                    }
                }
                else
                {
                    // Outlet not changed: keep existing OutletPricePerHeadAtSave / OutletPricePerHeadSavedAt unchanged
                    System.Diagnostics.Debug.WriteLine("Outlet unchanged — preserving existing OutletPricePerHeadAtSave snapshot.");
                }

                // 4) Persist header changes
                if (_pendingUsageDate.HasValue)
                {
                    _currentTransfer.UsageDate = _pendingUsageDate.Value.DateTime;
                }
                else
                {
                    _currentTransfer.UsageDate = UsageDatePicker.Date.DateTime;
                }

                _currentTransfer.Notes = string.IsNullOrWhiteSpace(NotesTextBox.Text) ? null : NotesTextBox.Text.Trim();
                _currentTransfer.ExpectedPeople = (int)ExpectedPeopleBox.Value;
                
                // ✅ เพิ่มบรรทัดนี้: บันทึก ActualPeople ถ้าสถานะเป็น Completed
                if (_currentTransfer.Status == TransferStatus.Completed)
                {
                    _currentTransfer.ActualPeople = (int)ActualPeopleBox.Value;
                }

                // ✅ เพิ่ม: บันทึก HiddenCostPercentage (ถ้ามีการเปลี่ยนแปลง)
                if (hiddenCostChanged)
                {
                    _currentTransfer.HiddenCostPercentage = currentHiddenCost;
                    System.Diagnostics.Debug.WriteLine($"📊 Updating HiddenCostPercentage: {originalHiddenCost:N4} → {currentHiddenCost:N4}");
                }

                System.Diagnostics.Debug.WriteLine($"💾 Updating header: Date={_currentTransfer.UsageDate}, OutletId={_currentTransfer.OutletId}, ExpectedPeople={_currentTransfer.ExpectedPeople}, ActualPeople={_currentTransfer.ActualPeople}, HiddenCost={_currentTransfer.HiddenCostPercentage}");

                bool mainSuccess = await _transferService.UpdateTransferAsync(
                    _currentTransfer,
                    Environment.UserName
                );

                if (!mainSuccess)
                {
                    ShowLoading(false);
                    await ShowErrorDialog("ล้มเหลว", "ไม่สามารถบันทึกข้อมูลหัวใบTransferได้");
                    return;
                }

                // --- Build and persist history BEFORE clearing staged lists or reloading ---
                try
                {
                    var sections = new List<string>();

                    if (_originalSnapshot != null)
                    {
                        var headerLines = new List<string>();
                        if (_originalSnapshot.ExpectedPeople != _currentTransfer!.ExpectedPeople)
                            headerLines.Add($"จำนวนคน: {_originalSnapshot.ExpectedPeople} → {_currentTransfer.ExpectedPeople}");
                        if (!string.Equals(_originalSnapshot.Notes ?? "", _currentTransfer.Notes ?? "", StringComparison.Ordinal))
                            headerLines.Add($"หมายเหตุ: \"{_originalSnapshot.Notes ?? ""}\" → \"{_currentTransfer.Notes ?? ""}\"");
                        if ((_originalSnapshot.UsageDate?.ToString("dd/MM/yyyy") ?? "") != (_currentTransfer.UsageDate?.ToString("dd/MM/yyyy") ?? ""))
                            headerLines.Add($"วันที่ใช้: {_originalSnapshot.UsageDate?.ToString("dd/MM/yyyy") ?? "ไม่ระบุ"} → {_currentTransfer.UsageDate?.ToString("dd/MM/yyyy") ?? "ไม่ระบุ"}");

                        // Use outlet display names instead of raw ids
                        if (_originalSnapshot.OutletId != _currentTransfer.OutletId)
                        {
                            var oldOutletName = GetOutletName(_originalSnapshot.OutletId);
                            var newOutletName = GetOutletName(_currentTransfer.OutletId);
                            headerLines.Add($"Outlet: \"{oldOutletName}\" → \"{newOutletName}\"");
                        }

                        // ✅ เพิ่ม: บันทึก HiddenCostPercentage ใน History
                        if (_originalSnapshot.HiddenCostPercentage != _currentTransfer.HiddenCostPercentage)
                        {
                            decimal oldHidden = _originalSnapshot.HiddenCostPercentage ?? 0m;
                            decimal newHidden = _currentTransfer.HiddenCostPercentage ?? 0m;
                            headerLines.Add($"ต้นทุนแฝง: {oldHidden:N4}% → {newHidden:N4}%");
                        }

                        if (headerLines.Count > 0)
                            sections.Add("หัวใบTransfer:\n- " + string.Join("\n- ", headerLines));
                    }

                    if (_stagedDeleteDescriptions.Count > 0)
                    {
                        sections.Add("รายการที่ลบ (staged):\n- " + string.Join("\n- ", _stagedDeleteDescriptions));
                    }

                    if (_itemsToUpdate.Count > 0)
                    {
                        var ulines = new List<string>();
                        foreach (var u in _itemsToUpdate)
                        {
                            string oldQtyText = _originalSnapshot != null && _originalSnapshot.ItemQuantities.TryGetValue(u.Id, out var oldQty)
                                ? oldQty.ToString("N4")
                                : "ไม่ทราบ";
                            ulines.Add($"{u.ProductName} ({u.ProductCode}): {oldQtyText} → {u.InitialQuantity:N4} {u.Unit}");
                        }
                        sections.Add("รายการที่แก้ไข (staged):\n- " + string.Join("\n- ", ulines));
                    }

                    if (_itemsToAdd.Count > 0)
                    {
                        var alines = _itemsToAdd.Select(a =>
                        {
                            string unitPriceText = a.UnitPrice.HasValue ? $"{a.UnitPrice.Value:N4} ฿" : "ไม่ระบุราคา";
                            string priceDateText = a.PriceDate.HasValue ? a.PriceDate.Value.ToString("dd/MM/yyyy") : "ไม่ระบุวันที่ราคา";
                            return $"{a.ProductName} ({a.ProductCode}) จำนวน {a.InitialQuantity:N4} {a.Unit} | ราคาต่อหน่วย: {unitPriceText} | วันที่ราคา: {priceDateText}";
                        }).ToList();

                        sections.Add("รายการใหม่ (staged):\n- " + string.Join("\n- ", alines));
                    }

                    string finalDescription = sections.Count > 0 ? string.Join("\n\n", sections) : "บันทึกการเปลี่ยนแปลง";

                    if (!string.IsNullOrWhiteSpace(saveReason))
                    {
                        finalDescription += "\n\nเหตุผล: " + saveReason;
                    }

                    string? oldValuesJson = _originalSnapshot != null ? JsonSerializer.Serialize(_originalSnapshot) : null;
                    string? newValuesJson = _currentTransfer != null ? JsonSerializer.Serialize(new
                    {
                        Header = new
                        {
                            ExpectedPeople = _currentTransfer.ExpectedPeople,
                            Notes = _currentTransfer.Notes,
                            UsageDate = _currentTransfer.UsageDate,
                            OutletId = _currentTransfer.OutletId,
                            OutletName = GetOutletName(_currentTransfer.OutletId)
                        },
                        Added = _itemsToAdd,
                        Updated = _itemsToUpdate,
                        RemovedIds = _itemIdsToRemove
                    }) : null;

                    bool histOk = await _transferService.AddHistoryEntryAsync(_transferId, "SavedChanges", finalDescription, Environment.UserName, oldValuesJson, newValuesJson);
                    if (!histOk)
                    {
                        System.Diagnostics.Debug.WriteLine("Warning: failed to insert summary history entry after save.");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AddHistoryEntryAsync failed: {ex.Message}");
                }
                // --- end history persist ---

                // All persisted successfully: clear staged lists and flags
                _hasPendingChanges = false;
                _itemsToAdd.Clear();
                _itemsToUpdate.Clear();
                _itemIdsToRemove.Clear();
                _hasUserModifiedDate = false;
                _hasUserModifiedOutlet = false;
                _hasUserModifiedExpectedPeople = false;
                _hasUserModifiedKitchen = false;
                _hasUserModifiedActualPeople = false;  // ✅ เพิ่ม
                ShowLoading(false);

                await ShowSuccessDialog("สำเร็จ", "บันทึกเรียบร้อยแล้ว");

                // Reload authoritative data and reset snapshot
                ShowLoading(true);
                await LoadTransferAsync();
                System.Diagnostics.Debug.WriteLine($"✅ Reloaded: {_currentTransfer?.Items.Count ?? 0} items");

                _originalSnapshot = TransferSnapshot.From(_currentTransfer!);

                try
                {
                    TransferEvents.NotifyTransferChanged(_transferId);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"NotifyTransferChanged failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                ShowLoading(false);
                System.Diagnostics.Debug.WriteLine($"❌ Save error: {ex.Message}");
                await ShowErrorDialog("เกิดข้อผิดพลาด", $"ข้อผิดพลาด: {ex.Message}");
            }

            // Clear staged delete summaries (we persisted history already)
            _stagedDeleteDescriptions.Clear();
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTransfer == null) return;

            if (_currentTransfer.IsDeleted)
            {
                await ShowErrorDialog("ไม่สามารถลบได้", "ใบTransferนี้ถูกลบไปแล้ว");
                return;
            }

            var reasonBox = new TextBox
            {
                Header = "เหตุผลในการลบ (บังคับ)",
                PlaceholderText = "เช่น: สร้างผิด, ยกเลิกงาน, ข้อมูลไม่ถูกต้อง...",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 100,
                MinWidth = 400
            };

            var dialog = new ContentDialog
            {
                Title = "⚠️ ยืนยันการลบใบTransfer",
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
            {
                new TextBlock
                {
                    Text = $"คุณกำลังจะลบใบTransfer '{_currentTransfer.TransferNo}'",
                    FontWeight = FontWeights.SemiBold
                },
                new TextBlock
                {
                    Text = "⚠️ หมายเหตุ: ข้อมูลจะยังอยู่ในระบบ แต่ว่าจะไม่แสดงในรายการ",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange),
                    TextWrapping = TextWrapping.Wrap
                },
                reasonBox
            }
                },

                PrimaryButtonText = "ยืนยันลบ",
                CloseButtonText = "ยกเลิก",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            dialog.Resources["ContentDialogPrimaryButtonBackground"] = new SolidColorBrush(Color.FromArgb(255, 196, 43, 28));

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            string reason = reasonBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(reason))
            {
                await ShowErrorDialog("ข้อผิดพลาด", "กรุณาระบุเหตุผลในการลบ");
                return;
            }

            try
            {
                ShowLoading(true);

                bool success = await _transferService.DeleteTransferAsync(
                    _transferId,
                    Environment.UserName,
                    reason
                );

                ShowLoading(false);

                if (success)
                {
                    await ShowSuccessDialog("สำเร็จ",
                        $"ลบใบTransferเรียบร้อยแล้ว\n\n📝 เหตุผล: {reason}");
                    try
                    {
                        TransferEvents.NotifyTransferChanged(_transferId);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"NotifyTransferChanged failed: {ex.Message}");
                    }
                    Frame.GoBack();
                }
                else
                {
                    await ShowErrorDialog("ล้มเหลว", "ไม่สามารถลบใบTransferได้");
                }
            }
            catch (Exception ex)
            {
                ShowLoading(false);
                await ShowErrorDialog("เกิดข้อผิดพลาด", $"ข้อผิดพลาด: {ex.Message}");
            }
        }

        #endregion

        #region Navigation & Helpers

        private void BackButton_Click(object sender, RoutedEventArgs e) => Frame.GoBack();

        private void ShowLoading(bool isLoading) => LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;

        private async Task ShowErrorDialog(string title, string message)
        {
            var dialog = new ContentDialog { Title = title, Content = message, CloseButtonText = "ปิด", XamlRoot = this.XamlRoot };
            await dialog.ShowAsync();
        }

        private async Task ShowSuccessDialog(string title, string message)
        {
            var dialog = new ContentDialog { Title = title, Content = message, CloseButtonText = "ปิด", XamlRoot = this.XamlRoot };
            await dialog.ShowAsync();
        }

        #endregion

        #region Complete

        private async void CompleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTransfer == null) return;

            if (!_currentTransfer.CanEdit || _isReadOnly)
            {
                await ShowErrorDialog("ข้อผิดพลาด", "ไม่สามารถดำเนินการได้");
                return;
            }

            var panel = new StackPanel { Spacing = 8 };
            var boxes = new Dictionary<int, NumberBox>();

            foreach (var item in _currentTransfer.Items)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(new TextBlock
                {
                    Text = $"{item.ProductName} ({item.ProductCode})",
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 420,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                var max = (double)(item.InitialQuantity + item.AdditionalQuantity);
                var nb = new NumberBox
                {
                    Value = (double)(item.ReturnedQuantity ?? 0M),
                    Minimum = 0,
                    Maximum = max,
                    SmallChange = 1,
                    LargeChange = 10,
                    Width = 140
                };

                boxes[item.Id] = nb;
                row.Children.Add(nb);
                panel.Children.Add(row);
            }

            var contentScroll = new ScrollViewer { Content = panel, MaxHeight = 280 };

            // Actual attendees input (required)
            var actualPeopleBox = new NumberBox
            {
                Header = "จำนวนคนที่มาใช้บริการจริง (บังคับ)",
                Value = _currentTransfer.ExpectedPeople > 0 ? _currentTransfer.ExpectedPeople : 0,
                Minimum = 0,
                SmallChange = 1,
                LargeChange = 10,
                Width = 200
            };

            // Add reason input (required)
            var reasonBox = new TextBox { Header = "เหตุผลการจบงาน (บังคับ)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinWidth = 400, Height = 100 };
            var dialogContent = new StackPanel { Spacing = 12 };
            dialogContent.Children.Add(contentScroll);

            // Put actualPeopleBox and reason below returned items
            dialogContent.Children.Add(actualPeopleBox);
            dialogContent.Children.Add(reasonBox);

            var dialog = new ContentDialog
            {
                Title = "จบงาน - ระบุจำนวนคืน, จำนวนผู้เข้าร่วมจริง และเหตุผล",
                Content = dialogContent,
                PrimaryButtonText = "จบงาน",
                CloseButtonText = "ยกเลิก",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            // Read returned quantities
            var returnedQuantities = new Dictionary<int, decimal>();
            foreach (var kv in boxes)
            {
                double value = kv.Value.Value;
                if (double.IsNaN(value)) value = 0.0;
                if (value < 0.0) value = 0.0;

                decimal decValue;
                try
                {
                    decValue = (decimal)value;
                }
                catch (OverflowException)
                {
                    decValue = 0m;
                }

                returnedQuantities[kv.Key] = decValue;
            }

            // Validate reason
            string reason = reasonBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(reason))
            {
                await ShowErrorDialog("ข้อผิดพลาด", "กรุณาระบุเหตุผลการจบงาน");
                return;
            }

            // Validate actual people
            double actualValue = actualPeopleBox.Value;
            if (double.IsNaN(actualValue) || actualValue < 0)
            {
                await ShowErrorDialog("ข้อผิดพลาด", "กรุณาระบุจำนวนคนที่มาใช้บริการจริง (>= 0)");
                return;
            }

            int actualPeople = (int)Math.Round(actualValue);

            try
            {
                ShowLoading(true);

                bool success = await _transfer_service_safe_call_Complete(_transferId, returnedQuantities, Environment.UserName, reason, actualPeople);
                ShowLoading(false);

                if (success)
                {
                    await LoadTransferAsync();
                    await ShowSuccessDialog("สำเร็จ", "ทำการจบงานเรียบร้อยแล้ว");
                    try
                    {
                        TransferEvents.NotifyTransferChanged(_transferId);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"NotifyTransferChanged failed: {ex.Message}");
                    }
                }
                else
                {
                    await ShowErrorDialog("ล้มเหลว", "ไม่สามารถจบงานได้");
                }
            }
            catch (Exception ex)
            {
                ShowLoading(false);
                System.Diagnostics.Debug.WriteLine($"CompleteRequisitionAsync failed: {ex.Message}");
                await ShowErrorDialog("เกิดข้อผิดพลาด", $"ไม่สามารถจบงาน: {ex.Message}");
            }
        }

        // Update helper wrapper to call new service signature
        private async Task<bool> _transfer_service_safe_call_Complete(int transferId, Dictionary<int, decimal>? returnedQuantities, string? completedBy, string? reason, int? actualPeople = null)
        {
            try
            {
                return await _transferService.CompleteTransferAsync(transferId, returnedQuantities, completedBy, actualPeople, reason);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CompleteTransferAsync failed: {ex.Message}");
                return false;
            }
        }

        #endregion

        private DependencyObject? FindChildByName(DependencyObject parent, string name)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is FrameworkElement fe && fe.Name == name)
                    return child;

                var result = FindChildByName(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }

        private class TransferSnapshot
        {
            public int ExpectedPeople { get; init; }
            public string? Notes { get; init; }
            public DateTime? UsageDate { get; init; }
            public int? OutletId { get; init; }
            public decimal? HiddenCostPercentage { get; init; }  // ← ✅ เพิ่มบรรทัดนี้
            public Dictionary<int, decimal> ItemQuantities { get; init; } = new();

            public static TransferSnapshot From(Models.Transfer r)
            {
                var snap = new TransferSnapshot
                {
                    ExpectedPeople = r.ExpectedPeople,
                    Notes = r.Notes,
                    UsageDate = r.UsageDate,
                    OutletId = r.OutletId,
                    HiddenCostPercentage = r.HiddenCostPercentage,  // ← ✅ เพิ่มบรรทัดนี้
                    ItemQuantities = r.Items.ToDictionary(i => i.Id, i => i.InitialQuantity)
                };
                return snap;
            }

            public bool Matches(Models.Transfer r)
            {
                if (r == null) return false;
                if (ExpectedPeople != r.ExpectedPeople) return false;
                if (!string.Equals(Notes ?? "", r.Notes ?? "", StringComparison.Ordinal)) return false;
                if (UsageDate?.ToString("o") != r.UsageDate?.ToString("o")) return false; // ISO compare
                if (OutletId != r.OutletId) return false;
                if (HiddenCostPercentage != r.HiddenCostPercentage) return false;  // ← ✅ เพิ่มบรรทัดนี้

                // Compare quantities for items that existed in the snapshot (IDs)
                foreach (var kv in ItemQuantities)
                {
                    var id = kv.Key;
                    var qty = kv.Value;

                    var item = r.Items.FirstOrDefault(it => it.Id == id);
                    if (item == null)
                    {
                        // item removed -> considered change
                        return false;
                    }

                    if (item.InitialQuantity != qty)
                        return false;
                }

                // Also if there are items in r that were not in snapshot but have positive Id (new persisted items) -> treat as change
                var extra = r.Items.Any(it => it.Id > 0 && !ItemQuantities.ContainsKey(it.Id));
                if (extra) return false;

                return true;
            }
        }

        // Build a small flattened diff between OldValues and NewValues JSON
        private string? BuildComparison(TransferHistory h)
        {
            if (string.IsNullOrWhiteSpace(h.OldValues) && string.IsNullOrWhiteSpace(h.NewValues))
                return null;

            try
            {
                var oldDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var newDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(h.OldValues))
                {
                    using var oldDoc = JsonDocument.Parse(h.OldValues);
                    FlattenJson(oldDoc.RootElement, oldDict);
                }

                if (!string.IsNullOrWhiteSpace(h.NewValues))
                {
                    using var newDoc = JsonDocument.Parse(h.NewValues);
                    FlattenJson(newDoc.RootElement, newDict);
                }

                // Collect all keys
                var keys = new HashSet<string>(oldDict.Keys, StringComparer.OrdinalIgnoreCase);
                keys.UnionWith(newDict.Keys);

                var diffs = new List<string>();
                foreach (var key in keys.OrderBy(k => k))
                {
                    oldDict.TryGetValue(key, out var oldVal);
                    newDict.TryGetValue(key, out var newVal);

                    oldVal = string.IsNullOrEmpty(oldVal) ? "—" : oldVal;
                    newVal = string.IsNullOrEmpty(newVal) ? "—" : newVal;

                    if (!string.Equals(oldVal, newVal, StringComparison.Ordinal))
                    {
                        diffs.Add($"{key}: {oldVal} → {newVal}");
                    }
                }

                if (diffs.Count == 0)
                {
                    // If JSONs differ structurally but flattening did not detect differences, show raw JSON snippets
                    if (!string.Equals(h.OldValues?.Trim() ?? "", h.NewValues?.Trim() ?? "", StringComparison.Ordinal))
                    {
                        return $"Old: {TruncateForDisplay(h.OldValues)}\nNew: {TruncateForDisplay(h.NewValues)}";
                    }

                    return null;
                }

                return string.Join("\n", diffs);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BuildComparison failed: {ex.Message}");
                // Fallback: return raw JSON snippets (truncated)
                return $"Old: {TruncateForDisplay(h.OldValues)}\nNew: {TruncateForDisplay(h.NewValues)}";
            }
        }

        // Flatten JSON into simple key->text pairs using path like "Header.ExpectedPeople" or "Added[0].ProductName"
        private void FlattenJson(JsonElement element, Dictionary<string, string> output, string prefix = "")
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in element.EnumerateObject())
                    {
                        var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                        FlattenJson(prop.Value, output, key);
                    }
                    break;

                case JsonValueKind.Array:
                    int idx = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        var key = $"{prefix}[{idx}]";
                        // For array of objects we flatten the object under the indexed key
                        FlattenJson(item, output, key);
                        idx++;
                    }
                    break;

                case JsonValueKind.String:
                    output[prefix] = element.GetString() ?? "";
                    break;

                case JsonValueKind.Number:
                    output[prefix] = element.GetRawText();
                    break;

                case JsonValueKind.True:
                case JsonValueKind.False:
                    output[prefix] = element.GetBoolean().ToString();
                    break;

                case JsonValueKind.Null:
                default:
                    output[prefix] = "";
                    break;
            }
        }

        private static string TruncateForDisplay(string? s, int max = 200)
        {
            if (string.IsNullOrEmpty(s)) return "—";
            s = s.Trim();
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "...";
        }

        public ManageTransferDetailPage()
        {
            InitializeComponent();

            // initialize readonly services so non-nullable contract is satisfied
            _transferService = new TransferService();
            _productService = new ProductService();
            _costPerHeadService = new CostPerHeadService();

            // wire page lifecycle / UI events
            Loaded += ManageTransferDetailPage_Loaded;
            UsageDatePicker.DateChanged += UsageDatePicker_DateChanged!;
            OutletCombo.SelectionChanged += OutletCombo_SelectionChanged;
            KitchenCombo.SelectionChanged += KitchenCombo_SelectionChanged;
        }
        private void ExpectedPeopleBox_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.NumberBoxValueChangedEventArgs e)
        {
            if (_currentTransfer == null) return;
            if (_suppressExpectedPeopleValueChanged) return;

            double val = e.NewValue;
            if (double.IsNaN(val) || val < 0) val = 0;

            int newExpected = (int)Math.Round(val);

            // Only update if changed to avoid unnecessary recalculation
            if (_currentTransfer.ExpectedPeople != newExpected)
            {
                _currentTransfer.ExpectedPeople = newExpected;
                _hasUserModifiedExpectedPeople = true;

                // Recalculate budget display immediately (no DB save)
                UpdateBudgetDisplay();

                // Mark header changed so Save button state updates
                MarkAsChanged();
            }
        }
        private void ExpectedPeopleBox_KeyUp(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (_currentTransfer == null) return;

            double val = ExpectedPeopleBox.Value;
            if (double.IsNaN(val) || val < 0) val = 0;

            int newExpected = (int)Math.Round(val);
            _currentTransfer.ExpectedPeople = newExpected;  // ✅ แยกบรรทัด
            _hasUserModifiedExpectedPeople = true;

            System.Diagnostics.Debug.WriteLine($"⏎ Enter pressed: ExpectedPeople = {newExpected}");

            // ✅ รีเฟรช
            UpdateBudgetDisplay();
            UpdateUI(skipExpectedPeopleUpdate: true);
            MarkAsChanged();

            // ✅ Clear focus
            RootPage?.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);

            e.Handled = true;
        }

        // Helper: resolve Outlet id -> display name (use cached _outlets when available)
        private string GetOutletName(int? outletId)
        {
            if (!outletId.HasValue) return "ไม่ระบุร้านค้า";
            try
            {
                var o = _outlets?.FirstOrDefault(x => x.Id == outletId.Value);
                if (o != null && !string.IsNullOrWhiteSpace(o.Name)) return o.Name;
            }
            catch { /* ignore */ }

            return $"#{outletId.Value}";
        }

        private Kitchen? GetSelectedKitchen()
        {
            if (KitchenCombo.SelectedItem is ComboBoxItem kitchenItem && kitchenItem.Tag is int kitchenId)
            {
                return _kitchens.FirstOrDefault(k => k.Id == kitchenId);
            }
            return null;
        }

        private void KitchenCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentTransfer != null && KitchenCombo.SelectedItem is ComboBoxItem item && item.Tag is int kitchenId)
            {
                _pendingKitchenId = kitchenId;
                _hasUserModifiedKitchen = true;
                System.Diagnostics.Debug.WriteLine($"🍳 User changed kitchen: {kitchenId}");
                MarkAsChanged();
            }
            else
            {
                _pendingKitchenId = null;
            }
        }
        private void QuantityBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not NumberBox box) return;

            void ApplyItem(TransferItem item)
            {
                try
                {
                    box.Value = (double)item.InitialQuantity;
                    box.Tag = item.Id;
                    System.Diagnostics.Debug.WriteLine($"📦 NumberBox loaded: {item.ProductName} (ID={item.Id}, Qty={item.InitialQuantity:N4})");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ ApplyItem error: {ex.Message}");
                }
            }

            // 1) Try DataContext first (most reliable inside DataTemplate)
            if (box.DataContext is TransferItem dcItem)
            {
                ApplyItem(dcItem);
                return;
            }

            // 2) Try visual-tree fallback (existing helper)
            var parentItem = FindParentItem(box);
            if (parentItem != null)
            {
                ApplyItem(parentItem);
                return;
            }

            // 3) If still not found, schedule a retry on the dispatcher (handles virtualization / late-binding)
            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            dq.TryEnqueue(async () =>
            {
                await Task.Delay(50); // small delay to allow template binding/virtualization to complete

                if (box.DataContext is TransferItem lateDc)
                {
                    ApplyItem(lateDc);
                    return;
                }

                var lateParent = FindParentItem(box);
                if (lateParent != null)
                {
                    ApplyItem(lateParent);
                    return;
                }

                System.Diagnostics.Debug.WriteLine("⚠️ Cannot find parent item (after retry) for NumberBox; leaving uninitialized.");
            });
        }

        private void QuantityBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (sender is not NumberBox box) return;

            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                SaveQuantityFromBox(box);
                _ = SaveButton?.Focus(FocusState.Programmatic);
                e.Handled = true;
            }
        }

        private void QuantityBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is NumberBox box)
            {
                SaveQuantityFromBox(box);
            }
        }
        #region Inline Quantity Editing Helpers

        private TransferItem? FindParentItem(DependencyObject child)
        {
            try
            {
                var parent = VisualTreeHelper.GetParent(child);

                while (parent != null)
                {
                    if (parent is FrameworkElement fe && fe.DataContext is TransferItem item)
                    {
                        return item;
                    }
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FindParentItem error: {ex.Message}");
            }

            return null;
        }

        private void ForceRefreshListView()
        {
            try
            {
                if (_currentTransfer?.Items == null || ItemsListView == null) return;

                System.Diagnostics.Debug.WriteLine($"🔄 Force refreshing ListView with {_currentTransfer.Items.Count} items");

                // ✅ สร้าง ObservableCollection ใหม่
                var items = new System.Collections.ObjectModel.ObservableCollection<TransferItem>();
                foreach (var item in _currentTransfer.Items)
                {
                    items.Add(item);
                }

                // ✅ Clear + Set ItemsSource ใหม่
                ItemsListView.ItemsSource = null;
                ItemsListView.UpdateLayout(); // Force layout update
                ItemsListView.ItemsSource = items;

                System.Diagnostics.Debug.WriteLine($"✅ ListView refreshed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ForceRefreshListView error: {ex.Message}");
            }
        }

        private void SaveQuantityFromBox(NumberBox box)
        {
            try
            {
                if (box.Tag is not int itemId)
                {
                    System.Diagnostics.Debug.WriteLine("❌ No item ID in box.Tag");
                    return;
                }

                if (_currentTransfer == null) return;

                double rawValue = box.Value;
                if (double.IsNaN(rawValue) || rawValue < 0)
                {
                    box.Value = 0;
                    return;
                }

                decimal newQuantity = (decimal)rawValue;

                // ✅ หา item (ลองทั้ง _currentTransfer.Items และ _itemsToAdd)
                var item = _currentTransfer.Items.FirstOrDefault(i => i.Id == itemId);
                if (item == null)
                {
                    item = _itemsToAdd.FirstOrDefault(i => i.Id == itemId);
                }

                if (item == null)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Item {itemId} not found");
                    return;
                }

                if (item.InitialQuantity == newQuantity) return;

                // ✅ บันทึกค่าใหม่
                decimal oldQty = item.InitialQuantity;
                item.InitialQuantity = newQuantity;

                System.Diagnostics.Debug.WriteLine($"✏️ {item.ProductName}: {oldQty:N4} → {newQuantity:N4}");

                // ✅ Stage changes
                if (item.Id > 0)
                {
                    // รายการจาก DB -> stage update
                    var staged = _itemsToUpdate.FirstOrDefault(i => i.Id == item.Id);
                    if (staged == null)
                    {
                        staged = new TransferItem
                        {
                            Id = item.Id,
                            TransferId = item.TransferId,
                            ProductCode = item.ProductCode,
                            ProductName = item.ProductName,
                            InitialQuantity = newQuantity,
                            Unit = item.Unit,
                            UnitPrice = item.UnitPrice,
                            PriceDate = item.PriceDate,
                            Notes = item.Notes
                        };
                        _itemsToUpdate.Add(staged);
                    }
                    else
                    {
                        staged.InitialQuantity = newQuantity;
                    }
                }
                else
                {
                    // รายการใหม่ -> อัปเดตใน _itemsToAdd
                    var existing = _itemsToAdd.FirstOrDefault(i => i.Id == item.Id);
                    if (existing != null)
                    {
                        existing.InitialQuantity = newQuantity;
                    }
                }

                // ✅ คำนวณใหม่
                _totalCost = CalculateTotalCost();
                UpdateBudgetDisplay();
                MarkAsChanged();

                // ✅ อัปเดต Total Quantity
                if (ItemCountText != null)
                    ItemCountText.Text = _currentTransfer.ItemCount.ToString();
                if (TotalQuantityText != null)
                    TotalQuantityText.Text = _currentTransfer.TotalQuantity.ToString("N4");

                // ✅ สำคัญ: Force refresh ListView เพื่อ update TotalCost display
                ForceRefreshListView();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SaveQuantityFromBox: {ex.Message}");
            }
        }

        #endregion
        // ✅ เพิ่ม Method ใหม่สำหรับคำนวณ Cost Per Actual Person (วางไว้ใกล้ๆ UpdateBudgetDisplay)
        private void UpdateActualCostDisplay()
        {
            if (_currentTransfer == null) return;
            // allow both InProgress and Completed
            if (_currentTransfer.Status != TransferStatus.Completed && _currentTransfer.Status != TransferStatus.InProgress)
                return;

            int actualPeople = (int)ActualPeopleBox.Value;

            if (actualPeople > 0 && _totalCost > 0)
            {
                decimal actualCostPerPerson = Math.Round(_totalCost / actualPeople, 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข
                ActualCostPerPersonText.Text = $"{actualCostPerPerson:N4} ฿/คน";
            }
            else
            {
                ActualCostPerPersonText.Text = "0.0000 ฿/คน";
            }

            // --- Populate compact comparison (Outlet vs actual cost-per-person) ---
            try
            {
                // determine outlet price per head (prefer snapshot for InProgress/Completed)
                int? targetOutletId = _hasUserModifiedOutlet ? _pendingOutletId : _currentTransfer.OutletId;
                decimal? outletPricePerHead = null;

                bool preferSnapshot = (_currentTransfer.Status == TransferStatus.InProgress || _currentTransfer.Status == TransferStatus.Completed)
                                       && _currentTransfer.OutletPricePerHeadAtSave.HasValue;

                if (preferSnapshot)
                {
                    outletPricePerHead = _currentTransfer.OutletPricePerHeadAtSave;
                }
                else if (targetOutletId.HasValue && _outlets != null)
                {
                    var o = _outlets.FirstOrDefault(x => x.Id == targetOutletId.Value);
                    if (o != null) outletPricePerHead = o.PricePerHead;
                }

                if (outletPricePerHead.HasValue)
                {
                    // show compare grid
                    if (ActualCostCompareGrid != null)
                        ActualCostCompareGrid.Visibility = Visibility.Visible;

                    if (ActualCompareOutletText != null)
                        ActualCompareOutletText.Text = $"{outletPricePerHead.Value:N4} ฿/คน";

                    if (ActualCompareDiffText != null)
                    {
                        decimal costPerActual = 0m;
                        if (actualPeople > 0 && _totalCost > 0) 
                            costPerActual = Math.Round(_totalCost / actualPeople, 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข

                        var diff = Math.Round(costPerActual - outletPricePerHead.Value, 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข
                        ActualCompareDiffText.Text = diff >= 0 ? $"+{diff:N4} ฿" : $"{diff:N4} ฿";

                        // color diff: red when over, green when under
                        ActualCompareDiffText.Foreground = diff > 0
                            ? new SolidColorBrush(Color.FromArgb(255, 220, 38, 28))
                            : new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                    }
                }
                else
                {
                    if (ActualCostCompareGrid != null)
                        ActualCostCompareGrid.Visibility = Visibility.Collapsed;
                    if (ActualCompareDiffText != null)
                        ActualCompareDiffText.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateActualCostDisplay (compare) failed: {ex.Message}");
            }
        }

        // ✅ เพิ่ม Event Handler สำหรับ ActualPeopleBox (วางไว้ใกล้ๆ ExpectedPeopleBox_ValueChanged)
        private void ActualPeopleBox_ValueChanged(object sender, NumberBoxValueChangedEventArgs e)
        {
            if (_currentTransfer == null) return;
            if (_currentTransfer.Status != TransferStatus.Completed) return;

            double val = e.NewValue;
            if (double.IsNaN(val) || val < 0) val = 0;

            int newActual = (int)Math.Round(val);

            if (_currentTransfer.ActualPeople != newActual)
            {
                _currentTransfer.ActualPeople = newActual;
                _hasUserModifiedActualPeople = true;

                // รีเฟรชการแสดงผลทันที
                UpdateActualCostDisplay();
                UpdateActualHiddenCostDisplay();  // ← ✅ เพิ่มบรรทัดนี้
                MarkAsChanged();

                System.Diagnostics.Debug.WriteLine($"👥 ActualPeople changed: {newActual}");
            }
        }

        // ✅ เพิ่ม KeyUp Handler (วางไว้ใกล้ๆ ExpectedPeopleBox_KeyUp)
        private void ActualPeopleBox_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (_currentTransfer == null) return;
            // allow both InProgress and Completed
            if (_currentTransfer.Status != TransferStatus.Completed && _currentTransfer.Status != TransferStatus.InProgress) return;

            double val = ActualPeopleBox.Value;
            if (double.IsNaN(val) || val < 0) val = 0;

            int newActual = (int)Math.Round(val);
            _currentTransfer.ActualPeople = newActual;
            _hasUserModifiedActualPeople = true;

            System.Diagnostics.Debug.WriteLine($"⏎ Enter pressed in ActualPeopleBox: {newActual}");

            // ✅ Refresh
            UpdateActualCostDisplay();
            UpdateActualHiddenCostDisplay();
            MarkAsChanged();

            // Clear focus
            RootPage?.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            e.Handled = true;
        }
        // track original value (set on first display)
        private decimal? _originalHiddenCostPercentage = null;
        // guard to avoid responding to programmatic NumberBox.Value changes
        private bool _suppressHiddenCostValueChanged = false;

        // Called from UpdateUI (already present) or whenever totals / expected people change
        private void UpdateHiddenCostDisplay()
        {
            if (_currentTransfer == null) return;

            // capture original on first run
            if (_originalHiddenCostPercentage == null)
                _originalHiddenCostPercentage = _currentTransfer.HiddenCostPercentage ?? 0m;

            // Desired numeric value from DB (no "%" suffix)
            double desired = (double)(_currentTransfer.HiddenCostPercentage ?? 0m);

            try
            {
                // Ensure we update UI controls on the UI thread
                var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                if (dq != null)
                {
                    dq.TryEnqueue(() =>
                    {
                        try
                        {
                            if (HiddenCostPercentageBox != null)
                            {
                                if (Math.Abs(HiddenCostPercentageBox.Value - desired) > 0.0001)
                                {
                                    _suppressHiddenCostValueChanged = true;
                                    HiddenCostPercentageBox.Value = desired;
                                    _suppressHiddenCostValueChanged = false;
                                }
                            }

                            // compute derived amounts and update total-with-hidden UI 
                            decimal computedCostPerPerson = (_currentTransfer.ExpectedPeople > 0 && _totalCost > 0)
                                ? (_totalCost / _currentTransfer.ExpectedPeople)
                                : 0m;
                            decimal pct = _currentTransfer.HiddenCostPercentage ?? 0m;
                            decimal hiddenAmount = Math.Round(computedCostPerPerson * (pct / 100m), 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข

                            if (HiddenCostAmountText != null)
                            {
                                // This text is a helper (keeps pct in the helper text) — NumberBox shows only the raw number
                                HiddenCostAmountText.Text = pct > 0m
                                    ? $"≈ {hiddenAmount:N4} ฿/คน ({pct:N4})"
                                    : "ไม่มีต้นทุนแฝง";
                            }

                            UpdateTotalCostWithHiddenDisplay(computedCostPerPerson, hiddenAmount, pct);
                        }
                        catch (Exception exInner)
                        {
                            System.Diagnostics.Debug.WriteLine($"UpdateHiddenCostDisplay (UI) failed: {exInner.Message}");
                        }
                    });
                }
                else
                {
                    // Fallback if dispatcher unavailable
                    if (HiddenCostPercentageBox != null)
                    {
                        if (Math.Abs(HiddenCostPercentageBox.Value - desired) > 0.0001)
                        {
                            _suppressHiddenCostValueChanged = true;
                            HiddenCostPercentageBox.Value = desired;
                            _suppressHiddenCostValueChanged = false;
                        }
                    }

                    decimal computedCostPerPerson = (_currentTransfer.ExpectedPeople > 0 && _totalCost > 0)
                        ? (_totalCost / _currentTransfer.ExpectedPeople)
                        : 0m;
                    decimal pct = _currentTransfer.HiddenCostPercentage ?? 0m;
                    decimal hiddenAmount = Math.Round(computedCostPerPerson * (pct / 100m), 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข

                    if (HiddenCostAmountText != null)
                    {
                        HiddenCostAmountText.Text = pct > 0m
                            ? $"≈ {hiddenAmount:N4} ฿/คน ({pct:N4})"
                            : "ไม่มีต้นทุนแฝง";
                    }

                    UpdateTotalCostWithHiddenDisplay(computedCostPerPerson, hiddenAmount, pct);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateHiddenCostDisplay failed: {ex.Message}");
            }
        }

        // updates the 'TotalCostWithHidden' card visuals (keeps same logic as Budget compare)
        private void UpdateTotalCostWithHiddenDisplay(decimal costPerPerson, decimal hiddenCostAmount, decimal hiddenCostPercentage)
        {
            if (_currentTransfer == null) return;

            try
            {
                decimal totalWithHidden = Math.Round(costPerPerson + hiddenCostAmount, 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข

                if (TotalCostWithHiddenText != null)
                    TotalCostWithHiddenText.Text = $"{totalWithHidden:N4} ฿/คน";

                if (TotalCostBreakdownText != null)
                {
                    TotalCostBreakdownText.Text = $"ต้นทุนต่อคน {costPerPerson:N4} + ต้นทุนแฝง {hiddenCostPercentage:N4}% ({hiddenCostAmount:N4} ฿)";
                    TotalCostBreakdownText.Visibility = Visibility.Visible;
                }

                int? targetOutletId = _hasUserModifiedOutlet ? _pendingOutletId : _currentTransfer.OutletId;
                decimal? outletPricePerHead = null;

                bool preferSnapshot = (_currentTransfer.Status == Models.TransferStatus.InProgress || _currentTransfer.Status == Models.TransferStatus.Completed)
                                       && _currentTransfer.OutletPricePerHeadAtSave.HasValue;

                if (preferSnapshot)
                {
                    outletPricePerHead = _currentTransfer.OutletPricePerHeadAtSave;
                }
                else if (targetOutletId.HasValue && _outlets != null)
                {
                    var o = _outlets.FirstOrDefault(x => x.Id == targetOutletId.Value);
                    if (o != null) outletPricePerHead = o.PricePerHead;
                }

                if (outletPricePerHead.HasValue)
                {
                    if (TotalCostTargetText != null)
                    {
                        TotalCostTargetText.Text = $"งบต่อคน : {outletPricePerHead.Value:N4} ฿";
                        TotalCostTargetText.Visibility = Visibility.Visible;
                    }

                    if (totalWithHidden > outletPricePerHead.Value)
                    {
                        // เกินงบ - สีแดง
                        BudgetCard.Background = new SolidColorBrush(Color.FromArgb(40, 220, 38, 28));
                        BudgetCard.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));
                        BudgetUsageText.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));
                        BudgetTargetText.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));

                        var exceed = Math.Round(totalWithHidden - outletPricePerHead.Value, 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข
                        BudgetWarningText!.Text = $"⚠️ เกินงบ {exceed:N4} ฿/คน";
                        BudgetWarningText.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        // ไม่เกินงบ - สีเขียว
                        BudgetCard.Background = new SolidColorBrush(Color.FromArgb(40, 34, 197, 94));
                        BudgetCard.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                        BudgetUsageText.Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                        BudgetTargetText.Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));

                        var remain = Math.Round(outletPricePerHead.Value - totalWithHidden, 4, MidpointRounding.AwayFromZero);
                        BudgetWarningText!.Text = $"✓ เหลืองบ {remain:N4} ฿/คน";
                        BudgetWarningText.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    if (TotalCostTargetText != null) TotalCostTargetText.Visibility = Visibility.Collapsed;
                    if (TotalCostWarningText != null) TotalCostWarningText.Visibility = Visibility.Collapsed;
                    
                    // ใช้สีเขียวเป็นค่าเริ่มต้น
                    var defaultBg = new SolidColorBrush(Color.FromArgb(40, 34, 197, 94));
                    var defaultBorder = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                    TotalCostWithHiddenCard.Background = defaultBg;
                    TotalCostWithHiddenCard.BorderBrush = defaultBorder;
                    TotalCostWithHiddenText!.Foreground = defaultBorder;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateTotalCostWithHiddenDisplay failed: {ex.Message}");
            }
        }

        // user edits the NumberBox
        private void HiddenCostPercentageBox_ValueChanged(object sender, NumberBoxValueChangedEventArgs e)
        {
            if (_currentTransfer == null) return;
            if (_suppressHiddenCostValueChanged) return;

            double raw = e.NewValue;
            if (double.IsNaN(raw)) raw = 0.0;
            if (raw < 0) raw = 0.0;
            if (raw > 100) raw = 100.0;

            decimal newPct = Math.Round((decimal)raw, 2);

            if ((_currentTransfer.HiddenCostPercentage ?? 0m) != newPct)
            {
                _currentTransfer.HiddenCostPercentage = newPct;
                _hasUserModifiedHiddenCost = true;
                MarkAsChanged();
            }

            UpdateHiddenCostDisplay();
        }

        private void HiddenCostPercentageBox_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (_currentTransfer == null) return;

            double raw = HiddenCostPercentageBox.Value;
            if (double.IsNaN(raw)) raw = 0.0;
            decimal newPct = Math.Round((decimal)raw, 2);

            if ((_currentTransfer.HiddenCostPercentage ?? 0m) != newPct)
            {
                _currentTransfer.HiddenCostPercentage = newPct;
                _hasUserModifiedHiddenCost = true;
                MarkAsChanged();
            }

            UpdateHiddenCostDisplay();
            RootPage?.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            e.Handled = true;
        }

        private void UpdateActualHiddenCostDisplay()
        {
            if (_currentTransfer == null) return;
            // allow both InProgress and Completed
            if (_currentTransfer.Status != TransferStatus.Completed && _currentTransfer.Status != TransferStatus.InProgress) return;

            int actualPeople = (int)ActualPeopleBox.Value;
            if (actualPeople <= 0)
            {
                // hide when no people
                ActualHiddenCostCard.Visibility = Visibility.Collapsed;
                TotalActualCostWithHiddenCard.Visibility = Visibility.Collapsed;
                return;
            }

            // show cards
            ActualHiddenCostCard.Visibility = Visibility.Visible;
            TotalActualCostWithHiddenCard.Visibility = Visibility.Visible;

            // calculations
            decimal actualCostPerPerson = _totalCost / actualPeople;
            decimal hiddenCostPct = _currentTransfer.HiddenCostPercentage ?? 0m;
            decimal hiddenCostAmount = Math.Round(actualCostPerPerson * (hiddenCostPct / 100m), 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข
            decimal totalActualWithHidden = Math.Round(actualCostPerPerson + hiddenCostAmount, 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข

            if (ActualHiddenCostAmountText != null)
            {
                ActualHiddenCostAmountText.Text = $"{hiddenCostPct:N4}";
            }

            if (ActualHiddenCostDetailText != null)
            {
                ActualHiddenCostDetailText.Text = hiddenCostPct > 0m
                    ? $"≈ {hiddenCostAmount:N4} ฿/คน ({actualCostPerPerson:N4})"
                    : "ไม่มีต้นทุนแฝง";
            }

            if (TotalActualCostWithHiddenText != null)
            {
                TotalActualCostWithHiddenText.Text = $"{totalActualWithHidden:N4} ฿/คน";
            }

            if (TotalActualCostBreakdownText != null)
            {
                TotalActualCostBreakdownText.Text =
                    $"ต้นทุนต่อคนจริง {actualCostPerPerson:N4} + ต้นทุนแฝง {hiddenCostPct:N4}% ({hiddenCostAmount:N4} ฿)";
            }

            // compare to outlet price (same logic as before)...
            int? targetOutletId = _hasUserModifiedOutlet ? _pendingOutletId : _currentTransfer.OutletId;
            decimal? outletPricePerHead = null;

            bool preferSnapshot = _currentTransfer.OutletPricePerHeadAtSave.HasValue;
            if (preferSnapshot)
            {
                outletPricePerHead = _currentTransfer.OutletPricePerHeadAtSave;
            }
            else if (targetOutletId.HasValue && _outlets != null)
            {
                var o = _outlets.FirstOrDefault(x => x.Id == targetOutletId.Value);
                if (o != null) outletPricePerHead = o.PricePerHead;
            }

            if (outletPricePerHead.HasValue)
            {
                if (TotalActualCostTargetText != null)
                {
                    TotalActualCostTargetText.Text = $"งบต่อคน : {outletPricePerHead.Value:N4} ฿";
                    TotalActualCostTargetText.Visibility = Visibility.Visible;
                }

                if (totalActualWithHidden > outletPricePerHead.Value)
                {
                    TotalActualCostWithHiddenCard.Background = new SolidColorBrush(Color.FromArgb(40, 220, 38, 28));
                    TotalActualCostWithHiddenCard.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));
                    TotalActualCostWithHiddenText!.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));
                    TotalActualCostTargetText!.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));

                    var exceed = Math.Round(totalActualWithHidden - outletPricePerHead.Value, 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข
                    TotalActualCostWarningText.Text = $"⚠️ เกินงบ {exceed:N4} ฿/คน";
                    TotalActualCostWarningText.Visibility = Visibility.Visible;
                }
                else
                {
                    TotalActualCostWithHiddenCard.Background = new SolidColorBrush(Color.FromArgb(40, 34, 197, 94));
                    TotalActualCostWithHiddenCard.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                    TotalActualCostWithHiddenText!.Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                    TotalActualCostTargetText!.Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));

                    var remain = Math.Round(outletPricePerHead.Value - totalActualWithHidden, 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข
                    TotalActualCostWarningText.Text = $"✓ เหลืองบ {remain:N4} ฿/คน";
                    TotalActualCostWarningText.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (TotalActualCostTargetText != null) TotalActualCostTargetText.Visibility = Visibility.Collapsed;
                if (TotalActualCostWarningText != null) TotalActualCostWarningText.Visibility = Visibility.Collapsed;

                // ใช้สีเขียวเป็นค่าเริ่มต้น
                var defaultBg = new SolidColorBrush(Color.FromArgb(40, 34, 197, 94));
                var defaultBorder = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                TotalActualCostWithHiddenCard.Background = defaultBg;
                TotalActualCostWithHiddenCard.BorderBrush = defaultBorder;
                TotalActualCostWithHiddenText!.Foreground = defaultBorder;
            }
        }
    }
}
