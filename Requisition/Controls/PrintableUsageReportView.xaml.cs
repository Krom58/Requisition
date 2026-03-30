using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Requisition.Models.Reports;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Requisition.Controls
{
    public sealed partial class PrintableUsageReportView : UserControl
    {
        // ✅ เปลี่ยน: เพิ่มจาก 30 เป็น 45 แถวต่อหน้า
        private const int ITEMS_PER_PAGE = 45;
        private List<UIElement> _printPages = new();

        public PrintableUsageReportView()
        {
            InitializeComponent();
        }

        public void SetMaterialData(
            List<MaterialUsageReportItem> items,
            string reportType,
            string periodType,
            DateTime startDate,
            DateTime endDate,
            Dictionary<string, string> filters)
        {
            // แบ่งข้อมูลเป็นหน้าๆ
            var totalPages = (int)Math.Ceiling(items.Count / (double)ITEMS_PER_PAGE);

            // ✅ ป้องกัน division by zero
            if (totalPages == 0) totalPages = 1;

            for (int pageNum = 0; pageNum < totalPages; pageNum++)
            {
                var pageItems = items
                    .Skip(pageNum * ITEMS_PER_PAGE)
                    .Take(ITEMS_PER_PAGE)
                    .ToList();

                var pageView = CreateMaterialPage(
                    pageItems,
                    reportType,
                    periodType,
                    startDate,
                    endDate,
                    filters,
                    pageNum + 1,
                    totalPages,
                    items.Count
                );

                _printPages.Add(pageView);
            }

            // ✅ แก้ไข: ไม่แสดงใน ContentContainer เพราะจะทำให้ element ถูก detach ตอน print
            // แทนที่จะแสดงสรุปข้อมูล
            ShowSummary(reportType, totalPages, items.Count);
        }

        public void SetCostData(
            List<CostUsageReportItem> items,
            string reportType,
            string periodType,
            DateTime startDate,
            DateTime endDate)
        {
            // แบ่งข้อมูลเป็นหน้าๆ
            var totalPages = (int)Math.Ceiling(items.Count / (double)ITEMS_PER_PAGE);
            
            // ✅ ป้องกัน division by zero
            if (totalPages == 0) totalPages = 1;

            for (int pageNum = 0; pageNum < totalPages; pageNum++)
            {
                var pageItems = items
                    .Skip(pageNum * ITEMS_PER_PAGE)
                    .Take(ITEMS_PER_PAGE)
                    .ToList();

                var pageView = CreateCostPage(
                    pageItems,
                    reportType,
                    periodType,
                    startDate,
                    endDate,
                    pageNum + 1,
                    totalPages,
                    items.Count
                );

                _printPages.Add(pageView);
            }

            // ✅ แก้ไข: แสดงสรุปข้อมูล
            ShowSummary(reportType, totalPages, items.Count);
        }

        // ✅ เพิ่ม: แสดงสรุปข้อมูลแทนการแสดงหน้าจริง
        private void ShowSummary(string reportType, int totalPages, int totalItems)
        {
            ContentContainer.Children.Clear();

            var summaryStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 16,
                Padding = new Thickness(40)
            };

            summaryStack.Children.Add(new TextBlock
            {
                Text = "✅ เตรียมพิมพ์พร้อมแล้ว",
                FontSize = 24,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0, 120, 212))
            });

            summaryStack.Children.Add(new TextBlock
            {
                Text = $"📄 รายงาน: {reportType}",
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
            });

            summaryStack.Children.Add(new TextBlock
            {
                Text = $"📋 จำนวนรายการ: {totalItems:F4} รายการ",
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 102, 102, 102))
            });

            summaryStack.Children.Add(new TextBlock
            {
                Text = $"📄 จำนวนหน้า: {totalPages} หน้า ({ITEMS_PER_PAGE} แถว/หน้า)",
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 102, 102, 102))
            });

            summaryStack.Children.Add(new TextBlock
            {
                Text = "กรุณาเลือกเครื่องพิมพ์และกด 'Print'",
                FontSize = 14,
                Margin = new Thickness(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 153, 153, 153))
                // ✅ ลบ FontStyle = ... ออก
            });

            ContentContainer.Children.Add(summaryStack);
        }

        // ✅ ปรับ: ลด font size และ spacing เพื่อให้พอดี 45 แถว + เพิ่ม 2 คอลัมน์ใหม่
        private Grid CreateMaterialPage(
            List<MaterialUsageReportItem> items,
            string reportType,
            string periodType,
            DateTime startDate,
            DateTime endDate,
            Dictionary<string, string> filters,
            int currentPage,
            int totalPages,
            int totalItems)
        {
            var page = new Grid
            {
                Width = 794,
                Height = 1123,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                Padding = new Thickness(30) // ✅ ลดจาก 40 เป็น 30
            };

            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header - ✅ เพิ่มขนาดตัวอักษร
            var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            headerStack.Children.Add(new TextBlock
            {
                Text = "รายงานการใช้งาน",
                FontSize = 20, // ✅ เพิ่มจาก 18 → 20
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
            });

            headerStack.Children.Add(new TextBlock
            {
                Text = $"📊 {reportType}",
                FontSize = 15, // ✅ เพิ่มจาก 13 → 15
                Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            headerStack.Children.Add(new TextBlock
            {
                Text = $"ช่วงเวลา: {periodType}",
                FontSize = 12, // ✅ เพิ่มจาก 11 → 12
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 102, 102, 102))
            });

            headerStack.Children.Add(new TextBlock
            {
                Text = $"วันที่: {startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}",
                FontSize = 12, // ✅ เพิ่มจาก 11 → 12
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
            });

            Grid.SetRow(headerStack, 0);
            page.Children.Add(headerStack);

            // Filters - ✅ เพิ่มขนาดตัวอักษร
            if (filters != null && filters.Count > 0)
            {
                var filtersStack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) }; // ลดจาก 16
                
                filtersStack.Children.Add(new TextBlock
                {
                    Text = "ตัวกรอง:",
                    FontSize = 11, // ✅ เพิ่มจาก 10 → 11
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 51, 51, 51)),
                    Margin = new Thickness(0, 0, 0, 3) // ✅ ลดจาก 4
                });

                filtersStack.Children.Add(new TextBlock
                {
                    Text = string.Join(" • ", filters.Select(f => $"{f.Key}: {f.Value}")),
                    FontSize = 10, // ✅ เพิ่มจาก 9 → 10
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 102, 102, 102)),
                    TextWrapping = TextWrapping.Wrap
                });

                Grid.SetRow(filtersStack, 1);
                page.Children.Add(filtersStack);
            }

            // Table
            var tableStack = new StackPanel();

            // Table Header - ✅ เพิ่ม 2 คอลัมน์ใหม่: ต้นทุน/หน่วย และ ต้นทุนรวม
            var headerGrid = new Grid
            {
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black),
                BorderThickness = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(0, 0, 0, 4), // ✅ ลดจาก 6
                Margin = new Thickness(0, 0, 0, 4) // ✅ ลดจาก 6
            };

            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); // วัตถุดิบ
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) }); // ประเภท
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) }); // ห้องครัว
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) }); // ช่วงเวลา
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) }); // จำนวน
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.6, GridUnitType.Star) }); // หน่วย
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) }); // ✅ ต้นทุน/หน่วย
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // ✅ ต้นทุนรวม

            var headers = new[] { "วัตถุดิบ", "ประเภท", "ห้องครัว", "ช่วงเวลา", "จำนวน", "หน่วย", "ต้นทุน/หน่วย", "ต้นทุนรวม" }; // ✅ เพิ่ม 2 คอลัมน์
            for (int i = 0; i < headers.Length; i++)
            {
                var tb = new TextBlock
                {
                    Text = headers[i],
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 10, // ✅ ลดจาก 11 เพื่อให้คอลัมน์พอดี
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
                };
                if (i >= 4) tb.HorizontalAlignment = HorizontalAlignment.Right; // ✅ เปลี่ยนจาก 3 เป็น 4
                Grid.SetColumn(tb, i);
                headerGrid.Children.Add(tb);
            }

            tableStack.Children.Add(headerGrid);

            // Table Data - ✅ เพิ่ม 2 คอลัมน์ใหม่
            foreach (var item in items)
            {
                var rowGrid = new Grid { Margin = new Thickness(0, 1, 0, 0) }; // ✅ ลดจาก 2
                
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.6, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) }); // ✅ เพิ่ม
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // ✅ เพิ่ม

                var data = new[] 
                { 
                    item.ProductName, 
                    item.Category, 
                    item.KitchenDisplay, 
                    item.PeriodDisplay, 
                    item.TotalQuantityDisplay, 
                    item.Unit,
                    item.UnitCostDisplay,
                    item.TotalCostDisplay
                };
                
                for (int i = 0; i < data.Length; i++)
                {
                    var tb = new TextBlock
                    {
                        Text = data[i] ?? "",
                        FontSize = 9, // ✅ ลดจาก 10 เพื่อให้คอลัมน์พอดี
                        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
                    };
                    
                    // ✅ เพิ่ม: ถ้าเป็นคอลัมน์ห้องครัว (index 2) ให้ wrap text
                    if (i == 2)
                    {
                        tb.TextWrapping = TextWrapping.Wrap;
                        tb.MaxLines = 2;
                        tb.VerticalAlignment = VerticalAlignment.Top;
                        tb.LineHeight = 11; // ✅ กำหนดความสูงบรรทัดเพื่อไม่ให้แน่นเกินไป
                    }
                    else if (i >= 4)
                    {
                        tb.HorizontalAlignment = HorizontalAlignment.Right;
                    }
                    
                    Grid.SetColumn(tb, i);
                    rowGrid.Children.Add(tb);
                }

                tableStack.Children.Add(rowGrid);
            }

            Grid.SetRow(tableStack, 2);
            page.Children.Add(tableStack);

            // Footer - ✅ เพิ่มขนาดตัวอักษร
            var footerStack = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; // ลดจาก 16
            
            // ✅ เพิ่ม: ตัวบอกหน้าที่ชัดเจน
            footerStack.Children.Add(new TextBlock
            {
                Text = $"━━━━━ หน้า {currentPage} / {totalPages} ━━━━━",
                FontSize = 12, // ✅ เพิ่มจาก 11 → 12
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0, 120, 212)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            footerStack.Children.Add(new TextBlock
            {
                Text = $"สร้างเมื่อ: {DateTime.Now:dd/MM/yyyy HH:mm:ss}",
                FontSize = 9, // ✅ เพิ่มจาก 8 → 9
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 153, 153, 153))
            });

            footerStack.Children.Add(new TextBlock
            {
                Text = $"จำนวนรายการทั้งหมด: {totalItems} รายการ • แสดง {items.Count} รายการในหน้านี้",
                FontSize = 9, // ✅ เพิ่มจาก 8 → 9
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 153, 153, 153))
            });

            Grid.SetRow(footerStack, 3);
            page.Children.Add(footerStack);

            return page;
        }

        // ✅ ใน CreateCostPage - เพิ่มคอลัมน์ต้นทุนแฝง
        private Grid CreateCostPage(
            List<CostUsageReportItem> items,
            string reportType,
            string periodType,
            DateTime startDate,
            DateTime endDate,
            int currentPage,
            int totalPages,
            int totalItems)
        {
            var page = new Grid
            {
                Width = 794,
                Height = 1123,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                Padding = new Thickness(30)
            };

            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            headerStack.Children.Add(new TextBlock
            {
                Text = "รายงานการใช้งาน",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
            });

            headerStack.Children.Add(new TextBlock
            {
                Text = $"📊 {reportType}",
                FontSize = 15,
                Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            headerStack.Children.Add(new TextBlock
            {
                Text = $"ช่วงเวลา: {periodType}",
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 102, 102, 102))
            });

            headerStack.Children.Add(new TextBlock
            {
                Text = $"วันที่: {startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}",
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
            });

            Grid.SetRow(headerStack, 0);
            page.Children.Add(headerStack);

            // Table
            var tableStack = new StackPanel();

            // Table Header - ✅ เพิ่มคอลัมน์ห้องครัวและจำนวนครั้ง
            var headerGrid = new Grid
            {
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black),
                BorderThickness = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(0, 0, 0, 4),
                Margin = new Thickness(0, 0, 0, 4)
            };

            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.8, GridUnitType.Star) }); // ช่วงเวลา
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) }); // ห้องครัว
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) }); // คน/ครั้ง
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) }); // จำนวนครั้ง
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // ยอดต้นทุน
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // ✅ ต้นทุนแฝง
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // ต้นทุน/หัว

            var headers = new[] { "ช่วงเวลา", "ห้องครัว", "คน/ครั้ง", "จ.ครั้ง", "ยอดต้นทุน", "ต้นทุนแฝง", "ต้นทุน/หัว" };
            for (int i = 0; i < headers.Length; i++)
            {
                var tb = new TextBlock
                {
                    Text = headers[i],
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 9, // ✅ ลดขนาดเพื่อให้คอลัมน์พอดี
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
                };
                if (i >= 2) tb.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(tb, i);
                headerGrid.Children.Add(tb);
            }

            tableStack.Children.Add(headerGrid);

            // Table Data - ✅ เพิ่มคอลัมน์ต้นทุนแฝง
            foreach (var item in items)
            {
                var rowGrid = new Grid { Margin = new Thickness(0, 1, 0, 0) };

                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.8, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // ✅ ต้นทุนแฝง
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var data = new[]
                {
                    item.PeriodDisplay,
                    item.KitchenDisplay,
                    item.PeoplePerMealDisplay,
                    item.MealCountDisplay,
                    item.TotalCostDisplay,
                    item.HiddenCostAmountDisplay, // ✅ เพิ่ม
                    item.CostPerHeadDisplay
                };

                for (int i = 0; i < data.Length; i++)
                {
                    var tb = new TextBlock
                    {
                        Text = data[i] ?? "",
                        FontSize = 8, // ✅ ลดขนาด
                        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
                    };

                    // ✅ ห้องครัวให้ wrap text
                    if (i == 1)
                    {
                        tb.TextWrapping = TextWrapping.Wrap;
                        tb.MaxLines = 2;
                        tb.VerticalAlignment = VerticalAlignment.Top;
                        tb.LineHeight = 10;
                    }
                    else if (i >= 2)
                    {
                        tb.HorizontalAlignment = HorizontalAlignment.Right;
                    }

                    Grid.SetColumn(tb, i);
                    rowGrid.Children.Add(tb);
                }

                tableStack.Children.Add(rowGrid);
            }

            Grid.SetRow(tableStack, 1);
            page.Children.Add(tableStack);

            // Footer - ✅ เพิ่มตัวบอกหน้าที่ชัดเจน
            var footerStack = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

            footerStack.Children.Add(new TextBlock
            {
                Text = $"━━━━━ หน้า {currentPage} / {totalPages} ━━━━━",
                FontSize = 12, // ✅ เพิ่มจาก 11 → 12
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0, 120, 212)),
                Margin = new Thickness(0, 0, 0, 4)
            });

            footerStack.Children.Add(new TextBlock
            {
                Text = $"สร้างเมื่อ: {DateTime.Now:dd/MM/yyyy HH:mm:ss}",
                FontSize = 9, // ✅ เพิ่มจาก 8 → 9
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 153, 153, 153))
            });

            footerStack.Children.Add(new TextBlock
            {
                Text = $"จำนวนรายการทั้งหมด: {totalItems} รายการ • แสดง {items.Count} รายการในหน้านี้",
                FontSize = 9, // ✅ เพิ่มจาก 8 → 9
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 153, 153, 153))
            });

            Grid.SetRow(footerStack, 2);
            page.Children.Add(footerStack);

            return page;
        }

        public List<UIElement> GetPrintPages()
        {
            return _printPages;
        }

        public int GetPageCount()
        {
            return _printPages.Count;
        }

        public UIElement GetPage(int pageIndex)
        {
            if (pageIndex >= 0 && pageIndex < _printPages.Count)
                return _printPages[pageIndex];
            return null!;
        }
    }
}
