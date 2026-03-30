using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Requisition.Pages;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace Requisition
{
    public sealed partial class MainWindow : Window
    {
        private MenuFlyout _settingsFlyout;

        public MainWindow()
        {
            InitializeComponent();

            _settingsFlyout = new MenuFlyout();
            var costPerHeadItem = new MenuFlyoutItem { Text = "ตั้งค่า Outlet" };
            costPerHeadItem.Click += CostPerHeadItem_Click;
            _settingsFlyout.Items.Add(costPerHeadItem);

            var kitchenItem = new MenuFlyoutItem { Text = "ตั้งค่าห้องครัว" };
            kitchenItem.Click += KitchenItem_Click;
            _settingsFlyout.Items.Add(kitchenItem);

            var templateItem = new MenuFlyoutItem { Text = "ตั้งค่าTemplate" };
            templateItem.Click += TemplateItem_Click;
            _settingsFlyout.Items.Add(templateItem);

            var hiddenCostItem = new MenuFlyoutItem { Text = "ตั้งค่าต้นทุนแฝง" };
            hiddenCostItem.Click += HiddenCostItem_Click;
            _settingsFlyout.Items.Add(hiddenCostItem);

            SettingsItem.Tapped += SettingsItem_Tapped;

            SelectNavByTag("Home");
            ContentFrame.Navigate(typeof(HomePage));
        }

        private void SettingsItem_Tapped(object? sender, TappedRoutedEventArgs e)
        {
            if (sender is UIElement el)
            {
                var options = new FlyoutShowOptions { Placement = FlyoutPlacementMode.Top };
                _settingsFlyout.ShowAt(el, options);
            }
        }

        private void CostPerHeadItem_Click(object? sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(typeof(CostPerHeadSettingsPage));
        }

        private void KitchenItem_Click(object? sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(typeof(KitchenSettingsPage));
        }

        private void TemplateItem_Click(object? sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(typeof(TemplatePage));
        }

        private void HiddenCostItem_Click(object? sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(typeof(HiddenCostSettingsPage));
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is NavigationViewItem item)
            {
                string? tag = item.Tag?.ToString();

                switch (tag)
                {
                    case "Home":
                        ContentFrame.Navigate(typeof(HomePage));
                        break;
                    case "Transfer":
                        ContentFrame.Navigate(typeof(TransferListPage));
                        break;
                    case "ManageTransfer":
                        ContentFrame.Navigate(typeof(ManageTransferPage));
                        break;
                    case "CombineTransfers":
                        // ✅ แก้ไข: Navigate ไปหน้า List ใหม่แทน
                        ContentFrame.Navigate(typeof(CombinedTransfersListPage));
                        break;
                    case "ImportExcel":
                        ContentFrame.Navigate(typeof(ImportExcelPage));
                        break;
                    case "ProductList":
                        ContentFrame.Navigate(typeof(ProductListPage));
                        break;
                    case "InactiveProducts":
                        ContentFrame.Navigate(typeof(InactiveProductsReportPage));
                        break;
                    case "CostReport":
                        ContentFrame.Navigate(typeof(CostReportPage));
                        break;
                    case "KitchenPeopleReport":
                        ContentFrame.Navigate(typeof(Pages.KitchenPeopleReportPage));
                        break;
                    case "Settings":
                        // ignore — settings handled by flyout
                        break;
                    case "UsageReport":
                        ContentFrame.Navigate(typeof(UsageReportPage));
                        break;
                    case "PriceChangeReport":
                        ContentFrame.Navigate(typeof(PriceChangeReportPage));
                        break;
                    case "OutletUsage":
                        ContentFrame.Navigate(typeof(Pages.OutletUsageComparisonPage));
                        break;
                    case "OutletCostComparison":
                        ContentFrame.Navigate(typeof(Pages.OutletCostComparisonPage));
                        break;
                    case "OutletDailyCost":
                        ContentFrame.Navigate(typeof(Pages.OutletDailyCostPage));
                        break;
                    case "OutletReports":
                        // keep previous behaviour if top-level selected: navigate to usage page as default
                        ContentFrame.Navigate(typeof(Pages.OutletUsageComparisonPage));
                        break;
                }
            }
        }

        private NavigationViewItem? FindNavItemByTag(IEnumerable items, string tag)
        {
            foreach (var obj in items)
            {
                if (obj is NavigationViewItem nvi)
                {
                    if (string.Equals(nvi.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                        return nvi;

                    var child = FindNavItemByTag(nvi.MenuItems, tag);
                    if (child != null) return child;
                }
            }
            return null;
        }

        public void SelectNavByTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;

            var found = FindNavItemByTag(NavView.MenuItems, tag)
                     ?? FindNavItemByTag(NavView.FooterMenuItems, tag);

            if (found == null && NavView.SettingsItem is NavigationViewItem settings)
            {
                found = FindNavItemByTag(settings.MenuItems, tag);
            }

            if (found != null) NavView.SelectedItem = found;
        }
    }
}