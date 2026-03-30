using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Requisition.Models;
using Requisition.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using System.Text.Json;
using System.Collections.Generic;

namespace Requisition.Pages
{
    public sealed partial class CostPerHeadSettingsPage : Page
    {
        private readonly CostPerHeadService _service;
        public ObservableCollection<Outlet> Rooms { get; } = new();

        public CostPerHeadSettingsPage()
        {
            this.InitializeComponent();
            _service = new CostPerHeadService();

            this.Loaded += CostPerHeadSettingsPage_Loaded;
            RoomsListView.ItemsSource = Rooms;
        }

        private async void CostPerHeadSettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadRoomsAsync();
        }

        private async Task LoadRoomsAsync()
        {
            Rooms.Clear();
            var items = await _service.GetAllAsync();

            // Diagnostic logging: show what IsActive actually is for each record
            System.Diagnostics.Debug.WriteLine($"LoadRoomsAsync: loaded {items?.Count ?? 0} outlets");
            if (items != null)
            {
                foreach (var r in items)
                {
                    System.Diagnostics.Debug.WriteLine($"Outlet: Id={r.Id}, Name='{r.Name}', IsActive={r.IsActive}, Price={r.PricePerHead}");
                    Rooms.Add(r);
                }
            }
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "เพิ่ม Outlet",
                PrimaryButtonText = "บันทึก",
                CloseButtonText = "ยกเลิก"
            };

            var nameBox = new TextBox { PlaceholderText = "ชื่อ Outlet" };
            var priceBox = new TextBox { PlaceholderText = "ราคาต่อหัว (ตัวเลขเท่านั้น)" };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(nameBox);
            panel.Children.Add(priceBox);
            dialog.Content = panel;

            // set XamlRoot so dialog shows in WinUI3
            dialog.XamlRoot = this.XamlRoot;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                decimal? price = null;
                if (decimal.TryParse(priceBox.Text, out var p)) price = p;

                var model = new Outlet
                {
                    Name = nameBox.Text,
                    PricePerHead = price
                };

