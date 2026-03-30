using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Printing;
using System;
using System.Threading.Tasks;
using Windows.Graphics.Printing;
using System.Diagnostics;
using System.Collections.Generic;

namespace Requisition.Helpers
{
    public class PrintHelper
    {
        private PrintManager? _printManager;
        private PrintDocument _printDocument;
        private IPrintDocumentSource _printDocumentSource;
        private UIElement? _printContent;
        private Window _window;
        private Panel? _hiddenContainer;
        private TaskCompletionSource<bool>? _printTaskCompletion;

        private int _currentPageIndex = 0;
        private int _totalPrintPages = 1;
        private List<UIElement>? _allPages;

        public PrintHelper(Window window)
        {
            _window = window;
            _printDocument = new PrintDocument();
            _printDocumentSource = _printDocument.DocumentSource;
        }

        /// <summary>
        /// แสดง Print Dialog และพิมพ์เนื้อหา
        /// </summary>
        public async Task<bool> ShowPrintUIAsync(UIElement contentToPrint)
        {
            try
            {
                Debug.WriteLine("=== Starting Print Process ===");
                
                _printContent = contentToPrint;

                // ✅ เพิ่มบรรทัดนี้: สร้าง TaskCompletionSource ใหม่ทุกครั้ง
                _printTaskCompletion = new TaskCompletionSource<bool>();

                // ✅ ตรวจสอบว่าเป็น PrintableUsageReportView หรือไม่
                if (contentToPrint is Controls.PrintableUsageReportView usageReportView)
                {
                    _allPages = usageReportView.GetPrintPages();
                    _totalPrintPages = _allPages?.Count ?? 1;
                    Debug.WriteLine($"📄 Total pages to print: {_totalPrintPages}");
                }
                else if (contentToPrint is Controls.PrintableReportView reportView)
                {
                    _allPages = reportView.GetPrintPages();
                    _totalPrintPages = _allPages?.Count ?? 1;
                }
                else
                {
                    _allPages = null;
                    _totalPrintPages = 1;
                }

                // สร้าง container ที่ซ่อนไว้เพื่อให้ content render ได้
                if (_window.Content is Panel rootPanel)
                {
                    _hiddenContainer = new Grid
                    {
                        Visibility = Visibility.Collapsed
                    };
                    rootPanel.Children.Add(_hiddenContainer);
                    _hiddenContainer.Children.Add(_printContent);

                    // รอให้ layout update
                    await Task.Delay(100);
                    
                    // Force measure and arrange
                    _printContent.Measure(new Windows.Foundation.Size(794, 1123));
                    _printContent.Arrange(new Windows.Foundation.Rect(0, 0, 794, 1123));
                    _printContent.UpdateLayout();

                    Debug.WriteLine($"Content measured: {_printContent.DesiredSize.Width}x{_printContent.DesiredSize.Height}");
                }
                else
                {
                    Debug.WriteLine("WARNING: Could not access root panel");
                }

                // Register for print
                _printDocument.Paginate += PrintDocument_Paginate;
                _printDocument.GetPreviewPage += PrintDocument_GetPreviewPage;
                _printDocument.AddPages += PrintDocument_AddPages;

                // Get PrintManager for current window
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
                _printManager = PrintManagerInterop.GetForWindow(hWnd);
                _printManager.PrintTaskRequested += PrintManager_PrintTaskRequested;

                Debug.WriteLine("Showing print UI...");

                // Show print UI (ไม่รอให้เสร็จเพราะเป็น dialog)
                await PrintManagerInterop.ShowPrintUIForWindowAsync(hWnd);

                Debug.WriteLine("Print UI dialog opened - waiting for completion...");

                // รอให้ print task เสร็จ (หรือถูกยกเลิก) - timeout 2 นาที
                var completedTask = await Task.WhenAny(_printTaskCompletion!.Task, Task.Delay(TimeSpan.FromMinutes(2)));
                
                if (completedTask == _printTaskCompletion.Task)
                {
                    bool result = await _printTaskCompletion.Task;
                    Debug.WriteLine($"Print task completed: {result}");
                    return result;
                }
                else
                {
                    Debug.WriteLine("Print task timed out");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Print error: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                _printTaskCompletion?.TrySetResult(false);
                return false;
            }
        }

        private void PrintManager_PrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args)
        {
            Debug.WriteLine("PrintTaskRequested event fired");
            
            var printTask = args.Request.CreatePrintTask("รายงานจำนวนคนตามห้องครัว", sourceRequested =>
            {
                Debug.WriteLine("Setting print source");
                sourceRequested.SetSource(_printDocumentSource);
            });

            // ✅ เพิ่ม: ตั้งค่าขนาดกระดาษเป็น A4 และ orientation
            printTask.Options.MediaSize = PrintMediaSize.IsoA4;
            printTask.Options.Orientation = PrintOrientation.Portrait;
            
            // ✅ เพิ่ม: ตั้งค่า margin
            printTask.Options.PageRangeOptions.AllowAllPages = true;
            
            // ✅ เพิ่ม: กำหนด page range ให้พิมพ์ได้ทุกหน้า
            printTask.Options.PageRangeOptions.AllowCurrentPage = false;
            printTask.Options.PageRangeOptions.AllowCustomSetOfPages = true;

            Debug.WriteLine($"✅ Print options set: A4, Portrait");

            // ติดตาม completion events
            printTask.Completed += (s, e) =>
            {
                Debug.WriteLine($"Print task completed with status: {e.Completion}");
                _printTaskCompletion?.TrySetResult(e.Completion == PrintTaskCompletion.Submitted || 
                                                    e.Completion == PrintTaskCompletion.Abandoned);
            };
        }

