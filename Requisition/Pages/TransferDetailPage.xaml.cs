using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Requisition.Models;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using System.Text.Json;
using System.ComponentModel;

namespace Requisition.Pages
{
    public sealed partial class TransferDetailPage : Page, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _canEditItems;
        public bool CanEditItems
        {
            get => _canEditItems;
            set
            {
                if (_canEditItems == value) return;
                _canEditItems = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEditItems)));
            }
        }

        private readonly TransferService _transferService;
        private readonly ProductService _productService;
        private readonly CostPerHeadService _costPerHeadService;
        private readonly TemplateService _templateService;

        private Models.Transfer? _currentTransfer;
        private int _transferId;
        private bool _isReadOnly;

        private List<Product> _availableProducts = new();
        private bool _hasPendingChanges = false;
        private List<TransferItem> _itemsToAdd = new();
        private decimal _totalCost = 0;

        private DateTimeOffset? _pendingUsageDate = null;
        private int? _pendingOutletId = null;
        private bool _hasUserModifiedDate = false;
        private bool _hasUserModifiedOutlet = false;

        private int _nextTempId = -1;

        private List<Template> _templates = new();

        private int? _activeTemplateId = null;
        private string? _activeTemplateName = null;

        private List<Requisition.Models.Outlet> _outlets = new();

        private List<Requisition.Models.Kitchen> _kitchens = new();
        private int? _pendingKitchenId = null;
        private bool _hasUserModifiedKitchen = false;

        // ADD: field near other private fields
        private int? _scrollToItemId = null;

        public TransferDetailPage()
        {
            InitializeComponent();
            _transferService = new TransferService();
            _productService = new ProductService();
            _costPerHeadService = new CostPerHeadService();
            _templateService = new TemplateService();

            Loaded += TransferDetailPage_Loaded;

            UsageDatePicker.DateChanged += UsageDatePicker_DateChanged!;
            OutletCombo.SelectionChanged += OutletCombo_SelectionChanged;
            KitchenCombo.SelectionChanged += KitchenCombo_SelectionChanged;

            TransferEvents.TransferChanged += OnTransferChanged;

            System.Diagnostics.Debug.WriteLine($"🔔 TransferDetailPage subscribed to events");
        }

        private async void OnTransferChanged(object? sender, int transferId)
        {
            if (transferId == _transferId)
            {
                System.Diagnostics.Debug.WriteLine($"🔔 RECEIVED change notification for transfer {transferId}");
                System.Diagnostics.Debug.WriteLine($"   Current page transfer ID: {_transferId}");

                await Task.Delay(100);

                DispatcherQueue.TryEnqueue(async () =>
                {
                    System.Diagnostics.Debug.WriteLine($"🔄 Reloading transfer data...");
                    await LoadTransferAsync();
                    System.Diagnostics.Debug.WriteLine($"✅ Reload completed");
                });
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            TransferEvents.TransferChanged -= OnTransferChanged;
            System.Diagnostics.Debug.WriteLine($"🔔 TransferDetailPage unsubscribed from events");
        }

        private void UsageDatePicker_DateChanged(object sender, DatePickerValueChangedEventArgs e)
        {
            if (_currentTransfer != null && UsageDatePicker?.Date != null)
            {
                _pendingUsageDate = UsageDatePicker.Date;
                _hasUserModifiedDate = true;
                System.Diagnostics.Debug.WriteLine($"📅 User changed date: {_pendingUsageDate}");
            }
        }

        private async Task LoadOutletsAsync()
        {
            try
            {
                OutletCombo.Items.Clear();

                // เรียก service จริงแล้วกรองเฉพาะ outlet ที่ active เท่านั้น
                _outlets = await _costPerHeadService.GetAllAsync() ?? new List<Requisition.Models.Outlet>();
                _outlets = _outlets.Where(o => o != null && o.IsActive).ToList();

                foreach (var o in _outlets)
                {
                    OutletCombo.Items.Add(new ComboBoxItem { Content = o.Name ?? $"#{o.Id}", Tag = o.Id });
                }

                System.Diagnostics.Debug.WriteLine($"LoadOutletsAsync: loaded {_outlets.Count} active outlets");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadOutletsAsync failed: {ex.Message}");
                _outlets = new List<Requisition.Models.Outlet>();
            }
        }

        private async Task LoadKitchensAsync()
        {
            try
            {
                KitchenCombo.Items.Clear();

                var kitchenService = new KitchenService();
                _kitchens = await kitchenService.GetAllAsync();

                // กรองเฉพาะที่ยัง active
                _kitchens = _kitchens.Where(k => k != null && k.IsActive).ToList();

                foreach (var k in _kitchens)
                {
                    KitchenCombo.Items.Add(new ComboBoxItem { Content = k.Name ?? $"#{k.Id}", Tag = k.Id });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadKitchensAsync failed: {ex.Message}");
                _kitchens = new List<Kitchen>();
            }
        }

        private async Task LoadCurrentPricesAsync()
        {
            try
            {
                if (_currentTransfer == null || _currentTransfer.Items == null || _currentTransfer.Items.Count == 0)
                    return;

                foreach (var item in _currentTransfer.Items)
                {
                    if (item.UnitPrice.HasValue)
                    {
                        item.CurrentPrice = item.UnitPrice;
                        continue;
                    }

                    try
                    {
                        var prod = await _productService.GetProductByCodeAsync(item.ProductCode);
                        if (prod?.Price != null)
                        {
                            item.CurrentPrice = prod.Price;
                        }
                    }
                    catch (Exception exInner)
                    {
                        System.Diagnostics.Debug.WriteLine($"LoadCurrentPricesAsync: failed to get price for {item.ProductCode}: {exInner.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadCurrentPricesAsync failed: {ex.Message}");
            }
        }

        private async Task LoadTemplatesAsync()
        {
            try
            {
                // ดึง templates ทั้งหมด แล้วกรองเฉพาะที่ยังไม่ถูกลบ (IsDeleted == false)
                _templates = await _templateService.GetAllAsync();
                _templates = _templates.Where(t => t != null && !t.IsDeleted).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadTemplatesAsync failed: {ex.Message}");
                _templates = new List<Template>();
            }
        }

        private Task LoadTemplatesForOutletAsync(int? outletId)
        {
            try
            {
                var filtered = _templates
                    .Where(t => !t.IsDeleted && (outletId == null ? t.OutletId == null : t.OutletId == outletId))
                    .OrderByDescending(t => t.LastModifiedDate ?? t.CreatedDate)
                    .ToList();

                var displayItems = filtered.Select(t => new TemplateDisplayItem
                {
                    Template = t,
                    IsActive = _activeTemplateId.HasValue && t.Id == _activeTemplateId.Value
                }).ToList();

                TemplatesListView.ItemsSource = displayItems;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadTemplatesForOutletAsync failed: {ex.Message}");
                TemplatesListView.ItemsSource = null;
            }
            return Task.CompletedTask;
        }

        private void UpdateActiveTemplateDisplay()
        {
            var activeText = this.FindName("ActiveTemplateText") as global::Microsoft.UI.Xaml.Controls.TextBlock;
            if (activeText == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_activeTemplateName))
            {
                activeText.Text = $"กำลังใช้ TEMPLATE: {_activeTemplateName.ToUpperInvariant()}";
                activeText.Visibility = Visibility.Visible;
                activeText.FontSize = 14;
                activeText.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                activeText.Text = string.Empty;
                activeText.Visibility = Visibility.Collapsed;
            }
        }

        private void OutletCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentTransfer != null && OutletCombo.SelectedItem is ComboBoxItem item && item.Tag is int oid)
            {
                _pendingOutletId = oid;
                _hasUserModifiedOutlet = true;
                System.Diagnostics.Debug.WriteLine($"📍 User changed outlet: {_pendingOutletId}");

                _activeTemplateId = null;
                _activeTemplateName = null;
                UpdateActiveTemplateDisplay();

                _ = LoadTemplatesForOutletAsync(_pendingOutletId);
            }
            else
            {
                _pendingOutletId = null;
                TemplatesListView.ItemsSource = null;
            }
        }

        private void KitchenCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentTransfer == null || KitchenCombo.SelectedItem == null)
                return;

            if (KitchenCombo.SelectedItem is ComboBoxItem selected && selected.Tag is int kitchenId)
            {
                if (_currentTransfer.KitchenId != kitchenId)
                {
                    _pendingKitchenId = kitchenId;
                    _hasUserModifiedKitchen = true;
                    System.Diagnostics.Debug.WriteLine($"🍳 User changed kitchen: {kitchenId}");
                }
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            InitializeDatePicker(); // เรียกใช้งานที่นี่

            if (e.Parameter is int transferId)
            {
                _transferId = transferId;
                await LoadTransferAsync();
            }
            else if (e.Parameter is TransferDetailPageParameter param)
            {
                _transferId = param.TransferId;
                _isReadOnly = param.IsReadOnly;
                await LoadTransferAsync();
            }
        }

        private void InitializeDatePicker()
        {
            // ตั้งค่า Calendar เป็นแบบไทย
            UsageDatePicker.CalendarIdentifier = Windows.Globalization.CalendarIdentifiers.Thai;
            
            // ตั้งค่า Language เป็นไทย (Optional)
            UsageDatePicker.Language = "th-TH";
        }

        private async void TransferDetailPage_Loaded(object sender, RoutedEventArgs e)
        {
            // ยังไม่พร้อมใช้งานในช่วง Loaded
            // await Task.Delay(100);
            // MarkAsChanged();
        }

        private async Task LoadTransferAsync()
        {
            try
            {
                ShowLoading(true);

                // ⚠️ FIX: เก็บ _itemsToAdd ก่อน Reload
                var preservedItemsToAdd = new List<TransferItem>(_itemsToAdd);

                _currentTransfer = await _transferService.GetTransferByIdAsync(_transferId);

                if (_currentTransfer == null)
                {
                    await ShowErrorDialog("ผิดพลาด", "ไม่พบข้อมูลที่ร้องขอ");
                    Frame.GoBack();
                    return;
                }

                // Load outlets so OutletCombo can be populated before UpdateUI selects item
                await LoadOutletsAsync();
                await LoadKitchensAsync();
                // ดึงราคาปัจจุบัน
                await LoadCurrentPricesAsync();

                // ⚠️ FIX: รวม items จาก DB + pending items — preserve object identity by Id
                foreach (var preserved in preservedItemsToAdd)
                {
                    // หากมี item เดิมใน _currentTransfer ที่มี Id เดียวกัน ให้แทนที่ด้วย preserved instance
                    var existingById = _currentTransfer.Items.FirstOrDefault(i => i.Id == preserved.Id);
                    if (existingById != null)
                    {
                        int idx = _currentTransfer.Items.IndexOf(existingById);
                        if (idx >= 0)
                        {
                            _currentTransfer.Items[idx] = preserved;
                        }
                    }
                    else
                    {
                        // หากยังไม่มี item ที่มี Id นี้ ให้เพิ่ม preserved instance เข้าไป
                        _currentTransfer.Items.Add(preserved);
                    }
                }

                // คืนค่า _itemsToAdd (preserved instances)
                _itemsToAdd = preservedItemsToAdd;

                // คำนวณต้นทุน
                _totalCost = CalculateTotalCost();

                UpdateUI();
                await LoadHistoryAsync();
                await LoadAvailableProductsAsync();
                await LoadTemplatesAsync();
                await LoadTemplatesForOutletAsync(_currentTransfer.OutletId);

                // Detect current template usage (if any)
                DetectActiveTemplateFromItems();

                // --- SCROLL TO EDITED ITEM IF REQUESTED ---
                if (_scrollToItemId.HasValue && _currentTransfer != null)
                {
                    var targetId = _scrollToItemId.Value;
                    var targetItem = _currentTransfer.Items.FirstOrDefault(i => i.Id == targetId);
                    if (targetItem != null && ItemsListView != null)
                    {
                        // Ensure UI updated then scroll (use dispatcher for virtualization)
                        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                        dq.TryEnqueue(async () =>
                        {
                            await Task.Delay(60);
                            try
                            {
                                ItemsListView.ScrollIntoView(targetItem);
                            }
                            catch { /* ignore */ }
                            _scrollToItemId = null;
                        });
                    }
                    else
                    {
                        // keep the id so next reload can try again
                    }
                }
                // --- end scroll logic ---
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

        private void ApplyDraftToModel(TransferDraft draft)
        {
            if (draft == null) return;

            // Apply simple fields
            if (draft.UsageDate.HasValue)
            {
                _pendingUsageDate = new DateTimeOffset(draft.UsageDate.Value);
                _hasUserModifiedDate = true;
            }

            if (draft.OutletId.HasValue)
            {
                _pendingOutletId = draft.OutletId;
                _hasUserModifiedOutlet = true;
            }

            if (draft.KitchenId.HasValue)
            {
                _pendingKitchenId = draft.KitchenId;
                _hasUserModifiedKitchen = true;
            }

            if (!string.IsNullOrWhiteSpace(draft.Notes))
            {
                // apply to notes UI and model
                NotesTextBox.Text = draft.Notes!;
                if (_currentTransfer != null)
                    _currentTransfer.Notes = draft.Notes;
            }

            // Items: merge staged draft items into _itemsToAdd and _currentTransfer.Items (avoid duplicates)
            if (draft.Items != null && draft.Items.Count > 0)
            {
                foreach (var di in draft.Items)
                {
                    // If item already exists in _currentTransfer.Items by ProductCode, skip
                    bool existsInCurrent = _currentTransfer?.Items.Any(i => string.Equals(i.ProductCode, di.ProductCode, StringComparison.OrdinalIgnoreCase)) == true;
                    bool existsInStaged = _itemsToAdd.Any(i => i.Id == di.Id || (string.Equals(i.ProductCode, di.ProductCode, StringComparison.OrdinalIgnoreCase) && i.InitialQuantity == di.InitialQuantity));

                    if (existsInCurrent) continue;
                    if (existsInStaged) continue;

                    var tempItem = new TransferItem
                    {
                        Id = di.Id <= 0 ? di.Id : _nextTempId--, // ensure negative temp ids if stored wrongly
                        TransferId = _transferId,
                        ProductCode = di.ProductCode,
                        ProductName = di.ProductName,
                        InitialQuantity = di.InitialQuantity,
                        Unit = di.Unit ?? string.Empty,
                        UnitPrice = di.UnitPrice,
                        Notes = di.Notes
                    };

                    _itemsToAdd.Add(tempItem);
                    _currentTransfer?.Items.Add(tempItem);
                }
            }
        }
        // Save current staged state as draft (staged items + pending fields)
        private async Task SaveDraftAsync()
        {
            try
            {
                // Build draft object
                var draft = new TransferDraft
                {
                    TransferId = _transferId,
                    UsageDate = _pendingUsageDate?.DateTime ?? (UsageDatePicker?.Date.DateTime ?? (DateTime?)null),
                    OutletId = _hasUserModifiedOutlet ? _pendingOutletId : _currentTransfer?.OutletId,
                    KitchenId = _hasUserModifiedKitchen ? _pendingKitchenId : _currentTransfer?.KitchenId,
                    Notes = NotesTextBox?.Text,
                    SavedAt = DateTime.UtcNow,
                    Items = _itemsToAdd.Select(i => new TransferItemDraft
                    {
                        Id = i.Id,
                        ProductCode = i.ProductCode,
                        ProductName = i.ProductName,
                        InitialQuantity = i.InitialQuantity,
                        Unit = i.Unit,
                        UnitPrice = i.UnitPrice,
                        Notes = i.Notes
                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveDraftAsync failed: {ex.Message}");
            }
        }

        // Called when the page is being unloaded
        private async void TransferDetailPage_Unloaded(object sender, RoutedEventArgs e)
        {
            await SaveDraftAsync();
        }

        private void MarkAsChanged()
        {
            _hasPendingChanges = _itemsToAdd.Count > 0 || HasAnyChanges();
            UpdateButtonVisibility();

            System.Diagnostics.Debug.WriteLine($"📝 Pending changes: {_hasPendingChanges}");
        }

        private bool HasAnyChanges()
        {
            if (_currentTransfer == null) return false;

            // เช็คหมายเหตุ
            if (!string.Equals(_currentTransfer.Notes?.Trim(), NotesTextBox?.Text?.Trim()))
                return true;

            // เช็ควันที่
            if (_hasUserModifiedDate) return true;
            if (_hasUserModifiedOutlet) return true;
            if (_hasUserModifiedKitchen) return true;

            return false;
        }

        private async Task LoadHistoryAsync()
        {
            try
            {
                var history = await _transfer_service_safe_call_GetHistory(_transferId);
                if (history.Count > 0)
                {
                    HistoryExpander.Visibility = Visibility.Visible;
                    HistoryListView.ItemsSource = history;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load history: {ex.Message}");
            }
        }

        private async Task<List<TransferHistory>> _transfer_service_safe_call_GetHistory(int transferId)
        {
            try
            {
                return await _transferService.GetTransferHistoryAsync(transferId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetTransferHistoryAsync failed: {ex.Message}");
                return new List<TransferHistory>();
            }
        }

        private async Task LoadAvailableProductsAsync()
        {
            // Only active products shown for selecting into transfers
            var products = await _productService.GetProductsAsync(includeInactive: false);

            // ✅ แก้ไข: เก็บไว้ใน _availableProducts ด้วย
            _availableProducts = products;

            // build view model / item wrapper as your UI expects
            var items = products.Select(p =>
            {
                var isAlready = _currentTransfer?.Items?.Any(i => i.ProductCode == p.Code) ?? false;
                return new
                {
                    Product = p,
                    IsAlreadySelected = isAlready,
                    IsNotAlreadySelected = !isAlready
                };
            }).ToList();

            AvailableProductsListView.ItemsSource = items;
            AvailableProductsCountText.Text = $"รายการสินค้า: {products.Count}";
        }

        /// <summary>
        /// Update available products list with visual indicators for already-selected items
        /// </summary>
        private void UpdateAvailableProductsDisplay()
        {
            if (_currentTransfer == null)
            {
                AvailableProductsListView.ItemsSource = _availableProducts;
                AvailableProductsCountText.Text = $"รายการสินค้า: {_availableProducts.Count}";
                return;
            }

            // Get list of already-selected product codes
            var selectedCodes = new HashSet<string>(
                _currentTransfer.Items.Select(i => i.ProductCode),
                StringComparer.OrdinalIgnoreCase
            );

            // Create a wrapper class to include selection state
            var displayProducts = _availableProducts.Select(p => new ProductDisplayItem
            {
                Product = p,
                IsAlreadySelected = selectedCodes.Contains(p.Code ?? string.Empty)
            }).ToList();

            AvailableProductsListView.ItemsSource = displayProducts;
            AvailableProductsCountText.Text = $"รายการสินค้า: {displayProducts.Count}";
        }

        // Add this nested class at the end of the TransferDetailPage class
        private class ProductDisplayItem
        {
            public Product Product { get; set; } = null!;
            public bool IsAlreadySelected { get; set; }

            // ⚠️ เพิ่ม property นี้เพื่อให้ปุ่ม "เพิ่ม" ใช้งานได้
            public bool IsNotAlreadySelected => !IsAlreadySelected;
        }

        // Add this nested class near ProductDisplayItem
        private class TemplateDisplayItem
        {
            public Template Template { get; set; } = null!;
            public bool IsActive { get; set; }
            public bool IsNotActive => !IsActive;

            // Expose Template properties for binding
            public int Id => Template.Id;
            public string? Name => Template.Name;
            public string? OutletName => Template.OutletName;
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
                filtered = _availableProducts
                    .Where(p => (p.Code ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase)
                             || (p.Name ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Get selected product codes
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
                Minimum = 0.0001,
                SmallChange = 0.0001,
                LargeChange = 1,
                MinWidth = 200,
                NumberFormatter = CreateDecimalFormatter() // ✅ เพิ่มบรรทัดนี้
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
                System.Diagnostics.Debug.WriteLine($"   Total items now: {_currentTransfer?.Items.Count ?? 0}");

                // ✅ สำคัญ: ต้อง Refresh UI
                MarkAsChanged();
                UpdateUI(); // <-- บังคับ refresh
                UpdateAvailableProductsDisplay();
                UpdateSummary(); // ✅ เพิ่มบรรทัดนี้
                // ✅ Scroll ไปที่รายการใหม่
                if (ItemsListView != null && newItem != null)
                {
                    ItemsListView.ScrollIntoView(newItem);
                }
            }
        }
        private async void EditItemButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"🔧 EditItemButton_Click triggered");

            if (sender is not Button btn)
            {
                System.Diagnostics.Debug.WriteLine($"❌ sender is not Button");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"✅ Button Tag: {btn.Tag}");

            int itemId;
            try
            {
                itemId = Convert.ToInt32(btn.Tag);
                System.Diagnostics.Debug.WriteLine($"✅ Item ID: {itemId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Parse error: {ex.Message}");
                await ShowErrorDialog("ข้อผิดพลาด", "ไม่สามารถอ่านข้อมูลรายการได้");
                return;
            }

            if (_currentTransfer == null || !_currentTransfer.CanEdit || _isReadOnly)
            {
                await ShowErrorDialog("ข้อผิดพลาด", "ไม่สามารถแก้ไขรายการได้");
                return;
            }

            // ⚠️ FIX: รวม items จาก _currentTransfer.Items + _itemsToAdd
            var allItems = _currentTransfer.Items.ToList();
            allItems.AddRange(_itemsToAdd);

            var item = allItems.FirstOrDefault(i => i.Id == itemId);

            if (item == null)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Item not found: ID={itemId}");
                System.Diagnostics.Debug.WriteLine($"   Available IDs: {string.Join(", ", allItems.Select(i => i.Id))}");
                await ShowErrorDialog("ข้อผิดพลาด", "ไม่พบรายการ");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"✅ Found item: {item.ProductName}");

            var qtyBox = new NumberBox
            {
                Header = "จำนวนเริ่มต้น",
                Value = 1,
                Minimum = 0.0001,
                SmallChange = 0.0001,
                LargeChange = 1,
                MinWidth = 200,
                NumberFormatter = CreateDecimalFormatter() // ✅ เพิ่มบรรทัดนี้
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
                        new TextBlock
                        {
                            Text = $"{item.ProductName} ({item.ProductCode})",
                            FontWeight = FontWeights.SemiBold
                        },
                        new TextBlock
                        {
                            Text = $"💰 ต้นทุน/หน่วย: {item.UnitPrice?.ToString("N4") ?? "ไม่ระบุ"} ฿",  // ✅ เปลี่ยนจาก CurrentPrice → UnitPrice
                            FontSize = 13,
                            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange)
                        },
                        qtyBox
                    }
                }
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (qtyBox.Value <= 0)
                {
                    await ShowErrorDialog("ข้อผิดพลาด", "จำนวนต้องมากกว่า 0");
                    return;
                }

                try
                {
                    decimal newQuantity = (decimal)qtyBox.Value;
                    decimal oldQuantity = item.InitialQuantity;

                    if (itemId > 0)
                    {
                        // Item ที่มีใน DB
                        ShowLoading(true);

                        item.InitialQuantity = newQuantity;
                        bool updateSuccess = await _transferService.UpdateItemAsync(item, Environment.UserName);

                        if (!updateSuccess)
                        {
                            item.InitialQuantity = oldQuantity;
                            ShowLoading(false);
                            await ShowErrorDialog("ล้มเหลว", "ไม่สามารถแก้ไขรายการได้");
                            return;
                        }

                        System.Diagnostics.Debug.WriteLine($"✅ Updated item ID={itemId} in database");

                        var tempItemsToAdd = new List<TransferItem>(_itemsToAdd);

                        // Request scroll to this item after reload
                        _scrollToItemId = itemId;

                        await LoadTransferAsync();
                        _itemsToAdd = tempItemsToAdd;
                        MarkAsChanged();
                        UpdateSummary(); // ✅ เพิ่มบรรทัดนี้

                        ShowLoading(false);
                        await ShowSuccessDialog("สำเร็จ", "แก้ไขรายการเรียบร้อยแล้ว");
                    }
                    else
                    {
                        // Item ใหม่ที่ยังไม่ได้บันทึก
                        item.InitialQuantity = newQuantity;

                        var existingItem = _itemsToAdd.FirstOrDefault(i => i.Id == itemId);
                        if (existingItem != null)
                        {
                            existingItem.InitialQuantity = newQuantity;
                        }

                        // ⚠️ สำคัญ: อัปเดต item ใน _currentTransfer.Items ด้วย
                        var itemInCurrent = _currentTransfer.Items.FirstOrDefault(i => i.Id == itemId);
                        if (itemInCurrent != null)
                        {
                            itemInCurrent.InitialQuantity = newQuantity;
                        }

                        _totalCost = CalculateTotalCost();
                        UpdateUI();
                        UpdateSummary(); // ✅ เพิ่มบรรทัดนี้
                        // Scroll into view the updated local item (use dispatcher to handle virtualization)
                        if (ItemsListView != null)
                        {
                            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                            dq.TryEnqueue(async () =>
                            {
                                await Task.Delay(60);
                                try
                                {
                                    ItemsListView.ScrollIntoView(item);
                                }
                                catch { }
                            });
                        }

                        System.Diagnostics.Debug.WriteLine($"✅ Updated new item (ID={itemId}) locally");
                        await ShowSuccessDialog("สำเร็จ", "แก้ไขรายการเรียบร้อยแล้ว");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Update error: {ex.Message}");
                    await ShowErrorDialog("เกิดข้อผิดพลาด", $"ไม่สามารถแก้ไขรายการได้: {ex.Message}");
                }
            }
        }

        private async void DeleteItemButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"🗑️ ===== DELETE CLICKED =====");
            System.Diagnostics.Debug.WriteLine($"   Sender: {sender?.GetType().Name}");

            if (sender is not Button btn)
            {
                System.Diagnostics.Debug.WriteLine($"❌ sender is not Button");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"✅ Button Tag Type: {btn.Tag?.GetType().Name}");
            System.Diagnostics.Debug.WriteLine($"✅ Button Tag Value: {btn.Tag}");

            int itemId;
            try
            {
                itemId = Convert.ToInt32(btn.Tag);
                System.Diagnostics.Debug.WriteLine($"✅ Item ID: {itemId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Parse error: {ex.Message}");
                await ShowErrorDialog("ข้อผิดพลาด", $"Tag parse failed: {btn.Tag}");
                return;
            }

            if (_currentTransfer == null)
            {
                System.Diagnostics.Debug.WriteLine($"❌ _currentTransfer is null");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"📋 Current items: {_currentTransfer.Items.Count}");
            System.Diagnostics.Debug.WriteLine($"📋 Items to add: {_itemsToAdd.Count}");

            // ⚠️ FIX: รวม items
            var allItems = _currentTransfer.Items.ToList();
            allItems.AddRange(_itemsToAdd);

            System.Diagnostics.Debug.WriteLine($"📋 Total items: {allItems.Count}");
            System.Diagnostics.Debug.WriteLine($"   IDs: {string.Join(", ", allItems.Select(i => i.Id))}");

            var item = allItems.FirstOrDefault(i => i.Id == itemId);

            if (item == null)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Item not found!");
                await ShowErrorDialog("ข้อผิดพลาด", $"ไม่พบรายการ ID={itemId}");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"✅ Found item: {item.ProductName} (ID={item.Id})");

            if (!_currentTransfer.CanEdit || _isReadOnly)
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

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ User cancelled");
                return;
            }

            try
            {
                if (itemId > 0)
                {
                    // ลบจาก DB
                    System.Diagnostics.Debug.WriteLine($"🗄️ Deleting from DB: ID={itemId}");
                    ShowLoading(true);

                    bool deleteSuccess = await _transferService.RemoveItemAsync(itemId, Environment.UserName);

                    if (!deleteSuccess)
                    {
                        ShowLoading(false);
                        await ShowErrorDialog("ล้มเหลว", "ไม่สามารถลบรายการได้");
                        return;
                    }

                    System.Diagnostics.Debug.WriteLine($"✅ Deleted from DB");

                    // ลบจาก local
                    _currentTransfer.Items.Remove(item);

                    // เก็บ _itemsToAdd
                    var tempItemsToAdd = new List<TransferItem>(_itemsToAdd);

                    // Reload
                    await LoadTransferAsync();

                    // คืนค่า _itemsToAdd
                    _itemsToAdd = tempItemsToAdd;
                    MarkAsChanged();
                    UpdateSummary(); // ✅ เพิ่มบรรทัดนี้
                    ShowLoading(false);
                    await ShowSuccessDialog("สำเร็จ", $"ลบรายการ '{item.ProductName}' เรียบร้อยแล้ว");
                }
                else
                {
                    // ลบ local เท่านั้น
                    System.Diagnostics.Debug.WriteLine($"🗑️ Removing local item: ID={itemId}");

                    _itemsToAdd.RemoveAll(i => i.Id == itemId);
                    _currentTransfer.Items.Remove(item);

                    _totalCost = CalculateTotalCost();

                    System.Diagnostics.Debug.WriteLine($"✅ Removed. Remaining: {_currentTransfer.Items.Count}");

                    // ⚠️ Force refresh
                    UpdateUI();
                    UpdateAvailableProductsDisplay();
                    UpdateSummary(); // ✅ เพิ่มบรรทัดนี้
                    await ShowSuccessDialog("สำเร็จ", $"ลบรายการ '{item.ProductName}' เรียบร้อยแล้ว");
                }

                System.Diagnostics.Debug.WriteLine($"===== DELETE COMPLETED =====");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Delete error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   StackTrace: {ex.StackTrace}");
                await ShowErrorDialog("เกิดข้อผิดพลาด", $"ไม่สามารถลบรายการได้: {ex.Message}");
            }
        }

        // Summary display elements
        // ---
        #endregion
        private void UpdateSummary()
        {
            if (_currentTransfer == null || _currentTransfer.Items == null)
            {
                if (SummaryTotalQuantityText != null)
                    SummaryTotalQuantityText.Text = "0.0000";
                if (SummaryTotalCostText != null)
                    SummaryTotalCostText.Text = "฿0.0000";
                return;
            }

            try
            {
                // คำนวณจำนวนรวม (ใช้ RemainingQuantityDouble - จำนวนหลังหักคืน)
                var totalQuantity = _currentTransfer.Items.Sum(i => i.RemainingQuantityDouble);

                // คำนวณต้นทุนรวม (ใช้ TotalCost ของแต่ละ item)
                var totalCost = _currentTransfer.Items.Sum(i => i.TotalCost);

                // อัปเดต UI
                if (SummaryTotalQuantityText != null)
                    SummaryTotalQuantityText.Text = totalQuantity.ToString("N4");

                if (SummaryTotalCostText != null)
                    SummaryTotalCostText.Text = $"฿{totalCost:N4}";

                System.Diagnostics.Debug.WriteLine($"📊 Summary Updated: Qty={totalQuantity:N4}, Cost=฿{totalCost:N4}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ UpdateSummary failed: {ex.Message}");
                
                if (SummaryTotalQuantityText != null)
                    SummaryTotalQuantityText.Text = "-";
                if (SummaryTotalCostText != null)
                    SummaryTotalCostText.Text = "฿-";
            }
        }

        #region Save & Delete
        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTransfer == null) return;

            // Find staged new items (in _itemsToAdd) that have quantity == 0
            var zeroQtyTemplateItems = _itemsToAdd
                .Where(i => i.InitialQuantity == 0)
                .ToList();

            if (zeroQtyTemplateItems.Any())
            {
                // Build a friendly list to show user
                var listText = string.Join("\n", zeroQtyTemplateItems.Select(i => $"- {i.ProductName} ({i.ProductCode})"));

                var promptDialog = new ContentDialog
                {
                    Title = "พบรายการที่มีจำนวนเป็น 0",
                    Content = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = "มีรายการที่ถูกเพิ่มจาก Template แต่จำนวนยังเป็น 0 ดังนี้:", TextWrapping = TextWrapping.Wrap },
                            new TextBlock { Text = listText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,6,0,0) },
                            new TextBlock { Text = "หากกด 'ยืนยัน' รายการที่เป็น 0 จะไม่ถูกบันทึก (จะถูกลบออก) และการบันทึกจะดำเนินต่อ หากกด 'ยกเลิก' จะยกเลิกการบันทึกทั้งหมดให้ผู้ใช้กลับไปแก้จำนวน", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,6,0,0) }
                        }
                    },
                    PrimaryButtonText = "ยืนยันและลบรายการ 0",
                    CloseButtonText = "ยกเลิก",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                var result = await promptDialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    // user cancelled save
                    return;
                }

                // Remove zero-quantity items from staging and from current transfer model
                foreach (var zero in zeroQtyTemplateItems)
                {
                    _itemsToAdd.RemoveAll(i => i.Id == zero.Id);
                    _currentTransfer.Items.RemoveAll(i => i.Id == zero.Id);
                }

                // Update state
                _totalCost = CalculateTotalCost();
                MarkAsChanged();
                UpdateUI();
                UpdateAvailableProductsDisplay();
                UpdateSummary(); // ✅ เพิ่มบรรทัดนี้
            }

            bool hasNewItems = _itemsToAdd.Count > 0;
            bool hasChanges = hasNewItems ||
                             !string.Equals(_currentTransfer.Notes?.Trim(), NotesTextBox.Text?.Trim()) ||
                             _hasUserModifiedDate ||
                             _hasUserModifiedOutlet ||
                             _hasUserModifiedKitchen;

            if (!hasChanges)
            {
                await ShowSuccessDialog("แจ้งเตือน", "ไม่มีการเปลี่ยนแปลงที่ต้องบันทึก");
                return;
            }

            // ตรวจสอบว่ามีรายการหรือไม่
            if (_currentTransfer.Items.Count == 0 && _itemsToAdd.Count == 0)
            {
                await ShowErrorDialog("ไม่สามารถบันทึกได้", "ใบTransferต้องมีรายการสินค้าอย่างน้อย 1 รายการ");
                return;
            }
            try
            {
                ShowLoading(true);

                // 1. Process additions
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

                    if (_currentTransfer.Status == TransferStatus.Draft)
                    {
                        _currentTransfer.Status = TransferStatus.InProgress;
                        System.Diagnostics.Debug.WriteLine("✅ Status changed: Draft → InProgress");
                    }
                }

                // 2. Update UsageDate and Outlet from user selection
                if (_pendingUsageDate.HasValue)
                {
                    _currentTransfer.UsageDate = _pendingUsageDate.Value.DateTime;
                }
                else
                {
                    _currentTransfer.UsageDate = UsageDatePicker.Date.DateTime;
                }

                int? finalOutletId = _hasUserModifiedOutlet ? _pendingOutletId : _currentTransfer.OutletId;
                _currentTransfer.OutletId = finalOutletId;

                // ✅ เพิ่มบรรทัดนี้
                int? finalKitchenId = _hasUserModifiedKitchen ? _pendingKitchenId : _currentTransfer.KitchenId;
                _currentTransfer.KitchenId = finalKitchenId;

                _currentTransfer.Notes = string.IsNullOrWhiteSpace(NotesTextBox.Text) ? null : NotesTextBox.Text.Trim();

                // --- IMPORTANT: we NO LONGER persist CostPerPerson snapshot --
                // keep outlet price snapshot only (so historical outlet rate is preserved).
                _totalCost = CalculateTotalCost();

                // Set outlet price snapshot if we have an outlet selected
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

                // 3. Update main transfer (will persist outlet snapshot)
                System.Diagnostics.Debug.WriteLine($"💾 Updating: Date={_currentTransfer.UsageDate}, OutletId={_currentTransfer.OutletId}, OutletPriceSnapshot={_currentTransfer.OutletPricePerHeadAtSave}");

                bool mainSuccess = await _transferService.UpdateTransferAsync(
                    _currentTransfer,
                    Environment.UserName
                );

                if (mainSuccess)
                {
                    // verify persisted snapshot by reloading only this transfer (light check)
                    var persisted = await _transfer_service_safe_call_GetById(_transferId); // helper to avoid exceptions
                    System.Diagnostics.Debug.WriteLine($"Persisted OutletPricePerHeadAtSave = {persisted?.OutletPricePerHeadAtSave}, savedAt = {persisted?.OutletPricePerHeadSavedAt}");

                    // If an active template name is set, add a history entry recording the template name (human readable)
                    if (!string.IsNullOrWhiteSpace(_activeTemplateName))
                    {
                        try
                        {
                            var newValues = JsonSerializer.Serialize(new { Template = _activeTemplateName });
                            await _transferService.AddHistoryEntryAsync(
                                _transferId,
                                "AppliedTemplate",
                                $"เลือก Template: {_activeTemplateName}",
                                Environment.UserName,
                                null,
                                newValues
                            );
                        }
                        catch (Exception hx)
                        {
                            System.Diagnostics.Debug.WriteLine($"AddHistoryEntryAsync failed: {hx.Message}");
                            // do not block save success if history write fails
                        }
                    }

                    // Reset flags after successful save
                    _hasPendingChanges = false;
                    _itemsToAdd.Clear();
                    _hasUserModifiedDate = false;
                    _hasUserModifiedOutlet = false;
                    _hasUserModifiedKitchen = false;
                    ShowLoading(false);

                    await ShowSuccessDialog("สำเร็จ",
                        _currentTransfer.Status == TransferStatus.InProgress
                            ? "บันทึกเรียบร้อยแล้ว\n\nสถานะได้เปลี่ยนเป็น 'กำลังดำเนินการ'"

                            : "บันทึกเรียบร้อยแล้ว");

                    // Reload to reflect DB state
                    ShowLoading(true);
                    await LoadTransferAsync();
                    System.Diagnostics.Debug.WriteLine($"✅ Reloaded: {_currentTransfer?.Items.Count ?? 0} items");
                }
                else
                {
                    ShowLoading(false);
                    await ShowErrorDialog("ล้มเหลว", "ไม่สามารถบันทึกข้อมูลได้");
                }
            }
            catch (Exception ex)
            {
                ShowLoading(false);
                System.Diagnostics.Debug.WriteLine($"❌ Save error: {ex.Message}");
                await ShowErrorDialog("เกิดข้อผิดพลาด", $"ข้อผิดพลาด: {ex.Message}");
            }
        }

        private async Task<Models.Transfer?> _transfer_service_safe_call_GetById(int id)
        {
            try
            {
                return await _transferService.GetTransferByIdAsync(id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetTransferByIdAsync failed: {ex.Message}");
                return null;
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTransfer == null) return;

            // ⚠️ เช็คว่าถูกลบไปแล้วหรือยัง UpdateSummary
            if (_currentTransfer.IsDeleted)
            {
                await ShowErrorDialog("ไม่สามารถลบได้", "ใบTransferนี้ถูกลบไปแล้ว");
                return;
            }

            // ถามเหตุผล
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
                    Text = "⚠️ หมายเหตุ: ข้อมูลจะยังอยู่ในระบบ แต่จะไม่แสดงในรายการ",
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

            // เปลี่ยนสีป.button ให้เป็นสีแดง
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

                // ⚠️ Soft Delete
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

            // Build UI to collect returned quantities per item
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
                    SmallChange = 0.0001,
                    LargeChange = 1,
                    Width = 140,
                    NumberFormatter = CreateDecimalFormatter() // ✅ เพิ่มบรรทัดนี้
                };

                boxes[item.Id] = nb;
                row.Children.Add(nb);
                panel.Children.Add(row);
            }

            var contentScroll = new ScrollViewer { Content = panel, MaxHeight = 420 };

            var dialog = new ContentDialog
            {
                Title = "จบงาน - ระบุจำนวนคืน",
                Content = contentScroll,
                PrimaryButtonText = "จบงาน",
                CloseButtonText = "ยกเลิก",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            // Gather returned quantities
            var returnedQuantities = new Dictionary<int, decimal>();
            foreach (var kv in boxes)
            {
                // NumberBox.Value is a double (non-nullable) in WinUI; handle NaN just in case
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

            try
            {
                ShowLoading(true);
                bool success = await _transferService.CompleteTransferAsync(_transferId, returnedQuantities, Environment.UserName);
                ShowLoading(false);

                if (success)
                {
                    await LoadTransferAsync();
                    await ShowSuccessDialog("สำเร็จ", "ทำการจบงานเรียบร้อยแล้ว");
                }
                else
                {
                    await ShowErrorDialog("ล้มเหลว", "ไม่สามารถจบงานได้");
                }
            }
            catch (Exception ex)
            {
                ShowLoading(false);
                System.Diagnostics.Debug.WriteLine($"CompleteTransferAsync failed: {ex.Message}");
                await ShowErrorDialog("เกิดข้อผิดพลาด", $"ไม่สามารถจบงาน: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// Helper method: หา child element ตามชื่อ
        /// </summary>
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

        // New ApplyTemplateButton_Click behavior (confirmation + full replace)
        private async void ApplyTemplateButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_currentTransfer == null)
            {
                await ShowErrorDialog("ข้อผิดพลาด", "ไม่พบข้อมูลใบTransfer");
                return;
            }

            // Disallow applying templates unless Draft
            if (_currentTransfer.Status != TransferStatus.Draft)
            {
                await ShowErrorDialog("ไม่สามารถเปลี่ยน Template", "ไม่สามารถเปลี่ยน Template ขณะใบTransfer อยู่ในสถานะที่ไม่ใช่ 'แบบร่าง'");
                return;
            }

            if (sender is not Button btn || btn.Tag == null) return;

            if (!int.TryParse(btn.Tag.ToString(), out int templateId))
            {
                await ShowErrorDialog("ข้อผิดพลาด", "ไม่สามารถอ่าน Template ได้");
                return;
            }

            // ✅ เพิ่ม: ตรวจสอบว่ากำลังใช้ template นี้อยู่หรือไม่
            if (_activeTemplateId.HasValue && _activeTemplateId.Value == templateId)
            {
                await ShowErrorDialog("ไม่สามารถเลือกได้", $"กำลังใช้ Template นี้อยู่แล้ว: {_activeTemplateName}");
                return;
            }

            var template = await _templateService.GetByIdAsync(templateId);
            if (template == null)
            {
                await ShowErrorDialog("ข้อผิดพลาด", "ไม่พบ Template");
                return;
            }

            // Confirm with user
            var confirmDialog = new ContentDialog
            {
                Title = "ยืนยันการเปลี่ยน Template",
                Content = $"คุณแน่ใจว่าจะเปลี่ยนเป็น Template: \"{template.Name}\"?\n\nการทำเช่นนี้จะลบรายการที่ได้มาจาก Template ก่อนหน้าออกทั้งหมด และนำรายการจาก Template นี้เข้ามาแทนที่\n\n(ค่าจำนวนของรายการที่ถูกเพิ่มจาก Template จะตั้งเป็น 0 โดยอัตโนมัติ — คุณสามารถแก้ไขจำนวนก่อนบันทึกได้)",
                PrimaryButtonText = "ยืนยัน",
                CloseButtonText = "ยกเลิก",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await confirmDialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return; // user cancelled
            }

            try
            {
                ShowLoading(true);

                // 1) Remove previously-applied template items (Notes starting with convention)
                var prevTemplateItems = _currentTransfer?.Items
                    .Where(i => !string.IsNullOrEmpty(i.Notes) && i.Notes.StartsWith("จาก Template:", StringComparison.Ordinal))
                    .ToList() ?? new List<TransferItem>();

                foreach (var old in prevTemplateItems)
                {
                    if (old.Id > 0)
                    {
                        // persisted -> remove immediately via service
                        try
                        {
                            bool ok = await _transferService.RemoveItemAsync(old.Id, Environment.UserName);
                            if (!ok)
                            {
                                ShowLoading(false);
                                await ShowErrorDialog("ล้มเหลว", $"ไม่สามารถลบรายการเดิม (ID={old.Id}) ได้");
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            ShowLoading(false);
                            System.Diagnostics.Debug.WriteLine($"RemoveItemAsync failed: {ex.Message}");
                            await ShowErrorDialog("เกิดข้อผิดพลาด", $"ไม่สามารถลบรายการเดิม: {ex.Message}");
                            return;
                        }
                    }
                    else
                    {
                        _itemsToAdd.RemoveAll(i => i.Id == old.Id);
                    }

                    _currentTransfer!.Items.RemoveAll(i => i.Id == old.Id);
                }

                // 2) Add new template items (as staged items with negative temp IDs) — quantity = 0
                foreach (var ingred in template.Ingredients)
                {
                    if (_currentTransfer!.Items.Any(i => string.Equals(i.ProductCode, ingred.ProductCode, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    int tempId = _nextTempId--;
                    decimal qty = 0m; // default 0 for template-added items

                    decimal? unitPrice = null;
                    try
                    {
                        var prod = await _productService.GetProductByCodeAsync(ingred.ProductCode);
                        if (prod?.Price != null) unitPrice = prod.Price.Value;
                    }
                    catch { }

                    var newItem = new TransferItem
                    {
                        Id = tempId,
                        TransferId = _transferId,
                        ProductCode = ingred.ProductCode,
                        ProductName = ingred.ProductName,
                        InitialQuantity = qty,
                        Unit = ingred.Unit,
                        UnitPrice = unitPrice,
                        PriceDate = unitPrice.HasValue ? DateTime.Now : (DateTime?)null,
                        Notes = $"จาก Template: {template.Name}"
                    };

                    _itemsToAdd.Add(newItem);
                    _currentTransfer.Items.Add(newItem);
                }

                _activeTemplateId = template.Id;
                _activeTemplateName = template.Name;
                UpdateActiveTemplateDisplay();

                _totalCost = CalculateTotalCost();
                MarkAsChanged();
                UpdateUI();
                UpdateAvailableProductsDisplay();
                UpdateSummary(); // ✅ เพิ่มบรรทัดนี้
                ShowLoading(false);
                await ShowSuccessDialog("สำเร็จ", $"เปลี่ยนมาใช้ Template '{template.Name}' เรียบร้อยแล้ว (จำนวนตั้งเป็น 0 ให้แก้ก่อนบันทึก)");
            }
            catch (Exception ex)
            {
                ShowLoading(false);
                System.Diagnostics.Debug.WriteLine($"ApplyTemplate failed: {ex.Message}");
                await ShowErrorDialog("เกิดข้อผิดพลาด", $"ไม่สามารถนำ Template มาใช้: {ex.Message}");
            }
        }

        // Detect which template (if any) provided current items and update the active-template display.
        // Place this inside the TransferDetailPage class.
        private void DetectActiveTemplateFromItems()
        {
            if (_currentTransfer == null)
            {
                _activeTemplateId = null;
                _activeTemplateName = null;
                UpdateActiveTemplateDisplay();
                return;
            }

            // Convention: template-applied items have Notes starting with "จาก Template: "
            var templItem = _currentTransfer.Items
                .FirstOrDefault(i => !string.IsNullOrEmpty(i.Notes) && i.Notes.StartsWith("จาก Template:", StringComparison.Ordinal));

            if (templItem != null)
            {
                var note = templItem.Notes!;
                var marker = "จาก Template:";
                var idx = note.IndexOf(marker, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var name = note.Substring(idx + marker.Length).Trim();
                    _activeTemplateName = string.IsNullOrEmpty(name) ? null : name;
                }
                else
                {
                    _activeTemplateName = null;
                }
            }
            else
            {
                _activeTemplateName = null;
            }

            UpdateActiveTemplateDisplay();
        }

        private decimal GetEffectiveCostPerPerson(bool preferSaved = false)
        {
            if (_currentTransfer == null) return 0m;

            // Fallback: compute from current items
            decimal totalCost = CalculateTotalCost(); // uses item.UnitPrice (saved) → TotalCost
            return _currentTransfer.ExpectedPeople > 0
                ? totalCost / _currentTransfer.ExpectedPeople
                : 0m;
        }
        private decimal CalculateTotalCost()
        {
            if (_currentTransfer == null) return 0m;

            decimal sum = 0m;
            int itemCount = 0;

            foreach (var item in _currentTransfer.Items ?? Enumerable.Empty<TransferItem>())
            {
                // จำนวนทั้งหมดที่ออก (Initial + Additional)
                decimal issuedQty = item.InitialQuantity + item.AdditionalQuantity;

                // จำนวนที่คืนแล้วใน DB
                decimal returnedQty = item.ReturnedQuantity ?? 0m;

                // จำนวนที่ผู้ใช้กรอกเพื่อคืน (pending) — มีใน model: ReturnQuantity
                decimal pendingReturn = item.ReturnQuantity;

                // จำนวนที่จะนำมาคำนวณต้นทุน = issued - returned - pending (ไม่ติดลบ)
                decimal effectiveQty = issuedQty - returnedQty - pendingReturn;
                if (effectiveQty < 0) effectiveQty = 0;

                decimal unitPrice = item.UnitPrice ?? (item.CurrentPrice ?? 0m);
                if (unitPrice < 0) unitPrice = 0;

                decimal itemTotal = effectiveQty * unitPrice;
                sum += itemTotal;
                itemCount++;

                System.Diagnostics.Debug.WriteLine($"  #{itemCount}: {item.ProductName} = {issuedQty:N4} - returned {returnedQty:N4} - pending {pendingReturn:N4} => {effectiveQty:N4} × {unitPrice:N4} = {itemTotal:N4}");
            }

            System.Diagnostics.Debug.WriteLine($"💰 Total Cost = {sum:N4}");
            return Math.Round(sum, 4, MidpointRounding.AwayFromZero); // ✅ แก้ไข: เพิ่ม MidpointRounding.AwayFromZero
        }

        private decimal GetCostPerPersonWithHidden()
        {
            if (_currentTransfer == null) return 0m;

            decimal perPerson = GetEffectiveCostPerPerson(); // already uses CalculateTotalCost()
            decimal hiddenPct = _currentTransfer.HiddenCostPercentage ?? 0m;

            if (hiddenPct == 0m) return perPerson;

            var result = perPerson * (1m + hiddenPct / 100m);
            return Math.Round(result, 4, MidpointRounding.AwayFromZero);
        }

        private void UpdateButtonVisibility()
        {
            if (_currentTransfer == null) return;

            // Editable only when Draft and not read-only
            bool canEdit = _currentTransfer.Status == TransferStatus.Draft && !_isReadOnly;
            CanEditItems = canEdit;

            // Action header column
            if (ActionHeaderText != null)
                ActionHeaderText.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;

            // Show/hide action buttons in each realized ListView item
            if (ItemsListView != null)
            {
                try { ItemsListView.UpdateLayout(); } catch { /* ignore timing issues */ }

                var panels = FindChildrenByName(ItemsListView, "ActionButtons");
                foreach (var panel in panels)
                {
                    if (panel is StackPanel sp)
                        sp.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            // Templates availability
            bool canChangeTemplate = _currentTransfer.Status == TransferStatus.Draft && !_isReadOnly;
            if (TemplatesListView != null)
                TemplatesListView.IsEnabled = canChangeTemplate;

            if (!canChangeTemplate && !string.IsNullOrWhiteSpace(_activeTemplateName))
                UpdateActiveTemplateDisplay();

            // ⚠️ เพิ่ม: ปิดการแก้ไข UsageDate และ Outlet เมื่อสถานะเป็น InProgress/Completed
            bool canEditDateAndOutlet = _currentTransfer.Status == TransferStatus.Draft && !_isReadOnly;
            if (UsageDatePicker != null)
                UsageDatePicker.IsEnabled = canEditDateAndOutlet;
            if (OutletCombo != null)
                OutletCombo.IsEnabled = canEditDateAndOutlet;
            if (KitchenCombo != null)
                KitchenCombo.IsEnabled = canEditDateAndOutlet;

            // Layout adjustments and available-products pane
            if (_currentTransfer.Status == TransferStatus.InProgress || _currentTransfer.Status == TransferStatus.Completed)
            {
                if (AvailableProductsPanel != null)
                {
                    AvailableProductsPanel.Visibility = Visibility.Collapsed;
                    if (AvailableProductsPanel.Parent is Grid grid && grid.ColumnDefinitions.Count >= 2)
                    {
                        grid.ColumnDefinitions[0].Width = new GridLength(0);
                        grid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
                    }
                }
            }
            else
            {
                if (AvailableProductsPanel != null)
                    AvailableProductsPanel.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;

                if (AvailableProductsPanel?.Parent is Grid grid && grid.ColumnDefinitions.Count >= 2)
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

                if (AvailableProductsListView != null) AvailableProductsListView.IsEnabled = canEdit;
                if (ProductSearchBox != null) ProductSearchBox.IsEnabled = canEdit;
                if (NotesTextBox != null) NotesTextBox.IsReadOnly = !canEdit;
            }

            // Notes editable for Draft or InProgress
            bool canEditNotes = (_currentTransfer.Status == TransferStatus.Draft || _currentTransfer.Status == TransferStatus.InProgress) && !_isReadOnly;
            if (NotesTextBox != null) NotesTextBox.IsReadOnly = !canEditNotes;

            // Save button only for Draft
            if (SaveButton != null)
            {
                if (_currentTransfer.Status == TransferStatus.Draft && !_isReadOnly)
                {
                    SaveButton.Visibility = Visibility.Visible;
                    SaveButton.IsEnabled = true;
                    SaveButton.Content = _hasPendingChanges ? "บันทึก *" : "บันทึก";
                }
                else
                {
                    SaveButton.Visibility = Visibility.Collapsed;
                    SaveButton.IsEnabled = false;
                }
            }

            // Delete only for Draft
            if (DeleteButton != null)
                DeleteButton.Visibility = (_currentTransfer.Status == TransferStatus.Draft && !_isReadOnly)
                    ? Visibility.Visible : Visibility.Collapsed;

            // Hide complete button here (kept collapsed per previous logic)
            if (CompleteButton != null)
                CompleteButton.Visibility = Visibility.Collapsed;
        }

        // Insert this method inside the TransferDetailPage class (near other UI helpers)
        private void UpdateUI()
        {
            if (_currentTransfer == null) return;

            try
            {
                // Header / status
                if (TransferNoText != null) TransferNoText.Text = _currentTransfer.TransferNo ?? string.Empty;
                if (StatusText != null) StatusText.Text = _currentTransfer.StatusText ?? string.Empty;

                // Created info
                if (CreatedDateText != null) CreatedDateText.Text = _currentTransfer.CreatedDate.ToString("dd/MM/yyyy HH:mm");
                if (CreatedByText != null) CreatedByText.Text = _currentTransfer.CreatedBy ?? "ไม่ทราบผู้สร้าง";

                // Usage date
                try
                {
                    UsageDatePicker.DateChanged -= UsageDatePicker_DateChanged!;
                    if (_hasUserModifiedDate && _pendingUsageDate.HasValue)
                        UsageDatePicker.Date = _pendingUsageDate.Value;
                    else if (_currentTransfer.UsageDate.HasValue)
                        UsageDatePicker.Date = new DateTimeOffset(_currentTransfer.UsageDate.Value);
                    else
                        UsageDatePicker.Date = DateTimeOffset.Now;
                }
                finally
                {
                    UsageDatePicker.DateChanged += UsageDatePicker_DateChanged!;
                }

                // Outlet
                int? targetOutletId = _hasUserModifiedOutlet ? _pendingOutletId : _currentTransfer.OutletId;
                if (OutletCombo != null)
                {
                    bool found = false;
                    foreach (var it in OutletCombo.Items)
                    {
                        if (it is ComboBoxItem cbi && cbi.Tag is int tag && targetOutletId.HasValue && tag == targetOutletId.Value)
                        {
                            OutletCombo.SelectedItem = cbi;
                            found = true;
                            break;
                        }
                    }
                    if (!found) OutletCombo.SelectedIndex = -1;
                }

                // Kitchen
                if (_currentTransfer.KitchenId.HasValue)
                {
                    var kitchenItem = KitchenCombo.Items.OfType<ComboBoxItem>()
                        .FirstOrDefault(i => i.Tag is int kid && kid == _currentTransfer.KitchenId.Value);
                    if (kitchenItem != null)
                    {
                        KitchenCombo.SelectedItem = kitchenItem;
                    }
                }

                // Summary
                if (ExpectedPeopleText != null) ExpectedPeopleText.Text = $"{_currentTransfer.ExpectedPeople} คน";
                _totalCost = CalculateTotalCost(); // ✅ คำนวณต้นทุน
                if (ItemCountText != null) ItemCountText.Text = _currentTransfer.ItemCount.ToString();
                if (TotalQuantityText != null) TotalQuantityText.Text = _currentTransfer.TotalQuantity.ToString("N4");

                // NEW: จำนวนเบิกทั้งหมดหลังคืน (รวม pending return ที่ผู้ใช้ตั้งไว้ใน TransferItem.ReturnQuantity)
                try
                {
                    decimal totalAfterReturn = 0m;
                    if (_currentTransfer.Items != null)
                    {
                        totalAfterReturn = _currentTransfer.Items.Sum(i => i.RemainingAfterPendingReturn);
                    }
                    if (TotalQuantityAfterReturnText != null)
                    {
                        TotalQuantityAfterReturnText.Text = $"หลังคืน: {totalAfterReturn:N4}";
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to compute totalAfterReturn: {ex.Message}");
                    if (TotalQuantityAfterReturnText != null)
                        TotalQuantityAfterReturnText.Text = "-";
                }

                UpdateBudgetDisplay(); // ✅ เรียกUpdateBudgetDisplay จะเรียก UpdateHiddenCostDisplay อัตโนมัติ
                UpdateSummary(); // ✅ เพิ่มบรรทัดนี้

                // ✅ สำคัญ: Items List
                var items = new System.Collections.ObjectModel.ObservableCollection<TransferItem>();
                if (_currentTransfer.Items != null)
                {
                    foreach (var it in _currentTransfer.Items)
                        items.Add(it);
                }

                System.Diagnostics.Debug.WriteLine($"📋 UpdateUI: {items.Count} items");

                if (ItemsListView != null)
                {
                    ItemsListView.ItemsSource = null;
                    ItemsListView.ItemsSource = items;

                    // ✅ แสดง/ซ่อน ListView และ EmptyPanel
                    if (items.Count > 0)
                    {
                        ItemsListView.Visibility = Visibility.Visible;
                        if (EmptyItemsPanel != null)
                            EmptyItemsPanel.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        ItemsListView.Visibility = Visibility.Collapsed;
                        if (EmptyItemsPanel != null)
                            EmptyItemsPanel.Visibility = Visibility.Visible;
                    }
                }

                // Notes
                if (NotesTextBox != null) NotesTextBox.Text = _currentTransfer.Notes ?? string.Empty;

                UpdateButtonVisibility();

                try { UpdateAvailableProductsDisplay(); } catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ UpdateUI failed: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   Stack: {ex.StackTrace}");
            }
        }

        private List<DependencyObject> FindChildrenByName(DependencyObject parent, string name)
        {
            var results = new List<DependencyObject>();
            if (parent == null || string.IsNullOrEmpty(name)) return results;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is FrameworkElement fe && fe.Name == name)
                    results.Add(child);

                // Recurse into child
                results.AddRange(FindChildrenByName(child, name));
            }

            return results;
        }

        public class BoolToVisibilityConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, string language)
            {
                if (value is bool boolValue)
                {
                    return boolValue ? Visibility.Visible : Visibility.Collapsed;
                }
                return Visibility.Collapsed;
            }

            public object ConvertBack(object value, Type targetType, object parameter, string language)
            {
                throw new NotImplementedException();
            }
        }

        public class BoolToOpacityConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, string language)
            {
                if (value is bool boolValue)
                {
                    return boolValue ? 0.5 : 1.0;
                }
                return 1.0;
            }

            public object ConvertBack(object value, Type targetType, object parameter, string language)
            {
                throw new NotImplementedException();
            }
        }

        private void UpdateBudgetDisplay()
        {
            if (_currentTransfer == null) return;

            // Compute current cost-per-person from items (fallback)
            decimal computedCostPerPerson = _currentTransfer.ExpectedPeople > 0
                ? (_totalCost / _currentTransfer.ExpectedPeople)
                : 0m;

            // Determine which Outlet to use for PricePerHead (respect user pending change)
            int? targetOutletId = _hasUserModifiedOutlet && _pendingOutletId.HasValue
                ? _pendingOutletId
                : _currentTransfer.OutletId;

            decimal? outletPricePerHead = null;
            DateTime? outletPriceModifiedDate = null;

            // For outlet price, prefer the saved outlet snapshot when transfer is InProgress/Completed.
            if (_currentTransfer != null)
            {
                bool preferOutletSnapshot = (_currentTransfer.Status == TransferStatus.InProgress || _currentTransfer.Status == TransferStatus.Completed)
                                     && _currentTransfer.OutletPricePerHeadAtSave.HasValue;

                if (preferOutletSnapshot)
                {
                    outletPricePerHead = _currentTransfer.OutletPricePerHeadAtSave;
                    outletPriceModifiedDate = _currentTransfer.OutletPricePerHeadSavedAt;
                }
                else
                {
                    // fallback to current outlet price
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

            // Display usage value (computed)
            BudgetUsageText.Text = $"{displayCostPerPerson:N4} ฿/คน";

            // Show target (PricePerHead) if available (Outlet current price or snapshot)
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

            //เราเลิกใช้ CostPerPersonAtSave แล้ว ให้ซ่อน SavedCostPerPersonText เสมอ
            SavedCostPerPersonText.Text = string.Empty;
            SavedCostPerPersonText.Visibility = Visibility.Collapsed;

            // Show outlet price info (source) with its modified date when available
            if (outletPricePerHead.HasValue)
            {
                var dateText = outletPriceModifiedDate.HasValue ? outletPriceModifiedDate.Value.ToLocalTime().ToString("dd/MM/yyyy") : "ไม่ระบุวันที่";
                OutletPriceInfoText.Text = $"ราคาของOutlet: {outletPricePerHead.Value:N4} ฿/คน (ตั้งวันที่ {dateText})";
                OutletPriceInfoText.Visibility = Visibility.Visible;
            }
            else
            {
                OutletPriceInfoText.Text = string.Empty;
                OutletPriceInfoText.Visibility = Visibility.Collapsed;
            }

            // If the outlet has a configured PricePerHead, compare and show warning if exceeded.
            if (outletPricePerHead.HasValue)
            {
                if (displayCostPerPerson > outletPricePerHead.Value)
                {
                    // Exceeded -> red / danger
                    BudgetCard.Background = new SolidColorBrush(Color.FromArgb(40, 220, 38, 28));
                    BudgetCard.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));
                    BudgetUsageText.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));
                    BudgetTargetText.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));

                    BudgetWarningText.Text = $"เกินงบต่อคน: {outletPricePerHead.Value:N4} ฿/คน";
                    BudgetWarningText.Visibility = Visibility.Visible;
                }
                else
                {
                    // Within budget -> green
                    BudgetCard.Background = new SolidColorBrush(Color.FromArgb(40, 34, 197, 94));
                    BudgetCard.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                    BudgetUsageText.Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                    BudgetTargetText.Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));

                    BudgetWarningText.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                // No outlet budget configured — fallback existing colour logic based on computed cost
                BudgetWarningText.Visibility = Visibility.Collapsed;

                if (displayCostPerPerson >= 500) // >= 500 บาท/คน
                {
                    BudgetCard.Background = new SolidColorBrush(Color.FromArgb(40, 220, 38, 28));
                    BudgetCard.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));
                    BudgetUsageText.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));
                    BudgetTargetText.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));
                    BudgetTargetText.Visibility = Visibility.Collapsed;
                }
                else if (displayCostPerPerson >= 300) // 300-500 บาท/คน
                {
                    BudgetCard.Background = new SolidColorBrush(Color.FromArgb(40, 234, 179, 8));
                    BudgetCard.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 234, 179, 8));
                    BudgetUsageText.Foreground = new SolidColorBrush(Color.FromArgb(255, 234, 179, 8));
                    BudgetTargetText.Foreground = new SolidColorBrush(Color.FromArgb(255, 234, 179, 8));
                    BudgetTargetText.Visibility = Visibility.Collapsed;
                }
                else // < 300 บาท/คน
                {
                    BudgetCard.Background = new SolidColorBrush(Color.FromArgb(40, 34, 197, 94));
                    BudgetCard.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                    BudgetUsageText.Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                    BudgetTargetText.Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                    BudgetTargetText.Visibility = Visibility.Collapsed;
                }
            }
            UpdateHiddenCostDisplay();
        }

        private void UpdateHiddenCostDisplay()
        {
            if (_currentTransfer == null) return;

            var hiddenCostPercentage = _currentTransfer.HiddenCostPercentage ?? 0m;

            // แสดงเปอร์เซ็นต์ต้นทุนแฝง
            HiddenCostPercentageText.Text = $"{hiddenCostPercentage:0}%";

            // ✅ แก้ไข: ใช้ _totalCost ที่คำนวณไว้นานแล้ว แทนการดึงจาก ItemsListView
            var expectedPeople = _currentTransfer.ExpectedPeople;

            var computedCostPerPerson = expectedPeople > 0 ? _totalCost / expectedPeople : 0m;
            var hiddenCostAmount = computedCostPerPerson * (hiddenCostPercentage / 100m);

            HiddenCostAmountText.Text = $"≈ {hiddenCostAmount:N4} ฿/คน";

            // อัพเดทกล่องต้นทุนรวม
            UpdateTotalCostWithHiddenDisplay(computedCostPerPerson, hiddenCostAmount, hiddenCostPercentage);
        }

        private void UpdateTotalCostWithHiddenDisplay(decimal costPerPerson, decimal hiddenCostAmount, decimal hiddenCostPercentage)
        {
            if (_currentTransfer == null) return;

            // คำนวณต้นทุนรวมต่อคน (ต้นทุน + ต้นทุนแฝง)
            var totalCostWithHidden = costPerPerson + hiddenCostAmount;

            // แสดงต้นทุนรวม
            TotalCostWithHiddenText.Text = $"{totalCostWithHidden:N4} ฿/คน";

            // ดึงราคาต่อหัวของ Outlet
            int? targetOutletId = _hasUserModifiedOutlet && _pendingOutletId.HasValue
                ? _pendingOutletId
                : _currentTransfer.OutletId;

            decimal? outletPricePerHead = null;
            DateTime? outletPriceModifiedDate = null;

            // ใช้ logic เดียวกับ UpdateBudgetDisplay
            bool preferOutletSnapshot = (_currentTransfer.Status == TransferStatus.InProgress || _currentTransfer.Status == TransferStatus.Completed)
                                 && _currentTransfer.OutletPricePerHeadAtSave.HasValue;

            if (preferOutletSnapshot)
            {
                outletPricePerHead = _currentTransfer.OutletPricePerHeadAtSave;
                outletPriceModifiedDate = _currentTransfer.OutletPricePerHeadSavedAt;
            }
            else
            {
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

            // แสดงรายละเอียดการคำนวณ
            TotalCostBreakdownText.Text = $"ต้นทุนต่อคน {costPerPerson:N4} + ต้นทุนแฝง {hiddenCostPercentage:0}% ({hiddenCostAmount:N4})";
            TotalCostBreakdownText.Visibility = Visibility.Visible;

            // เทียบกับเป้าหมาย
            if (outletPricePerHead.HasValue)
            {
                var dateText = outletPriceModifiedDate.HasValue ? outletPriceModifiedDate.Value.ToLocalTime().ToString("dd/MM/yyyy") : "ไม่ระบุวันที่";
                TotalCostTargetText.Text = $"งบต่อคน : {outletPricePerHead.Value:N4} ฿";
                TotalCostTargetText.Visibility = Visibility.Visible;

                // เปรียบเทียบและแสดงสถานะ
                if (totalCostWithHidden > outletPricePerHead.Value)
                {
                    // เกินงบ -> แสดงสีแดง
                    TotalCostWithHiddenCard.Background = new SolidColorBrush(Color.FromArgb(40, 220, 38, 28));
                    TotalCostWithHiddenCard.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));
                    TotalCostWithHiddenText.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));
                    TotalCostTargetText.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));

                    var exceed = totalCostWithHidden - outletPricePerHead.Value;
                    TotalCostWarningText.Text = $"⚠️ เกินงบ {exceed:N4} ฿/คน";
                    TotalCostWarningText.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 38, 28));
                    TotalCostWarningText.Visibility = Visibility.Visible;
                }
                else
                {
                    // อยู่ในงบ -> แสดงสีเขียว
                    TotalCostWithHiddenCard.Background = new SolidColorBrush(Color.FromArgb(40, 34, 197, 94));
                    TotalCostWithHiddenCard.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                    TotalCostWithHiddenText.Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                    TotalCostTargetText.Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));

                    var remaining = outletPricePerHead.Value - totalCostWithHidden;
                    TotalCostWarningText.Text = $"✓ เหลืองบ {remaining:N4} ฿/คน";
                    TotalCostWarningText.Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
                    TotalCostWarningText.Visibility = Visibility.Visible;
                }
            }
            else
            {
                // ไม่มีงบกำหนด 
                TotalCostTargetText.Visibility = Visibility.Collapsed;
                TotalCostWarningText.Visibility = Visibility.Collapsed;

                // ใช้สีเริ่มต้น
                TotalCostWithHiddenCard.Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as SolidColorBrush;
                TotalCostWithHiddenCard.BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as SolidColorBrush;
                TotalCostWithHiddenText.Foreground = new SolidColorBrush(Color.FromArgb(255, 139, 92, 246)); // #8B5CF6
            }
        }

        /// <summary>
        /// สร้าง NumberFormatter สำหรับแสดงทศนิยม 4 ตำแหน่ง
        /// </summary>
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
}