using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Requisition.Helpers
{
    /// <summary>
    /// Helper สำหรับแสดง Dialog ง่ายๆ จากทุกหน้า
    /// ใช้ App.Window.Content.XamlRoot เมื่อไม่ได้ระบุ XamlRoot
    /// ป้องกันการเปิด Dialog หลายตัวพร้อมกัน
    /// </summary>
    public static class DialogHelper
    {
        private static int _dialogOpen = 0;

        private static XamlRoot? ResolveXamlRoot(XamlRoot? provided)
        {
            if (provided != null) return provided;
            if (App.Window?.Content is FrameworkElement fe) return fe.XamlRoot;
            return null;
        }

        /// <summary>
        /// แสดง Error Dialog
        /// </summary>
        public static async Task ShowErrorAsync(string title, string message, XamlRoot? xamlRoot = null)
        {
            if (Interlocked.CompareExchange(ref _dialogOpen, 1, 0) == 1) return;

            try
            {
                var xr = ResolveXamlRoot(xamlRoot);
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    CloseButtonText = "ตกลง",
                    XamlRoot = xr
                };
                await dialog.ShowAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _dialogOpen, 0);
            }
        }

        /// <summary>
        /// แสดง Success Dialog
        /// </summary>
        public static async Task ShowSuccessAsync(string title, string message, XamlRoot? xamlRoot = null)
        {
            if (Interlocked.CompareExchange(ref _dialogOpen, 1, 0) == 1) return;

            try
            {
                var xr = ResolveXamlRoot(xamlRoot);
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    CloseButtonText = "ตกลง",
                    XamlRoot = xr
                };
                await dialog.ShowAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _dialogOpen, 0);
            }
        }

        /// <summary>
        /// แสดง Confirmation Dialog
        /// </summary>
        public static async Task<bool> ShowConfirmAsync(string title, string message, XamlRoot? xamlRoot = null)
        {
            if (Interlocked.CompareExchange(ref _dialogOpen, 1, 0) == 1) return false;

            try
            {
                var xr = ResolveXamlRoot(xamlRoot);
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = message,
                    PrimaryButtonText = "ยืนยัน",
                    CloseButtonText = "ยกเลิก",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = xr
                };
                var result = await dialog.ShowAsync();
                return result == ContentDialogResult.Primary;
            }
            finally
            {
                Interlocked.Exchange(ref _dialogOpen, 0);
            }
        }
    }
}
