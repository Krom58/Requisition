using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Requisition.Models;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Requisition.Pages
{
    public sealed partial class TemplatePage : Page
    {
        private readonly TemplateService _service;
        public ObservableCollection<Template> Templates { get; } = new();

        public TemplatePage()
        {
            this.InitializeComponent();
            _service = new TemplateService();

            this.Loaded += TemplatePage_Loaded;
            TemplatesListView.ItemsSource = Templates;
        }

        private async void TemplatePage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadTemplatesAsync();
        }

        private async Task LoadTemplatesAsync()
        {
            Templates.Clear();
            var items = await _service.GetAllAsync();
            foreach (var t in items)
            {
                // แสดงทั้งที่เปิดและปิดใช้งาน
                Templates.Add(t);
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(TemplateEditorPage), "สร้าง Template ใหม่");
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
                Frame.Navigate(typeof(TemplateEditorPage), id);
        }

        private async void ToggleStatusButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                var template = Templates.FirstOrDefault(t => t.Id == id);
                if (template == null) return;

                string actionText = template.IsDeleted ? "เปิดการใช้งาน" : "ปิดการใช้งาน";
                string confirmMessage = template.IsDeleted
                    ? $"คุณต้องการเปิดการใช้งาน Template '{template.Name}' หรือไม่?"
                    : $"คุณต้องการปิดการใช้งาน Template '{template.Name}' หรือไม่?";

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
                    await _service.ToggleStatusAsync(id, reasonBox.Text, Environment.UserName);
                    await LoadTemplatesAsync();
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
                    Content = "คุณต้องการลบ Template นี้หรือไม่? การกระทำนี้สามารถย้อนกลับได้โดยประวัติเท่านั้น",
                    PrimaryButtonText = "ลบ",
                    CloseButtonText = "ยกเลิก"
                };

                confirm.XamlRoot = this.XamlRoot;
                var res = await confirm.ShowAsync();
                if (res == ContentDialogResult.Primary)
                {
                    await _service.DeleteAsync(id, Environment.UserName);
                    await LoadTemplatesAsync();
                }
            }
        }

        private async void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                List<TemplateService.HistoryRecord> history;
                try
                {
                    history = await _service.GetHistoryAsync(id);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("GetHistoryAsync failed: " + ex);
                    await ShowErrorAsync("ไม่สามารถดึงประวัติได้", ex.Message);
                    return;
                }

                await ShowHistoryDialogAsync(history, title: "ประวัติการเปลี่ยนแปลง");
            }
        }

        private async void ViewAllHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            List<TemplateService.HistoryRecord> history;
            try
            {
                history = await _service.GetAllHistoryAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetAllHistoryAsync failed: " + ex);
                await ShowErrorAsync("ไม่สามารถดึงประวัติทั้งหมดได้", ex.Message);
                return;
            }

            await ShowHistoryDialogAsync(history, title: "ประวัติทั้งหมดของ Template");
        }

        private async Task ShowHistoryDialogAsync(List<TemplateService.HistoryRecord> history, string title)
        {
            var panel = new StackPanel { Spacing = 8 };

            foreach (var h in history.OrderByDescending(x => x.ActionDate))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"{h.ActionDate:yyyy-MM-dd HH:mm} | {h.ActionType} by {h.ActionBy}",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });

                var lines = BuildShortHistoryLines(h);
                foreach (var line in lines)
                {
                    if ((line.StartsWith("Added:", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("Removed:", StringComparison.OrdinalIgnoreCase)) &&
                        line.Contains(","))
                    {
                        var idx = line.IndexOf(':');
                        var label = line.Substring(0, idx + 1);
                        var itemsPart = line.Substring(idx + 1).Trim();

                        panel.Children.Add(new TextBlock
                        {
                            Text = label,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Margin = new Thickness(0, 4, 0, 0)
                        });

                        var items = itemsPart.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                             .Select(s => s.Trim())
                                             .Where(s => !string.IsNullOrEmpty(s));
                        foreach (var item in items)
                        {
                            panel.Children.Add(new TextBlock
                            {
                                Text = "• " + item,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(12, 0, 0, 0) // indent bullets
                            });
                        }
                    }
                    else
                    {
                        panel.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
                    }
                }

                panel.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Height = 1,
                    Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 221, 221, 221)),
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

        private List<string> BuildShortHistoryLines(TemplateService.HistoryRecord h)
        {
            var result = new List<string>();

            if (!string.IsNullOrEmpty(h.ChangedFields))
            {
                try
                {
                    var changes = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(h.ChangedFields);
                    if (changes != null)
                    {
                        foreach (var c in changes)
                        {
                            c.TryGetValue("Field", out var field);
                            c.TryGetValue("NewValue", out var newVal);
                            c.TryGetValue("OldValue", out var oldVal);
                            c.TryGetValue("DisplayName", out var displayNameObj);

                            var displayName = displayNameObj?.ToString() ?? field?.ToString() ?? string.Empty;
                            var fieldName = field?.ToString() ?? string.Empty;

                            if (string.Equals(fieldName, "IngredientsAdded", StringComparison.OrdinalIgnoreCase)
                                || displayName.Contains("เพิ่ม"))
                            {
                                var added = ParseCodeNameList(newVal);
                                if (added.Any())
                                    result.Add("Added: " + string.Join(", ", added.Select(x => $"{x.code} - {x.name}")));
                            }
                            else if (string.Equals(fieldName, "IngredientsRemoved", StringComparison.OrdinalIgnoreCase)
                                     || displayName.Contains("ลบ"))
                            {
                                var removed = ParseCodeNameList(oldVal);
                                if (removed.Any())
                                    result.Add("Removed: " + string.Join(", ", removed.Select(x => $"{x.code} - {x.name}")));
                            }
                            else if (string.Equals(fieldName, "Name", StringComparison.OrdinalIgnoreCase))
                            {
                                var oldName = oldVal?.ToString() ?? "-";
                                var newName = newVal?.ToString() ?? "-";
                                result.Add($"Name: {oldName} → {newName}");
                            }
                            else if (string.Equals(fieldName, "Outlet", StringComparison.OrdinalIgnoreCase) || displayName.Contains("Outlet"))
                            {
                                var oldOutlet = ParseOutletName(oldVal);
                                var newOutlet = ParseOutletName(newVal);

                                if (!string.IsNullOrEmpty(oldOutlet) || !string.IsNullOrEmpty(newOutlet))
                                {
                                    if (string.IsNullOrEmpty(oldOutlet))
                                        result.Add($"Outlet: {newOutlet}");
                                    else if (string.IsNullOrEmpty(newOutlet))
                                        result.Add($"Outlet: {oldOutlet} → (removed)");
                                    else
                                        result.Add($"Outlet: {oldOutlet} → {newOutlet}");
                                }
                            }
                            else if (string.Equals(fieldName, "IsDeleted", StringComparison.OrdinalIgnoreCase) 
                                     || displayName.Contains("สถานะ"))
                            {
                                // แสดงการเปลี่ยนแปลงสถานะ
                                var oldStatus = oldVal?.ToString() ?? "-";
                                var newStatus = newVal?.ToString() ?? "-";
                                result.Add($"สถานะ: {oldStatus} → {newStatus}");
                            }
                            else if (string.Equals(fieldName, "Reason", StringComparison.OrdinalIgnoreCase))
                            {
                                result.Add($"เหตุผล: {newVal}");
                            }
                        }
                    }
                }
                catch { }
            }

            if (!result.Any())
                result.Add("(ไม่มีรายละเอียด)");

            return result;
        }

        private List<(string code, string name)> ParseCodeNameList(object? val)
        {
            if (val == null) return new List<(string, string)>();
            try
            {
                var str = val.ToString();
                if (string.IsNullOrWhiteSpace(str)) return new List<(string, string)>();

                var arr = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(str);
                if (arr == null) return new List<(string, string)>();

                return arr.Select(x =>
                {
                    x.TryGetValue("ProductCode", out var c);
                    x.TryGetValue("ProductName", out var n);
                    return (c ?? "", n ?? "");
                }).ToList();
            }
            catch
            {
                return new List<(string, string)>();
            }
        }

        private string ParseOutletName(object? val)
        {
            if (val == null) return string.Empty;
            try
            {
                var str = val.ToString();
                if (string.IsNullOrWhiteSpace(str)) return string.Empty;

                var obj = JsonSerializer.Deserialize<Dictionary<string, object>>(str);
                if (obj == null) return string.Empty;

                obj.TryGetValue("Name", out var nameObj);
                return nameObj?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task ShowErrorAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "ตกลง"
            };
            dialog.XamlRoot = this.XamlRoot;
            await dialog.ShowAsync();
        }
    }

    // Converters (สำหรับ IsDeleted: false = เปิดใช้งาน, true = ปิดใช้งาน)
    public class StatusIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isDeleted)
            {
                return isDeleted ? "\uE7E8" : "\uE74D"; // ปิด: CheckMark, เปิด: ถังขยะ
            }
            return "\uE74D";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class StatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isDeleted)
            {
                return new SolidColorBrush(isDeleted ? Microsoft.UI.Colors.Green : Microsoft.UI.Colors.Red);
            }
            return new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class StatusTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isDeleted)
            {
                return isDeleted ? "เปิดการใช้งาน" : "ปิดการใช้งาน";
            }
            return "เปลี่ยนสถานะ";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
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
}
