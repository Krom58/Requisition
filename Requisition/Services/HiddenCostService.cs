using Microsoft.Data.SqlClient;
using Requisition.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Requisition.Services
{
    public class HiddenCostService
    {
        private readonly string _connectionString;

        public HiddenCostService()
        {
            _connectionString = ConfigurationHelper.GetConnectionString();
        }

        // ดึงค่าเปอร์เซ็นต์ปัจจุบัน
        public async Task<decimal> GetCurrentPercentageAsync()
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
                SELECT TOP 1 Percentage 
                FROM HiddenCostSettings 
                WHERE IsActive = 1 
                ORDER BY Id DESC", conn);

            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
                return 0m;

            return Convert.ToDecimal(result);
        }

        // บันทึกค่าเปอร์เซ็นต์ใหม่ (ปิด Active เก่า + สร้างใหม่ + บันทึกประวัติ)
        public async Task SavePercentageAsync(decimal percentage, string? actionBy = null)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var tran = conn.BeginTransaction();

            try
            {
                // 1. ดึงค่าเก่า
                decimal? oldPercentage = null;
                var cmdOld = new SqlCommand(@"
                    SELECT TOP 1 Percentage 
                    FROM HiddenCostSettings 
                    WHERE IsActive = 1 
                    ORDER BY Id DESC", conn, tran);

                var oldResult = await cmdOld.ExecuteScalarAsync();
                if (oldResult != null && oldResult != DBNull.Value)
                {
                    oldPercentage = Convert.ToDecimal(oldResult);
                }

                // 2. ปิด Active ของค่าเก่า
                var cmdDeactivate = new SqlCommand(@"
                    UPDATE HiddenCostSettings 
                    SET IsActive = 0 
                    WHERE IsActive = 1", conn, tran);

                await cmdDeactivate.ExecuteNonQueryAsync();

                // 3. เพิ่มค่าใหม่
                var cmdInsert = new SqlCommand(@"
                    INSERT INTO HiddenCostSettings (Percentage, IsActive, CreatedBy, CreatedDate)
                    VALUES (@Percentage, 1, @CreatedBy, GETDATE())", conn, tran);

                cmdInsert.Parameters.AddWithValue("@Percentage", percentage);
                cmdInsert.Parameters.AddWithValue("@CreatedBy", (object?)actionBy ?? DBNull.Value);

                await cmdInsert.ExecuteNonQueryAsync();

                // 4. บันทึกประวัติ
                var cmdHistory = new SqlCommand(@"
                    INSERT INTO HiddenCostHistory (OldPercentage, NewPercentage, ActionBy, ActionDate)
                    VALUES (@OldPercentage, @NewPercentage, @ActionBy, GETDATE())", conn, tran);

                cmdHistory.Parameters.AddWithValue("@OldPercentage", (object?)oldPercentage ?? DBNull.Value);
                cmdHistory.Parameters.AddWithValue("@NewPercentage", percentage);
                cmdHistory.Parameters.AddWithValue("@ActionBy", (object?)actionBy ?? DBNull.Value);

                await cmdHistory.ExecuteNonQueryAsync();

                tran.Commit();
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }

        // ดึงประวัติทั้งหมด
        public async Task<List<HiddenCostHistoryRecord>> GetHistoryAsync()
        {
            var list = new List<HiddenCostHistoryRecord>();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
                SELECT Id, OldPercentage, NewPercentage, ActionBy, ActionDate
                FROM HiddenCostHistory
                ORDER BY ActionDate DESC", conn);

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new HiddenCostHistoryRecord
                {
                    Id = rdr.GetInt32(0),
                    OldPercentage = rdr.IsDBNull(1) ? null : rdr.GetDecimal(1),
                    NewPercentage = rdr.GetDecimal(2),
                    ActionBy = rdr.IsDBNull(3) ? null : rdr.GetString(3),
                    ActionDate = rdr.GetDateTime(4)
                });
            }

            return list;
        }
    }

    // DTO สำหรับประวัติ
    public class HiddenCostHistoryRecord
    {
        public int Id { get; set; }
        public decimal? OldPercentage { get; set; }
        public decimal NewPercentage { get; set; }
        public string? ActionBy { get; set; }
        public DateTime ActionDate { get; set; }
    }
}