                // CreateAsync records history internally
                await _service.CreateAsync(model, Environment.UserName);
                await LoadRoomsAsync();
            }
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                var room = Rooms.FirstOrDefault(r => r.Id == id);
                if (room == null) return;

                var dialog = new ContentDialog
                {
                    Title = "แก้ไข Outlet",
                    PrimaryButtonText = "บันทึก",
                    CloseButtonText = "ยกเลิก"
                };

                var nameBox = new TextBox { Text = room.Name };
                var priceBox = new TextBox { Text = room.PricePerHead?.ToString("N4") ?? string.Empty };

                var panel = new StackPanel { Spacing = 8 };
                panel.Children.Add(nameBox);
                panel.Children.Add(priceBox);
                dialog.Content = panel;

                // set XamlRoot
                dialog.XamlRoot = this.XamlRoot;

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    decimal? price = null;
                    if (decimal.TryParse(priceBox.Text, out var p)) price = p;

                    room.Name = nameBox.Text;
                    room.PricePerHead = price;

                    await _service.UpdateAsync(room, Environment.UserName);
                    await LoadRoomsAsync();
                }
            }
        }

        private async void ToggleStatusButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                var outlet = Rooms.FirstOrDefault(r => r.Id == id);
                if (outlet == null) return;

                string actionText = outlet.IsActive ? "ปิดการใช้งาน" : "เปิดการใช้งาน";
                string confirmMessage = outlet.IsActive
                    ? $"คุณต้องการปิดการใช้งาน Outlet '{outlet.Name}' หรือไม่?"
                    : $"คุณต้องการเปิดการใช้งาน Outlet '{outlet.Name}' หรือไม่?";

                var reasonBox = new TextBox
                {
                    PlaceholderText = "กรุณาระบุเหตุผล (บังคับ)",
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    MinHeight = 80
                };

                var panel = new StackPanel { Spacing = 12 };
                panel.Children.Add(new TextBlock
                {
                    Text = confirmMessage,
                    TextWrapping = TextWrapping.Wrap
                });
                panel.Children.Add(new TextBlock
                {
                    Text = "เหตุผล:",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });
                panel.Children.Add(reasonBox);

                var dialog = new ContentDialog
                {
                    Title = actionText,
                    Content = panel,
                    PrimaryButtonText = "ยืนยัน",
                    CloseButtonText = "ยกเลิก",
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    // ตรวจสอบว่าต้องกรอกเหตุผล
                    if (string.IsNullOrWhiteSpace(reasonBox.Text))
                    {
                        var errorDialog = new ContentDialog
                        {
                            Title = "ข้อผิดพลาด",
                            Content = "กรุณาระบุเหตุผลในการเปลี่ยนสถานะ",
                            CloseButtonText = "ตกลง",
                            XamlRoot = this.XamlRoot
                        };
                        await errorDialog.ShowAsync();
                        return;
                    }

                    // เปลี่ยนสถานะ
                    outlet.IsActive = !outlet.IsActive;
                    outlet.ModifiedBy = Environment.UserName;
                    outlet.ModifiedDate = DateTime.Now;

                    // บันทึกและสร้างประวัติ
                    await _service.ToggleStatusAsync(outlet, reasonBox.Text, Environment.UserName);
                    await LoadRoomsAsync();
                }
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                var confirm = new ContentDialog
                {
                    Title = "ยืนยันการลบ",
                    Content = "คุณต้องการลบห้องอาหารนี้หรือไม่? การกระทำนี้สามารถย้อนกลับได้โดยประวัติเท่านั้น",
                    PrimaryButtonText = "ลบ",
                    CloseButtonText = "ยกเลิก"
                };

                // set XamlRoot
                confirm.XamlRoot = this.XamlRoot;

                var res = await confirm.ShowAsync();
                if (res == ContentDialogResult.Primary)
                {
                    await _service.DeleteAsync(id, Environment.UserName);
                    await LoadRoomsAsync();
                }
            }
        }

        private async void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                var history = await _service.GetHistoryAsync(id);
                await ShowHistoryDialogAsync(history, title: "ประวัติการเปลี่ยนแปลง");
            }
        }

        private async void ViewAllHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var history = await _service.GetAllHistoryAsync();
            await ShowHistoryDialogAsync(history, title: "ประวัติการกระทำทั้งหมด");
        }

        // Reusable history renderer for both per-item and all-history
        private async Task ShowHistoryDialogAsync(List<CostPerHeadService.HistoryRecord> history, string title)
        {
            var panel = new StackPanel { Spacing = 8 };

            foreach (var h in history.OrderByDescending(x => x.ActionDate))
            {
                // header: timestamp | action | by | optional Outlet
                string headerText = $"{h.ActionDate:yyyy-MM-dd HH:mm} | {h.ActionType} by {h.ActionBy}";
                if (!string.IsNullOrEmpty(h.OutletName))
                {
                    headerText += $" | Outlet: {h.OutletName}";
                }

                var header = new TextBlock
                {
                    Text = headerText,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                };
                panel.Children.Add(header);

                bool addedAny = false;

                if (!string.IsNullOrEmpty(h.ChangedFields))
                {
                    try
                    {
                        var changed = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(h.ChangedFields);
                        if (changed != null)
                        {
                            foreach (var c in changed)
                            {
                                c.TryGetValue("DisplayName", out var displayNameObj);
                                c.TryGetValue("Field", out var fieldObj);
                                c.TryGetValue("OldValue", out var oldObj);
                                c.TryGetValue("NewValue", out var newObj);

                                string displayName = displayNameObj?.ToString() ?? "";
                                string oldVal = oldObj?.ToString() ?? "-";
                                string newVal = newObj?.ToString() ?? "-";

                                var detail = new TextBlock
                                {
                                    Text = $"  • {displayName}: {oldVal} → {newVal}",
                                    Margin = new Thickness(12, 2, 0, 2)
                                };
                                panel.Children.Add(detail);
                                addedAny = true;
                            }
                        }
                    }
                    catch { /* ignore parse errors */ }
                }

                if (!addedAny)
                {
                    var noChange = new TextBlock
                    {
                        Text = "  (ไม่มีรายละเอียดการเปลี่ยนแปลง)",
                        Margin = new Thickness(12, 2, 0, 2),
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray)
                    };
                    panel.Children.Add(noChange);
                }
            }

            if (history.Count == 0)
            {
                panel.Children.Add(new TextBlock { Text = "ไม่มีประวัติ" });
            }

            var scrollViewer = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 400
            };

            var dlg = new ContentDialog
            {
                Title = title,
                Content = scrollViewer,
                CloseButtonText = "ปิด",
                XamlRoot = this.XamlRoot
            };

            await dlg.ShowAsync();
        }
    }
}
