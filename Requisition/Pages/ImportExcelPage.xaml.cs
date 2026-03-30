using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfficeOpenXml;
using Requisition.Models;
using Requisition.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Requisition.Pages
{
    public sealed partial class ImportExcelPage : Page
    {
        private string? _selectedFilePath;
        private string? _selectedFileName;
        private List<string> _sheetNames = new();
        private List<Product> _previewData = new();
        private readonly DatabaseService _databaseService;

        public ImportExcelPage()
        {
            InitializeComponent();
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            _databaseService = new DatabaseService();

            _ = CheckDatabaseConnectionAsync();
        }

        private async Task CheckDatabaseConnectionAsync()
        {
            var isConnected = await _databaseService.TestConnectionAsync();
            if (!isConnected)
            {
                await ShowErrorDialog("⚠️ ไม่สามารถเชื่อมต่อกับฐานข้อมูลได้\n\nกรุณาตรวจสอบ:\n- SQL Server ทำงานอยู่หรือไม่\n- Connection String ใน appsettings.json ถูกต้องหรือไม่\n- รัน SQL Script สร้างฐานข้อมูลแล้วหรือยัง");
            }
        }

        private async void SelectFileButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            var hwnd = WindowNative.GetWindowHandle(App.Window);
            InitializeWithWindow.Initialize(picker, hwnd);

            picker.FileTypeFilter.Add(".xlsx");
            picker.FileTypeFilter.Add(".xls");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                _selectedFilePath = file.Path;
                _selectedFileName = file.Name;
                SelectedFileText.Text = file.Name;
                SelectedFileText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green);

                await LoadSheetsAsync();
            }
        }

        private async Task LoadSheetsAsync()
        {
            try
            {
                _sheetNames.Clear();
                SheetComboBox.Items.Clear();

                using var package = new ExcelPackage(new FileInfo(_selectedFilePath!));

                foreach (var worksheet in package.Workbook.Worksheets)
                {
                    _sheetNames.Add(worksheet.Name);
                    SheetComboBox.Items.Add(worksheet.Name);
                }

                if (SheetComboBox.Items.Count > 0)
                {
                    SheetComboBox.SelectedIndex = 0;
                    SheetSelectionPanel.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog($"ไม่สามารถอ่านไฟล์ได้: {ex.Message}");
            }
        }

        private void SheetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SheetComboBox.SelectedIndex >= 0)
            {
                LoadColumnHeaders();
                ActionPanel.Visibility = Visibility.Visible;
            }
        }

        private void LoadColumnHeaders()
        {
            try
            {
                using var package = new ExcelPackage(new FileInfo(_selectedFilePath!));
                var worksheet = package.Workbook.Worksheets[SheetComboBox.SelectedIndex];

                CodeColumnComboBox.Items.Clear();
                NameColumnComboBox.Items.Clear();
                CategoryColumnComboBox.Items.Clear();
                UnitColumnComboBox.Items.Clear();
                PriceColumnComboBox.Items.Clear();
                PriceDateColumnComboBox.Items.Clear();
                RemarksColumnComboBox.Items.Clear();

                var comboBoxes = new[] {
                    CodeColumnComboBox,
                    NameColumnComboBox,
                    CategoryColumnComboBox,
                    UnitColumnComboBox,
                    PriceColumnComboBox,
                    PriceDateColumnComboBox,
                    RemarksColumnComboBox
                };

                foreach (var cb in comboBoxes)
                {
                    cb.Items.Add("ไม่ระบุ");
                }

                int colCount = worksheet.Dimension?.Columns ?? 0;
                for (int col = 1; col <= colCount; col++)
                {
                    var cellValue = worksheet.Cells[1, col].Value?.ToString() ?? $"Column {col}";
                    var displayText = $"{GetExcelColumnName(col)} - {cellValue}";

                    foreach (var cb in comboBoxes)
                    {
                        cb.Items.Add(displayText);
                    }

                    var lowerValue = cellValue.ToLower();
                    if (lowerValue.Contains("รหัส") || lowerValue.Contains("code"))
                        CodeColumnComboBox.SelectedIndex = col;
                    else if (lowerValue.Contains("ชื่อ") || lowerValue.Contains("name"))
                        NameColumnComboBox.SelectedIndex = col;
                    else if (lowerValue.Contains("ประเภท") || lowerValue.Contains("category"))
                        CategoryColumnComboBox.SelectedIndex = col;
                    else if (lowerValue.Contains("หน่วย") || lowerValue.Contains("unit"))
                        UnitColumnComboBox.SelectedIndex = col;
                    else if (lowerValue.Contains("ราคา") || lowerValue.Contains("price"))
                        PriceColumnComboBox.SelectedIndex = col;
                    else if (lowerValue.Contains("วันที่") || lowerValue.Contains("date"))
                        PriceDateColumnComboBox.SelectedIndex = col;
                    else if (lowerValue.Contains("หมายเหตุ") || lowerValue.Contains("remark") || lowerValue.Contains("note"))
                        RemarksColumnComboBox.SelectedIndex = col;
                }

                ColumnMappingPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                _ = ShowErrorDialog($"ไม่สามารถอ่าน columns ได้: {ex.Message}");
            }
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (CodeColumnComboBox.SelectedIndex <= 0 || NameColumnComboBox.SelectedIndex <= 0)
            {
                _ = ShowErrorDialog("กรุณาเลือก Column สำหรับ รหัสสินค้า และ ชื่อสินค้า");
                return;
            }

            LoadPreviewData();
        }

        private void LoadPreviewData()
        {
            try
            {
                _previewData.Clear();

                using var package = new ExcelPackage(new FileInfo(_selectedFilePath!));
                var worksheet = package.Workbook.Worksheets[SheetComboBox.SelectedIndex];

                var mapping = new ColumnMapping
                {
                    CodeColumn = CodeColumnComboBox.SelectedIndex,
                    NameColumn = NameColumnComboBox.SelectedIndex,
                    CategoryColumn = CategoryColumnComboBox.SelectedIndex > 0 ? CategoryColumnComboBox.SelectedIndex : null,
                    UnitColumn = UnitColumnComboBox.SelectedIndex > 0 ? UnitColumnComboBox.SelectedIndex : null,
                    PriceColumn = PriceColumnComboBox.SelectedIndex > 0 ? PriceColumnComboBox.SelectedIndex : null,
                    PriceDateColumn = PriceDateColumnComboBox.SelectedIndex > 0 ? PriceDateColumnComboBox.SelectedIndex : null,
                    RemarksColumn = RemarksColumnComboBox.SelectedIndex > 0 ? RemarksColumnComboBox.SelectedIndex : null
                };

                int rowCount = worksheet.Dimension?.Rows ?? 0;
                int importedCount = 0;
                int skippedCount = 0;
                
                // 🔑 ใช้ Dictionary เพื่อ deduplicate ตาม (Code, PriceDate)
                var tempData = new Dictionary<string, Product>();

                for (int row = 2; row <= rowCount; row++)
                {
                    var code = worksheet.Cells[row, mapping.CodeColumn.Value].Value?.ToString()?.Trim();

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        skippedCount++;
                        continue;
                    }

                    decimal? parsedPrice = null;
                    if (mapping.PriceColumn.HasValue)
                    {
                        parsedPrice = ParseDecimal(worksheet.Cells[row, mapping.PriceColumn.Value].Value);
                    }

                    string? priceDateRaw = null;
                    DateTime? priceDateParsed = null;
                    if (mapping.PriceDateColumn.HasValue)
                    {
                        var raw = worksheet.Cells[row, mapping.PriceDateColumn.Value].Value;
                        priceDateRaw = raw?.ToString()?.Trim();
                        priceDateParsed = ParseDateTime(raw);
                    }

                    string? remarks = null;
                    if (mapping.RemarksColumn.HasValue)
                    {
                        remarks = worksheet.Cells[row, mapping.RemarksColumn.Value].Value?.ToString()?.Trim();
                    }

                    var product = new Product
                    {
                        Code = code,
                        Name = worksheet.Cells[row, mapping.NameColumn.Value].Value?.ToString()?.Trim() ?? "",
                        Category = mapping.CategoryColumn.HasValue
                            ? worksheet.Cells[row, mapping.CategoryColumn.Value].Value?.ToString()?.Trim() ?? ""
                            : "",
                        Unit = mapping.UnitColumn.HasValue
                            ? worksheet.Cells[row, mapping.UnitColumn.Value].Value?.ToString()?.Trim() ?? ""
                            : "",
                        Price = parsedPrice ?? 0.00m,
                        PriceDate = priceDateParsed,
                        PriceDateRaw = priceDateRaw,
                        Remarks = remarks,
                        ExcelRow = row
                    };

                    // 🔑 สร้าง unique key จาก (Code + PriceDate)
                    var priceDate = product.PriceDate?.Date ?? DateTime.Today;
                    var uniqueKey = $"{product.Code}|{priceDate:yyyy-MM-dd}";

                    // ถ้ามีซ้ำ ให้ใช้ record ล่าสุด (แถวท้ายสุด)
                    if (tempData.ContainsKey(uniqueKey))
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ พบข้อมูลซ้ำ: {product.Code} วันที่ {priceDate:yyyy-MM-dd} (ใช้ข้อมูลจากแถว {row})");
                        skippedCount++;
                    }
                    
                    tempData[uniqueKey] = product; // จะ overwrite ถ้ามีซ้ำ
                    importedCount++;
                }

                // แปลง Dictionary กลับเป็น List
                _previewData = tempData.Values.ToList();

                PreviewListView.ItemsSource = _previewData.Take(10).ToList();
                
                var duplicateCount = importedCount - _previewData.Count;
                if (duplicateCount > 0)
                {
                    PreviewInfoBar.Message = $"พบข้อมูล {_previewData.Count} รายการ (ข้าม {skippedCount} รายการ) | ⚠️ พบข้อมูลซ้ำ {duplicateCount} รายการ (ใช้ข้อมูลล่าสุด)";
                    PreviewInfoBar.Severity = InfoBarSeverity.Warning;
                }
                else
                {
                    PreviewInfoBar.Message = $"พบข้อมูล {importedCount} รายการ (ข้าม {skippedCount} รายการ)";
                    PreviewInfoBar.Severity = InfoBarSeverity.Informational;
                }
                
                PreviewPanel.Visibility = Visibility.Visible;
                ActionPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                _ = ShowErrorDialog($"ไม่สามารถโหลดข้อมูลได้: {ex.Message}");
            }
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (CodeColumnComboBox.SelectedIndex <= 0 || NameColumnComboBox.SelectedIndex <= 0)
            {
                await ShowErrorDialog("กรุณาเลือก Column สำหรับ รหัสสินค้า และ ชื่อสินค้า");
                return;
            }

            if (_previewData.Count == 0)
            {
                LoadPreviewData();
            }

            if (_previewData.Count == 0)
            {
                await ShowErrorDialog("ไม่มีข้อมูลให้ Import");
                return;
            }

            var t0 = DateTime.Now;
            var importDate = DateTime.UtcNow;
            var conflictResult = await _databaseService.AnalyzeImportConflictsAsync(_previewData);
            var t1 = DateTime.Now;
            System.Diagnostics.Debug.WriteLine(
                $"[ImportTiming] AnalyzeImportConflictsAsync took {(t1 - t0).TotalSeconds:F2} seconds");

            // ----- เคส 2: Code ไม่ซ้ำ + Name ซ้ำ -----
            if (conflictResult.NameOnlyMatch.Count > 0)
            {
                var lines = conflictResult.NameOnlyMatch
                    .Select((pair, index) =>
                    {
                        var excel = pair.ExcelProduct;
                        var db = pair.DbProduct;
                        var rowInfo = excel.ExcelRow.HasValue ? $" (แถวที่ {excel.ExcelRow.Value})" : "";
                        return $"{index + 1}. Excel{rowInfo}: [{excel.Code}] {excel.Name} → ซ้ำชื่อกับในระบบ: [{db.Code}] {db.Name}";
                    });

                string message =
                    "พบรายการที่ \"รหัสไม่ซ้ำ แต่ชื่อซ้ำ\" กับข้อมูลในฐานข้อมูล:\n\n" +
                    string.Join("\n", lines) +
                    "\n\nคุณมั่นใจหรือไม่ว่าต้องการบันทึกรายการเหล่านี้ต่อไป?";

                var dialog = new ContentDialog
                {
                    Title = "ยืนยันการนำเข้า (รหัสไม่ซ้ำ แต่ชื่อซ้ำ)",
                    Content = message,
                    PrimaryButtonText = "มั่นใจ / ดำเนินการต่อ",
                    CloseButtonText = "ยกเลิก",
                    XamlRoot = XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    // ผู้ใช้ไม่มั่นใจ → ยกเลิก Import ทั้งหมด
                    return;
                }
            }

            // ----- เคส 3: Code ซ้ำ + Name ไม่ซ้ำ -----
            if (conflictResult.CodeOnlyMatch.Count > 0)
            {
                var lines = conflictResult.CodeOnlyMatch
                    .Select((pair, index) =>
                    {
                        var excel = pair.ExcelProduct;
                        var db = pair.DbProduct;
                        var rowInfo = excel.ExcelRow.HasValue ? $" (แถวที่ {excel.ExcelRow.Value})" : "";
                        return $"{index + 1}. Excel{rowInfo}: [{excel.Code}] {excel.Name} → รหัสนี้ในระบบปัจจุบันคือ: [{db.Code}] {db.Name}";
                    });

                string message =
                    "พบรายการที่ \"รหัสซ้ำ แต่ชื่อไม่ซ้ำ\" กับข้อมูลในฐานข้อมูล:\n\n" +
                    string.Join("\n", lines) +
                    "\n\nการดำเนินการต่ออาจทับข้อมูลเดิม คุณมั่นใจหรือไม่ว่าจะบันทึกรายการเหล่านี้?";

                var dialog = new ContentDialog
                {
                    Title = "ยืนยันการนำเข้า (รหัสซ้ำ แต่ชื่อไม่ซ้ำ)",
                    Content = message,
                    PrimaryButtonText = "มั่นใจ / ดำเนินการต่อ",
                    CloseButtonText = "ยกเลิก",
                    XamlRoot = XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    // ผู้ใช้ไม่มั่นใจ → ยกเลิก Import ทั้งหมด
                    return;
                }
            }

            ImportButton.IsEnabled = false;

            var progressDialog = new ContentDialog
            {
                Title = "กำลัง Import ข้อมูล...",
                Content = new ProgressRing { IsActive = true, Width = 50, Height = 50 },
                XamlRoot = XamlRoot
            };

            _ = progressDialog.ShowAsync();

            try
            {
                var importedBy = Environment.UserName;

                // Use current time in UTC+7 as the importDate passed to DB service
                var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                var importDateTz = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

                var t2 = DateTime.Now;
                var importResult = await _databaseService.BulkImportProductsWithPriceHistoryAsync(
                    _previewData,
                    importDateTz,
                    _selectedFileName ?? "Unknown",
                    _sheetNames[SheetComboBox.SelectedIndex],
                    importedBy
                );
                var t3 = DateTime.Now;

                System.Diagnostics.Debug.WriteLine(
                    $"[ImportTiming] BulkImportProductsWithPriceHistoryAsync took {(t3 - t2).TotalSeconds:F2} seconds");

                progressDialog.Hide();

                int buddhistYear = importDateTz.Year + 543;
                string displayDate = $"{importDateTz.Day:D2}/{importDateTz.Month:D2}/{buddhistYear}";

                // สร้างข้อความสรุปผล
                var summaryMessage = new System.Text.StringBuilder();
                summaryMessage.AppendLine("✅ Import เสร็จสิ้น!\n");
                summaryMessage.AppendLine("📊 สรุปผลการ Import:");
                summaryMessage.AppendLine($"• สำเร็จ: {importResult.SuccessCount} รายการ");
                summaryMessage.AppendLine($"• ล้มเหลว: {importResult.FailedCount} รายการ");
                summaryMessage.AppendLine($"• สินค้าใหม่: {importResult.NewProducts} รายการ");
                summaryMessage.AppendLine($"• อัปเดตราคา: {importResult.UpdatedPrices} รายการ");
                summaryMessage.AppendLine($"• รวมทั้งหมด: {_previewData.Count} รายการ\n");
                summaryMessage.AppendLine($"📅 วันที่บันทึก: {displayDate}");
                summaryMessage.AppendLine($"👤 ผู้ Import: {importedBy}");

                // ถ้ามีข้อผิดพลาด แสดงรายละเอียด
                if (importResult.Errors.Count > 0)
                {
                    summaryMessage.AppendLine("\n⚠️ รายการที่ล้มเหลว:");
                    summaryMessage.AppendLine("━━━━━━━━━━━━━━━━━━━━━━");
                    
                    var groupedErrors = importResult.Errors
                        .GroupBy(e => e.ErrorType)
                        .OrderByDescending(g => g.Count());

                    foreach (var errorGroup in groupedErrors)
                    {
                        summaryMessage.AppendLine($"\n📌 {GetErrorTypeIcon(errorGroup.Key)} {GetErrorTypeName(errorGroup.Key)} ({errorGroup.Count()} รายการ):");
                        
                        foreach (var error in errorGroup.Take(5)) // แสดงไม่เกิน 5 รายการต่อประเภท
                        {
                            var rowInfo = error.ExcelRow.HasValue ? $"แถว {error.ExcelRow.Value}" : "ไม่ระบุ";
                            summaryMessage.AppendLine($"  • [{error.ProductCode}] {error.ProductName}");
                            summaryMessage.AppendLine($"    ({rowInfo}) - {error.ErrorMessage}");
                        }

                        if (errorGroup.Count() > 5)
                        {
                            summaryMessage.AppendLine($"  ... และอีก {errorGroup.Count() - 5} รายการ");
                        }
                    }
                }

                // แสดง Dialog แบบ ScrollViewer สำหรับข้อความยาว
                var scrollViewer = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = summaryMessage.ToString(),
                        TextWrapping = TextWrapping.Wrap,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas")
                    },
                    MaxHeight = 500,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                var resultDialog = new ContentDialog
                {
                    Title = importResult.FailedCount > 0 ? "⚠️ Import เสร็จสิ้น (มีข้อผิดพลาด)" : "✅ Import สำเร็จ",
                    Content = scrollViewer,
                    CloseButtonText = "ตกลง",
                    XamlRoot = XamlRoot
                };

                await resultDialog.ShowAsync();

                // นำทางไปหน้า ProductList
                Frame.Navigate(typeof(ProductListPage));

                if (App.Window is MainWindow mainWindow)
                {
                    mainWindow.SelectNavByTag("ProductList");
                }
            }
            catch (Exception ex)
            {
                progressDialog.Hide();
                await ShowErrorDialog($"❌ เกิดข้อผิดพลาดในการ Import:\n\n{ex.Message}\n\n{ex.StackTrace}");
            }
            finally
            {
                ImportButton.IsEnabled = true;
            }
        }

        private static string GetExcelColumnName(int columnNumber)
        {
            string columnName = "";
            while (columnNumber > 0)
            {
                int modulo = (columnNumber - 1) % 26;
                columnName = Convert.ToChar('A' + modulo) + columnName;
                columnNumber = (columnNumber - modulo) / 26;
            }
            return columnName;
        }

        private static decimal? ParseDecimal(object? value)
        {
            if (value == null) return null;
            if (decimal.TryParse(value.ToString(), out decimal result))
                return result;
            return null;
        }

        /// <summary>
        /// แปลงวันที่จาก Excel โดยรองรับ พ.ศ. และแปลงเป็น ค.ศ. สำหรับบันทึกลง Database
        /// </summary>
        private static DateTime? ParseDateTime(object? value)
        {
            if (value == null) return null;

            DateTime? resultDate = null;

            // กรณีที่ 1: Excel เก็บเป็น DateTime object
            if (value is DateTime dt)
            {
                resultDate = dt.Date;
            }
            // กรณีที่ 2: Excel เก็บเป็น Double (OLE Automation Date)
            else if (double.TryParse(value.ToString(), out double oaDate))
            {
                try
                {
                    resultDate = DateTime.FromOADate(oaDate).Date;
                }
                catch
                {
                    // ถ้าแปลงไม่ได้ให้ลองแปลงเป็น String
                }
            }

            // กรณีที่ 3: String วันที่ (เช่น "25/01/2568", "2568-01-25")
            if (resultDate == null)
            {
                var strValue = value.ToString()?.Trim();
                if (!string.IsNullOrEmpty(strValue))
                {
                    // ลองแยกด้วย /, -, .
                    var parts = strValue.Split(new[] { '/', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length == 3 &&
                        int.TryParse(parts[0], out int part1) &&
                        int.TryParse(parts[1], out int part2) &&
                        int.TryParse(parts[2], out int part3))
                    {
                        int day, month, year;

                        // ตรวจสอบว่าเป็น dd/MM/yyyy หรือ yyyy-MM-dd
                        if (part1 > 31) // yyyy-MM-dd
                        {
                            year = part1;
                            month = part2;
                            day = part3;
                        }
                        else // dd/MM/yyyy
                        {
                            day = part1;
                            month = part2;
                            year = part3;
                        }

                        // ⚠️ แปลง พ.ศ. → ค.ศ.
                        if (year > 2500)
                        {
                            year -= 543;
                        }

                        try
                        {
                            resultDate = new DateTime(year, month, day);
                        }
                        catch
                        {
                            return null;
                        }
                    }
                    // Fallback: ลอง Parse แบบปกติ
                    else if (DateTime.TryParse(strValue, out DateTime result))
                    {
                        resultDate = result.Date;
                    }
                }
            }

            // ⚠️ สำคัญ! เช็คปีอีกครั้งก่อน return (กรณี DateTime object จาก Excel)
            if (resultDate.HasValue && resultDate.Value.Year > 2500)
            {
                resultDate = resultDate.Value.AddYears(-543);
            }

            return resultDate;
        }
        private async Task ShowErrorDialog(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "ข้อผิดพลาด",
                Content = message,
                CloseButtonText = "ตกลง",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async Task ShowSuccessDialog(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "สำเร็จ",
                Content = message,
                CloseButtonText = "ตกลง",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }

        private string GetErrorTypeIcon(string errorType)
        {
            return errorType switch
            {
                "Validation" => "🔴",
                "DuplicateKey" => "🔁",
                "ForeignKey" => "🔗",
                "DataTooLong" => "📏",
                "Database" => "💾",
                _ => "❌"
            };
        }

        private string GetErrorTypeName(string errorType)
        {
            return errorType switch
            {
                "Validation" => "ข้อมูลไม่ถูกต้อง",
                "DuplicateKey" => "ข้อมูลซ้ำ",
                "ForeignKey" => "ข้อมูลอ้างอิงไม่ถูกต้อง",
                "DataTooLong" => "ข้อมูลยาวเกินกำหนด",
                "Database" => "ข้อผิดพลาดจากฐานข้อมูล",
                _ => "ข้อผิดพลาดไม่ทราบสาเหตุ"
            };
        }
    }
}