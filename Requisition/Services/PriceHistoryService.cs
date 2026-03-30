using Microsoft.Data.SqlClient;
using Requisition.Helpers;
using Requisition.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Requisition.Services
{
    /// <summary>
    /// Service สำหรับจัดการประวัติราคาสินค้า
    /// </summary>
    public class PriceHistoryService
    {
        private readonly string _connectionString;

        public PriceHistoryService()
        {
            _connectionString = ConfigurationHelper.GetConnectionString();
        }

        /// <summary>
        /// ดึงราคาล่าสุดของสินค้า ณ วันที่กำหนด
        /// รวมทั้งจาก Import Excel และ Manual Edit
        /// </summary>
        /// <param name="productCode">รหัสสินค้า</param>
        /// <param name="targetDate">วันที่ต้องการดึงราคา</param>
        /// <returns>ราคา ณ วันที่กำหนด หรือ null ถ้าไม่มี</returns>
        public async Task<PriceAtDateResult?> GetLatestPriceAtDateAsync(
            string productCode, 
            DateTime targetDate)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = new SqlCommand("usp_GetLatestPriceAtDate", connection);
            command.CommandType = System.Data.CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@ProductCode", productCode);
            command.Parameters.AddWithValue("@TargetDate", targetDate);

            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new PriceAtDateResult
                {
                    ProductCode = reader.GetString(0),
                    Price = reader.GetDecimal(1),
                    PriceDateTime = reader.GetDateTime(2),
                    Source = reader.GetString(3),
                    ImportBatchId = reader.IsDBNull(4) ? null : reader.GetInt32(4)
                };
            }

            return null;
        }

        /// <summary>
        /// ดึงราคาสำหรับหลายสินค้าพร้อมกัน (สำหรับ Report)
        /// </summary>
        /// <param name="productCodes">รายการรหัสสินค้า</param>
        /// <param name="targetDate">วันที่ต้องการดึงราคา</param>
        /// <returns>Dictionary ของราคาแยกตามรหัสสินค้า</returns>
        public async Task<Dictionary<string, PriceAtDateResult>> GetPricesForMultipleProductsAsync(
            List<string> productCodes,
            DateTime targetDate)
        {
            var results = new Dictionary<string, PriceAtDateResult>();

            foreach (var code in productCodes)
            {
                var priceResult = await GetLatestPriceAtDateAsync(code, targetDate);
                if (priceResult != null)
                {
                    results[code] = priceResult;
                }
            }

            return results;
        }

        /// <summary>
        /// ดึงประวัติราคาทั้งหมดของสินค้า (เฉพาะจาก Import Excel)
        /// </summary>
        /// <param name="productCode">รหัสสินค้า</param>
        /// <returns>รายการประวัติราคา</returns>
        public async Task<List<PriceHistoryItem>> GetImportPriceHistoryAsync(string productCode)
        {
            var history = new List<PriceHistoryItem>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT Price, PriceDate, ImportBatchId
                FROM PriceHistory
                WHERE ProductCode = @Code
                ORDER BY PriceDate DESC";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Code", productCode);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (reader.IsDBNull(1)) continue;

                history.Add(new PriceHistoryItem
                {
                    Price = reader.IsDBNull(0) ? 0m : reader.GetDecimal(0),
                    Date = reader.GetDateTime(1),
                    Source = "Import",
                    ImportBatchId = reader.IsDBNull(2) ? null : reader.GetInt32(2)
                });
            }

            return history;
        }

        /// <summary>
        /// ดึงประวัติราคาทั้งหมด (รวม Import + Manual Edit)
        /// </summary>
        /// <param name="productCode">รหัสสินค้า</param>
        /// <returns>รายการประวัติราคาทั้งหมด</returns>
        public async Task<List<PriceHistoryItem>> GetAllPriceHistoryAsync(string productCode)
        {
            var history = new List<PriceHistoryItem>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT Price, PriceDateTime, Source, ImportBatchId
                FROM (
                    SELECT 
                        Price,
                        PriceDate AS PriceDateTime,
                        'Import' AS Source,
                        ImportBatchId
                    FROM PriceHistory
                    WHERE ProductCode = @Code
                    
                    UNION ALL
                    
                    SELECT 
                        TRY_CAST(JSON_VALUE(NewValues, '$.Price') AS DECIMAL(18, 2)) AS Price,
                        ModifiedDate AS PriceDateTime,
                        'Manual' AS Source,
                        NULL AS ImportBatchId
                    FROM ProductModificationHistory
                    WHERE ProductCode = @Code
                      AND NewValues IS NOT NULL
                      AND JSON_VALUE(NewValues, '$.Price') IS NOT NULL
                ) AS CombinedHistory
                WHERE Price IS NOT NULL
                ORDER BY PriceDateTime DESC";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Code", productCode);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                history.Add(new PriceHistoryItem
                {
                    Price = reader.IsDBNull(0) ? 0m : reader.GetDecimal(0),
                    Date = reader.IsDBNull(1) ? DateTime.MinValue : reader.GetDateTime(1),
                    Source = reader.IsDBNull(2) ? "Unknown" : reader.GetString(2),
                    ImportBatchId = reader.IsDBNull(3) ? null : reader.GetInt32(3)
                });
            }

            return history;
        }
    }
}
