using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Requisition.Models;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Requisition.Pages
{
    public sealed partial class TemplateEditorPage : Page
    {
        private readonly TemplateService _templateService;
        private readonly ProductService _productService;

        private List<Product> _allProducts = new();
        private List<Product> _filtered = new();

        // Selected items map
        private readonly Dictionary<string, TemplateIngredient> _selected = new(StringComparer.OrdinalIgnoreCase);

        // UI collections
        public ObservableCollection<Product> ProductsPage { get; } = new();
        public ObservableCollection<TemplateIngredient> SelectedItems { get; } = new();

        private Template? _editingTemplate;
        private string _templateName = string.Empty;

        public TemplateEditorPage()
        {
            this.InitializeComponent();
            _templateService = new TemplateService();
            _productService = new ProductService();

            ProductsListView.ItemsSource = ProductsPage;
            SelectedListView.ItemsSource = SelectedItems;

            // Disable Save initially until user selects at least one item
            SaveButton.IsEnabled = false;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Title: caller can pass explicit title string
            if (e.Parameter is string title)
            {
                PageTitleText.Text = title;
            }
            else if (e.Parameter == null)
            {
                PageTitleText.Text = "สร้าง Template ใหม่";
            }
            else
            {
                PageTitleText.Text = "แก้ไข Template";
            }

            // parameter can be int id or Template object (optional)
            if (e.Parameter is int id)
            {
                _editingTemplate = await _templateService.GetByIdAsync(id);
                if (_editingTemplate != null)
                    _templateName = _editingTemplate.Name ?? string.Empty;
            }
            else if (e.Parameter is Template t)
            {
                _editingTemplate = t;
                _templateName = t.Name ?? string.Empty;
            }

            await LoadProductsAsync();

            if (_editingTemplate != null)
            {
                // preselect existing ingredients
                foreach (var ing in _editingTemplate.Ingredients)
                {
                    _selected[ing.ProductCode] = new TemplateIngredient
                    {
                        ProductCode = ing.ProductCode,
                        ProductName = ing.ProductName,
                        Quantity = ing.Quantity,
                        Unit = ing.Unit
                    };
                }
                RefreshSelectedList();
            }

            // Populate UI fields (name + Outlet) and load Outlet list
            TemplateNameBox.Text = _templateName ?? string.Empty;
            await LoadOutletsAsync();

            RenderPage();
        }

        // Keep responsive handler because XAML wires SizeChanged="RootGrid_SizeChanged"
        // This reflows TwoColumnGrid into stacked rows when width is below threshold.
        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                const double threshold = 900; // px
                if (TwoColumnGrid == null || LeftStack == null || RightStack == null)
                    return;

                if (e.NewSize.Width < threshold)
                {
                    // stacked: left above right
                    Grid.SetColumn(LeftStack, 0);
                    Grid.SetRow(LeftStack, 0);
                    Grid.SetColumn(RightStack, 0);
                    Grid.SetRow(RightStack, 1);

                    // ensure row definitions exist for stacked layout
                    TwoColumnGrid.RowDefinitions.Clear();
                    TwoColumnGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    TwoColumnGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                    // collapse second column
                    if (TwoColumnGrid.ColumnDefinitions.Count >= 2)
                    {
                        TwoColumnGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                        TwoColumnGrid.ColumnDefinitions[1].Width = new GridLength(0);
                    }
                }
                else
                {
                    // two-column layout
                    Grid.SetRow(LeftStack, 0);
                    Grid.SetColumn(LeftStack, 0);
                    Grid.SetRow(RightStack, 0);
                    Grid.SetColumn(RightStack, 1);

                    // restore two-column layout
                    TwoColumnGrid.RowDefinitions.Clear();
                    if (TwoColumnGrid.ColumnDefinitions.Count >= 2)
                    {
                        TwoColumnGrid.ColumnDefinitions[0].Width = new GridLength(2, GridUnitType.Star);
                        TwoColumnGrid.ColumnDefinitions[1].Width = new GridLength(3, GridUnitType.Star);
                    }
                }
            }
            catch
            {
                // swallow layout exceptions to avoid breaking UI; no action needed
            }
        }

        private async Task LoadProductsAsync()
        {
            _allProducts.Clear();
            var prods = await _productService.GetAllProductsAsync();
            _allProducts.AddRange(prods);
            _filtered = _allProducts.ToList();
            // Populate ProductsPage with all filtered results
            RenderPage();
        }

        private void RenderPage()
        {
            ProductsPage.Clear();

            // exclude already selected items from left list
            var available = _filtered.Where(p => !_selected.ContainsKey(p.Code)).ToList();

            foreach (var p in available) ProductsPage.Add(p);

            ProductsCountText.Text = $"รายการที่พบ: {available.Count}";
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string code)
            {
                var prod = _allProducts.FirstOrDefault(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
                if (prod == null) return;

                if (!_selected.ContainsKey(code))
                {
                    _selected[code] = new TemplateIngredient
                    {
                        ProductCode = prod.Code,
                        ProductName = prod.Name,
                        Quantity = null,
                        Unit = prod.Unit
                    };
                }
                else
                {
                    _selected.Remove(code);
                }
                RefreshSelectedList();
                RenderPage();
            }
        }

        private void RefreshSelectedList()
        {
            SelectedItems.Clear();
            foreach (var kv in _selected.Values)
            {
                SelectedItems.Add(kv);
            }

            // Enable/disable Save according to whether there are selected items
            SaveButton.IsEnabled = SelectedItems.Count > 0;
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string code)
            {
                if (_selected.ContainsKey(code))
                {
                    _selected.Remove(code);
                    RefreshSelectedList();
                    RenderPage();
                }
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var q = (SearchBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(q))
                _filtered = _allProducts.ToList();
            else
            {
                var low = q.ToLowerInvariant();
                _filtered = _allProducts.Where(p => (p.Code ?? string.Empty).ToLowerInvariant().Contains(low)
                                                 || (p.Name ?? string.Empty).ToLowerInvariant().Contains(low)).ToList();
            }
            RenderPage();
        }

        // Load outlets into OutletComboBox and preselect when editing
        private async Task LoadOutletsAsync()
        {
            OutletComboBox.Items.Clear();
            try
            {
                var cph = new CostPerHeadService();
                var outletList = await cph.GetAllAsync();
                foreach (var o in outletList)
                {
                    if (o == null) continue;
                    var item = new ComboBoxItem { Content = o.Name ?? $"#{o.Id}", Tag = o.Id };
                    OutletComboBox.Items.Add(item);

                    // preselect if editing template and outlet matches
                    if (_editingTemplate != null && _editingTemplate.OutletId.HasValue && _editingTemplate.OutletId.Value == o.Id)
                        OutletComboBox.SelectedItem = item;
                }
            }
            catch
            {
                // ignore load errors; UI will still show but without outlet options
            }
        }

        // Save: use inputs from page (TemplateNameBox + OutletComboBox) and require outlet selection
        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // If no items selected, show error and return early
            if (_selected.Count == 0)
            {
                var dlg2 = new ContentDialog { Title = "ข้อผิดพลาด", Content = "กรุณาเลือกวัตถุดิบอย่างน้อย 1 รายการ", CloseButtonText = "ตกลง" };
                dlg2.XamlRoot = this.XamlRoot;
                await dlg2.ShowAsync();
                return;
            }

            var name = TemplateNameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                var err = new ContentDialog { Title = "ข้อผิดพลาด", Content = "กรุณากรอกชื่อ Template", CloseButtonText = "ตกลง" };
                err.XamlRoot = this.XamlRoot;
                await err.ShowAsync();
                return;
            }

            // Enforce outlet selection (required)
            if (!(OutletComboBox.SelectedItem is ComboBoxItem selItem && selItem.Tag is int))
            {
                var err = new ContentDialog { Title = "ข้อผิดพลาด", Content = "กรุณาเลือก Outlet", CloseButtonText = "ตกลง" };
                err.XamlRoot = this.XamlRoot;
                await err.ShowAsync();
                return;
            }

            int selectedOutletId = (int)selItem.Tag;
            string? selectedOutletName = selItem.Content?.ToString();

            var template = _editingTemplate ?? new Template();
            template.Name = name;
            template.OutletId = selectedOutletId;
            template.OutletName = selectedOutletName;
            template.Ingredients = _selected.Values.Select(i => new TemplateIngredient
            {
                ProductCode = i.ProductCode,
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                Unit = i.Unit
            }).ToList();

            if (_editingTemplate == null)
                await _templateService.CreateAsync(template, Environment.UserName);
            else
                await _templateService.UpdateAsync(template, Environment.UserName);

            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(TemplatePage));
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(TemplatePage));
        }

        // Show history for current template (if editing) or all history
        private async void ShowHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            List<TemplateService.HistoryRecord> history;
            if (_editingTemplate != null)
                history = await _templateService.GetHistoryAsync(_editingTemplate.Id);
            else
                history = await _templateService.GetAllHistoryAsync();

            await ShowHistoryDialogAsync(history, title: _editingTemplate == null ? "ประวัติทั้งหมดของ Template" : $"ประวัติ: {_editingTemplate.Name}");
        }

        // Render history similar to CostPerHeadSettingsPage for readability
        private async Task ShowHistoryDialogAsync(List<TemplateService.HistoryRecord> history, string title)
        {
            var panel = new StackPanel { Spacing = 8 };

            foreach (var h in history.OrderByDescending(x => x.ActionDate))
            {
                // Header: timestamp + action + actor
                var header = new TextBlock
                {
                    Text = $"{h.ActionDate:yyyy-MM-dd HH:mm} | {h.ActionType} by {h.ActionBy}",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                };
                panel.Children.Add(header);

                // Try to build a concise human-readable description from ChangedFields (added / removed / name change)
                var descriptionLines = new List<string>();

                if (!string.IsNullOrEmpty(h.ChangedFields))
                {
                    try
                    {
                        var changes = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(h.ChangedFields);
                        if (changes != null)
                        {
                            foreach (var c in changes)
                            {
                                c.TryGetValue("Field", out var fieldObj);
                                c.TryGetValue("DisplayName", out var displayNameObj);
                                c.TryGetValue("OldValue", out var oldObj);
                                c.TryGetValue("NewValue", out var newObj);

                                var fieldName = fieldObj?.ToString() ?? displayNameObj?.ToString() ?? string.Empty;

                                static List<(string code, string name)> ParseCodeNameList(object? raw)
                                {
                                    var result = new List<(string code, string name)>();
                                    if (raw == null) return result;

                                    string s = raw.ToString() ?? string.Empty;
                                    if (string.IsNullOrWhiteSpace(s)) return result;

                                    try
                                    {
                                        if (s.StartsWith("[") || s.StartsWith("{"))
                                        {
                                            var items = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(s);
                                            if (items != null)
                                            {
                                                foreach (var it in items)
                                                {
                                                    it.TryGetValue("ProductCode", out var codeObj);
                                                    it.TryGetValue("ProductName", out var nameObj);
                                                    var code = codeObj?.ToString() ?? string.Empty;
                                                    var name = nameObj?.ToString() ?? string.Empty;
                                                    if (!string.IsNullOrEmpty(code)) result.Add((code, name));
                                                }
                                            }
                                        }
                                    }
                                    catch
                                    {
                                        // ignore parse errors
                                    }

                                    return result;
                                }

                                if (string.Equals(fieldName, "IngredientsAdded", StringComparison.OrdinalIgnoreCase))
                                {
                                    var added = ParseCodeNameList(newObj);
                                    if (added.Any()) descriptionLines.Add("Added: " + string.Join(", ", added.Select(x => $"{x.code} - {x.name}")));
                                }
                                else if (string.Equals(fieldName, "IngredientsRemoved", StringComparison.OrdinalIgnoreCase))
                                {
                                    var removed = ParseCodeNameList(oldObj);
                                    if (removed.Any()) descriptionLines.Add("Removed: " + string.Join(", ", removed.Select(x => $"{x.code} - {x.name}")));
                                }
                                else if (string.Equals(fieldName, "Name", StringComparison.OrdinalIgnoreCase))
                                {
                                    var oldName = oldObj?.ToString() ?? "-";
                                    var newName = newObj?.ToString() ?? "-";
                                    descriptionLines.Add($"Name: {oldName} → {newName}");
                                }
                                else if (string.Equals(fieldName, "Outlet", StringComparison.OrdinalIgnoreCase))
                                {
                                    // oldObj/newObj are serialized { Id, Name } objects
                                    string ParseOutletName(object? raw)
                                    {
                                        if (raw == null) return string.Empty;
                                        try
                                        {
                                            string json = raw.ToString() ?? string.Empty;
                                            if (string.IsNullOrWhiteSpace(json)) return string.Empty;
                                            if (json.StartsWith("\"") && json.EndsWith("\""))
                                            {
                                                try { json = JsonSerializer.Deserialize<string>(json) ?? json; } catch { }
                                            }
                                            var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
                                            if (dict != null)
                                            {
                                                if (dict.TryGetValue("Name", out var n)) return n?.ToString() ?? string.Empty;
                                                if (dict.TryGetValue("name", out var n2)) return n2?.ToString() ?? string.Empty;
                                            }
                                        }
                                        catch { }
                                        return string.Empty;
                                    }

                                    var oldO = ParseOutletName(oldObj);
                                    var newO = ParseOutletName(newObj);
                                    if (string.IsNullOrEmpty(oldO) && !string.IsNullOrEmpty(newO))
                                        descriptionLines.Add($"Outlet: {newO}");
                                    else if (!string.IsNullOrEmpty(oldO) && string.IsNullOrEmpty(newO))
                                        descriptionLines.Add($"Outlet: {oldO} → (removed)");
                                    else if (!string.IsNullOrEmpty(oldO) && !string.IsNullOrEmpty(newO))
                                        descriptionLines.Add($"Outlet: {oldO} → {newO}");
                                }
                            }
                        }
                    }
                    catch
                    {
                        // ignore parse errors, fallback below
                    }
                }

                if (!descriptionLines.Any())
                {
                    if (!string.IsNullOrEmpty(h.ChangedSummary))
                        descriptionLines.Add(h.ChangedSummary);
                    else if (!string.IsNullOrEmpty(h.ChangedFields))
                        descriptionLines.Add(Shorten(h.ChangedFields, 1000));
                    else if (!string.IsNullOrEmpty(h.NewValues))
                        descriptionLines.Add("New: " + Shorten(h.NewValues));
                    else if (!string.IsNullOrEmpty(h.OldValues))
                        descriptionLines.Add("Old: " + Shorten(h.OldValues));
                }

                foreach (var line in descriptionLines)
                {
                    panel.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
                }

                // separator
                panel.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Height = 1,
                    Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 221, 221, 221)),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 8, 0, 8)
                });
            }

            var dialog = new ContentDialog
            {
                Title = title,
                Content = new ScrollViewer { Content = panel, Height = 500 },
                CloseButtonText = "ปิด"
            };
            dialog.XamlRoot = this.XamlRoot;
            await dialog.ShowAsync();
        }

        private static string Shorten(string s, int max = 300)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        private async Task ShowErrorAsync(string title, string message)
        {
            var dlg = new ContentDialog { Title = title, Content = message, CloseButtonText = "ปิด" };
            dlg.XamlRoot = this.XamlRoot;
            await dlg.ShowAsync();
        }
    }
}

