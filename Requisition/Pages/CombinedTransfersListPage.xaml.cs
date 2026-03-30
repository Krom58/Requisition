using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Requisition.Helpers;
using Requisition.Models;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;

namespace Requisition.Pages
{
    public sealed partial class CombinedTransfersListPage : Page
    {
        private readonly CombinedTransferService _service;
        private List<CombinedTransferListViewModel> _allCombinedTransfers = new();
        private List<CombinedTransferListViewModel> _filteredCombinedTransfers = new();
        private bool _showDeleted = false;

        // Pagination
        private const int PageSize = 20;
        private int _currentPage = 1;
        private int _totalPages = 1;
        private List<CombinedTransferListViewModel> _currentPageItems = new();

        public CombinedTransfersListPage()
        {
            InitializeComponent();
            _service = new CombinedTransferService();
            Loaded += CombinedTransfersListPage_Loaded;
        }

        private async void CombinedTransfersListPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                LoadingRing.IsActive = true;
                LoadingRing.Visibility = Visibility.Visible;
                CombinedListView.Visibility = Visibility.Collapsed;
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                PaginationPanel.Visibility = Visibility.Collapsed;

                _allCombinedTransfers = await _service.GetAllCombinedTransfersAsync(includeDeleted: true);
                System.Diagnostics.Debug.WriteLine($"📦 Loaded {_allCombinedTransfers.Count} combined transfers");
                
                _currentPage = 1; // reset to first page when reloading data
                ApplyFilter();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error loading: {ex.Message}");
                await DialogHelper.ShowErrorAsync("เกิดข้อผิดพลาด", $"ไม่สามารถโหลดข้อมูลได้: {ex.Message}");
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyFilter()
        {
            var searchText = CombinedSearchBox?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
            
            System.Diagnostics.Debug.WriteLine($"🔍 Applying filters - ShowDeleted: {_showDeleted}, Search: '{searchText}'");

            // 1. กรองตามสถานะลบ
            _filteredCombinedTransfers = _showDeleted
                ? _allCombinedTransfers.ToList()
                : _allCombinedTransfers.Where(x => !x.IsDeleted).ToList();

            System.Diagnostics.Debug.WriteLine($"   After deleted filter: {_filteredCombinedTransfers.Count} items");

            // 2. กรองตาม SearchText
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                _filteredCombinedTransfers = _filteredCombinedTransfers.Where(c =>
                    (c.CombinedNo?.ToLowerInvariant().Contains(searchText) == true) ||
                    (c.CreatedBy?.ToLowerInvariant().Contains(searchText) == true) ||
                    (c.TransferNosDisplay?.ToLowerInvariant().Contains(searchText) == true)
                ).ToList();

                System.Diagnostics.Debug.WriteLine($"   After search filter: {_filteredCombinedTransfers.Count} items");
            }

            // 3. เรียงตาม Id (ล่าสุดก่อน - ID ที่มากกว่า = สร้างทีหลัง)
            _filteredCombinedTransfers = _filteredCombinedTransfers
                .OrderByDescending(c => c.Id)
                .ToList();

            // 4. Pagination: calculate total pages and take current page slice
            var totalItems = _filteredCombinedTransfers.Count;
            _totalPages = totalItems == 0 ? 1 : (int)Math.Ceiling(totalItems / (double)PageSize);
            _currentPage = Math.Clamp(_currentPage, 1, _totalPages);

            var skip = (_currentPage - 1) * PageSize;
            _currentPageItems = _filteredCombinedTransfers.Skip(skip).Take(PageSize).ToList();

            // 5. แสดงผล
            if (totalItems == 0)
            {
                EmptyStatePanel.Visibility = Visibility.Visible;
                CombinedListView.Visibility = Visibility.Collapsed;
                PaginationPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                CombinedListView.ItemsSource = _currentPageItems;
                CombinedListView.Visibility = Visibility.Visible;
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                PaginationPanel.Visibility = Visibility.Visible;
                UpdatePaginationControls();
            }