        private void PrintDocument_Paginate(object sender, PaginateEventArgs e)
        {
            Debug.WriteLine($"📄 Paginate event - Total pages: {_totalPrintPages}");

            try
            {
                _currentPageIndex = 0;

                // ✅ บอก print system ว่ามีกี่หน้า
                _printDocument.SetPreviewPageCount(_totalPrintPages, PreviewPageCountType.Final);
                Debug.WriteLine($"✅ Preview page count set to {_totalPrintPages}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Paginate error: {ex.Message}");
            }
        }

        private void PrintDocument_GetPreviewPage(object sender, GetPreviewPageEventArgs e)
        {
            Debug.WriteLine($"👁️ GetPreviewPage event - Page: {e.PageNumber}");

            try
            {
                UIElement pageContent = null!;

                // ✅ ดึงหน้าที่ถูกต้อง
                if (_allPages != null && _allPages.Count > 0)
                {
                    int pageIndex = e.PageNumber - 1; // PageNumber เริ่มจาก 1
                    if (pageIndex >= 0 && pageIndex < _allPages.Count)
                    {
                        pageContent = _allPages[pageIndex];
                    }
                }
                else
                {
                    pageContent = _printContent!;
                }

                if (pageContent != null)
                {
                    _printDocument.SetPreviewPage(e.PageNumber, pageContent);
                    Debug.WriteLine($"✅ Preview page {e.PageNumber} set successfully");
                }
                else
                {
                    Debug.WriteLine($"❌ Page content is null for page {e.PageNumber}!");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ GetPreviewPage error: {ex.Message}");
            }
        }

        private void PrintDocument_AddPages(object sender, AddPagesEventArgs e)
        {
            Debug.WriteLine("📋 AddPages event fired");

            try
            {
                // ✅ เพิ่มทุกหน้า
                if (_allPages != null && _allPages.Count > 0)
                {
                    foreach (var page in _allPages)
                    {
                        _printDocument.AddPage(page);
                    }
                    Debug.WriteLine($"✅ Added {_allPages.Count} pages successfully");
                }
                else if (_printContent != null)
                {
                    _printDocument.AddPage(_printContent);
                    Debug.WriteLine("✅ Added single page successfully");
                }
                else
                {
                    Debug.WriteLine("❌ Print content is null!");
                }

                _printDocument.AddPagesComplete();
                Debug.WriteLine("✅ AddPagesComplete called");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ AddPages error: {ex.Message}");
            }
        }

        /// <summary>
        /// เรียกเมื่อเสร็จสิ้นการพิมพ์เพื่อ cleanup
        /// </summary>
        public void Dispose()
        {
            Debug.WriteLine("🧹 Disposing PrintHelper");

            // Set completion if not already set
            _printTaskCompletion?.TrySetResult(false);

            if (_printDocument != null)
            {
                _printDocument.Paginate -= PrintDocument_Paginate;
                _printDocument.GetPreviewPage -= PrintDocument_GetPreviewPage;
                _printDocument.AddPages -= PrintDocument_AddPages;
            }

            if (_printManager != null)
            {
                _printManager.PrintTaskRequested -= PrintManager_PrintTaskRequested;
            }

            // Cleanup hidden container
            if (_hiddenContainer != null && _window.Content is Panel rootPanel)
            {
                try
                {
                    _hiddenContainer.Children.Clear();
                    rootPanel.Children.Remove(_hiddenContainer);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Warning: Cleanup error: {ex.Message}");
                }
                _hiddenContainer = null;
            }

            Debug.WriteLine("✅ PrintHelper disposed");
        }
    }
}
