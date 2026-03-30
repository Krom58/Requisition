using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Requisition.Models.Reports;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Requisition.Controls
{
    public sealed partial class PrintableReportView : UserControl
    {
        private List<KitchenPeopleReportItem> _allItems = new();
        private List<PriceChangeReportItem> _priceItems = new();
        private List<InactiveProductReportItem> _inactiveItems = new();
        private int _itemsPerPage = 45; // จำนวนแถวต่อหน้า (เปลี่ยนได้เมื่อเรียก SetData)

        private enum ReportType
        {
            KitchenPeople,
            PriceChange,
            InactiveProducts
        }

        private ReportType _reportType = ReportType.KitchenPeople;

        public PrintableReportView()
        {
            InitializeComponent();
        }

        // Existing kitchen people API (เพิ่มพารามิเตอร์ itemsPerPage แบบ optional)
        public void SetData(List<KitchenPeopleReportItem> items, DateTime startDate, DateTime endDate, int itemsPerPage = 45)
        {
            _reportType = ReportType.KitchenPeople;
            _allItems = items ?? new List<KitchenPeopleReportItem>();
            _itemsPerPage = itemsPerPage;
            DateRangeText.Text = $"ช่วงเวลา: {startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}";
            GeneratedDateText.Text = $"สร้างเมื่อ: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";

            // แสดงหน้าแรกก่อน
            if (_allItems.Count > 0)
            {
                var firstPageItems = _allItems.Take(_itemsPerPage).ToList();
                ReportItemsControl.ItemTemplate = CreateItemTemplate();
                ReportItemsControl.ItemsSource = firstPageItems;
            }
        }

        // New overload for price-change report (เพิ่มพารามิเตอร์ itemsPerPage แบบ optional)
        public void SetData(List<PriceChangeReportItem> items, DateTime startDate, DateTime endDate, int itemsPerPage = 28)
        {
            _reportType = ReportType.PriceChange;
            _priceItems = items ?? new List<PriceChangeReportItem>();
            _itemsPerPage = itemsPerPage;
            DateRangeText.Text = $"ช่วงเวลา: {startDate:dd/MM/yyyy} - {endDate:dd/MM/yyyy}";
            GeneratedDateText.Text = $"สร้างเมื่อ: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";

            // แสดงหน้าแรกก่อน
            if (_priceItems.Count > 0)
            {
                var firstPageItems = _priceItems.Take(_itemsPerPage).ToList();
                ReportItemsControl.ItemTemplate = CreateItemTemplateForPriceChange();
                ReportItemsControl.ItemsSource = firstPageItems;
            }
        }

        // New overload for Inactive products report
        public void SetData(List<InactiveProductReportItem> items, DateTime printedAt, int itemsPerPage = 28)
        {
            _reportType = ReportType.InactiveProducts;
            _inactiveItems = items ?? new List<InactiveProductReportItem>();
            _itemsPerPage = itemsPerPage;
            DateRangeText.Text = $"สร้างเมื่อ: {printedAt:dd/MM/yyyy HH:mm}";
            GeneratedDateText.Text = $"สร้างเมื่อ: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";

            if (_inactiveItems.Count > 0)
            {
                var firstPageItems = _inactiveItems.Take(_itemsPerPage).ToList();
                ReportItemsControl.ItemTemplate = CreateItemTemplateForInactive();
                ReportItemsControl.ItemsSource = firstPageItems;
            }
        }

        /// <summary>
        /// สร้างหน้าแยกสำหรับการพิมพ์
        /// </summary>
        public List<UIElement> GetPrintPages()
        {
            var pages = new List<UIElement>();

            if (_reportType == ReportType.InactiveProducts)
            {
                if (_inactiveItems == null || _inactiveItems.Count == 0)
                {
                    pages.Add(this);
                    return pages;
                }

                var totalPages = (int)Math.Ceiling((double)_inactiveItems.Count / _itemsPerPage);

                for (int pageIndex = 0; pageIndex < totalPages; pageIndex++)
                {
                    var pageItems = _inactiveItems
                        .Skip(pageIndex * _itemsPerPage)
                        .Take(_itemsPerPage)
                        .ToList();

                    var pageView = CreatePageViewForInactive(pageItems, pageIndex + 1, totalPages);
                    pages.Add(pageView);
                }

                return pages;
            }

            if (_reportType == ReportType.KitchenPeople)
            {
                if (_allItems == null || _allItems.Count == 0)
                {
                    pages.Add(this);
                    return pages;
                }

                // แบ่งข้อมูลเป็นหน้า ๆ โดยใช้ _itemsPerPage
                var totalPages = (int)Math.Ceiling((double)_allItems.Count / _itemsPerPage);

                for (int pageIndex = 0; pageIndex < totalPages; pageIndex++)
                {
                    var pageItems = _allItems
                        .Skip(pageIndex * _itemsPerPage)
                        .Take(_itemsPerPage)
                        .ToList();

                    var pageView = CreatePageView(pageItems, pageIndex + 1, totalPages);
                    pages.Add(pageView);
                }

                return pages;
            }
            else // PriceChange
            {
                if (_priceItems == null || _priceItems.Count == 0)
                {
                    pages.Add(this);
                    return pages;
                }

                var totalPages = (int)Math.Ceiling((double)_priceItems.Count / _itemsPerPage);

                for (int pageIndex = 0; pageIndex < totalPages; pageIndex++)
                {
                    var pageItems = _priceItems
                        .Skip(pageIndex * _itemsPerPage)
                        .Take(_itemsPerPage)
                        .ToList();

                    var pageView = CreatePageViewForPriceChange(pageItems, pageIndex + 1, totalPages);
                    pages.Add(pageView);
                }

                return pages;
            }
        }

        private Grid CreatePageView(List<KitchenPeopleReportItem> items, int pageNumber, int totalPages)
        {
            var grid = new Grid
            {
                Width = 794,
                Height = 1123,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                Padding = new Thickness(40)
            };

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            Grid.SetRow(headerStack, 0);

            var title = new TextBlock
            {
                Text = "รายงานจำนวนคนตามห้องครัว",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
            };
            headerStack.Children.Add(title);

            var dateRange = new TextBlock
            {
                Text = DateRangeText.Text,
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
            };
            headerStack.Children.Add(dateRange);

            var generatedDate = new TextBlock
            {
                Text = GeneratedDateText.Text,
                FontSize = 10,
                Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 102, 102, 102))
            };
            headerStack.Children.Add(generatedDate);

            grid.Children.Add(headerStack);

            // Table Header
            var tableHeader = CreateTableHeader();
            Grid.SetRow(tableHeader, 1);
            grid.Children.Add(tableHeader);

            // Table Content - ✅ แก้ไขที่นี่
            var itemsControl = new ItemsControl
            {
                ItemTemplate = CreateItemTemplate(),
                ItemsSource = items
            };
            Grid.SetRow(itemsControl, 2);
            grid.Children.Add(itemsControl);

            // Page Number
            var pageInfo = new TextBlock
            {
                Text = $"หน้า {pageNumber}/{totalPages}",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
            };
            Grid.SetRow(pageInfo, 3);
            grid.Children.Add(pageInfo);

            return grid;
        }

        private Grid CreatePageViewForPriceChange(List<PriceChangeReportItem> items, int pageNumber, int totalPages)
        {
            var grid = new Grid
            {
                Width = 794,
                Height = 1123,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                Padding = new Thickness(40)
            };

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            Grid.SetRow(headerStack, 0);

            var title = new TextBlock
            {
                Text = "รายงานการเปลี่ยนแปลงราคา",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
            };
            headerStack.Children.Add(title);

            var dateRange = new TextBlock
            {
                Text = DateRangeText.Text,
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
            };
            headerStack.Children.Add(dateRange);

            var generatedDate = new TextBlock
            {
                Text = GeneratedDateText.Text,
                FontSize = 10,
                Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 102, 102, 102))
            };
            headerStack.Children.Add(generatedDate);

            grid.Children.Add(headerStack);

            // Table Header for PriceChange
            var tableHeader = CreateTableHeaderForPriceChange();
            Grid.SetRow(tableHeader, 1);
            grid.Children.Add(tableHeader);

            // Table Content for PriceChange
            var itemsControl = new ItemsControl
            {
                ItemTemplate = CreateItemTemplateForPriceChange(),
                ItemsSource = items
            };
            Grid.SetRow(itemsControl, 2);
            grid.Children.Add(itemsControl);

            // Page Number
            var pageInfo = new TextBlock
            {
                Text = $"หน้า {pageNumber}/{totalPages}",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
            };
            Grid.SetRow(pageInfo, 3);
            grid.Children.Add(pageInfo);

            return grid;
        }

        private Grid CreatePageViewForInactive(List<InactiveProductReportItem> items, int pageNumber, int totalPages)
        {
            var grid = new Grid
            {
                Width = 794,
                Height = 1123,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                Padding = new Thickness(40)
            };

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            Grid.SetRow(headerStack, 0);

            var title = new TextBlock
            {
                Text = "รายงานสินค้าที่ถูกยกเลิก",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
            };
            headerStack.Children.Add(title);

            var dateRange = new TextBlock
            {
                Text = DateRangeText.Text,
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
            };
            headerStack.Children.Add(dateRange);

            var generatedDate = new TextBlock
            {
                Text = GeneratedDateText.Text,
                FontSize = 10,
                Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 102, 102, 102))
            };
            headerStack.Children.Add(generatedDate);

            grid.Children.Add(headerStack);

            // Table Header for Inactive
            var tableHeader = CreateTableHeaderForInactive();
            Grid.SetRow(tableHeader, 1);
            grid.Children.Add(tableHeader);

            // Table Content for Inactive
            var itemsControl = new ItemsControl
            {
                ItemTemplate = CreateItemTemplateForInactive(),
                ItemsSource = items
            };
            Grid.SetRow(itemsControl, 2);
            grid.Children.Add(itemsControl);

            // Page Number
            var pageInfo = new TextBlock
            {
                Text = $"หน้า {pageNumber}/{totalPages}",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
            };
            Grid.SetRow(pageInfo, 3);
            grid.Children.Add(pageInfo);

            return grid;
        }

        private Grid CreateTableHeader()
        {
            var grid = new Grid
            {
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black),
                BorderThickness = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(0, 0, 0, 6),
                Margin = new Thickness(0, 0, 0, 6)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            var headers = new[]
            {
                ("วันที่", 0, HorizontalAlignment.Left),
                ("ห้องครัว", 1, HorizontalAlignment.Left),
                ("จำนวนใบ", 2, HorizontalAlignment.Right),
                ("คาดหวัง", 3, HorizontalAlignment.Right),
                ("จริง", 4, HorizontalAlignment.Right),
                ("ส่วนต่าง", 5, HorizontalAlignment.Right),
                ("% (จริง/คาดหวัง)", 6, HorizontalAlignment.Right)
            };

            foreach (var (text, col, alignment) in headers)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 11,
                    HorizontalAlignment = alignment,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
                };
                Grid.SetColumn(tb, col);
                grid.Children.Add(tb);
            }

            return grid;
        }

        private Grid CreateTableHeaderForPriceChange()
        {
            var grid = new Grid
            {
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black),
                BorderThickness = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(0, 0, 0, 6),
                Margin = new Thickness(0, 0, 0, 6)
            };

            // Columns arranged to match CSV export order:
            // ProductCode, ProductName, Category, Unit, OldPrice, NewPrice, PriceChange, PercentChange, OldPriceDate, NewPriceDate, IsActive
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // ProductCode
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); // ProductName
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Category
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); // Unit
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // OldPrice
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // NewPrice
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // PriceChange
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); // PercentChange
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // OldPriceDate
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // NewPriceDate
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // IsActive / Status

            var headers = new[]
            {
                ("รหัส", 0, HorizontalAlignment.Left),
                ("สินค้า", 1, HorizontalAlignment.Left),
                ("ประเภท", 2, HorizontalAlignment.Left),
                ("หน่วย", 3, HorizontalAlignment.Center),
                ("ราคาเดิม", 4, HorizontalAlignment.Right),
                ("ราคาใหม่", 5, HorizontalAlignment.Right),
                ("เปลี่ยนแปลง", 6, HorizontalAlignment.Right),
                ("%", 7, HorizontalAlignment.Right),
                ("วันที่ราคาเดิม", 8, HorizontalAlignment.Right),
                ("วันที่ราคาใหม่", 9, HorizontalAlignment.Right),
                ("สถานะ", 10, HorizontalAlignment.Center)
            };

            foreach (var (text, col, alignment) in headers)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 11,
                    HorizontalAlignment = alignment,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
                };
                Grid.SetColumn(tb, col);
                grid.Children.Add(tb);
            }

            return grid;
        }

        private Grid CreateTableHeaderForInactive()
        {
            var grid = new Grid
            {
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black),
                BorderThickness = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(0, 0, 0, 6),
                Margin = new Thickness(0, 0, 0, 6)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // Code
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); // Name
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Category
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) }); // Unit
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); // Reason
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) }); // By / Date

            var headers = new[]
            {
                ("รหัส", 0, HorizontalAlignment.Left),
                ("สินค้า", 1, HorizontalAlignment.Left),
                ("ประเภท", 2, HorizontalAlignment.Left),
                ("หน่วย", 3, HorizontalAlignment.Center),
                ("เหตุผลการปิด", 4, HorizontalAlignment.Left),
                ("โดย / วันที่", 5, HorizontalAlignment.Center)
            };

            foreach (var (text, col, alignment) in headers)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    FontSize = 11,
                    HorizontalAlignment = alignment,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black)
                };
                Grid.SetColumn(tb, col);
                grid.Children.Add(tb);
            }

            return grid;
        }

        // ✅ แก้ไข method นี้ให้สร้าง DataTemplate แบบ programmatically
        private DataTemplate CreateItemTemplate()
        {
            string xaml = @"
                <DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                    <Grid Margin=""0,2"">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width=""100""/>
                            <ColumnDefinition Width=""2*""/>
                            <ColumnDefinition Width=""70""/>
                            <ColumnDefinition Width=""70""/>
                            <ColumnDefinition Width=""70""/>
                            <ColumnDefinition Width=""70""/>
                            <ColumnDefinition Width=""90""/>
                        </Grid.ColumnDefinitions>

                        <TextBlock Grid.Column=""0"" Text=""{Binding DateDisplay}"" FontSize=""10"" Foreground=""Black"" TextWrapping=""NoWrap""/>
                        <TextBlock Grid.Column=""1"" Text=""{Binding KitchenDisplay}"" FontSize=""10"" Foreground=""Black"" TextWrapping=""NoWrap"" TextTrimming=""CharacterEllipsis""/>
                        <TextBlock Grid.Column=""2"" Text=""{Binding TransfersCount}"" TextAlignment=""Right"" FontSize=""10"" Foreground=""Black""/>
                        <TextBlock Grid.Column=""3"" Text=""{Binding TotalExpectedPeople}"" TextAlignment=""Right"" FontSize=""10"" Foreground=""Black""/>
                        <TextBlock Grid.Column=""4"" Text=""{Binding TotalActualPeople}"" TextAlignment=""Right"" FontSize=""10"" Foreground=""Black""/>
                        <TextBlock Grid.Column=""5"" Text=""{Binding Difference}"" TextAlignment=""Right"" FontSize=""10"" Foreground=""Black""/>
                        <TextBlock Grid.Column=""6"" Text=""{Binding PercentDisplay}"" TextAlignment=""Right"" FontSize=""10"" Foreground=""Black""/>
                    </Grid>
                </DataTemplate>";

            return (DataTemplate)XamlReader.Load(xaml);
        }

        private DataTemplate CreateItemTemplateForPriceChange()
        {
            // Build DataTemplate that matches CSV export order and includes ProductCode, Unit, Old/New dates, IsActive.
            string xaml = @"
                <DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
                    <Grid Margin=""0,2"">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width=""80""/>
                            <ColumnDefinition Width=""2*""/>
                            <ColumnDefinition Width=""1*""/>
                            <ColumnDefinition Width=""60""/>
                            <ColumnDefinition Width=""80""/>
                            <ColumnDefinition Width=""80""/>
                            <ColumnDefinition Width=""80""/>
                            <ColumnDefinition Width=""60""/>
                            <ColumnDefinition Width=""100""/>
                            <ColumnDefinition Width=""100""/>
                            <ColumnDefinition Width=""80""/>
                        </Grid.ColumnDefinitions>

                        <TextBlock Grid.Column=""0"" Text=""{Binding ProductCode}"" FontSize=""10"" Foreground=""Black"" TextWrapping=""NoWrap""/>
                        <StackPanel Grid.Column=""1"" Orientation=""Vertical"">
                            <TextBlock Text=""{Binding ProductName}"" FontSize=""11"" FontWeight=""SemiBold"" Foreground=""Black"" TextTrimming=""CharacterEllipsis""/>
                            <TextBlock Text=""{Binding Category}"" FontSize=""9"" Foreground=""#666666""/>
                        </StackPanel>
                        <TextBlock Grid.Column=""2"" Text=""{Binding Category}"" FontSize=""10"" Foreground=""#666666"" TextWrapping=""NoWrap""/>
                        <TextBlock Grid.Column=""3"" Text=""{Binding Unit}"" FontSize=""10"" Foreground=""#666666"" HorizontalAlignment=""Center""/>
                        <TextBlock Grid.Column=""4"" Text=""{Binding OldPriceDisplay}"" TextAlignment=""Right"" FontSize=""10"" Foreground=""Black""/>
                        <TextBlock Grid.Column=""5"" Text=""{Binding NewPriceDisplay}"" TextAlignment=""Right"" FontSize=""10"" Foreground=""Black""/>
                        <TextBlock Grid.Column=""6"" Text=""{Binding PriceChangeDisplay}"" TextAlignment=""Right"" FontSize=""10"" Foreground=""Black""/>
                        <TextBlock Grid.Column=""7"" Text=""{Binding PercentChangeDisplay}"" TextAlignment=""Right"" FontSize=""10"" Foreground=""Black""/>
                        <TextBlock Grid.Column=""8"" Text=""{Binding OldPriceDateDisplay}"" TextAlignment=""Right"" FontSize=""10"" Foreground=""#888888""/>
                        <TextBlock Grid.Column=""9"" Text=""{Binding NewPriceDateDisplay}"" TextAlignment=""Right"" FontSize=""10"" Foreground=""#888888""/>
                        <TextBlock Grid.Column=""10"" Text=""{Binding StatusDisplay}"" TextAlignment=""Center"" FontSize=""10"" Foreground=""Black""/>
                    </Grid>
                </DataTemplate>";

            return (DataTemplate)XamlReader.Load(xaml);
        }

        private DataTemplate CreateItemTemplateForInactive()
        {
            string xaml = @"
                <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                    <Grid Margin='0,2'>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width='80'/>
                            <ColumnDefinition Width='2*'/>
                            <ColumnDefinition Width='1*'/>
                            <ColumnDefinition Width='60'/>
                            <ColumnDefinition Width='2*'/>
                            <ColumnDefinition Width='120'/>
                        </Grid.ColumnDefinitions>

                        <TextBlock Grid.Column='0' Text='{Binding ProductCode}' FontSize='10' Foreground='Black' VerticalAlignment='Center' />
                        <StackPanel Grid.Column='1'>
                            <TextBlock Text='{Binding ProductName}' FontWeight='SemiBold' FontSize='11' Foreground='Black' TextTrimming='CharacterEllipsis'/>
                        </StackPanel>
                        <TextBlock Grid.Column='2' Text='{Binding Category}' FontSize='10' Foreground='Black' VerticalAlignment='Center'/>
                        <TextBlock Grid.Column='3' Text='{Binding Unit}' FontSize='10' Foreground='Black' HorizontalAlignment='Center' VerticalAlignment='Center'/>
                        <TextBlock Grid.Column='4' Text='{Binding DisabledReason}' FontSize='10' Foreground='Black' TextWrapping='Wrap'/>
                        <StackPanel Grid.Column='5'>
                            <TextBlock Text='{Binding DisabledBy}' FontSize='10' HorizontalAlignment='Center'/>
                            <TextBlock Text='{Binding DisabledAtDisplay}' FontSize='9' HorizontalAlignment='Center' Foreground='#666'/>
                        </StackPanel>
                    </Grid>
                </DataTemplate>";
            return (DataTemplate)XamlReader.Load(xaml);
        }
    }
}