            System.Diagnostics.Debug.WriteLine($"✅ Showing {_currentPageItems.Count} combined transfers (page {_currentPage}/{_totalPages})");
        }

        private void UpdatePaginationControls()
        {
            PageInfoTextBlock.Text = $"Page {_currentPage} of {_totalPages}";

            PrevPageButton.IsEnabled = _currentPage > 1;
            NextPageButton.IsEnabled = _currentPage < _totalPages;
        }

        private void CombinedSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                System.Diagnostics.Debug.WriteLine($"🔍 Search text changed: '{sender.Text}'");
                _currentPage = 1; // reset when search changes
                ApplyFilter();
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("🔄 Refresh button clicked");
            
            if (CombinedSearchBox != null)
            {
                CombinedSearchBox.Text = string.Empty;
            }

            await LoadDataAsync();
        }

        private void ShowDeletedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _showDeleted = ShowDeletedCheckBox.IsChecked == true;
            System.Diagnostics.Debug.WriteLine($"📊 Show deleted changed: {_showDeleted}");
            _currentPage = 1; // reset when filter changes
            ApplyFilter();
        }

        private void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                ApplyFilter();
            }
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages)
            {
                _currentPage++;
                ApplyFilter();
            }
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SelectTransfersPage));
        }

        private void ViewButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int combinedId)
            {
                System.Diagnostics.Debug.WriteLine($"👁️ View combined: {combinedId}");
                Frame.Navigate(typeof(CombinedTransferDetailPage), combinedId);
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int combinedId)
            {
                System.Diagnostics.Debug.WriteLine($"✏️ Edit combined: {combinedId}");
                Frame.Navigate(typeof(SelectTransfersPage), combinedId);
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not int combinedId)
                return;

            var combined = _allCombinedTransfers.FirstOrDefault(x => x.Id == combinedId);
            if (combined == null) return;

            var dialog = new ContentDialog
            {
                Title = "ยืนยันการลบ",
                Content = $"คุณต้องการลบใบรวม {combined.CombinedNo} ใช่หรือไม่?\n\n" +
                          "กรุณาระบุเหตุผล (จำเป็น):",
                PrimaryButtonText = "ลบ",
                CloseButtonText = "ยกเลิก",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            var reasonBox = new TextBox
            {
                PlaceholderText = "ระบุเหตุผลในการลบ (จำเป็น)",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 100,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = dialog.Content.ToString() });
            stack.Children.Add(reasonBox);
            dialog.Content = stack;

            dialog.IsPrimaryButtonEnabled = false;
            TextChangedEventHandler? handler = null;
            handler = (_, __) =>
            {
                dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(reasonBox.Text);
            };
            reasonBox.TextChanged += handler;

            var result = await dialog.ShowAsync();
            reasonBox.TextChanged -= handler;

            if (result != ContentDialogResult.Primary) return;

            if (string.IsNullOrWhiteSpace(reasonBox.Text))
            {
                await DialogHelper.ShowErrorAsync("ไม่สามารถดำเนินการได้", "กรุณาระบุเหตุผลก่อนลบใบรวม");
                return;
            }

            try
            {
                LoadingRing.IsActive = true;
                LoadingRing.Visibility = Visibility.Visible;

                var username = Environment.UserName;
                var deleteResult = await _service.DeleteCombinedTransferAsync(
                    combinedId,
                    username,
                    reasonBox.Text.Trim());

                if (deleteResult.success)
                {
                    await DialogHelper.ShowSuccessAsync("สำเร็จ", "ลบใบรวมเรียบร้อยแล้ว");
                    await LoadDataAsync();
                }
                else
                {
                    await DialogHelper.ShowErrorAsync("เกิดข้อผิดพลาด", deleteResult.error);
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

        private async Task<string?> ResolveIdsToTransferNosAsync(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    var ids = new List<int>();
                    foreach (var el in root.EnumerateArray())
                    {
                        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var id))
                            ids.Add(id);
                        else if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var id2))
                            ids.Add(id2);
                    }

                    if (ids.Count > 0)
                    {
                        var map = await _service.GetTransferNosByIdsAsync(ids);
                        var resolved = ids.Select(i => map.TryGetValue(i, out var no) && !string.IsNullOrWhiteSpace(no) ? no : $"#{i}");
                        return $"[{string.Join(", ", resolved)}]";
                    }

                    var items = root.EnumerateArray().Select(e => e.ToString());
                    return "[" + string.Join(", ", items) + "]";
                }

                if (root.ValueKind == JsonValueKind.Object)
                {
                    var obj = JsonSerializer.Deserialize<object>(raw);
                    return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
                }

                return raw;
            }
            catch
            {
            }

            var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                           .Select(p => p.Trim())
                           .Where(p => p.Length > 0)
                           .ToArray();

            var numericIds = new List<int>();
            foreach (var p in parts)
            {
                if (int.TryParse(p, out var id))
                    numericIds.Add(id);
            }

            if (numericIds.Count > 0)
            {
                var map = await _service.GetTransferNosByIdsAsync(numericIds);
                var resolved = numericIds.Select(i => map.TryGetValue(i, out var no) && !string.IsNullOrWhiteSpace(no) ? no : $"#{i}");
                return $"[{string.Join(", ", resolved)}]";
            }

            return raw;
        }

        private async void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not int combinedId)
                return;

            try
            {
                LoadingRing.IsActive = true;
                LoadingRing.Visibility = Visibility.Visible;

                var history = await _service.GetCombinedTransferHistoryAsync(combinedId);

                var dialog = new ContentDialog
                {
                    Title = "ประวัติการแก้ไข",
                    XamlRoot = XamlRoot,
                    CloseButtonText = "ปิด"
                };

                if (history == null || history.Count == 0)
                {
                    dialog.Content = new TextBlock
                    {
                        Text = "ไม่มีประวัติ",
                        FontSize = 14,
                        TextWrapping = TextWrapping.Wrap
                    };
                }
                else
                {
                    var panel = new StackPanel { Spacing = 8, MaxHeight = 400 };

                    foreach (var h in history)
                    {
                        SolidColorBrush? strokeBrush = null;

                        try
                        {
                            if (Resources != null && Resources.ContainsKey("CardStrokeColorDefaultBrush"))
                            {
                                strokeBrush = Resources["CardStrokeColorDefaultBrush"] as SolidColorBrush;
                            }
                        }
                        catch
                        {
                            strokeBrush = null;
                        }

                        if (strokeBrush == null)
                        {
                            try
                            {
                                if (Application.Current?.Resources != null && Application.Current.Resources.ContainsKey("CardStrokeColorDefaultBrush"))
                                {
                                    strokeBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as SolidColorBrush;
                                }
                            }
                            catch
                            {
                                strokeBrush = null;
                            }
                        }

                        if (strokeBrush == null)
                        {
                            strokeBrush = new SolidColorBrush(Microsoft.UI.Colors.LightGray);
                        }

                        var oldFormatted = await ResolveIdsToTransferNosAsync(h.OldValues);
                        var newFormatted = await ResolveIdsToTransferNosAsync(h.NewValues);

                        var detailsLines = new List<string>();
                        var detailText = h.Details ?? h.Description;
                        if (!string.IsNullOrWhiteSpace(detailText))
                            detailsLines.Add(detailText);

                        if (!string.IsNullOrWhiteSpace(oldFormatted))
                            detailsLines.Add($"Old: {oldFormatted}");
                        if (!string.IsNullOrWhiteSpace(newFormatted))
                            detailsLines.Add($"New: {newFormatted}");

                        var details = detailsLines.Count == 0 ? "-" : string.Join("\n", detailsLines);

                        var tb = new TextBlock
                        {
                            Text = $"{h.PerformedAtDisplay} • {h.ActionType} • {h.PerformedBy ?? "-"}\n{details}",
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 4, 0, 4)
                        };
                        panel.Children.Add(tb);

                        var sep = new Border
                        {
                            Height = 1,
                            Background = strokeBrush,
                            Margin = new Thickness(0, 4, 0, 4)
                        };
                        panel.Children.Add(sep);
                    }

                    var scroll = new ScrollViewer
                    {
                        Content = panel,
                        MaxHeight = 400
                    };

                    dialog.Content = scroll;
                }

                await dialog.ShowAsync();
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

        private async void HistoryAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadingRing.IsActive = true;
                LoadingRing.Visibility = Visibility.Visible;

                var combinedList = await _service.GetAllCombinedTransfersAsync(includeDeleted: true);
                var combinedMap = combinedList.ToDictionary(c => c.Id, c => c.CombinedNo);

                var allHistory = await _service.GetAllCombinedTransferHistoryAsync();
                var sorted = allHistory.OrderByDescending(h => h.ModifiedDate).ToList();

                var dialog = new ContentDialog
                {
                    Title = "ประวัติทั้งหมดของใบรวม",
                    XamlRoot = XamlRoot,
                    CloseButtonText = "ปิด"
                };

                if (sorted.Count == 0)
                {
                    dialog.Content = new TextBlock
                    {
                        Text = "ไม่มีประวัติ",
                        FontSize = 14,
                        TextWrapping = TextWrapping.Wrap
                    };
                }
                else
                {
                    SolidColorBrush? strokeBrush = null;
                    try
                    {
                        if (Resources != null && Resources.ContainsKey("CardStrokeColorDefaultBrush"))
                            strokeBrush = Resources["CardStrokeColorDefaultBrush"] as SolidColorBrush;
                    }
                    catch { strokeBrush = null; }
                    if (strokeBrush == null)
                    {
                        try
                        {
                            if (Application.Current?.Resources != null && Application.Current.Resources.ContainsKey("CardStrokeColorDefaultBrush"))
                                strokeBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as SolidColorBrush;
                        }
                        catch { strokeBrush = null; }
                    }
                    if (strokeBrush == null) strokeBrush = new SolidColorBrush(Microsoft.UI.Colors.LightGray);

                    var panel = new StackPanel { Spacing = 8 };

                    foreach (var h in sorted)
                    {
                        combinedMap.TryGetValue(h.CombinedTransferId, out var combinedNo);

                        var oldFormatted = await ResolveIdsToTransferNosAsync(h.OldValues);
                        var newFormatted = await ResolveIdsToTransferNosAsync(h.NewValues);

                        var header = $"{combinedNo ?? ("#" + h.CombinedTransferId)} • {h.ModifiedDate.ToString("dd/MM/yyyy HH:mm")} • {(!string.IsNullOrWhiteSpace(h.Action) ? h.Action : h.ActionType)}";
                        var actor = $"By: {(!string.IsNullOrWhiteSpace(h.ModifiedBy) ? h.ModifiedBy : h.PerformedBy ?? "-")}".Trim();
                        var desc = string.IsNullOrWhiteSpace(h.Description ?? h.Details) ? "-" : (h.Description ?? h.Details);

                        var lines = new List<string> { header, actor, desc! };
                        if (!string.IsNullOrWhiteSpace(oldFormatted)) lines.Add($"Old: {oldFormatted}");
                        if (!string.IsNullOrWhiteSpace(newFormatted)) lines.Add($"New: {newFormatted}");

                        var tb = new TextBlock
                        {
                            Text = string.Join("\n", lines),
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 4, 0, 4)
                        };
                        panel.Children.Add(tb);

                        var sep = new Border
                        {
                            Height = 1,
                            Background = strokeBrush,
                            Margin = new Thickness(0, 4, 0, 4)
                        };
                        panel.Children.Add(sep);
                    }

                    var scroll = new ScrollViewer
                    {
                        Content = panel,
                        MaxHeight = (this.ActualHeight > 0) ? this.ActualHeight * 0.7 : 600,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    };

                    dialog.Content = scroll;
                }

                await dialog.ShowAsync();
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
