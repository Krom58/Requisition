using Microsoft.Data.SqlClient;
using Requisition.Helpers;
using Requisition.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace Requisition.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService()
        {
            _connectionString = ConfigurationHelper.GetConnectionString();
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Import ข้อมูลจาก Excel พร้อมบันทึกประวัติราคาและการแก้ไข
        /// 
        /// หลักการทำงานของ Optional Fields:
        /// - ถ้า mapping = null → ไม่อัปเดตฟิลด์นั้น (เก็บค่าเดิม)
        /// - ถ้า mapping ≠ null + มีข้อมูล → อัปเดตข้อมูลใหม่
        /// - ถ้า mapping ≠ null + ว่าง → อัปเดตเป็น NULL
        /// </summary>
        // เปลี่ยนการ return จาก tuple เป็น ImportResult
        public async Task<ImportResult> BulkImportProductsWithPriceHistoryAsync(
            List<Product> products,
            DateTime importDate,
            string fileName,
            string sheetName,
            string? importedBy = null)
        {
            var result = new ImportResult();
            var errors = new List<ImportError>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // create import batch
            int importBatchId;
            var createBatchQuery = @"
                INSERT INTO ImportHistory (FileName, SheetName, ImportDate, TotalRecords, SuccessRecords, FailedRecords, NewProducts, UpdatedPrices, ImportedBy) 
                OUTPUT INSERTED.Id
                VALUES (@FileName, @SheetName, @ImportDate, @TotalRecords, 0, 0, 0, 0, @ImportedBy)";
            using (var batchCommand = new SqlCommand(createBatchQuery, connection))
            {
                batchCommand.Parameters.AddWithValue("@FileName", fileName);
                batchCommand.Parameters.AddWithValue("@SheetName", sheetName);
                batchCommand.Parameters.AddWithValue("@ImportDate", importDate.Date);
                batchCommand.Parameters.AddWithValue("@TotalRecords", products.Count);
                batchCommand.Parameters.AddWithValue("@ImportedBy", (object?)importedBy ?? DBNull.Value);
                var batchResult = await batchCommand.ExecuteScalarAsync();
                importBatchId = Convert.ToInt32(batchResult);
            }

            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");

            foreach (var product in products)
            {
                using var transaction = connection.BeginTransaction();
                try
                {
                    // ตรวจสอบข้อมูลก่อน Import
                    if (string.IsNullOrWhiteSpace(product.Code))
                    {
                        errors.Add(new ImportError
                        {
                            ProductCode = product.Code ?? "",
                            ProductName = product.Name ?? "",
                            ExcelRow = product.ExcelRow,
                            ErrorMessage = "รหัสสินค้าไม่สามารถเป็นค่าว่างได้",
                            ErrorType = "Validation"
                        });
                        result.FailedCount++;
                        transaction.Rollback();
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(product.Name))
                    {
                        errors.Add(new ImportError
                        {
                            ProductCode = product.Code,
                            ProductName = product.Name ?? "",
                            ExcelRow = product.ExcelRow,
                            ErrorMessage = "ชื่อสินค้าไม่สามารถเป็นค่าว่างได้",
                            ErrorType = "Validation"
                        });
                        result.FailedCount++;
                        transaction.Rollback();
                        continue;
                    }

                    // load existing product (if any)
                    Product? existingProduct = null;
                    bool isNewProduct = false;
                    var checkQuery = @"
                        SELECT Code, Name, Category, Unit, CurrentPrice, Remarks, CreatedDate, LastModifiedDate, ModificationCount
                        FROM Products WHERE Code = @Code";
                    using (var checkCommand = new SqlCommand(checkQuery, connection, transaction))
                    {
                        checkCommand.Parameters.AddWithValue("@Code", product.Code);
                        using var reader = await checkCommand.ExecuteReaderAsync();
                        if (await reader.ReadAsync())
                        {
                            existingProduct = new Product
                            {
                                Code = reader.GetString(0),
                                Name = reader.GetString(1),
                                Category = reader.IsDBNull(2) ? null : reader.GetString(2),
                                Unit = reader.IsDBNull(3) ? null : reader.GetString(3),
                                Price = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                                Remarks = reader.IsDBNull(5) ? null : reader.GetString(5),
                                CreatedDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                                LastModifiedDate = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                                ModificationCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8)
                            };
                        }
                        else
                        {
                            isNewProduct = true;
                        }
                    }

                    // compute effective PriceDate (normalize to date-only, UTC+7 policy for dates)
                    // (use the tz variable declared above; do NOT redeclare it here)
                    var nowUtc = DateTime.UtcNow;
                    var nowTz = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);

                    // Normalize price date to date-only (00:00). This avoids mismatches caused by time components.
                    DateTime effectiveDateValue;
                    if (product.PriceDate.HasValue)
                    {
                        effectiveDateValue = product.PriceDate.Value.Date; // time 00:00
                    }
                    else
                    {
                        // when no PriceDate provided, use import date (in TZ) date-only
                        effectiveDateValue = nowTz.Date;
                    }

                    // 1) Upsert Products (insert or update CurrentPrice etc.)
                    if (isNewProduct)
                    {
                        var insertQuery = @"
                            INSERT INTO Products (Code, Name, Category, Unit, CurrentPrice, Remarks, IsActive, CreatedDate, LastModifiedDate, ModificationCount)
                            VALUES (@Code, @Name, @Category, @Unit, @Price, @Remarks, 1, GETDATE(), GETDATE(), 0)";
                        using var cmd = new SqlCommand(insertQuery, connection, transaction);
                        cmd.Parameters.AddWithValue("@Code", product.Code);
                        cmd.Parameters.AddWithValue("@Name", product.Name);
                        cmd.Parameters.AddWithValue("@Category", (object?)product.Category ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Unit", (object?)product.Unit ?? DBNull.Value);
                        var priceParam = new SqlParameter("@Price", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = product.Price.HasValue ? (object)product.Price.Value : DBNull.Value };
                        cmd.Parameters.Add(priceParam);
                        cmd.Parameters.AddWithValue("@Remarks", (object?)product.Remarks ?? DBNull.Value);
                        await cmd.ExecuteNonQueryAsync();
                        result.NewProducts++;
                    }
                    else
                    {
                        var updateQuery = @"
                            UPDATE Products SET Name=@Name, Category=@Category, Unit=@Unit, CurrentPrice=@Price, Remarks=@Remarks, LastModifiedDate=GETDATE(), ModificationCount=ModificationCount+1
                            WHERE Code=@Code";
                        using var cmd = new SqlCommand(updateQuery, connection, transaction);
                        cmd.Parameters.AddWithValue("@Code", product.Code);
                        cmd.Parameters.AddWithValue("@Name", product.Name);
                        cmd.Parameters.AddWithValue("@Category", (object?)product.Category ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Unit", (object?)product.Unit ?? DBNull.Value);
                        var priceParam = new SqlParameter("@Price", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = product.Price.HasValue ? (object)product.Price.Value : DBNull.Value };
                        cmd.Parameters.Add(priceParam);
                        cmd.Parameters.AddWithValue("@Remarks", (object?)product.Remarks ?? DBNull.Value);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // 2) Check if price already exists for this date, then UPDATE or INSERT
                    int? linkedPriceHistoryId = null;
                    if (product.Price.HasValue)
                    {
                        // Use explicit SqlDbType for PriceDate to avoid casting/mismatch issues.
                        var checkPriceExistsSql = @"
        SELECT TOP 1 Id
        FROM PriceHistory
        WHERE ProductCode = @ProductCode AND CAST(PriceDate AS DATE) = @PriceDate";

                        using var checkPriceCmd = new SqlCommand(checkPriceExistsSql, connection, transaction);
                        checkPriceCmd.Parameters.AddWithValue("@ProductCode", product.Code);
                        // ensure parameter is date-only
                        checkPriceCmd.Parameters.Add(new SqlParameter("@PriceDate", SqlDbType.Date) { Value = effectiveDateValue.Date });

                        var existingPriceId = await checkPriceCmd.ExecuteScalarAsync();

                        if (existingPriceId != null && existingPriceId != DBNull.Value)
                        {
                            // existing row -> UPDATE
                            linkedPriceHistoryId = Convert.ToInt32(existingPriceId);

                            var updatePriceSql = @"
            UPDATE PriceHistory
            SET Price = @Price, ImportBatchId = @ImportBatchId
            WHERE Id = @Id";

                            using var updatePriceCmd = new SqlCommand(updatePriceSql, connection, transaction);
                            updatePriceCmd.Parameters.Add(new SqlParameter("@Price", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = product.Price.Value });
                            updatePriceCmd.Parameters.AddWithValue("@ImportBatchId", importBatchId);
                            updatePriceCmd.Parameters.AddWithValue("@Id", linkedPriceHistoryId.Value);
                            await updatePriceCmd.ExecuteNonQueryAsync();

                            System.Diagnostics.Debug.WriteLine($"✅ Updated existing price for {product.Code} on {effectiveDateValue:yyyy-MM-dd}");
                        }
                        else
                        {
                            // no existing -> INSERT (use OUTPUT ... INTO table variable to avoid trigger/OUTPUT issues)
                            var insertPriceSql = @"
DECLARE @InsertedIds TABLE (Id INT);
INSERT INTO PriceHistory (ProductCode, Price, PriceDate, ImportBatchId)
OUTPUT INSERTED.Id INTO @InsertedIds
VALUES (@ProductCode, @Price, @PriceDate, @ImportBatchId);
SELECT TOP 1 Id FROM @InsertedIds;";

                            using var insertPriceCmd = new SqlCommand(insertPriceSql, connection, transaction);
                            insertPriceCmd.Parameters.AddWithValue("@ProductCode", product.Code);
                            insertPriceCmd.Parameters.Add(new SqlParameter("@Price", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = product.Price.Value });
                            // bind date as SqlDbType.Date to ensure exact match with DB date-only values
                            insertPriceCmd.Parameters.Add(new SqlParameter("@PriceDate", SqlDbType.Date) { Value = effectiveDateValue.Date });
                            insertPriceCmd.Parameters.AddWithValue("@ImportBatchId", importBatchId);

                            var idObj = await insertPriceCmd.ExecuteScalarAsync();
                            if (idObj != null && idObj != DBNull.Value)
                                linkedPriceHistoryId = Convert.ToInt32(idObj);

                            System.Diagnostics.Debug.WriteLine($"✅ Inserted new price for {product.Code} on {effectiveDateValue:yyyy-MM-dd}");
                        }
                    }

                    // 3) write ProductActionLog (single entry for price change) including relatedId
                    if (product.Price.HasValue)
                    {
                        var palInsert = @"
INSERT INTO ProductActionLog
    (ProductCode, ActionType, EffectiveDate, OldValue, NewValue, Reason, PerformedBy, PerformedAt, Source, RelatedId)
VALUES
    (@ProductCode, @ActionType, @EffectiveDate, @OldValue, @NewValue, @Reason, @PerformedBy, SYSUTCDATETIME(), @Source, @RelatedId);";
                        using var palCmd = new SqlCommand(palInsert, connection, transaction);
                        palCmd.Parameters.AddWithValue("@ProductCode", product.Code);
                        palCmd.Parameters.AddWithValue("@ActionType", "PriceChange");
                        palCmd.Parameters.AddWithValue("@EffectiveDate", effectiveDateValue);
                        palCmd.Parameters.AddWithValue("@OldValue", (object?)(existingProduct?.Price?.ToString()) ?? DBNull.Value);
                        palCmd.Parameters.AddWithValue("@NewValue", product.Price.Value.ToString());
                        palCmd.Parameters.AddWithValue("@Reason", DBNull.Value);
                        palCmd.Parameters.AddWithValue("@PerformedBy", (object?)importedBy ?? DBNull.Value);
                        palCmd.Parameters.AddWithValue("@Source", "Excel Import");
                        palCmd.Parameters.AddWithValue("@RelatedId", (object?)linkedPriceHistoryId ?? DBNull.Value);
                        await palCmd.ExecuteNonQueryAsync();
                    }

                    // 4) single ProductModificationHistory with linkedPriceHistoryId (or null)
                    var desc = isNewProduct ? "Created new product via Excel Import" : $"Updated product via Excel Import (Count: {existingProduct!.ModificationCount + 1})";
                    await AddHistoryRecordAsync(connection, transaction, product.Code, desc, "Excel Import", importedBy, System.Text.Json.JsonSerializer.Serialize(existingProduct), System.Text.Json.JsonSerializer.Serialize(product), null, linkedPriceHistoryId);

                    transaction.Commit();
                    result.SuccessCount++;
                }
                catch (Microsoft.Data.SqlClient.SqlException sqlEx)
                {
                    transaction.Rollback();
                    
                    // แยกประเภทข้อผิดพลาด SQL ออกมา
                    string errorType = "Database";
                    string errorMessage = sqlEx.Message;

                    if (sqlEx.Number == 2601 || sqlEx.Number == 2627) // Duplicate key
                    {
                        errorType = "DuplicateKey";
                        errorMessage = "มีข้อมูลซ้ำในฐานข้อมูล (รหัสสินค้าซ้ำ)";
                    }
                    else if (sqlEx.Number == 547) // Foreign key violation
                    {
                        errorType = "ForeignKey";
                        errorMessage = "ข้อมูลอ้างอิงไม่ถูกต้อง";
                    }
                    else if (sqlEx.Number == 8152) // String truncation
                    {
                        errorType = "DataTooLong";
                        errorMessage = "ข้อมูลยาวเกินกว่าที่กำหนดในฐานข้อมูล";
                    }

                    errors.Add(new ImportError
                    {
                        ProductCode = product.Code ?? "",
                        ProductName = product.Name ?? "",
                        ExcelRow = product.ExcelRow,
                        ErrorMessage = $"{errorMessage} (SQL Error {sqlEx.Number})",
                        ErrorType = errorType
                    });

                    System.Diagnostics.Debug.WriteLine($"SQL Error importing {product.Code}: {sqlEx.Message}");
                    result.FailedCount++;
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    
                    errors.Add(new ImportError
                    {
                        ProductCode = product.Code ?? "",
                        ProductName = product.Name ?? "",
                        ExcelRow = product.ExcelRow,
                        ErrorMessage = ex.Message,
                        ErrorType = "Unknown"
                    });

                    System.Diagnostics.Debug.WriteLine($"Error importing {product.Code}: {ex.Message}");
                    result.FailedCount++;
                }
            }

            // update import batch summary
            var updateBatchQuery = @"
UPDATE ImportHistory SET SuccessRecords=@SuccessRecords, FailedRecords=@FailedRecords, NewProducts=@NewProducts, UpdatedPrices=@UpdatedPrices WHERE Id=@Id";
            using var updateCommand = new SqlCommand(updateBatchQuery, connection);
            updateCommand.Parameters.AddWithValue("@SuccessRecords", result.SuccessCount);
            updateCommand.Parameters.AddWithValue("@FailedRecords", result.FailedCount);
            updateCommand.Parameters.AddWithValue("@NewProducts", result.NewProducts);
            updateCommand.Parameters.AddWithValue("@UpdatedPrices", result.UpdatedPrices);
            updateCommand.Parameters.AddWithValue("@Id", importBatchId);
            await updateCommand.ExecuteNonQueryAsync();

            result.Errors = errors;
            return result;
        }

        /// <summary>
        /// บันทึกประวัติการแก้ไขลงใน ProductModificationHistory
        /// เพิ่ม optional parameter priceHistoryId เพื่อผูกกับ PriceHistory.Id
        /// </summary>
        private async Task AddHistoryRecordAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string productCode,
            string description,
            string? source,
            string? modifiedBy,
            string? oldValues,
            string? newValues,
            string? reason = null,
            int? priceHistoryId = null)
        {
            // ปรับให้ใส่ฟิลด์ PriceHistoryId ด้วย (ฐานข้อมูลต้องมีคอลัมน์นี้แล้ว)
            var insertHistoryQuery = @"
                INSERT INTO ProductModificationHistory 
                (ProductCode, ModifiedDate, ModifiedBy, ModificationSource, ChangesDescription, OldValues, NewValues, Reason, PriceHistoryId)
                VALUES (@Code, GETDATE(), @ModifiedBy, @Source, @Description, @OldValues, @NewValues, @Reason, @PriceHistoryId)";

            using var command = new SqlCommand(insertHistoryQuery, connection, transaction);
            command.Parameters.AddWithValue("@Code", productCode);
            command.Parameters.AddWithValue("@ModifiedBy", (object?)modifiedBy ?? DBNull.Value);
            command.Parameters.AddWithValue("@Source", (object?)source ?? DBNull.Value);
            command.Parameters.AddWithValue("@Description", description);
            command.Parameters.AddWithValue("@OldValues", (object?)oldValues ?? DBNull.Value);
            command.Parameters.AddWithValue("@NewValues", (object?)newValues ?? DBNull.Value);
            command.Parameters.AddWithValue("@Reason", (object?)reason ?? DBNull.Value);
            command.Parameters.AddWithValue("@PriceHistoryId", (object?)priceHistoryId ?? DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }

        public async Task<List<Product>> GetAllProductsAsync()
        {
            var products = new List<Product>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = "SELECT Code, Name, Category, Unit, CurrentPrice, Remarks FROM Products WHERE IsActive = 1 ORDER BY Code";
            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                products.Add(new Product
                {
                    Code = reader.GetString(0),
                    Name = reader.GetString(1),
                    Category = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Unit = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Price = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                    Remarks = reader.IsDBNull(5) ? null : reader.GetString(5)
                });
            }

            return products;
        }

        public sealed class ImportConflictCheckResult
        {
            public List<(Product ExcelProduct, Product DbProduct)> CodeAndNameMatch { get; } = new();
            public List<(Product ExcelProduct, Product DbProduct)> NameOnlyMatch { get; } = new();
            public List<(Product ExcelProduct, Product DbProduct)> CodeOnlyMatch { get; } = new();
            public List<Product> NewProducts { get; } = new();
        }

        /// <summary>
        /// ตรวจสอบรายการจาก Excel เทียบกับฐานข้อมูล ว่าอยู่ในเคสไหนบ้าง
        /// 1) Code ซ้ำ + Name ซ้ำ
        /// 2) Code ไม่ซ้ำ + Name ซ้ำ
        /// 3) Code ซ้ำ + Name ไม่ซ้ำ
        /// 4) Code ไม่ซ้ำ + Name ไม่ซ้ำ
        /// </summary>
        public async Task<ImportConflictCheckResult> AnalyzeImportConflictsAsync(List<Product> excelProducts)
        {
            var result = new ImportConflictCheckResult();

            if (excelProducts.Count == 0)
                return result;

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // ดึง Product ทั้งหมดที่อาจเกี่ยวข้อง (ตาม Code หรือ Name)
            var codes = excelProducts
                .Select(p => p.Code)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var names = excelProducts
                .Select(p => p.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (codes.Count == 0 && names.Count == 0)
                return result;

            // สร้าง temp table สำหรับ codes และ names เพื่อใช้ JOIN
            using var cmd = new SqlCommand
            {
                Connection = connection,
                CommandType = CommandType.Text
            };

            // ใช้ Table-Valued Parameters จะดีกว่า แต่เพื่อความง่ายใช้ IN แบบ batch
            // ดึงทั้งหมดที่ Code หรือ Name ตรงกับ excelProducts
            var whereParts = new List<string>();
            if (codes.Count > 0)
            {
                var codeParams = new List<string>();
                for (int i = 0; i < codes.Count; i++)
                {
                    string p = $"@Code{i}";
                    codeParams.Add(p);
                    cmd.Parameters.AddWithValue(p, codes[i]);
                }

                whereParts.Add($"Code IN ({string.Join(",", codeParams)})");
            }

            if (names.Count > 0)
            {
                var nameParams = new List<string>();
                for (int i = 0; i < names.Count; i++)
                {
                    string p = $"@Name{i}";
                    nameParams.Add(p);
                    cmd.Parameters.AddWithValue(p, names[i]);
                }

                whereParts.Add($"Name IN ({string.Join(",", nameParams)})");
            }

            cmd.CommandText = $"SELECT Code, Name, Category, Unit, CurrentPrice, Remarks, CreatedDate, LastModifiedDate, ModificationCount FROM Products WHERE {string.Join(" OR ", whereParts)}";
            using var readerAll = await cmd.ExecuteReaderAsync();

            var dbProducts = new List<Product>();
            while (await readerAll.ReadAsync())
            {
                dbProducts.Add(new Product
                {
                    Code = readerAll.GetString(0),
                    Name = readerAll.GetString(1),
                    Category = readerAll.IsDBNull(2) ? null : readerAll.GetString(2),
                    Unit = readerAll.IsDBNull(3) ? null : readerAll.GetString(3),
                    Price = readerAll.IsDBNull(4) ? null : readerAll.GetDecimal(4),
                    Remarks = readerAll.IsDBNull(5) ? null : readerAll.GetString(5),
                    CreatedDate = readerAll.IsDBNull(6) ? null : readerAll.GetDateTime(6),
                    LastModifiedDate = readerAll.IsDBNull(7) ? null : readerAll.GetDateTime(7),
                    ModificationCount = readerAll.IsDBNull(8) ? 0 : readerAll.GetInt32(8)
                });
            }

            // จับคู่ Excel vs DB ทีละแถว
            foreach (var excelProduct in excelProducts)
            {
                // หาว่าใน DB มีรายการที่ Code หรือ Name ตรงไหม
                var dbByCode = dbProducts
                    .FirstOrDefault(p => p.Code.Equals(excelProduct.Code, StringComparison.OrdinalIgnoreCase));

                var dbByName = dbProducts
                    .FirstOrDefault(p => p.Name.Equals(excelProduct.Name, StringComparison.OrdinalIgnoreCase));

                if (dbByCode != null && dbByName != null &&
                    dbByCode.Code.Equals(excelProduct.Code, StringComparison.OrdinalIgnoreCase) &&
                    dbByName.Name.Equals(excelProduct.Name, StringComparison.OrdinalIgnoreCase) &&
                    dbByCode.Code.Equals(dbByName.Code, StringComparison.OrdinalIgnoreCase))
                {
                    // 1) Code ซ้ำ + Name ซ้ำ → อัปเดตราคา
                    result.CodeAndNameMatch.Add((excelProduct, dbByCode));
                }
                else if (dbByCode == null && dbByName != null)
                {
                    // 2) Code ไม่ซ้ำ + Name ซ้ำ
                    result.NameOnlyMatch.Add((excelProduct, dbByName));
                }
                else if (dbByCode != null && !dbByCode.Name.Equals(excelProduct.Name, StringComparison.OrdinalIgnoreCase))
                {
                    // 3) Code ซ้ำ + Name ไม่ซ้ำ
                    result.CodeOnlyMatch.Add((excelProduct, dbByCode));
                }
                else
                {
                    // 4) ไม่เข้าเคสใดเลย → ถือเป็นสินค้าใหม่
                    result.NewProducts.Add(excelProduct);
                }
            }

            return result;
        }
    }
}