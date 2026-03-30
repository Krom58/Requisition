using Microsoft.Data.SqlClient;
using Requisition.Helpers;
using Requisition.Models;
using Requisition.Models.Reports; // ✅ เพิ่มบรรทัดนี้
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Requisition.Services
{
    public class ProductService
    {
        private readonly string _connectionString;

        public ProductService()
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

        public async Task<List<Product>> GetProductsAsync(bool includeInactive = true)
        {
            var products = new List<Product>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT Code, Name, Category, Unit, CurrentPrice, Remarks,
                       CreatedDate, LastModifiedDate, ModificationCount, IsActive, DisabledUntil
                FROM Products
            ";

            if (!includeInactive)
                query += " WHERE IsActive = 1 ";

            query += " ORDER BY Code";

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
                    Remarks = reader.IsDBNull(5) ? null : reader.GetString(5),
                    CreatedDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    LastModifiedDate = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    ModificationCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                    IsActive = reader.IsDBNull(9) ? true : reader.GetBoolean(9),
                    DisabledUntil = reader.IsDBNull(10) ? null : reader.GetDateTime(10)
                });
            }

            return products;
        }

        // keep existing API for compatibility
        public Task<List<Product>> GetAllProductsAsync() => GetProductsAsync(includeInactive: true);

        public async Task<Product?> GetProductByCodeAsync(string code)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT Code, Name, Category, Unit, CurrentPrice, Remarks,
                       CreatedDate, LastModifiedDate, ModificationCount, IsActive, DisabledUntil
                FROM Products 
                WHERE Code = @Code";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Code", code);

            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new Product
                {
                    Code = reader.GetString(0),
                    Name = reader.GetString(1),
                    Category = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Unit = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Price = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                    Remarks = reader.IsDBNull(5) ? null : reader.GetString(5),
                    CreatedDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    LastModifiedDate = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    ModificationCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                    IsActive = reader.IsDBNull(9) ? true : reader.GetBoolean(9),
                    DisabledUntil = reader.IsDBNull(10) ? null : reader.GetDateTime(10)
                };
            }

            return null;
        }

        public async Task<bool> AddProductAsync(Product product, string source = "Manual Edit", string? modifiedBy = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                // 1. Insert Product
                var insertQuery = @"
                    INSERT INTO Products (Code, Name, Category, Unit, CurrentPrice, Remarks, IsActive, CreatedDate, LastModifiedDate, ModificationCount)
                    VALUES (@Code, @Name, @Category, @Unit, @Price, @Remarks, 1, GETDATE(), GETDATE(), 0)";

                using (var command = new SqlCommand(insertQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@Code", product.Code);
                    command.Parameters.AddWithValue("@Name", product.Name);
                    command.Parameters.AddWithValue("@Category", (object?)product.Category ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Unit", (object?)product.Unit ?? DBNull.Value);
                    var priceParam = new SqlParameter("@Price", SqlDbType.Decimal)
                    {
                        Precision = 18,
                        Scale = 4,
                        Value = product.Price.HasValue ? (object)product.Price.Value : DBNull.Value
                    };
                    command.Parameters.Add(priceParam);
                    command.Parameters.AddWithValue("@Remarks", (object?)product.Remarks ?? DBNull.Value);

                    await command.ExecuteNonQueryAsync();
                }

                // 2. Add History
                await AddHistoryRecordAsync(connection, transaction, product.Code, "Created new product",
                    source, modifiedBy, null, JsonSerializer.Serialize(product), null);

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// Disable product until specified date (soft-disable for requisitions). If disabledUntil is null, clears the field.
        /// </summary>
        public async Task<bool> DisableProductUntilAsync(string code, DateTime? disabledUntil, string? reason = null, string? modifiedBy = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                var query = @"
                    UPDATE Products
                    SET DisabledUntil = @DisabledUntil,
                        IsActive = CASE 
                                     WHEN @DisabledUntil IS NOT NULL AND CONVERT(date, @DisabledUntil) > CONVERT(date, GETDATE()) THEN 0 
                                     ELSE 1 
                                   END,
                        LastModifiedDate = GETDATE(),
                        ModificationCount = ModificationCount + 1
                    WHERE Code = @Code";

                using var cmd = new SqlCommand(query, connection, transaction);
                cmd.Parameters.AddWithValue("@Code", code);
                if (disabledUntil.HasValue)
                    cmd.Parameters.AddWithValue("@DisabledUntil", disabledUntil.Value.Date);
                else
                    cmd.Parameters.AddWithValue("@DisabledUntil", DBNull.Value);

                await cmd.ExecuteNonQueryAsync();

                // record action only in ProductActionLog (no ProductModificationHistory entry)
                await LogProductActionAsync(
                    connection,
                    transaction,
                    productCode: code,
                    actionType: "Disable",
                    oldValue: null,
                    newValue: null,
                    reason: reason,
                    effectiveDate: disabledUntil,
                    performedBy: modifiedBy,
                    performedAt: DateTime.UtcNow,
                    source: "UI"
                );

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<bool> UpdateProductAsync(Product product, DateTime? performedAt, string source = "Manual Edit", string? modifiedBy = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                var oldProduct = await GetProductByCodeForTransactionAsync(connection, transaction, product.Code);
                if (oldProduct == null)
                    return false;

                var oldValues = JsonSerializer.Serialize(oldProduct);
                var newValues = JsonSerializer.Serialize(product);

                // Normalize performedAt to UTC+7 (policy)
                var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                DateTime performedAtEffective = performedAt.HasValue
                    ? TimeZoneInfo.ConvertTimeFromUtc(performedAt.Value.ToUniversalTime(), tz)
                    : TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

                // Get latest PriceHistory (for timestamp comparison)
                DateTime? latestPriceDate = null;
                decimal? latestPriceValue = null;
                var latestPriceSql = @"
                    SELECT TOP 1 Price, PriceDate
                    FROM PriceHistory
                    WHERE ProductCode = @Code
                    ORDER BY PriceDate DESC, Id DESC";
                using (var lpCmd = new SqlCommand(latestPriceSql, connection, transaction))
                {
                    lpCmd.Parameters.AddWithValue("@Code", product.Code);
                    using var reader = await lpCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        if (!reader.IsDBNull(0)) latestPriceValue = reader.GetDecimal(0);
                        if (!reader.IsDBNull(1)) latestPriceDate = reader.GetDateTime(1);
                    }
                    reader.Close();
                }

                bool shouldApplyManualPrice = true;
                if (latestPriceDate.HasValue)
                {
                    var latestInTz = TimeZoneInfo.ConvertTimeFromUtc(latestPriceDate.Value.ToUniversalTime(), tz);
                    if (latestInTz > performedAtEffective)
                        shouldApplyManualPrice = false;
                }

                // 1) Update Products table (current row)
                var updateQuery = @"
                    UPDATE Products 
                    SET Name = @Name,
                        Category = @Category,
                        Unit = @Unit,
                        CurrentPrice = CASE WHEN @ApplyPrice = 1 THEN @Price ELSE CurrentPrice END,
                        Remarks = @Remarks,
                        LastModifiedDate = GETDATE(),
                        ModificationCount = ModificationCount + 1
                    WHERE Code = @Code AND IsActive = 1";

                using (var cmd = new SqlCommand(updateQuery, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@Code", product.Code);
                    cmd.Parameters.AddWithValue("@Name", product.Name);
                    cmd.Parameters.AddWithValue("@Category", (object?)product.Category ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Unit", (object?)product.Unit ?? DBNull.Value);
                    var priceParam = new SqlParameter("@Price", System.Data.SqlDbType.Decimal)
                    {
                        Precision = 18,
                        Scale = 4,
                        Value = product.Price.HasValue ? (object)product.Price.Value : DBNull.Value
                    };
                    cmd.Parameters.Add(priceParam);
                    cmd.Parameters.AddWithValue("@Remarks", (object?)product.Remarks ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ApplyPrice", shouldApplyManualPrice ? 1 : 0);

                    await cmd.ExecuteNonQueryAsync();
                }

                // Build description
                var changedFields = new List<string>();
                if (!string.Equals(oldProduct.Name, product.Name, StringComparison.Ordinal))
                    changedFields.Add("ชื่อสินค้า");
                if (!string.Equals(oldProduct.Category, product.Category, StringComparison.Ordinal))
                    changedFields.Add("ประเภทสินค้า");
                if (!string.Equals(oldProduct.Unit, product.Unit, StringComparison.Ordinal))
                    changedFields.Add("หน่วย");
                if (oldProduct.Price != product.Price)
                    changedFields.Add("ราคา");
                if (!string.Equals(oldProduct.Remarks ?? "", product.Remarks ?? "", StringComparison.Ordinal))
                    changedFields.Add("หมายเหตุ");

                string fieldsText = changedFields.Count == 0 ? "ไม่มีการเปลี่ยนแปลง" : string.Join(", ", changedFields);
                var desc = $"แก้ไขสินค้า: {fieldsText} (ครั้งที่ {oldProduct.ModificationCount + 1})";

                // 2) If price changed and allowed, MERGE into PriceHistory using MERGE+OUTPUT to get Id atomically
                int? priceHistoryId = null;
                if (oldProduct.Price != product.Price && shouldApplyManualPrice && product.Price.HasValue)
                {
                    var mergeSql = @"
DECLARE @InsertedIds TABLE (Id INT);
MERGE INTO PriceHistory AS target
USING (SELECT @Code AS ProductCode, @PriceDate AS PriceDate) AS source
ON target.ProductCode = source.ProductCode AND target.PriceDate = source.PriceDate
WHEN MATCHED THEN
    UPDATE SET Price = @Price
WHEN NOT MATCHED THEN
    INSERT (ProductCode, Price, PriceDate)
    VALUES (@Code, @Price, @PriceDate)
OUTPUT INSERTED.Id INTO @InsertedIds;
SELECT TOP 1 Id FROM @InsertedIds;";

                    using (var mergeCmd = new SqlCommand(mergeSql, connection, transaction))
                    {
                        mergeCmd.Parameters.AddWithValue("@Code", product.Code);
                        var p = new SqlParameter("@Price", System.Data.SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = product.Price.Value };
                        mergeCmd.Parameters.Add(p);

                        // <-- send date-only (strip time) so PriceHistory stores date at 00:00:00
                        mergeCmd.Parameters.AddWithValue("@PriceDate", performedAtEffective.Date);

                        var idObj = await mergeCmd.ExecuteScalarAsync();
                        if (idObj != null && idObj != DBNull.Value)
                            priceHistoryId = Convert.ToInt32(idObj);
                    }
                }

                // 3) Log action entries (ProductActionLog) — create single entries and include relatedId if available
                // PriceChange
                if (oldProduct.Price != product.Price)
                {
                    await LogProductActionAsync(
                        connection,
                        transaction,
                        productCode: product.Code,
                        actionType: "PriceChange",
                        oldValue: oldProduct.Price?.ToString(),
                        newValue: product.Price?.ToString(),
                        reason: null,
                        effectiveDate: null,
                        performedBy: modifiedBy,
                        performedAt: performedAtEffective,
                        source: source,
                        relatedId: priceHistoryId);
                }

                // Unit change
                if (!string.Equals(oldProduct.Unit, product.Unit, StringComparison.OrdinalIgnoreCase))
                {
                    await LogProductActionAsync(
                        connection,
                        transaction,
                        productCode: product.Code,
                        actionType: "UnitChange",
                        oldValue: oldProduct.Unit,
                        newValue: product.Unit,
                        reason: null,
                        effectiveDate: null,
                        performedBy: modifiedBy,
                        performedAt: performedAtEffective,
                        source: source);
                }

                // Remarks change
                if (!string.Equals(oldProduct.Remarks, product.Remarks, StringComparison.Ordinal))
                {
                    await LogProductActionAsync(
                        connection,
                        transaction,
                        productCode: product.Code,
                        actionType: "RemarksChange",
                        oldValue: oldProduct.Remarks,
                        newValue: product.Remarks,
                        reason: null,
                        effectiveDate: null,
                        performedBy: modifiedBy,
                        performedAt: performedAtEffective,
                        source: source);
                }

                // 4) Single ProductModificationHistory entry (with priceHistoryId if any)
                await AddHistoryRecordAsync(connection, transaction, product.Code,
                    desc,
                    source, modifiedBy, oldValues, newValues, null, priceHistoryId);

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        public async Task<bool> DisableProductNowAsync(string code, string? reason = null, string? modifiedBy = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                var query = @"
            UPDATE Products
            SET DisabledUntil = NULL,
                IsActive = 0,
                LastModifiedDate = GETDATE(),
                ModificationCount = ModificationCount + 1
            WHERE Code = @Code";

                using var cmd = new SqlCommand(query, connection, transaction);
                cmd.Parameters.AddWithValue("@Code", code);

                await cmd.ExecuteNonQueryAsync();

                // record action in ProductActionLog
                await LogProductActionAsync(
                    connection,
                    transaction,
                    productCode: code,
                    actionType: "Disable",
                    oldValue: null,
                    newValue: null,
                    reason: reason,
                    effectiveDate: null,
                    performedBy: modifiedBy,
                    performedAt: DateTime.UtcNow,
                    source: "UI"
                );

                // optional: add ProductModificationHistory entry
                await AddHistoryRecordAsync(connection, transaction, code, "Disabled product (manual)",
                    "UI", modifiedBy, null, null, reason);

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<List<ProductModificationHistory>> GetProductHistoryAsync(string productCode)
        {
            var histories = new List<ProductModificationHistory>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT Id, ProductCode, ModifiedDate, ModifiedBy, ModificationSource, 
                       ChangesDescription, OldValues, NewValues
                FROM ProductModificationHistory
                WHERE ProductCode = @Code
                ORDER BY ModifiedDate DESC";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Code", productCode);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                histories.Add(new ProductModificationHistory
                {
                    Id = reader.GetInt32(0),
                    ProductCode = reader.GetString(1),
                    ModifiedDate = reader.GetDateTime(2),
                    ModifiedBy = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ModificationSource = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ChangesDescription = reader.IsDBNull(5) ? null : reader.GetString(5),
                    OldValues = reader.IsDBNull(6) ? null : reader.GetString(6),
                    NewValues = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }

            return histories;
        }

        /// <summary>
        /// ดึงประวัติทั้งหมด รวมทั้งจาก ProductModificationHistory และ PriceHistory
        /// พร้อมระบุ SourceTable และ SourceId เพื่อใช้ในการแก้ไข
        /// </summary>
        public async Task<List<ProductModificationHistory>> GetCombinedProductHistoryAsync(string productCode)
        {
            var combinedHistories = new List<ProductModificationHistory>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Build union without ORDER BY, then order in outer query to satisfy SQL Server rules
            var query = @"
SELECT *
FROM (
    SELECT 
        PMH.Id,
        PMH.ProductCode,
        PMH.ModifiedDate,
        CONVERT(date, PH.PriceDate) AS PriceDate,  -- date-only
        PMH.ModifiedBy,
        PMH.ModificationSource,
        PMH.ChangesDescription,
        PMH.OldValues,
        PMH.NewValues,
        'ModificationHistory' AS SourceTable,
        PMH.Id AS SourceId,
        PMH.OriginalNewValues,
        PMH.EditCount,
        PMH.IsModifiedToday,
        PMH.LastEditDate,
        PMH.EditHistory
    FROM ProductModificationHistory PMH
    LEFT JOIN PriceHistory PH ON PMH.PriceHistoryId = PH.Id
    WHERE PMH.ProductCode = @Code

    UNION ALL

    SELECT
        PH.Id AS Id,
        PH.ProductCode,
        PH.PriceDate AS ModifiedDate,
        CONVERT(date, PH.PriceDate) AS PriceDate,  -- date-only
        COALESCE(PAL.PerformedBy, IH.ImportedBy, 'System') AS ModifiedBy,
        CASE
            WHEN IH.Id IS NOT NULL THEN 'Excel Import'
            WHEN PAL.Id IS NOT NULL THEN ISNULL(PAL.Source, 'Manual Edit')
            ELSE 'System'
        END AS ModificationSource,
        CONCAT('Import price: ', CAST(PH.Price AS NVARCHAR(32)), ' ฿') AS ChangesDescription,
        NULL AS OldValues,
        CONCAT('{""Price"":', CAST(PH.Price AS NVARCHAR(32)), '}') AS NewValues,
        'PriceHistory' AS SourceTable,
        PH.Id AS SourceId,
        NULL AS OriginalNewValues,
        ISNULL(PH.EditCount, 0) AS EditCount,
        ISNULL(PH.IsModifiedToday, 0) AS IsModifiedToday,
        PH.LastEditDate,
        PH.EditHistory
    FROM PriceHistory PH
    LEFT JOIN ImportHistory IH ON PH.ImportBatchId = IH.Id
    OUTER APPLY (
        SELECT TOP 1 Id, PerformedBy, Source, PerformedAt
        FROM ProductActionLog PAL2
        WHERE PAL2.RelatedId = PH.Id AND PAL2.ActionType = 'PriceChange'
        ORDER BY PAL2.PerformedAt DESC
    ) PAL
    WHERE PH.ProductCode = @Code
      AND NOT EXISTS (
          SELECT 1 FROM ProductModificationHistory pmh WHERE pmh.PriceHistoryId = PH.Id
      )
) AS combined
-- final ordering: prefer rows with a PriceDate, newest PriceDate first,
-- when PriceDate ties prefer PriceHistory rows, then newest ModifiedDate
ORDER BY
    CASE WHEN combined.PriceDate IS NOT NULL THEN 0 ELSE 1 END,
    combined.PriceDate DESC,
    CASE WHEN combined.SourceTable = 'PriceHistory' THEN 0 ELSE 1 END,
    combined.ModifiedDate DESC;";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Code", productCode);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var history = new ProductModificationHistory
                {
                    Id = reader.GetInt32(0),
                    ProductCode = reader.GetString(1),
                    ModifiedDate = reader.GetDateTime(2),
                    PriceDate = reader.IsDBNull(3) ? null : (DateTime?)reader.GetDateTime(3),
                    ModifiedBy = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ModificationSource = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ChangesDescription = reader.IsDBNull(6) ? null : reader.GetString(6),
                    OldValues = reader.IsDBNull(7) ? null : reader.GetString(7),
                    NewValues = reader.IsDBNull(8) ? null : reader.GetString(8),
                    SourceTable = reader.IsDBNull(9) ? null : reader.GetString(9),
                    SourceId = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    OriginalNewValues = reader.IsDBNull(11) ? null : reader.GetString(11),
                    EditCount = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                    IsModifiedToday = reader.IsDBNull(13) ? false : reader.GetBoolean(13),
                    LastEditDate = reader.IsDBNull(14) ? null : (DateTime?)reader.GetDateTime(14),
                    EditHistory = reader.IsDBNull(15) ? null : reader.GetString(15)
                };

                // ⬇️ เพิ่ม Debug Log
                System.Diagnostics.Debug.WriteLine($"📊 Loaded: #{history.Id} | {history.SourceTable} | ModDate: {history.ModifiedDate:yyyy-MM-dd HH:mm:ss} | PriceDate: {history.PriceDate?.ToString("yyyy-MM-dd") ?? "NULL"}");

                combinedHistories.Add(history);
            }

            System.Diagnostics.Debug.WriteLine($"✅ Total loaded: {combinedHistories.Count} records for {productCode}");
            return combinedHistories;
        }

        private async Task<Product?> GetProductByCodeForTransactionAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string code)
        {
            var query = @"
                SELECT Code, Name, Category, Unit, CurrentPrice, Remarks, 
                       CreatedDate, LastModifiedDate, ModificationCount, IsActive, DisabledUntil
                FROM Products 
                WHERE Code = @Code AND IsActive = 1";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@Code", code);

            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new Product
                {
                    Code = reader.GetString(0),
                    Name = reader.GetString(1),
                    Category = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Unit = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Price = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                    Remarks = reader.IsDBNull(5) ? null : reader.GetString(5),
                    CreatedDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    LastModifiedDate = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    ModificationCount = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                    IsActive = reader.IsDBNull(9) ? true : reader.GetBoolean(9),
                    DisabledUntil = reader.IsDBNull(10) ? null : reader.GetDateTime(10)
                };
            }

            return null;
        }

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

        /// <summary>
        /// Returns the latest calendar year that has a PriceHistory entry for the given product,
        /// or null if none exist.
        /// </summary>
        public async Task<int?> GetLatestPriceYearAsync(string productCode)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT TOP 1 YEAR(PriceDate) AS PriceYear
                    FROM PriceHistory
                    WHERE ProductCode = @ProductCode
                    ORDER BY PriceDate DESC, Id DESC";

                using var cmd = new SqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@ProductCode", productCode);

                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetLatestPriceYearAsync error for {productCode}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Returns the latest price for the given product inside the specified calendar year.
        /// If no PriceHistory exists in that year, returns null.
        /// </summary>
        public async Task<decimal?> GetLatestPriceForProductInYearAsync(string productCode, int year)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT TOP 1 Price
                    FROM PriceHistory
                    WHERE ProductCode = @ProductCode
                      AND YEAR(PriceDate) = @Year
                    ORDER BY PriceDate DESC, Id DESC";

                using var cmd = new SqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@ProductCode", productCode);
                cmd.Parameters.AddWithValue("@Year", year);

                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    return Convert.ToDecimal(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetLatestPriceForProductInYearAsync error for {productCode} year={year}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Returns the most recent Price and PriceDate for a product from PriceHistory, or (null, null) if none.
        /// </summary>
        public async Task<(decimal? Price, DateTime? PriceDate)> GetLatestPriceRecordAsync(string productCode)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT TOP 1 Price, PriceDate
                    FROM PriceHistory
                    WHERE ProductCode = @ProductCode
                    ORDER BY PriceDate DESC, Id DESC";

                using var cmd = new SqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@ProductCode", productCode);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var price = reader.IsDBNull(0) ? (decimal?)null : reader.GetDecimal(0);
                    var dt = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);
                    return (price, dt);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetLatestPriceRecordAsync error for {productCode}: {ex.Message}");
            }

            return (null, null);
        }

        /// <summary>
        /// Returns the latest Price and PriceDate for a product from PriceHistory before the specified date,
        /// or (null, null) if none.
        /// </summary>
        public async Task<(decimal? Price, DateTime? PriceDate)> GetLatestPriceBeforeAsync(string productCode, DateTime beforeDate)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT TOP 1 Price, PriceDate
                    FROM PriceHistory
                    WHERE ProductCode = @ProductCode
                      AND PriceDate < @BeforeDate
                    ORDER BY PriceDate DESC, Id DESC";

                using var cmd = new SqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@ProductCode", productCode);
                cmd.Parameters.AddWithValue("@BeforeDate", beforeDate);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var price = reader.IsDBNull(0) ? (decimal?)null : reader.GetDecimal(0);
                    var dt = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);
                    return (price, dt);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetLatestPriceBeforeAsync error for {productCode} before {beforeDate}: {ex.Message}");
            }

            return (null, null);
        }

        /// <summary>
        /// Returns the latest Price and PriceDate for a product from PriceHistory within the given month/year,
        /// or (null, null) if none.
        /// </summary>
        public async Task<decimal?> GetLatestPriceForProductInMonthAsync(string productCode, int month, int year)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT TOP 1 Price
                    FROM PriceHistory
                    WHERE ProductCode = @ProductCode 
                        AND YEAR(PriceDate) = @Year 
                        AND MONTH(PriceDate) = @Month
                    ORDER BY PriceDate DESC, Id DESC";

                using var cmd = new SqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@ProductCode", productCode);
                cmd.Parameters.AddWithValue("@Year", year);
                cmd.Parameters.AddWithValue("@Month", month);

                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    return Convert.ToDecimal(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetLatestPriceForProductInMonthAsync error for {productCode} {year}-{month:D2}: {ex.Message}");
            }

            return null;
        }

        public async Task<decimal?> GetAveragePriceAsync(string productCode, DateTime startInclusive, DateTime endInclusive)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT AVG(CAST(Price AS DECIMAL(18,4))) 
                    FROM PriceHistory
                    WHERE ProductCode = @ProductCode
                      AND PriceDate >= @Start
                      AND PriceDate <= @End";

                using var cmd = new SqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@ProductCode", productCode);
                cmd.Parameters.AddWithValue("@Start", startInclusive);
                cmd.Parameters.AddWithValue("@End", endInclusive);

                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    return Convert.ToDecimal(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetAveragePriceAsync error for {productCode} {startInclusive:yyyy-MM-dd}..{endInclusive:yyyy-MM-dd}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Returns the latest Price and PriceDate for a product from PriceHistory within the specified date range,
        /// or (null, null) if none.
        /// </summary>
        public async Task<(decimal? Price, DateTime? PriceDate)> GetLatestPriceInRangeAsync(string productCode, DateTime startInclusive, DateTime endInclusive)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT TOP 1 Price, PriceDate
                    FROM PriceHistory
                    WHERE ProductCode = @ProductCode
                      AND PriceDate >= @Start
                      AND PriceDate <= @End
                    ORDER BY PriceDate DESC, Id DESC";

                using var cmd = new SqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@ProductCode", productCode);
                cmd.Parameters.AddWithValue("@Start", startInclusive);
                cmd.Parameters.AddWithValue("@End", endInclusive);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var price = reader.IsDBNull(0) ? (decimal?)null : reader.GetDecimal(0);
                    var dt = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);
                    return (price, dt);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetLatestPriceInRangeAsync error for {productCode} {startInclusive:yyyy-MM-dd}..{endInclusive:yyyy-MM-dd}: {ex.Message}");
            }

            return (null, null);
        }

        // Added to ProductService class near other helpers
        public async Task<bool> HasPriceInYearAsync(string productCode, int year)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT TOP 1 1
                    FROM PriceHistory
                    WHERE ProductCode = @ProductCode
                      AND YEAR(PriceDate) = @Year";

                using var cmd = new SqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@ProductCode", productCode);
                cmd.Parameters.AddWithValue("@Year", year);

                var result = await cmd.ExecuteScalarAsync();
                return result != null && result != DBNull.Value;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ HasPriceInYearAsync error for {productCode} year={year}: {ex.Message}");
                return false;
            }
        }

        // เพิ่ม method ใหม่สำหรับดึงราคาล่าสุดในวันที่กำหนด
        public async Task<decimal?> GetLatestPriceForProductOnDateAsync(string productCode, DateTime date)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // ดึงราคาล่าสุดในวันนั้น (ไม่สนใจเวลา)
                var query = @"
                    SELECT TOP 1 Price
                    FROM PriceHistory
                    WHERE ProductCode = @ProductCode 
                        AND CONVERT(date, PriceDate) = @PriceDate
                    ORDER BY PriceDate DESC, Id DESC";

                using var cmd = new SqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@ProductCode", productCode);
                cmd.Parameters.AddWithValue("@PriceDate", date.Date);

                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    return Convert.ToDecimal(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetLatestPriceForProductOnDateAsync error for {productCode} {date:yyyy-MM-dd}: {ex.Message}");
            }

            return null;
        }

        private async Task LogProductActionAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string productCode,
            string actionType,
            string? oldValue,
            string? newValue,
            string? reason,
            DateTime? effectiveDate,
            string? performedBy,
            DateTime? performedAt,
            string? source = "UI",
            int? relatedId = null)
        {
            var sql = @"
        INSERT INTO ProductActionLog
            (ProductCode, ActionType, EffectiveDate, OldValue, NewValue, Reason,
             PerformedBy, PerformedAt, Source, RelatedId)
        VALUES
            (@ProductCode, @ActionType, @EffectiveDate, @OldValue, @NewValue, @Reason,
             @PerformedBy, SYSUTCDATETIME(), @Source, @RelatedId);";

            using var cmd = new SqlCommand(sql, connection, transaction);
            cmd.Parameters.AddWithValue("@ProductCode", productCode);
            cmd.Parameters.AddWithValue("@ActionType", actionType);
            cmd.Parameters.AddWithValue("@EffectiveDate", (object?)effectiveDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OldValue", (object?)oldValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@NewValue", (object?)newValue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Reason", (object?)reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PerformedBy", (object?)performedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Source", (object?)source ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RelatedId", (object?)relatedId ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<ProductActionLog>> GetProductActionLogAsync(string productCode)
        {
            var logs = new List<ProductActionLog>();
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
    SELECT Id, ProductCode, ActionType, EffectiveDate, OldValue, NewValue, Reason, PerformedBy, PerformedAt, Source
    FROM ProductActionLog
    WHERE ProductCode = @ProductCode
    ORDER BY PerformedAt";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@ProductCode", productCode);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                logs.Add(new ProductActionLog
                {
                    Id = reader.GetInt32(0),
                    ProductCode = reader.GetString(1),
                    ActionType = reader.IsDBNull(2) ? null : reader.GetString(2),
                    EffectiveDate = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                    OldValue = reader.IsDBNull(4) ? null : reader.GetString(4),
                    NewValue = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Reason = reader.IsDBNull(6) ? null : reader.GetString(6),
                    PerformedBy = reader.IsDBNull(7) ? null : reader.GetString(7),
                    PerformedAt = reader.IsDBNull(8)
                        ? null
                        : reader.GetFieldType(8) == typeof(DateTime)
                            ? reader.GetDateTime(8)
                            : DateTime.TryParse(reader.GetString(8), out var dt) ? dt : (DateTime?)null,
                    Source = reader.IsDBNull(9) ? null : reader.GetString(9)
                });
            }
            return logs;
        }

        public async Task<bool> EnableProductAsync(string code, string reason, string? modifiedBy)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                // Set IsActive = 1, clear DisabledUntil and bump modification count
                var updateQuery = @"
            UPDATE Products
            SET IsActive = 1,
                DisabledUntil = NULL,
                LastModifiedDate = GETDATE()
            WHERE Code = @Code";

                using (var command = new SqlCommand(updateQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@Code", code);
                    await command.ExecuteNonQueryAsync();
                }

                // Use the enable timestamp as the EffectiveDate for the ProductActionLog
                var effectiveDate = DateTime.Now;

                await LogProductActionAsync(
                    connection,
                    transaction,
                    productCode: code,
                    actionType: "Enable",
                    oldValue: null,
                    newValue: null,
                    reason: reason,
                    effectiveDate: effectiveDate,
                    performedBy: modifiedBy,
                    performedAt: DateTime.UtcNow,
                    source: "UI"
                );

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// ดึงรายการสินค้าที่มีการเปลี่ยนแปลงราคาในช่วงเวลาที่กำหนด
        /// เปลี่ยนหลักการการคำนวณราคาเก่า/ใหม่:
        /// - ราคากลางอ้างอิง (OldRefDate) = ถ้า startDate.Day <= 3 ใช้วันแรกของเดือนนั้น มิฉะนั้นใช้ startDate - 1 วัน
        /// - ราคากลางอ้างอิง (NewRefDate) = ถ้า endDate.Day <= 3 ใช้วันแรกของเดือนนั้น มิฉะนั้นใช้ endDate - 1 วัน
        /// ผลลัพธ์ยังคงเลือกเฉพาะสินค้าที่มีบันทึกราคาในช่วง startDate..endDate (เพื่อให้เป็นรายงานการเปลี่ยนแปลงในช่วงนั้น)
        /// แต่ราคาเก่า/ใหม่ที่นำมาเปรียบเทียบจะถูกดึงเป็นราคาล่าสุดที่มีอยู่ ณ หรือต่ำกว่า OldRefDate / NewRefDate ตามลำดับ
        /// </summary>
        public async Task<List<Requisition.Models.Reports.PriceChangeReportItem>> GetPriceChangesAsync(DateTime startDate, DateTime endDate)
        {
            var list = new List<Requisition.Models.Reports.PriceChangeReportItem>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // keep inclusive range for detecting changes inside the requested window
            var startParam = startDate.Date;
            var endParam = endDate.Date.AddDays(1).AddSeconds(-1);

            // Business rule requested:
            // - If day <= 3 then use first day of that month as the reference (e.g., start=3 -> oldRef=1)
            // - Otherwise use previous day (start - 1)
            DateTime oldRefDate = startDate.Day <= 3
                ? new DateTime(startDate.Year, startDate.Month, 1)
                : startDate.Date.AddDays(-1);

            DateTime newRefDate = endDate.Day <= 3
                ? new DateTime(endDate.Year, endDate.Month, 1)
                : endDate.Date.AddDays(-1);

            var sql = @"
WITH ChangedInRange AS (
    -- products that have any price history inside the selected window
    SELECT DISTINCT ProductCode
    FROM PriceHistory
    WHERE PriceDate >= @StartDate AND PriceDate <= @EndDate
)
SELECT 
    p.Code AS ProductCode,
    p.Name,
    p.Category,
    p.Unit,
    COALESCE(op.Price, 0) AS OldPrice,
    COALESCE(np.Price, 0) AS NewPrice,
    COALESCE(op.PriceDate, @OldRefDate) AS OldPriceDate,
    COALESCE(np.PriceDate, @NewRefDate) AS NewPriceDate,
    p.IsActive
FROM ChangedInRange c
INNER JOIN Products p ON p.Code = c.ProductCode
OUTER APPLY (
    -- latest price on or before oldRefDate
    SELECT TOP 1 Price, PriceDate
    FROM PriceHistory ph
    WHERE ph.ProductCode = c.ProductCode
      AND ph.Price IS NOT NULL
      AND ph.PriceDate <= @OldRefDate
    ORDER BY ph.PriceDate DESC, ph.Id DESC
) op
OUTER APPLY (
    -- latest price on or before newRefDate
    SELECT TOP 1 Price, PriceDate
    FROM PriceHistory ph
    WHERE ph.ProductCode = c.ProductCode
      AND ph.Price IS NOT NULL
      AND ph.PriceDate <= @NewRefDate
    ORDER BY ph.PriceDate DESC, ph.Id DESC
) np
-- require a new-price found (we're interested in items that have a price up to newRefDate)
WHERE np.Price IS NOT NULL
ORDER BY 
    -- percent change ordering (safeguard division by zero by using COALESCE fallback)
    ABS(
        (COALESCE(np.Price, 0) - COALESCE(op.Price, COALESCE(np.Price, 0)))
        /
        NULLIF(COALESCE(op.Price, COALESCE(np.Price, 0)), 0)
    ) DESC;";

            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@StartDate", startParam);
            cmd.Parameters.AddWithValue("@EndDate", endParam);
            cmd.Parameters.AddWithValue("@OldRefDate", oldRefDate.Date);
            cmd.Parameters.AddWithValue("@NewRefDate", newRefDate.Date);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var item = new Requisition.Models.Reports.PriceChangeReportItem
                {
                    ProductCode = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    ProductName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Category = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Unit = reader.IsDBNull(3) ? null : reader.GetString(3),
                    OldPrice = reader.IsDBNull(4) ? 0m : reader.GetDecimal(4),
                    NewPrice = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
                    OldPriceDate = reader.IsDBNull(6) ? oldRefDate : reader.GetDateTime(6),
                    NewPriceDate = reader.IsDBNull(7) ? newRefDate : reader.GetDateTime(7),
                    IsActive = !reader.IsDBNull(8) && reader.GetBoolean(8)
                };

                list.Add(item);
            }

            return list;
        }

        /// <summary>
        /// Find products whose Category suggests "meat" or "egg" but Unit doesn't match expected units.
        /// Heuristics:
        ///  - meat: Category contains "เนื้อ"  -> expected Unit in { "kg","กg","กก","กิโลกรัม","กิโล","kilogram" }
        ///  - egg:  Category contains "ไข่"   -> expected Unit in { "ฟอง","ฟ.","egg","pcs","pc" }
        /// Returns tuples: (Code, Name, Category, Unit, Issue)
        /// </summary>
        public async Task<List<(string Code, string Name, string? Category, string? Unit, string Issue)>> FindCategoryUnitMismatchesAsync()
        {
            var result = new List<(string, string, string?, string?, string)>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
SELECT Code, Name, Category, Unit
FROM Products
WHERE
    -- meat category but unit not in kg-like tokens
    (
        Category IS NOT NULL AND Category LIKE N'%เนื้อ%'
        AND LOWER(ISNULL(Unit,'')) NOT IN ('kg','kg.','กก','กิโลกรัม','กิโล','kilogram')
    )
    OR
    -- egg category but unit not in piece/ฟอง tokens
    (
        Category IS NOT NULL AND Category LIKE N'%ไข่%'
        AND LOWER(ISNULL(Unit,'')) NOT IN ('ฟอง','ฟ','egg','pcs','pc')
    )";

            using var cmd = new SqlCommand(sql, connection);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var code = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var cat = reader.IsDBNull(2) ? null : reader.GetString(2);
                var unit = reader.IsDBNull(3) ? null : reader.GetString(3);
                string issue = "";

                if (!string.IsNullOrWhiteSpace(cat) && cat.Contains("เนื้อ"))
                {
                    issue = $"Category indicates meat but Unit='{unit ?? ""}' is not kg-like";
                }
                else if (!string.IsNullOrWhiteSpace(cat) && cat.Contains("ไข่"))
                {
                    issue = $"Category indicates egg but Unit='{unit ?? ""}' is not piece/ฟอง-like";
                }
                else
                {
                    issue = "Category/Unit mismatch";
                }

                result.Add((code, name, cat, unit, issue));
            }

            return result;
        }
    }
}