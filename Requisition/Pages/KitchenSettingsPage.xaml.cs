using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Requisition.Models;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.UI;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Requisition.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class KitchenSettingsPage : Page
    {
        private readonly KitchenService _service;
        public ObservableCollection<Kitchen> Kitchens { get; } = new();

        public KitchenSettingsPage()
        {
            this.InitializeComponent();
            _service = new KitchenService();

            this.Loaded += KitchenSettingsPage_Loaded;
            KitchensListView.ItemsSource = Kitchens;
        }

        private async void KitchenSettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadKitchensAsync();
        }

        private async Task LoadKitchensAsync()
        {
            Kitchens.Clear();
            var items = await _service.GetAllAsync();
            foreach (var k in items) Kitchens.Add(k);
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "เพิ่มห้องครัว",
                PrimaryButtonText = "บันทึก",
                CloseButtonText = "ยกเลิก"
            };

            var nameBox = new TextBox { PlaceholderText = "ชื่อห้องครัว" };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(nameBox);
            dialog.Content = panel;

            dialog.XamlRoot = this.XamlRoot;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var model = new Kitchen
                {
                    Name = nameBox.Text
                };

                await _service.CreateAsync(model, Environment.UserName);
                await LoadKitchensAsync();
            }
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                var kitchen = Kitchens.FirstOrDefault(k => k.Id == id);
                if (kitchen == null) return;

                var dialog = new ContentDialog
                {
                    Title = "แก้ไขห้องครัว",
                    PrimaryButtonText = "บันทึก",
                    CloseButtonText = "ยกเลิก"
                };

                var nameBox = new TextBox { Text = kitchen.Name };

                var panel = new StackPanel { Spacing = 8 };
                panel.Children.Add(nameBox);
                dialog.Content = panel;

                dialog.XamlRoot = this.XamlRoot;

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    kitchen.Name = nameBox.Text;

                    await _service.UpdateAsync(kitchen, Environment.UserName);
                    await LoadKitchensAsync();
                }
            }
        }

        private async void ToggleStatusButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                var kitchen = Kitchens.FirstOrDefault(k => k.Id == id);
                if (kitchen == null) return;

                string actionText = kitchen.IsActive ? "ปิดการใช้งาน" : "เปิดการใช้งาน";
                string confirmMessage = kitchen.IsActive
                    ? $"คุณต้องการปิดการใช้งานห้องครัว '{kitchen.Name}' หรือไม่?"
                    : $"คุณต้องการเปิดการใช้งานห้องครัว '{kitchen.Name}' หรือไม่?";

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
                    kitchen.IsActive = !kitchen.IsActive;
                    kitchen.ModifiedBy = Environment.UserName;
                    kitchen.ModifiedDate = DateTime.Now;

                    // บันทึกและสร้างประวัติ
                    await _service.ToggleStatusAsync(kitchen, reasonBox.Text, Environment.UserName);
                    await LoadKitchensAsync();
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
                    Content = "คุณต้องการลบห้องครัวนี้หรือไม่?",
                    PrimaryButtonText = "ลบ",
                    CloseButtonText = "ยกเลิก"
                };

                confirm.XamlRoot = this.XamlRoot;

                var res = await confirm.ShowAsync();
                if (res == ContentDialogResult.Primary)
                {
                    await _service.DeleteAsync(id, Environment.UserName);
                    await LoadKitchensAsync();
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

        private async Task ShowHistoryDialogAsync(List<KitchenService.HistoryRecord> history, string title)
        {
            var panel = new StackPanel { Spacing = 8 };

            foreach (var h in history.OrderByDescending(x => x.ActionDate))
            {
                string headerText = $"{h.ActionDate:yyyy-MM-dd HH:mm} | {h.ActionType} by {h.ActionBy}";
                if (!string.IsNullOrEmpty(h.KitchenName))
                {
                    headerText += $" | ห้องครัว: {h.KitchenName}";
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
                    catch { }
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
