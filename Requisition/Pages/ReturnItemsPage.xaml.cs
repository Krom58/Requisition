using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Requisition.Models;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Requisition.Pages
{
    public sealed partial class ReturnItemsPage : Page
    {
        private readonly TransferService _transferService;
        private Models.Transfer? _transfer;
        private List<TransferItem> _returnableItems = new();

        public ReturnItemsPage()
        {
            InitializeComponent();
            _transferService = new TransferService();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is int transferId)
            {
                System.Diagnostics.Debug.WriteLine($"🔄 ReturnItemsPage: Navigated with Transfer ID: {transferId}");
                await LoadTransferAsync(transferId);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"❌ ReturnItemsPage: Invalid parameter type: {e.Parameter?.GetType().Name ?? "null"}");
                await ShowErrorDialogAsync("ข้อผิดพลาด", "ไม่พบข้อมูลใบโอน");
                Frame.GoBack();
            }
        }

        private async Task LoadTransferAsync(int transferId)
        {
            LoadingRing.IsActive = true;
            LoadingRing.Visibility = Visibility.Visible;
            ReturnItemsListView.Visibility = Visibility.Collapsed;
            EmptyStatePanel.Visibility = Visibility.Collapsed;

            System.Diagnostics.Debug.WriteLine($"🔄 ===== LOADING RETURN ITEMS PAGE =====");
            System.Diagnostics.Debug.WriteLine($"   Transfer ID: {transferId}");

            try
            {
                _transfer = await _transferService.GetTransferByIdAsync(transferId);

                if (_transfer == null)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Transfer not found!");
                    await ShowErrorDialogAsync("ข้อผิดพลาด", "ไม่พบใบtransferนี้");
                    Frame.GoBack();
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"✅ Transfer loaded: {_transfer.TransferNo}");

                if (_transfer.Status == TransferStatus.Completed)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Transfer is completed - cannot return items");
                    await ShowErrorDialogAsync("ไม่สามารถดำเนินการได้", "ใบtransferนี้จบงานแล้ว ไม่สามารถคืนของได้");
                    Frame.GoBack();
                    return;
                }

                UpdateHeaderInfo();

                _returnableItems = await _transferService.GetReturnableItemsAsync(transferId);
                System.Diagnostics.Debug.WriteLine($"📦 Returnable items loaded: {_returnableItems.Count}");

                if (_returnableItems.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"ℹ️ No returnable items - showing empty state");
                    EmptyStatePanel.Visibility = Visibility.Visible;
                }
                else
                {
                    ReturnItemsListView.ItemsSource = _returnableItems;
                    ReturnItemsListView.Visibility = Visibility.Visible;

                    // initialize summary (all return quantities default to 0)
                    UpdateSummary();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ LoadTransferAsync ERROR: {ex.Message}");
                await ShowErrorDialogAsync("เกิดข้อผิดพลาด", $"ไม่สามารถโหลดข้อมูลได้: {ex.Message}");
                Frame.GoBack();
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine($"🏁 LoadTransferAsync completed");
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateHeaderInfo()
        {
            if (_transfer == null) return;

            TransferNoText.Text = _transfer.TransferNo;
            CreatedDateText.Text = _transfer.CreatedDate.ToString("dd/MM/yyyy HH:mm");
            CreatedByText.Text = _transfer.CreatedBy ?? "ไม่ระบุ";
            StatusText.Text = _transfer.StatusText;
            StatusBadge.Background = new SolidColorBrush(_transfer.StatusColor);
        }

        private void UpdateSummary()
        {
            int selectedCount = 0;
            decimal totalReturn = 0;

            System.Diagnostics.Debug.WriteLine($"🔍 ===== UPDATE SUMMARY =====");
            System.Diagnostics.Debug.WriteLine($"   Returnable items count: {_returnableItems.Count}");

            foreach (var item in _returnableItems)
            {
                if (item.ReturnQuantity > 0)
                {
                    selectedCount++;
                    totalReturn += Math.Round(item.ReturnQuantity, 4);
                    System.Diagnostics.Debug.WriteLine($"   ✅ ItemID={item.Id} ({item.ProductCode}): {item.ReturnQuantity:N4} {item.Unit}");
                }
            }

            SelectedCountBadge.Value = selectedCount;

            if (selectedCount == 0)
            {
                SelectedSummaryText.Text = "เลือกรายการที่ต้องการคืน";
                SaveButton.IsEnabled = false;
            }
            else
            {
                SelectedSummaryText.Text = $"จะคืน {selectedCount} รายการ (รวม {Math.Round(totalReturn,4):N4} หน่วย)";
                SaveButton.IsEnabled = true;
            }

            System.Diagnostics.Debug.WriteLine($"📊 Summary Result: {selectedCount} items, Total: {totalReturn:N4}");
        }

        private async void EditReturnItemButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag is not int itemId)
            {
                System.Diagnostics.Debug.WriteLine($"❌ EditButton: Invalid Tag");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"✏️ EditButton clicked: ItemID={itemId}");

            var item = _returnableItems.FirstOrDefault(i => i.Id == itemId);
            if (item == null)
            {
                System.Diagnostics.Debug.WriteLine($"   ❌ Item not found");
                await ShowErrorDialogAsync("ข้อผิดพลาด", "ไม่พบรายการนี้");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"   Item: {item.ProductCode} - {item.ProductName}");
            System.Diagnostics.Debug.WriteLine($"   Max: {item.AvailableToReturnDouble}");

            var dialog = new ContentDialog
            {
                Title = $"แก้ไขการคืน: {item.ProductName}",
                PrimaryButtonText = "บันทึก",
                CloseButtonText = "ยกเลิก",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            // use a local NumberBox in dialog to collect value, but update model property when saved
            var numberBox = new NumberBox
            {
                Header = "จำนวนที่ต้องการคืน",
                Minimum = 0,
                Maximum = item.AvailableToReturnDouble,
                Value = (double)item.ReturnQuantity,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                SmallChange = 1,
                LargeChange = 10,
                Width = 300
            };

            var infoText = new TextBlock
            {
                Text = $"คงเหลือที่สามารถคืนได้: {item.RemainingQuantity:N4} {item.Unit}",
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };

            var stackPanel = new StackPanel { Spacing = 8 };
            stackPanel.Children.Add(numberBox);
            stackPanel.Children.Add(infoText);

            dialog.Content = stackPanel;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                // update model property directly (rounded)
                var newValue = Math.Round((decimal)numberBox.Value, 4);
                item.ReturnQuantity = newValue;
                System.Diagnostics.Debug.WriteLine($"✅ Updated ItemID={item.Id} ReturnQuantity={item.ReturnQuantity}");
                UpdateSummary();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"   Cancelled");
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_transfer == null) return;

            var returnQuantities = new Dictionary<int, decimal>();

            foreach (var item in _returnableItems)
            {
                if (item.ReturnQuantity > 0)
                {
                    returnQuantities[item.Id] = Math.Round(item.ReturnQuantity, 4);
                }
            }

            System.Diagnostics.Debug.WriteLine($"💾 ===== SAVE BUTTON CLICKED =====");
            System.Diagnostics.Debug.WriteLine($"   Items to return: {returnQuantities.Count}");
            foreach (var kv in returnQuantities)
            {
                var item = _returnableItems.FirstOrDefault(i => i.Id == kv.Key);
                System.Diagnostics.Debug.WriteLine($"   ItemID={kv.Key}: {item?.ProductCode} - Quantity={kv.Value}");
            }

            if (returnQuantities.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"❌ No items to return");
                await ShowErrorDialogAsync("ไม่มีข้อมูล", "กรุณาระบุจำนวนที่ต้องการคืน");
                return;
            }

            var confirmDialog = new ContentDialog
            {
                Title = "ยืนยันการคืนสินค้า",
                Content = $"คุณต้องการคืนสินค้า {returnQuantities.Count} รายการใช่หรือไม่?",
                PrimaryButtonText = "ยืนยัน",
                CloseButtonText = "ยกเลิก",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var result = await confirmDialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                System.Diagnostics.Debug.WriteLine($"❌ User cancelled");
                return;
            }

            LoadingRing.IsActive = true;
            SaveButton.IsEnabled = false;

            try
            {
                System.Diagnostics.Debug.WriteLine($"📞 Calling ReturnMultipleItemsAsync...");

                bool success = await _transferService.ReturnMultipleItemsAsync(
                    returnQuantities,
                    Environment.UserName);

                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Return successful");
                    await ShowSuccessDialogAsync("สำเร็จ", "บันทึกการคืนสินค้าเรียบร้อยแล้ว");

                    if (Frame.CanGoBack)
                        Frame.GoBack();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Return failed");
                    await ShowErrorDialogAsync("ผิดพลาด", "ไม่สามารถบันทึกการคืนได้");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Save ERROR: {ex.Message}");
                await ShowErrorDialogAsync("เกิดข้อผิดพลาด", $"ไม่สามารถบันทึกได้: {ex.Message}");
            }
            finally
            {
                LoadingRing.IsActive = false;
                SaveButton.IsEnabled = true;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"🔙 Back button clicked");
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Cancel button clicked");
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private async Task ShowErrorDialogAsync(string title, string message)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Error Dialog: {title} - {message}");
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "ตกลง",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async Task ShowSuccessDialogAsync(string title, string message)
        {
            System.Diagnostics.Debug.WriteLine($"✅ Success Dialog: {title} - {message}");
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "ตกลง",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
