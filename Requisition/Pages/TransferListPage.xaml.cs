using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Requisition.Models;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Requisition.Pages
{
    public sealed partial class TransferListPage : Page
    {
        private readonly TransferService _transferService;
        private List<Models.Transfer> _allTransfers = new();
        private List<Models.Transfer> _filteredTransfers = new();
        private TransferStatus? _selectedStatus = null;
        private int _dialogOpen = 0;

        // Pagination
        private const int PageSize = 20;
        private int _currentPage = 1;
        private int _totalPages = 1;
        private List<Models.Transfer> _currentPageItems = new();

        public TransferListPage()
        {
            InitializeComponent();
            _transferService = new TransferService();
            TransferEvents.TransferChanged += OnTransferChanged;
            
            // Setup status filter
            StatusFilter.Items.Add(new ComboBoxItem { Content = "ทั้งหมด", Tag = null });
            StatusFilter.Items.Add(new ComboBoxItem { Content = "แบบร่าง", Tag = TransferStatus.Draft });
            StatusFilter.Items.Add(new ComboBoxItem { Content = "กำลังดำเนินการ", Tag = TransferStatus.InProgress });
            StatusFilter.Items.Add(new ComboBoxItem { Content = "จบงานแล้ว", Tag = TransferStatus.Completed });
            StatusFilter.SelectedIndex = 0;

            Loaded += TransferListPage_Loaded;
        }

        private async void TransferListPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadTransfersAsync();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await LoadTransfersAsync();
        }

        private async Task LoadTransfersAsync()
        {
            LoadingRing.IsActive = true;
            TransferListView.Visibility = Visibility.Collapsed;
            PaginationPanel.Visibility = Visibility.Collapsed;

            try
            {
                _allTransfers = await _transferService.GetAllTransfersAsync();
                System.Diagnostics.Debug.WriteLine($"📦 Loaded {_allTransfers.Count} transfers");
                _currentPage = 1; // reset to first page when reloading
                ApplyFilters();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error loading transfers: {ex.Message}");
                await ShowErrorDialog("เกิดข้อผิดพลาด", $"ไม่สามารถโหลดข้อมูลได้: {ex.Message}");
            }
            finally
            {
                LoadingRing.IsActive = false;
            }
        }

        private void ApplyFilters()
        {
            var searchText = TransferSearchBox?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
            
            System.Diagnostics.Debug.WriteLine($"🔍 Applying filters - Status: {_selectedStatus}, Search: '{searchText}'");

            // 1. กรองตาม Status
            _filteredTransfers = _selectedStatus.HasValue
                ? _allTransfers.Where(t => t.Status == _selectedStatus.Value).ToList()
                : _allTransfers.ToList();

            System.Diagnostics.Debug.WriteLine($"   After status filter: {_filteredTransfers.Count} items");

            // 2. กรองตาม SearchText
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                _filteredTransfers = _filteredTransfers.Where(t =>
                    (t.TransferNo?.ToLowerInvariant().Contains(searchText) == true) ||
                    (t.CreatedBy?.ToLowerInvariant().Contains(searchText) == true) ||
                    (t.Notes?.ToLowerInvariant().Contains(searchText) == true)
                ).ToList();

                System.Diagnostics.Debug.WriteLine($"   After search filter: {_filteredTransfers.Count} items");
            }

            // 3. เรียงตามวันที่สร้าง (ล่าสุดก่อน)
            _filteredTransfers = _filteredTransfers
                .OrderByDescending(t => t.CreatedDate)
                .ToList();

            // 4. Pagination: calculate total pages and take current page slice
            var totalItems = _filteredTransfers.Count;
            _totalPages = totalItems == 0 ? 1 : (int)Math.Ceiling(totalItems / (double)PageSize);
            _currentPage = Math.Clamp(_currentPage, 1, _totalPages);

            var skip = (_currentPage - 1) * PageSize;
            _currentPageItems = _filteredTransfers.Skip(skip).Take(PageSize).ToList();

            // 5. แสดงผล
            if (totalItems == 0)
            {
                TransferListView.ItemsSource = null;
                TransferListView.Visibility = Visibility.Visible; // show empty list if needed
                PaginationPanel.Visibility = Visibility.Collapsed;
                System.Diagnostics.Debug.WriteLine("✅ No transfers to show");
            }
            else
            {
                TransferListView.ItemsSource = _currentPageItems;
                TransferListView.Visibility = Visibility.Visible;
                PaginationPanel.Visibility = (_totalPages > 1) ? Visibility.Visible : Visibility.Collapsed;
                UpdatePaginationControls();
                System.Diagnostics.Debug.WriteLine($"✅ Showing {_currentPageItems.Count} transfers (page {_currentPage}/{_totalPages})");
            }
        }

        private void UpdatePaginationControls()
        {
            PageInfoTextBlock.Text = $"Page {_currentPage} of {_totalPages}";
            PrevPageButton.IsEnabled = _currentPage > 1;
            NextPageButton.IsEnabled = _currentPage < _totalPages;
        }

        private void TransferSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                System.Diagnostics.Debug.WriteLine($"🔍 Search text changed: '{sender.Text}'");
                _currentPage = 1; // reset when search changes
                ApplyFilters();
            }
        }

        private void StatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StatusFilter.SelectedItem is ComboBoxItem item)
            {
                _selectedStatus = item.Tag as TransferStatus?;
                System.Diagnostics.Debug.WriteLine($"📊 Status filter changed: {_selectedStatus}");
                _currentPage = 1; // reset when filter changes
                ApplyFilters();
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("🔄 Refresh button clicked");
            
            // รีเซ็ต SearchBox
            if (TransferSearchBox != null)
            {
                TransferSearchBox.Text = string.Empty;
            }

            await LoadTransfersAsync();
        }

        private async void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            // สร้าง controls สำหรับ dialog
            var expectedPeopleBox = new NumberBox
            {
                Header = "จำนวนคนที่คาดว่าจะเข้า *",
                PlaceholderText = "กรอกจำนวนคน",
                Minimum = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Margin = new Thickness(0, 8, 0, 0)
            };

            // Allow choosing any past date (no MinDate) — usage date is required
            var usageDatePicker = new CalendarDatePicker
            {
                Header = "วันที่จะใช้ของ *",
                PlaceholderText = "เลือกวันที่",
                Margin = new Thickness(0, 8, 0, 0)
            };

            var notesBox = new TextBox
            {
                Header = "หมายเหตุ",
                PlaceholderText = "กรอกหมายเหตุ... (ไม่บังคับ)",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 80,
                Margin = new Thickness(0, 8, 0, 0)
            };

            // Outlet selector (required)
            var outletCombo = new ComboBox
            {
                Header = "Outlet *",
                PlaceholderText = "เลือก Outlet (บังคับ)",
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // Kitchen selector (required)
            var kitchenCombo = new ComboBox
            {
                Header = "ห้องครัว *",
                PlaceholderText = "เลือกห้องครัว (บังคับ)",
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // โหลด Outlets (เฉพาะที่ active)
            try
            {
                var cph = new CostPerHeadService();
                var outlets = await cph.GetAllAsync();
                foreach (var o in outlets)
                {
                    if (o == null || !o.IsActive) continue;
                    outletCombo.Items.Add(new ComboBoxItem { Content = o.Name ?? $"#{o.Id}", Tag = o.Id });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error loading outlets: {ex.Message}");
            }

            // โหลด Kitchens (เฉพาะที่ active)
            try
            {
                var kitchenService = new KitchenService();
                var kitchens = await kitchenService.GetAllAsync();
                foreach (var k in kitchens)
                {
                    if (k == null || !k.IsActive) continue;
                    kitchenCombo.Items.Add(new ComboBoxItem { Content = k.Name ?? $"#{k.Id}", Tag = k.Id });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error loading kitchens: {ex.Message}");
            }

            var validationText = new TextBlock
            {
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red),
                FontSize = 12,
                Visibility = Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap
            };

            var dialogPanel = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "กรุณากรอกข้อมูลใบTransfer", FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = "* จำเป็นต้องกรอก", FontSize = 12, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red) },
                    validationText,
                    expectedPeopleBox,
                    outletCombo,
                    kitchenCombo,
                    usageDatePicker,
                    notesBox
                }
            };

            var dialog = new ContentDialog
            {
                Title = "สร้างใบTransferใหม่",
                Content = dialogPanel,
                PrimaryButtonText = "สร้าง",
                CloseButtonText = "ยกเลิก",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            dialog.PrimaryButtonClick += (s, args) =>
            {
                // ตรวจสอบจำนวนคน
                if (double.IsNaN(expectedPeopleBox.Value) || expectedPeopleBox.Value < 1)
                {
                    args.Cancel = true;
                    validationText.Text = "กรุณากรอกจำนวนคนที่คาดว่าจะเข้า";
                    validationText.Visibility = Visibility.Visible;
                    return;
                }

                // ตรวจสอบ Outlet
                if (!(outletCombo.SelectedItem is ComboBoxItem oc && oc.Tag is int))
                {
                    args.Cancel = true;
                    validationText.Text = "กรุณาเลือก Outlet (บังคับ)";
                    validationText.Visibility = Visibility.Visible;
                    return;
                }

                // ตรวจสอบ Kitchen
                if (!(kitchenCombo.SelectedItem is ComboBoxItem kc && kc.Tag is int))
                {
                    args.Cancel = true;
                    validationText.Text = "กรุณาเลือกห้องครัว (บังคับ)";
                    validationText.Visibility = Visibility.Visible;
                    return;
                }

                // ตรวจสอบวันที่ใช้งาน (บังคับ)
                if (!usageDatePicker.Date.HasValue)
                {
                    args.Cancel = true;
                    validationText.Text = "กรุณาเลือกวันที่จะใช้ของ (บังคับ)";
                    validationText.Visibility = Visibility.Visible;
                    return;
                }
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    LoadingRing.IsActive = true;

                    int expectedPeople = (int)expectedPeopleBox.Value;

                    int outletId = 0;
                    if (outletCombo.SelectedItem is ComboBoxItem sel && sel.Tag is int oid)
                        outletId = oid;

                    int kitchenId = 0;
                    if (kitchenCombo.SelectedItem is ComboBoxItem ksel && ksel.Tag is int kid)
                        kitchenId = kid;

                    DateTime? usageDate = usageDatePicker.Date?.DateTime;
                    string? notes = string.IsNullOrWhiteSpace(notesBox.Text) ? null : notesBox.Text.Trim();

                    var newTransfer = await _transferService.CreateTransferAsync(
                        expectedPeople: expectedPeople,
                        outletId: outletId,
                        kitchenId: kitchenId,
                        budget: 0,
                        usageDate: usageDate,
                        createdBy: Environment.UserName,
                        notes: notes
                    );

                    if (newTransfer != null)
                    {
                        await LoadTransfersAsync();
                        await ShowSuccessDialog("สำเร็จ", $"สร้างใบTransfer {newTransfer.TransferNo} เรียบร้อยแล้ว");
                        Frame.Navigate(typeof(TransferDetailPage), newTransfer.Id);
                    }
                }
                catch (Exception ex)
                {
                    await ShowErrorDialog("เกิดข้อผิดพลาด", $"ไม่สามารถสร้างใบTransferได้: {ex.Message}");
                }
                finally
                {
                    LoadingRing.IsActive = false;
                }
            }
        }

        private void ViewButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int transferId)
            {
                System.Diagnostics.Debug.WriteLine($"👁️ View transfer: {transferId}");
                Frame.Navigate(typeof(TransferDetailPage), new TransferDetailPageParameter(transferId, isReadOnly: false));
            }
        }

        private void AddMoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int transferId)
            {
                System.Diagnostics.Debug.WriteLine($"➕ Add more items: {transferId}");
                Frame.Navigate(typeof(AddMoreItemsPage), transferId);
            }
        }

        private void ReturnButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int transferId)
            {
                System.Diagnostics.Debug.WriteLine($"🔄 Return items: {transferId}");
                Frame.Navigate(typeof(ReturnItemsPage), transferId);
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not int transferId)
                return;

            var transfer = _allTransfers.FirstOrDefault(r => r.Id == transferId);
            if (transfer == null)
                return;

            // ตรวจสอบว่าเป็น Draft และไม่มีรายการ
            if (transfer.Status != TransferStatus.Draft)
            {
                await ShowErrorDialog("ไม่สามารถลบได้", "สามารถลบได้เฉพาะใบTransferที่เป็นแบบร่างเท่านั้น");
                return;
            }

            if (transfer.ItemCount > 0)
            {
                await ShowErrorDialog("ไม่สามารถลบได้", "ไม่สามารถลบใบTransferที่มีรายการสินค้าได้\nกรุณาลบรายการสินค้าออกก่อน");
                return;
            }

            // สร้าง dialog เพื่อขอเหตุผลในการลบ
            var reasonBox = new TextBox
            {
                Header = "เหตุผลในการลบ (บังคับ)",
                PlaceholderText = "ระบุเหตุผลในการลบ...",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 120,
                MinWidth = 400
            };

            var validationText = new TextBlock
            {
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red),
                FontSize = 12,
                Visibility = Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap
            };

            var panel = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"คุณกำลังจะลบใบTransfer '{transfer.TransferNo}'",
                        FontWeight = FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "⚠️ หมายเหตุ: กรุณาระบุเหตุผลในการลบ",
                        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange),
                        TextWrapping = TextWrapping.Wrap
                    },
                    validationText,
                    reasonBox
                }
            };

            var dialog = new ContentDialog
            {
                Title = "⚠️ ยืนยันการลบ",
                Content = panel,
                PrimaryButtonText = "ลบ",
                CloseButtonText = "ยกเลิก",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            dialog.PrimaryButtonClick += (s, args) =>
            {
                if (string.IsNullOrWhiteSpace(reasonBox.Text))
                {
                    args.Cancel = true;
                    validationText.Text = "กรุณาระบุเหตุผลในการลบ (บังคับ)";
                    validationText.Visibility = Visibility.Visible;
                    return;
                }
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            string reason = reasonBox.Text.Trim();

            LoadingRing.IsActive = true;
            try
            {
                bool success = await _transferService.DeleteTransferAsync(
                    transferId,
                    Environment.UserName,
                    reason
                );

                if (success)
                {
                    await ShowSuccessDialog("สำเร็จ", "ลบใบTransferเรียบร้อยแล้ว");
                    await LoadTransfersAsync();
                }
                else
                {
                    await ShowErrorDialog("ผิดพลาด", "ไม่สามารถลบใบTransferได้");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog("เกิดข้อผิดพลาด", $"ไม่สามารถลบได้: {ex.Message}");
            }
            finally
            {
                LoadingRing.IsActive = false;
            }
        }

        private void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                ApplyFilters();
            }
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages)
            {
                _currentPage++;
                ApplyFilters();
            }
        }

        // ✅ แก้ไข signature ให้ตรงกับ EventHandler<int>
        private async void OnTransferChanged(object? sender, int transferId)
        {
            System.Diagnostics.Debug.WriteLine($"🔔 Transfer changed notification: {transferId}");
            await LoadTransfersAsync();
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