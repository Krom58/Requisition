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
    public sealed partial class ManageTransferPage : Page
    {
        private readonly TransferService _transferService;
        private List<Models.Transfer> _allTransfers = new();
        private List<Models.Transfer> _filteredTransfers = new();
        private string _selectedStatus = "ทั้งหมด";
        private int _dialogOpen = 0;

        // Pagination
        private const int PageSize = 20;
        private int _currentPage = 1;
        private int _totalPages = 1;
        private List<Models.Transfer> _currentPageItems = new();

        public ManageTransferPage()
        {
            InitializeComponent();
            _transferService = new TransferService();
            TransferEvents.TransferChanged += OnTransferChanged;

            StatusFilter.SelectedIndex = 0;
            Loaded += ManageTransferPage_Loaded;
        }

        private async void ManageTransferPage_Loaded(object sender, RoutedEventArgs e)
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
            EmptyState.Visibility = Visibility.Collapsed;
            PaginationPanel.Visibility = Visibility.Collapsed;

            try
            {
                _allTransfers = await _transferService.GetAllTransfersAsync();
                System.Diagnostics.Debug.WriteLine($"📦 Loaded {_allTransfers.Count} transfers");
                _currentPage = 1; // reset on reload
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
            if (_selectedStatus == "ทั้งหมด")
            {
                _filteredTransfers = _allTransfers.ToList();
            }
            else if (_selectedStatus == "กำลังดำเนินการ")
            {
                _filteredTransfers = _allTransfers.Where(t => t.Status == TransferStatus.InProgress).ToList();
            }
            else if (_selectedStatus == "จบงานแล้ว")
            {
                _filteredTransfers = _allTransfers.Where(t => t.Status == TransferStatus.Completed).ToList();
            }
            else
            {
                _filteredTransfers = _allTransfers.ToList();
            }

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
            if (_currentPageItems.Count > 0)
            {
                TransferListView.ItemsSource = _currentPageItems;
                TransferListView.Visibility = Visibility.Visible;
                EmptyState.Visibility = Visibility.Collapsed;
                PaginationPanel.Visibility = (_totalPages > 1) ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                TransferListView.Visibility = Visibility.Collapsed;
                EmptyState.Visibility = Visibility.Visible;
                PaginationPanel.Visibility = Visibility.Collapsed;
            }

            UpdatePaginationControls();
            System.Diagnostics.Debug.WriteLine($"✅ Showing {_currentPageItems.Count} transfers (page {_currentPage}/{_totalPages})");
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

        private void StatusFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (StatusFilter.SelectedItem is string status)
            {
                _selectedStatus = status;
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

        private void ViewDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int transferId)
            {
                System.Diagnostics.Debug.WriteLine($"📄 View details: {transferId}");
                Frame.Navigate(typeof(ManageTransferDetailPage), new TransferDetailPageParameter(transferId, isReadOnly: true));
            }
        }

        private async void CompleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not int transferId)
                return;

            var transfer = _allTransfers.FirstOrDefault(t => t.Id == transferId);
            if (transfer == null)
                return;

            // Build input UI with fixed width and larger controls
            var panel = new StackPanel { Spacing = 12, Width = 520 };

            panel.Children.Add(new TextBlock
            {
                Text = $"กำลังจะจบงานใบTransfer '{transfer.TransferNo}'\nกรุณาระบุจำนวนผู้เข้าร่วมจริง และเหตุผล (ถ้ามี)",
                TextWrapping = TextWrapping.Wrap
            });

            panel.Children.Add(new TextBlock
            {
                Text = "จำนวนผู้เข้าร่วมจริง",
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 0)
            });

            var actualPeopleBox = new Microsoft.UI.Xaml.Controls.NumberBox
            {
                Minimum = 0,
                Maximum = 100000,
                SmallChange = 1,
                Value = 0,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(actualPeopleBox);

            panel.Children.Add(new TextBlock
            {
                Text = "เหตุผล (ถ้ามี)",
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 0)
            });

            // create reason TextBox (larger, multi-line)
            var reasonBox = new TextBox
            {
                PlaceholderText = "ระบุมูลเหตุหรือหมายเหตุเพิ่มเติม (ไม่บังคับ)",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 120,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 4)
            };

            // set the ScrollViewer attached property correctly
            ScrollViewer.SetVerticalScrollBarVisibility(reasonBox, ScrollBarVisibility.Auto);

            panel.Children.Add(reasonBox);

            var inputDialog = new ContentDialog
            {
                Title = "ข้อมูลก่อนจบงาน",
                Content = panel,
                PrimaryButtonText = "ยืนยันและจบงาน",
                CloseButtonText = "ยกเลิก",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            // Loop until user cancels or provides valid actual people (> 0)
            while (true)
            {
                var result = await inputDialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                    return; // user cancelled

                var value = actualPeopleBox.Value;
                if (double.IsNaN(value) || value <= 0)
                {
                    await ShowErrorDialog("ไม่สามารถบันทึกได้", "กรุณาระบุจำนวนผู้เข้าร่วมจริง (ค่าต้องมากกว่า 0) เพื่อบันทึก");
                    // re-show dialog (loop)
                    continue;
                }

                int actualPeople;
                try
                {
                    actualPeople = Convert.ToInt32(Math.Round(value));
                }
                catch
                {
                    await ShowErrorDialog("ผิดพลาด", "ค่าไม่ถูกต้อง กรุณาระบุจำนวนผู้เข้าร่วมใหม่");
                    continue;
                }

                var reason = string.IsNullOrWhiteSpace(reasonBox.Text) ? null : reasonBox.Text.Trim();

                LoadingOverlay.Visibility = Visibility.Visible;

                try
                {
                    bool success = await _transferService.CompleteTransferAsync(
                        transferId,
                        returnedQuantities: null,
                        completedBy: Environment.UserName,
                        actualPeople: actualPeople,
                        reason: reason
                    );

                    if (success)
                    {
                        await ShowSuccessDialog("สำเร็จ", "จบงานเรียบร้อยแล้ว");
                        await LoadTransfersAsync();
                    }
                    else
                    {
                        await ShowErrorDialog("ผิดพลาด", "ไม่สามารถจบงานได้");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Complete error: {ex.Message}");
                    await ShowErrorDialog("เกิดข้อผิดพลาด", $"ไม่สามารถจบงานได้: {ex.Message}");
                }
                finally
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }

                break; // finished
            }
        }

        private async void OnTransferChanged(object? sender, int transferId)
        {
            System.Diagnostics.Debug.WriteLine($"🔔 Transfer changed notification: {transferId}");
            await LoadTransfersAsync();
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
