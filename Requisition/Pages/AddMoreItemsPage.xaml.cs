using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Requisition.Models;
using Requisition.Services;
using System.Threading.Tasks;
using System.ComponentModel;

namespace Requisition.Pages;

public sealed partial class AddMoreItemsPage : Page
{
    private readonly TransferService _transferService;
    private readonly ProductService _productService;
    private readonly CostPerHeadService _costPerHeadService = new();
    private Models.Transfer? _transfer;
    private int _transferId;
    private readonly Dictionary<int, decimal> _additionalQuantities = new();
    private readonly List<TransferItem> _newItems = new();
    private List<Product> _allProducts = new();
    private HashSet<int> _originalItemIds = new();
    private ObservableCollection<TransferItemDisplay> _displayItems = new();
    private List<Outlet> _Outlets = new();

    private decimal _totalCost = 0;
    private int _nextTempId = -1;

    public AddMoreItemsPage()
    {
        InitializeComponent();
        _transferService = new TransferService();
        _productService = new ProductService();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is int transferId)
        {
            _transferId = transferId;
            await LoadDataAsync();
        }
    }

    private async Task LoadDataAsync()
    {
        try
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            
            System.Diagnostics.Debug.WriteLine($"🔄 Loading transfer ID: {_transferId}");

            _transfer = await _transferService.GetTransferByIdAsync(_transferId);

            if (_transfer == null)
            {
                await ShowErrorDialog("ไม่พบข้อมูล", "ไม่พบใบTransferที่ต้องการ");
                Frame.GoBack();
                return;
            }

            System.Diagnostics.Debug.WriteLine($"✅ Loaded transfer: {_transfer.TransferNo}");
            System.Diagnostics.Debug.WriteLine($"   Status: {_transfer.Status}");
            System.Diagnostics.Debug.WriteLine($"   Items count: {_transfer.Items.Count}");

            _originalItemIds = new HashSet<int>(_transfer.Items.Select(i => i.Id));
            System.Diagnostics.Debug.WriteLine($"   Original item IDs: [{string.Join(", ", _originalItemIds)}]");

            // Debug แต่ละรายการ
            foreach (var item in _transfer.Items)
            {
                System.Diagnostics.Debug.WriteLine($"      Item {item.Id}: {item.ProductCode} - Initial:{item.InitialQuantity}, Additional:{item.AdditionalQuantity}");
            }

            _allProducts = await _product_service_safe_call_GetAllProductsAsync();

            // ⚠️ แก้ไข: ใช้ LoadCurrentPricesAsync เหมือน TransferDetailPage
            await LoadCurrentPricesAsync();

            // Call LoadOutletsAsync from LoadDataAsync
            await LoadOutletsAsync();

            UpdateUI();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ LoadDataAsync error: {ex.Message}");
            await ShowErrorDialog("เกิดข้อผิดพลาด", ex.Message);
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    // safe wrapper for product service call (small helper to reduce exceptions bubbling)
    private async Task<List<Product>> _product_service_safe_call_GetAllProductsAsync()
    {
        try
        {
            return await _productService.GetAllProductsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetAllProductsAsync failed: {ex.Message}");
            return new List<Product>();
        }
    }

    // ⚠️ แก้ไข: ใช้ logic เดียวกับ RequisitionDetailPage
    private async Task LoadCurrentPricesAsync()
    {
        if (_transfer == null || _transfer.Items.Count == 0) return;

        foreach (var item in _transfer.Items)
        {
            // ⚠️ ถ้ามี UnitPrice บันทึกไว้แล้วให้ใช้จากฐานข้อมูล
            if (item.UnitPrice.HasValue)
            {
                // ใช้ราคาที่บันทึกไว้
                continue;
            }

            // ⚠️ ถ้าไม่มี UnitPrice ให้ดึงราคาปัจจุบัน (สำหรับ backward compatibility)
            var product = await _productService.GetProductByCodeAsync(item.ProductCode);
            if (product?.Price != null)
            {
                item.CurrentPrice = product.Price.Value; // สำหรับแสดงใน UI
            }
        }
    }

    private async Task LoadOutletsAsync()
    {
        try
        {
            _Outlets = await _costPerHead_service_safe_call_GetAllAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadOutletsAsync failed: {ex.Message}");
            _Outlets = new List<Outlet>();
        }
    }

    private async Task<List<Outlet>> _costPerHead_service_safe_call_GetAllAsync()
    {
        try
        {
            return await _costPerHeadService.GetAllAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CostPerHeadService.GetAllAsync failed: {ex.Message}");
            return new List<Outlet>();
        }
    }

    private void UpdateUI()
    {
        if (_transfer == null) return;

        TransferNoText.Text = _transfer.TransferNo;
        ItemCountText.Text = $"{_transfer.ItemCount} รายการ";
        TotalQuantityText.Text = $"{_transfer.TotalQuantity:N4} หน่วย";

        // ⚠️ เพิ่ม: แสดงจำนวนคนและต้นทุนต่อคน
        ExpectedPeopleText.Text = $"{_transfer.ExpectedPeople}";
        CalculateAndUpdateBudget();
        UpdateHiddenCostDisplay();
        UpdateItemsDisplay();
        UpdateAvailableProductsList();
        UpdateNewItemsCount();
    }

    // ⚠️ แก้ไข: ใช้ UnitPrice แทน CurrentPrice
    private void CalculateAndUpdateBudget()
    {
        if (_transfer == null) return;

        // คำนวณต้นทุนรวม (หลังหักคืน + จำนวนที่จะเบิกเพิ่ม)
        _totalCost = 0;

        foreach (var item in _transfer.Items)
        {
            // จำนวนสุทธิที่ใช้ไปแล้ว (หลังหักคืน)
            decimal quantity = item.RemainingQuantity;

            // บวกจำนวนที่จะเบิกเพิ่มในครั้งนี้ (ถ้ามี)
            if (_additionalQuantities.TryGetValue(item.Id, out var additionalQty))
            {
                quantity += additionalQty;
            }

            _totalCost += quantity * (item.UnitPrice ?? 0);
        }

        // คำนวณต้นทุนต่อคน
        decimal costPerPerson = _transfer.ExpectedPeople > 0
            ? _totalCost / _transfer.ExpectedPeople
            : 0;

        BudgetUsageText.Text = $"{costPerPerson:N4} ฿/คน";

        // Determine outlet PricePerHead:
        // Prefer OutletPricePerHeadAtSave (snapshot on Transfer) when available,
        // otherwise fallback to current outlet price from _Outlets.
        decimal? outletPricePerHead = null;
        DateTime? outletPriceSavedAt = null;

        if (_transfer.OutletPricePerHeadAtSave.HasValue)
        {
            outletPricePerHead = _transfer.OutletPricePerHeadAtSave.Value;
            outletPriceSavedAt = _transfer.OutletPricePerHeadSavedAt;
        }
        else if (_transfer.OutletId.HasValue && _Outlets != null && _Outlets.Count > 0)
        {
            var o = _Outlets.FirstOrDefault(x => x.Id == _transfer.OutletId.Value);
            if (o != null) outletPricePerHead = o.PricePerHead;
        }

        // Show target if available
        if (outletPricePerHead.HasValue)
        {
            BudgetTargetText.Text = $"งบต่อคน: {outletPricePerHead.Value:N4} ฿";
            BudgetTargetText.Visibility = Visibility.Visible;
        }
        else
        {
            BudgetTargetText.Text = string.Empty;
            BudgetTargetText.Visibility = Visibility.Collapsed;
        }

        // Color rules: prefer outlet limit; fallback to original thresholds
        if (outletPricePerHead.HasValue)
        {
            if (costPerPerson > outletPricePerHead.Value)
            {
                // Exceeded -> red
                BudgetCard.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(40, 220, 38, 38));
                BudgetCard.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 220, 38, 38));
                BudgetUsageText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 220, 38, 38));
                BudgetTargetText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 220, 38, 38));
            }
            else
            {
                // Within budget -> green
                BudgetCard.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(40, 34, 197, 94));
                BudgetCard.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 34, 197, 94));
                BudgetUsageText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 34, 197, 94));
                BudgetTargetText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 34, 197, 94));
            }
        }
        else
        {
            // No outlet budget configured — fallback to original rules
            if (costPerPerson >= 500)
            {
                BudgetCard.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(40, 220, 38, 38));
                BudgetCard.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 220, 38, 38));
                BudgetUsageText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 220, 38, 38));
            }
            else if (costPerPerson >= 300)
            {
                BudgetCard.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(40, 234, 179, 8));
                BudgetCard.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 234, 179, 8));
                BudgetUsageText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 234, 179, 8));
            }
            else
            {
                BudgetCard.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(40, 34, 197, 94));
                BudgetCard.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 34, 197, 94));
                BudgetUsageText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 34, 197, 94));
            }
        }

        System.Diagnostics.Debug.WriteLine($"💰 Total Cost: {_totalCost:N4}, Cost/Person: {costPerPerson:N4}");
    }

    /// <summary>
    /// ⚠️ Method ใหม่: อัปเดตการแสดงรายการพร้อมระบุว่าเป็นรายการใหม่หรือเปล่า
    /// </summary>
    private void UpdateItemsDisplay()
    {
        if (_transfer == null) return;

        _displayItems.Clear();

        foreach (var item in _transfer.Items)
        {
            var displayItem = new TransferItemDisplay
            {
                Item = item,
                IsNewItem = !_originalItemIds.Contains(item.Id)
            };

            // 🆕 ตั้งค่าจำนวนเบิกเพิ่มจาก Dictionary
            if (_additionalQuantities.TryGetValue(item.Id, out var qty))
            {
                displayItem.AdditionalQuantity = qty;
            }

            _displayItems.Add(displayItem);
        }

        ItemsListView.ItemsSource = _displayItems;
    }

    private void UpdateAvailableProductsList()
    {
        // กรองสินค้าที่ยังไม่มีในใบTransfer และไม่ถูกปิดใช้งาน
        var existingCodes = new HashSet<string>(
            _transfer?.Items.Select(i => i.ProductCode) ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        var availableProducts = _allProducts
            .Where(p =>
                !existingCodes.Contains(p.Code ?? string.Empty)
                // exclude explicit inactive products
                && p.IsActive
                // exclude products temporarily disabled until a future date
                && !p.IsCurrentlyDisabled)
            .ToList();

        AvailableProductsListView.ItemsSource = availableProducts;
        AvailableProductsCountText.Text = $"{availableProducts.Count} รายการ";
    }

    private void UpdateNewItemsCount()
    {
        if (_newItems.Count > 0)
        {
            NewItemsCountBorder.Visibility = Visibility.Visible;
            NewItemsCountText.Text = $"รอบันทึก: {_newItems.Count} รายการ";
        }
        else
        {
            NewItemsCountBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void ProductSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        var query = sender.Text?.Trim();
        
        if (string.IsNullOrWhiteSpace(query))
        {
            UpdateAvailableProductsList();
            return;
        }

        var existingCodes = new HashSet<string>(
            _transfer?.Items.Select(i => i.ProductCode) ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        var filtered = _allProducts
            .Where(p =>
                !existingCodes.Contains(p.Code ?? string.Empty)
                && p.IsActive
                && !p.IsCurrentlyDisabled
                && (((p.Code ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    ((p.Name ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        AvailableProductsListView.ItemsSource = filtered;
        AvailableProductsCountText.Text = $"{filtered.Count} รายการ";
    }

    private async void AddProductButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not Product product) return;

        if (_transfer?.Items.Any(i => string.Equals(i.ProductCode, product.Code, StringComparison.OrdinalIgnoreCase)) == true)
        {
            await ShowErrorDialog("ข้อผิดพลาด", "สินค้านี้มีในรายการแล้ว");
            return;
        }

        // ถามจำนวน
        var qtyBox = new NumberBox
        {
            Header = "จำนวน",
            Value = 1,
            Minimum = 0.0001,
            SmallChange = 0.0001,
            LargeChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            NumberFormatter = CreateDecimalFormatter() // ✅ เพิ่มบรรทัดนี้
        };

        var dialog = new ContentDialog
        {
            Title = $"เพิ่ม: {product.Name}",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = $"รหัส: {product.Code}", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") },
                    new TextBlock { Text = $"หน่วย: {product.Unit}" },
                    new TextBlock { Text = $"💰 ต้นทุน: {product.Price?.ToString("N4") ?? "ไม่ระบุ"} ฿", Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange) },
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
            // ⚠️ FIX: ใช้ sequential negative ID
            int tempId = _nextTempId--;

            var newItem = new TransferItem
            {
                Id = tempId,
                TransferId = _transferId,
                ProductCode = product.Code,
                ProductName = product.Name,
                InitialQuantity = (decimal)qtyBox.Value,
                AdditionalQuantity = 0,
                Unit = product.Unit,
                ReturnedQuantity = null,
                // ⚠️ เพิ่ม: บันทึกราคาและวันที่ของราคา ณ ตอนนั้น เหมือน RequisitionDetailPage
                UnitPrice = product.Price,      
                PriceDate = DateTime.Now,
                CurrentPrice = product.Price    // สำหรับแสดงใน UI
            };

            _newItems.Add(newItem);
            
            // ⚠️ อัพเดทรายการ
            if (_transfer != null)
            {
                _transfer.Items.Add(newItem);
            }

            UpdateItemsDisplay();
            UpdateAvailableProductsList();
            UpdateNewItemsCount();
            
            // ⚠️ เพิ่ม: คำนวณต้นทุนใหม่
            CalculateAndUpdateBudget();

            await ShowSuccessInfoBar($"เพิ่ม {product.Name} ({qtyBox.Value} {product.Unit}) เรียบร้อย");
        }
    }

    // 🆕 Event Handler สำหรับปุ่มเบิกเพิ่ม
    private async void AddMoreQuantityButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not TransferItemDisplay display) return;

        var item = display.Item;
        var currentAdditional = _additionalQuantities.ContainsKey(item.Id) 
            ? _additionalQuantities[item.Id] 
            : 0m;

        var qtyBox = new NumberBox
        {
            Header = "จำนวนที่ต้องการเบิกเพิ่ม",
            Value = (double)currentAdditional,
            Minimum = 0,
            SmallChange = 0.0001,
            LargeChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            NumberFormatter = CreateDecimalFormatter()
        };

        var dialog = new ContentDialog
        {
            Title = $"เบิกเพิ่ม: {item.ProductName}",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock 
                    { 
                        Text = $"รหัส: {item.ProductCode}", 
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        FontSize = 14
                    },
                    new TextBlock 
                    { 
                        Text = $"ใช้อยู่: {item.RemainingQuantity:N4} {item.Unit}",
                        FontSize = 14,
                        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green)
                    },
                    new TextBlock 
                    { 
                        Text = $"💰 ต้นทุน/หน่วย: {(item.UnitPrice ?? 0):N4} ฿",
                        FontSize = 14,
                        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange)
                    },
                    new Border 
                    { 
                        Height = 1, 
                        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                        Margin = new Thickness(0, 8, 0, 8)
                    },
                    qtyBox
                }
            },
            PrimaryButtonText = "บันทึก",
            CloseButtonText = "ยกเลิก",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            decimal newQty = (decimal)qtyBox.Value;
            
            System.Diagnostics.Debug.WriteLine($"📊 AddMoreQuantity: ItemID={item.Id}, NewValue={newQty}");

            if (newQty > 0)
            {
                _additionalQuantities[item.Id] = newQty;
            }
            else
            {
                _additionalQuantities.Remove(item.Id);
            }

            // 🆕 อัพเดท Display
            display.AdditionalQuantity = newQty;

            CalculateAndUpdateBudget();
            UpdateHiddenCostDisplay();

            await ShowSuccessInfoBar(newQty > 0 
                ? $"ตั้งค่าเบิกเพิ่ม {item.ProductName}: {newQty:N4} {item.Unit}"
                : $"ยกเลิกการเบิกเพิ่ม {item.ProductName}");
        }
    }

    private async void RemoveNewItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not TransferItemDisplay display) return;

        var confirmDialog = new ContentDialog
        {
            Title = "ยืนยันการลบ",
            Content = $"ต้องการลบ '{display.Item.ProductName}' ออกจากรายการหรือไม่?",
            PrimaryButtonText = "ลบ",
            CloseButtonText = "ยกเลิก",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await confirmDialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _newItems.RemoveAll(i => i.Id == display.Item.Id);

            _transfer?.Items.Remove(display.Item);

            _additionalQuantities.Remove(display.Item.Id);

            UpdateItemsDisplay();
            UpdateAvailableProductsList();
            UpdateNewItemsCount();
            
            CalculateAndUpdateBudget();
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_transfer == null) return;

        var itemsToAddMore = _additionalQuantities.Where(kv => kv.Value > 0).ToList();
        
        System.Diagnostics.Debug.WriteLine($"💾 ===== SAVE BUTTON CLICKED =====");
        System.Diagnostics.Debug.WriteLine($"   Transfer ID: {_transferId}");
        System.Diagnostics.Debug.WriteLine($"   Original item IDs: [{string.Join(", ", _originalItemIds)}]");

        if (itemsToAddMore.Count == 0 && _newItems.Count == 0)
        {
            await ShowErrorDialog("ไม่มีรายการ", "กรุณาระบุจำนวนที่ต้องการเบิกเพิ่ม หรือเพิ่มรายการใหม่");
            return;
        }

        try
        {
            LoadingOverlay.Visibility = Visibility.Visible;

            System.Diagnostics.Debug.WriteLine($"🔄 Reloading latest data from DB...");

            var latestTransfer = await _transferService.GetTransferByIdAsync(_transferId);
            if (latestTransfer == null)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                await ShowErrorDialog("ข้อผิดพลาด", "ไม่พบใบTransferในระบบ");
                return;
            }

            var latestItemIds = new HashSet<int>(latestTransfer.Items.Select(i => i.Id));
            System.Diagnostics.Debug.WriteLine($"   📋 Latest item IDs in DB: [{string.Join(", ", latestItemIds)}]");

            int successCount = 0;
            int newItemsCount = 0;
            var failedItems = new List<string>();

            var validExistingItems = itemsToAddMore
                .Where(kv => _originalItemIds.Contains(kv.Key) && latestItemIds.Contains(kv.Key))
                .ToList();

            var invalidItems = itemsToAddMore
                .Where(kv => _originalItemIds.Contains(kv.Key) && !latestItemIds.Contains(kv.Key))
                .ToList();

            // รายงานรายการที่ถูกลบไปแล้ว
            foreach (var kv in invalidItems)
            {
                var item = _transfer!.Items.FirstOrDefault(i => i.Id == kv.Key);
                var itemName = item?.ProductName ?? $"ItemID={kv.Key}";
                failedItems.Add($"{itemName} (ถูกลบไปแล้ว)");
                System.Diagnostics.Debug.WriteLine($"   ❌ Item removed from DB: ID={kv.Key} - {itemName}");
            }

            System.Diagnostics.Debug.WriteLine($"📤 Valid existing items to add more: {validExistingItems.Count}");
            
            foreach (var kv in validExistingItems)
            {
                System.Diagnostics.Debug.WriteLine($"📞 AddMoreQuantityAsync(ItemID={kv.Key}, +{kv.Value})");
                
                try
                {
                    bool success = await _transferService.AddMoreQuantityAsync(
                        kv.Key, 
                        kv.Value, 
                        Environment.UserName
                    );

                    if (success)
                    {
                        successCount++;
                        System.Diagnostics.Debug.WriteLine($"   ✅ SUCCESS for ItemID={kv.Key}");
                    }
                    else
                    {
                        var item = _transfer!.Items.FirstOrDefault(i => i.Id == kv.Key);
                        var itemName = item?.ProductName ?? $"ItemID={kv.Key}";
                        failedItems.Add($"{itemName} (ไม่สามารถเบิกเพิ่ม)");
                        System.Diagnostics.Debug.WriteLine($"   ❌ FAILED for ItemID={kv.Key}");
                    }
                }
                catch (Exception ex)
                {
                    var item = _transfer!.Items.FirstOrDefault(i => i.Id == kv.Key);
                    var itemName = item?.ProductName ?? $"ItemID={kv.Key}";
                    failedItems.Add($"{itemName} (เกิดข้อผิดพลาด)");
                    System.Diagnostics.Debug.WriteLine($"   💥 EXCEPTION for ItemID={kv.Key}: {ex.Message}");
                }
            }

            // 2. เพิ่มรายการใหม่
            if (_newItems.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"📞 AddMultipleItemsAsync(Count={_newItems.Count})");
                
                try
                {
                    bool success = await _transferService.AddMultipleItemsAsync(
                        _transferId,
                        _newItems,
                        Environment.UserName
                    );

                    if (success)
                    {
                        newItemsCount = _newItems.Count;
                        System.Diagnostics.Debug.WriteLine($"   ✅ SUCCESS - Added {newItemsCount} new items");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"   ❌ FAILED to add new items");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"   💥 EXCEPTION adding new items: {ex.Message}");
                }
            }

            LoadingOverlay.Visibility = Visibility.Collapsed;

            // สร้างข้อความผลลัพธ์
            string resultMessage = "";
            bool anyChanges = successCount > 0 || newItemsCount > 0;

            if (anyChanges)
            {
                resultMessage = $"บันทึกเรียบร้อยแล้ว\n\n";
                if (successCount > 0)
                    resultMessage += $"✓ เบิกเพิ่มรายการเดิม: {successCount} รายการ\n";
                if (newItemsCount > 0)
                    resultMessage += $"✓ เพิ่มรายการใหม่: {newItemsCount} รายการ";
            }

            // แสดงรายการที่ล้มเหลว (ถ้ามี)
            if (failedItems.Count > 0)
            {
                string failedMessage = anyChanges 
                    ? $"{resultMessage}\n\n⚠️ รายการที่ไม่สามารถดำเนินการได้:\n• {string.Join("\n• ", failedItems)}"
                    : $"ไม่สามารถดำเนินการได้:\n• {string.Join("\n• ", failedItems)}";

                if (anyChanges)
                {
                    // มีบางรายการสำเร็จ
                    await ShowSuccessDialog("บันทึกบางส่วน", failedMessage);

                    TransferEvents.NotifyTransferChanged(_transferId);
                    Frame.GoBack();
                }
                else
                {
                    // ไม่มีรายการไหนสำเร็จ
                    await ShowErrorDialog("บันทึกไม่สำเร็จ", failedMessage);
                }
                return;
            }

            if (!anyChanges)
            {
                await ShowErrorDialog("บันทึกไม่สำเร็จ", "ไม่มีการเปลี่ยนแปลงที่บันทึกได้");
                return;
            }

            // ทุกอย่างสำเร็จ
            await ShowSuccessDialog("สำเร็จ", resultMessage);

            TransferEvents.NotifyTransferChanged(_transferId);
            Frame.GoBack();
        }
        catch (Exception ex)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            System.Diagnostics.Debug.WriteLine($"❌ Save error: {ex.Message}");
            await ShowErrorDialog("เกิดข้อผิดพลาด", ex.Message);
        }

    }
    // ------------------- NEW: Hidden cost helpers -------------------

    private void UpdateHiddenCostDisplay()
    {
        if (_transfer == null) return;

        var hiddenCostPercentage = _transfer.HiddenCostPercentage ?? 0m;

        // Set percentage text
        try
        {
            if (HiddenCostPercentageText != null)
                HiddenCostPercentageText.Text = $"{hiddenCostPercentage:0}%";
        }
        catch { /* ignore if control missing */ }

        // Compute per-person hidden cost using already calculated _totalCost
        var expectedPeople = _transfer.ExpectedPeople;
        var computedCostPerPerson = expectedPeople > 0 ? _totalCost / expectedPeople : 0m;
        var hiddenCostAmount = computedCostPerPerson * (hiddenCostPercentage / 100m);

        try
        {
            if (HiddenCostAmountText != null)
                HiddenCostAmountText.Text = $"≈ {hiddenCostAmount:N4} ฿/คน";
        }
        catch { /* ignore */ }

        UpdateTotalCostWithHiddenDisplay(computedCostPerPerson, hiddenCostAmount, hiddenCostPercentage);
    }

    private void UpdateTotalCostWithHiddenDisplay(decimal costPerPerson, decimal hiddenCostAmount, decimal hiddenCostPercentage)
    {
        if (_transfer == null) return;

        var totalCostWithHidden = costPerPerson + hiddenCostAmount;

        try
        {
            if (TotalCostWithHiddenText != null)
                TotalCostWithHiddenText.Text = $"{totalCostWithHidden:N4} ฿/คน";
        }
        catch { /* ignore */ }

        // Determine outlet price-per-head (prefer saved snapshot when available)
        int? targetOutletId = _transfer.OutletId;
        decimal? outletPricePerHead = null;
        DateTime? outletPriceModifiedDate = null;

        if (_transfer.OutletPricePerHeadAtSave.HasValue)
        {
            outletPricePerHead = _transfer.OutletPricePerHeadAtSave;
            outletPriceModifiedDate = _transfer.OutletPricePerHeadSavedAt;
        }
        else
        {
            if (targetOutletId.HasValue && _Outlets != null && _Outlets.Count > 0)
            {
                var o = _Outlets.FirstOrDefault(x => x.Id == targetOutletId.Value);
                if (o != null)
                {
                    outletPricePerHead = o.PricePerHead;
                    outletPriceModifiedDate = o.ModifiedDate;
                }
            }
        }

        try
        {
            if (TotalCostBreakdownText != null)
            {
                TotalCostBreakdownText.Text = $"ต้นทุนต่อคน {costPerPerson:N4} + ต้นทุนแฝง {hiddenCostPercentage:0}% ({hiddenCostAmount:N4})";
                TotalCostBreakdownText.Visibility = Visibility.Visible;
            }
        }
        catch { /* ignore */ }

        if (outletPricePerHead.HasValue)
        {
            var dateText = outletPriceModifiedDate.HasValue ? outletPriceModifiedDate.Value.ToLocalTime().ToString("dd/MM/yyyy") : "ไม่ระบุวันที่";
            try
            {
                if (TotalCostTargetText != null)
                {
                    TotalCostTargetText.Text = $"งบต่อคน : {outletPricePerHead.Value:N4} ฿";
                    TotalCostTargetText.Visibility = Visibility.Visible;
                }
            }
            catch { }

            // Compare and set visuals
            if (totalCostWithHidden > outletPricePerHead.Value)
            {
                try
                {
                    TotalCostWithHiddenCard.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, 220, 38, 28));
                    TotalCostWithHiddenCard.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 220, 38, 28));
                    TotalCostWithHiddenText!.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 220, 38, 28));
                    TotalCostTargetText!.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 220, 38, 28));

                    if (TotalCostWarningText != null)
                    {
                        var exceed = totalCostWithHidden - outletPricePerHead.Value;
                        TotalCostWarningText.Text = $"⚠️ เกินงบ {exceed:N4} ฿/คน";
                        TotalCostWarningText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 220, 38, 28));
                        TotalCostWarningText.Visibility = Visibility.Visible;
                    }
                }
                catch { }
            }
            else
            {
                try
                {
                    TotalCostWithHiddenCard.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, 34, 197, 94));
                    TotalCostWithHiddenCard.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 34, 197, 94));
                    TotalCostWithHiddenText!.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 34, 197, 94));
                    TotalCostTargetText!.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 34, 197, 94));

                    if (TotalCostWarningText != null)
                    {
                        var remaining = outletPricePerHead.Value - totalCostWithHidden;
                        TotalCostWarningText.Text = $"✓ เหลืองบ {remaining:N4} ฿/คน";
                        TotalCostWarningText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 34, 197, 94));
                        TotalCostWarningText.Visibility = Visibility.Visible;
                    }
                }
                catch { }
            }
        }
        else
        {
            // No outlet target — hide target & warning and restore default visuals
            try
            {
                if (TotalCostTargetText != null) TotalCostTargetText.Visibility = Visibility.Collapsed;
                if (TotalCostWarningText != null) TotalCostWarningText.Visibility = Visibility.Collapsed;

                var defaultBg = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.SolidColorBrush;
                var defaultBorder = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Microsoft.UI.Xaml.Media.SolidColorBrush;

                if (defaultBg != null) TotalCostWithHiddenCard.Background = defaultBg;
                if (defaultBorder != null) TotalCostWithHiddenCard.BorderBrush = defaultBorder;

                TotalCostWithHiddenText!.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 139, 92, 246)); // #8B5CF6
            }
            catch { }
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Frame.GoBack();

    private async Task ShowErrorDialog(string title, string message)
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

    private async Task ShowSuccessDialog(string title, string message)
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

    private async Task ShowSuccessInfoBar(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "✅ สำเร็จ",
            Content = message,
            CloseButtonText = "ตกลง",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private Windows.Globalization.NumberFormatting.DecimalFormatter CreateDecimalFormatter()
    {
        return new Windows.Globalization.NumberFormatting.DecimalFormatter
        {
            IntegerDigits = 1,
            FractionDigits = 4,
            IsDecimalPointAlwaysDisplayed = true,
            IsZeroSigned = false
        };
    }
}

// 🆕 เพิ่ม INotifyPropertyChanged สำหรับ Binding
public class TransferItemDisplay : INotifyPropertyChanged
{
    public TransferItem Item { get; set; } = null!;
    public bool IsNewItem { get; set; }
    
    public Visibility DeleteButtonVisibility => IsNewItem ? Visibility.Visible : Visibility.Collapsed;

    private decimal _additionalQuantity;
    public decimal AdditionalQuantity
    {
        get => _additionalQuantity;
        set
        {
            if (_additionalQuantity != value)
            {
                _additionalQuantity = value;
                OnPropertyChanged(nameof(AdditionalQuantity));
                OnPropertyChanged(nameof(AdditionalQuantityDisplay));
            }
        }
    }

    public string AdditionalQuantityDisplay => 
        AdditionalQuantity > 0 
            ? $"+{AdditionalQuantity:N4}" 
            : "-";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
