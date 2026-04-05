using Microsoft.Data.SqlClient;
using Requisition.Helpers;
using Requisition.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
// (Append the following method inside the CombinedTransferService class)
using Requisition.Models.Reports;
using System.Globalization;

namespace Requisition.Services
{
    public partial class CombinedTransferService
    {
        private readonly string _connectionString;

        public CombinedTransferService()
        {
            _connectionString = ConfigurationHelper.GetConnectionString();
        }

        /// <summary>
        /// สร้างเลข CombinedNo ใหม่ (CT-YYYYMMDD-XXX) - บังคับใช้ปี ค.ศ.
        /// </summary>
        private async Task<string> GenerateCombinedNoAsync(SqlConnection connection, SqlTransaction? transaction = null)
        {
            // ✅ แก้ไข: บังคับใช้ Gregorian Calendar (ค.ศ.) และ InvariantCulture
            var today = DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            var prefix = $"CT-{today}-";

            var query = @"
                SELECT TOP 1 CombinedNo 
                FROM CombinedTransfer 
                WHERE CombinedNo LIKE @Prefix + '%' 
                ORDER BY CombinedNo DESC";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@Prefix", prefix);

            var lastNo = await command.ExecuteScalarAsync() as string;

            if (string.IsNullOrEmpty(lastNo))
                return prefix + "001";

            var lastNumber = int.Parse(lastNo.Substring(lastNo.LastIndexOf('-') + 1));
            return prefix + (lastNumber + 1).ToString("D3");
        }

        /// <summary>
        /// helper: insert history record (non-fatal)
        /// schema: CombinedTransferHistory (Id, CombinedTransferId, Action, Description, ModifiedBy, ModifiedDate, OldValues, NewValues)
        /// NOTE: swallow exceptions so history issues don't break main flow
        /// </summary>
        private async Task TryInsertHistoryAsync(SqlConnection connection, SqlTransaction? transaction, int combinedId, string action, string modifiedBy, string? description, string? oldValues, string? newValues)
        {
            try
            {
                var historyQuery = @"
                    INSERT INTO CombinedTransferHistory
                        (CombinedTransferId, Action, Description, ModifiedBy, ModifiedDate, OldValues, NewValues)
                    VALUES
                        (@CombinedTransferId, @Action, @Description, @ModifiedBy, @ModifiedDate, @OldValues, @NewValues)";

                using var histCmd = new SqlCommand(historyQuery, connection, transaction);
                histCmd.Parameters.AddWithValue("@CombinedTransferId", combinedId);
                histCmd.Parameters.AddWithValue("@Action", action);
                histCmd.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
                histCmd.Parameters.AddWithValue("@ModifiedBy", (object?)modifiedBy ?? DBNull.Value);
                histCmd.Parameters.AddWithValue("@ModifiedDate", DateTime.Now);
                histCmd.Parameters.AddWithValue("@OldValues", (object?)oldValues ?? DBNull.Value);
                histCmd.Parameters.AddWithValue("@NewValues", (object?)newValues ?? DBNull.Value);
                await histCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // swallow to avoid failing main transaction if history table missing/other issue
            }
        }

        /// <summary>
        /// ดึงรายการใบ Transfer ที่จบงานแล้วและยังไม่ถูกรวม (หรือถูกรวมในใบที่ถูกลบแล้ว)
        /// </summary>
        public async Task<List<SelectableTransferViewModel>> GetAvailableTransfersAsync(string? searchTransferNo = null, string? searchOutletName = null)
        {
            var transfers = new List<SelectableTransferViewModel>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    t.Id,
                    t.TransferNo,
                    t.CreatedDate,
                    t.ExpectedPeople,
                    t.ActualPeople,              -- NEW
                    t.UsageDate,
                    t.OutletId,
                    K.Name AS OutletName,
                    -- ⚠️ แก้ไข: ตรวจสอบว่าใบรวมที่ผูกไว้ถูกลบหรือยัง
                    CASE WHEN ct.Id IS NULL OR ct.IsDeleted = 1 THEN 0 ELSE 1 END as IsAlreadyCombined,
                    ISNULL((SELECT SUM(ti.InitialQuantity + ti.AdditionalQuantity - ISNULL(ti.ReturnedQuantity, 0)) 
                            FROM TransferItems ti 
                            WHERE ti.TransferId = t.Id), 0) as TotalQuantity,
                    ISNULL((SELECT SUM(ROUND((ti.InitialQuantity + ti.AdditionalQuantity - ISNULL(ti.ReturnedQuantity, 0)) * ISNULL(ti.UnitPrice, 0), 4)) 
                            FROM TransferItems ti 
                            WHERE ti.TransferId = t.Id), 0) as TotalCost,
                    ct.Id as BoundCombinedId,
                    ct.CombinedNo as BoundCombinedNo
                FROM Transfer t
                LEFT JOIN Outlets K ON t.OutletId = K.Id
                LEFT JOIN CombinedTransferSource cts ON cts.TransferId = t.Id
                LEFT JOIN CombinedTransfer ct ON ct.Id = cts.CombinedTransferId
                WHERE t.Status = 'Completed'
                    AND t.IsDeleted = 0
                    AND (@SearchTransferNo IS NULL OR t.TransferNo LIKE '%' + @SearchTransferNo + '%')
                    AND (@SearchOutletName IS NULL OR K.Name LIKE '%' + @SearchOutletName + '%')
                ORDER BY t.CreatedDate DESC";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@SearchTransferNo", (object?)searchTransferNo ?? DBNull.Value);
            command.Parameters.AddWithValue("@SearchOutletName", (object?)searchOutletName ?? DBNull.Value);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var transferId = reader.GetInt32(0);
                var expectedPeople = reader.GetInt32(3);
                var actualPeople = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4); // NEW
                var totalQuantity = reader.GetDecimal(9);
                var totalCost = reader.GetDecimal(10);
                var outletId = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);

                decimal? outletPricePerHead = null;
                if (outletId.HasValue)
                {
                    outletPricePerHead = await GetLatestOutletPriceAsync(outletId.Value, connection);
                }

                decimal? costPerPerson = expectedPeople > 0 ? totalCost / expectedPeople : null;

                transfers.Add(new SelectableTransferViewModel
                {
                    Id = transferId,
                    TransferNo = reader.GetString(1),
                    CreatedDate = reader.GetDateTime(2),
                    ExpectedPeople = expectedPeople,
                    ActualPeople = actualPeople, // NEW
                    UsageDate = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                    OutletId = outletId, // NEW
                    OutletName = reader.IsDBNull(7) ? "-" : reader.GetString(7),
                    CostPerPerson = costPerPerson,
                    TotalQuantity = totalQuantity,
                    TotalCost = totalCost,
                    IsAlreadyCombined = reader.GetInt32(8) == 1,
                    BoundCombinedId = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                    BoundCombinedNo = reader.IsDBNull(12) ? null : reader.GetString(12),
                    OutletPricePerHead = outletPricePerHead
                });
            }

            return transfers;
        }

        /// <summary>
        /// ดึงราคาต่อหัวล่าสุดของOutlet
        /// </summary>
        private async Task<decimal?> GetLatestOutletPriceAsync(int outletId, SqlConnection connection)
        {
            var query = @"
                SELECT PricePerHead
                FROM Outlets
                WHERE Id = @OutletId AND IsActive = 1";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@OutletId", outletId);

            var result = await command.ExecuteScalarAsync();
            return result == DBNull.Value ? null : (decimal?)result;
        }

        /// <summary>
        /// สร้างใบรวม Transfer ใหม่
        /// </summary>
        public async Task<(bool success, int combinedId, string combinedNo, string error)> CreateCombinedTransferAsync(
            List<int> transferIds,
            string? reason,
            string? createdBy)
        {
            if (transferIds == null || transferIds.Count == 0)
                return (false, 0, "", "ไม่มีใบ Transfer ที่เลือก");

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // ตรวจสอบว่าใบที่เลือกยังไม่ถูกรวม
                var checkQuery = @"
                    SELECT COUNT(*) 
                    FROM CombinedTransferSource 
                    WHERE TransferId IN (" + string.Join(",", transferIds) + ")";

                using (var checkCmd = new SqlCommand(checkQuery, connection, transaction))
                {
                    var alreadyCombinedCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    if (alreadyCombinedCount > 0)
                    {
                        await transaction.RollbackAsync();
                        return (false, 0, "", "มีใบที่เลือกถูกรวมไปแล้ว กรุณาตรวจสอบอีกครั้ง");
                    }
                }

                // คำนวณข้อมูลรวม
                var summaryQuery = @"
                    SELECT 
                        COUNT(DISTINCT t.Id) as TransferCount,
                        SUM(t.ExpectedPeople) as TotalPeople,
                        SUM(ISNULL(ti.TotalQuantity, 0)) as TotalQuantity,
                        SUM(ISNULL(ti.TotalCost, 0)) as TotalCost
                    FROM Transfer t
                    LEFT JOIN (
                        SELECT 
                            ti2.TransferId,
                            SUM(ti2.InitialQuantity + ti2.AdditionalQuantity - ISNULL(ti2.ReturnedQuantity, 0)) AS TotalQuantity,
                            SUM(ROUND((ti2.InitialQuantity + ti2.AdditionalQuantity - ISNULL(ti2.ReturnedQuantity, 0)) * ISNULL(ti2.UnitPrice, 0), 4)) AS TotalCost
                        FROM TransferItems ti2
                        WHERE ti2.TransferId IN (" + string.Join(",", transferIds) + @")
                        GROUP BY ti2.TransferId
                    ) ti ON ti.TransferId = t.Id
                    WHERE t.Id IN (" + string.Join(",", transferIds) + ")";

                int transferCount = 0;
                int totalPeople = 0;
                decimal totalQuantity = 0;
                decimal totalCost = 0;

                using (var summaryCmd = new SqlCommand(summaryQuery, connection, transaction))
                {
                    using var reader = await summaryCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        transferCount = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                        totalPeople = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                        totalQuantity = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2);
                        totalCost = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);
                    }
                }

                decimal? costPerHead = totalPeople > 0 ? totalCost / totalPeople : null;

                // สร้าง CombinedTransfer
                var combinedNo = await GenerateCombinedNoAsync(connection, transaction);
                var insertQuery = @"
                    INSERT INTO CombinedTransfer 
                        (CombinedNo, CreatedDate, CreatedBy, CombinedCount, TotalQuantity, TotalCost, TotalPeople, CombinedCostPerHead, Reason, IsDeleted)
                    OUTPUT INSERTED.Id
                    VALUES 
                        (@CombinedNo, @CreatedDate, @CreatedBy, @CombinedCount, @TotalQuantity, @TotalCost, @TotalPeople, @CombinedCostPerHead, @Reason, 0)";

                int combinedId;
                using (var insertCmd = new SqlCommand(insertQuery, connection, transaction))
                {
                    insertCmd.Parameters.AddWithValue("@CombinedNo", combinedNo);
                    insertCmd.Parameters.AddWithValue("@CreatedDate", DateTime.Now);
                    insertCmd.Parameters.AddWithValue("@CreatedBy", (object?)createdBy ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@CombinedCount", transferCount);
                    insertCmd.Parameters.AddWithValue("@TotalQuantity", totalQuantity);
                    insertCmd.Parameters.AddWithValue("@TotalCost", totalCost);
                    insertCmd.Parameters.AddWithValue("@TotalPeople", totalPeople);
                    insertCmd.Parameters.AddWithValue("@CombinedCostPerHead", (object?)costPerHead ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@Reason", (object?)reason ?? DBNull.Value);

                    var insertedIdObj = await insertCmd.ExecuteScalarAsync();
                    if (insertedIdObj == null || insertedIdObj == DBNull.Value)
                        throw new InvalidOperationException("Failed to insert CombinedTransfer and retrieve the new ID.");
                    combinedId = Convert.ToInt32(insertedIdObj);
                }

                // บันทึก CombinedTransferSources (ไม่มี CreatedDate column ในตารางนี้)
                foreach (var transferId in transferIds)
                {
                    var sourceQuery = @"
                        INSERT INTO CombinedTransferSource (CombinedTransferId, TransferId)
                        VALUES (@CombinedTransferId, @TransferId)";

                    using var sourceCmd = new SqlCommand(sourceQuery, connection, transaction);
                    sourceCmd.Parameters.AddWithValue("@CombinedTransferId", combinedId);
                    sourceCmd.Parameters.AddWithValue("@TransferId", transferId);
                    await sourceCmd.ExecuteNonQueryAsync();
                }

                // ← ใหม่: อัพเดท Transfer.CombinedTransferId ภายใน transaction เดียวกัน
                var updateTransfersQuery = "UPDATE Transfer SET CombinedTransferId = @CombinedId WHERE Id IN (" + string.Join(",", transferIds) + ")";
                using (var updCmd = new SqlCommand(updateTransfersQuery, connection, transaction))
                {
                    updCmd.Parameters.AddWithValue("@CombinedId", combinedId);
                    await updCmd.ExecuteNonQueryAsync();
                }

                // history: Create (OldValues null, NewValues = transferIds JSON)
                var newValuesJson = JsonSerializer.Serialize(transferIds);
                var description = $"สร้างใบรวมจากใบtransfers{Environment.NewLine}เหตุผล: {(reason ?? "-")}";
                await TryInsertHistoryAsync(connection, transaction, combinedId, "Create", createdBy ?? string.Empty, description, null, newValuesJson);

                await transaction.CommitAsync();
                return (true, combinedId, combinedNo, "");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, 0, "", $"เกิดข้อผิดพลาด: {ex.Message}");
            }
        }

        /// <summary>
        /// ดึงรายการใบรวมทั้งหมด (รวมที่ถูกลบด้วย)
        /// </summary>
        public async Task<List<CombinedTransferListViewModel>> GetAllCombinedTransfersAsync(bool includeDeleted = false)
        {
            var list = new List<CombinedTransferListViewModel>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    ct.Id,
                    ct.CombinedNo,
                    ct.CreatedDate,
                    ct.CreatedBy,
                    ct.CombinedCount,
                    ct.TotalCost,
                    ct.Reason,
                    ct.IsDeleted
                FROM CombinedTransfer ct
                WHERE (@IncludeDeleted = 1 OR ct.IsDeleted = 0)
                ORDER BY ct.CreatedDate DESC";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@IncludeDeleted", includeDeleted);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var combinedId = reader.GetInt32(0);

                // ดึงรายละเอียดของแต่ละใบ Transfer ที่อยู่ในใบรวม
                var transferDetails = await GetTransferDetailsByCombinedIdAsync(combinedId, connection);

                // คำนวณข้อมูลสรุป
                var transferNos = transferDetails.Select(t => t.TransferNo).ToList();
                var peopleCounts = transferDetails.Select(t => t.PeopleCount).ToList();
                var individualCosts = transferDetails.Select(t => new { t.TransferNo, t.TotalCost }).ToList();

                // 🔥 จัดกลุ่มตาม Outlet และดึงราคาต่อหัวจากฐานข้อมูล
                var outletPrices = new Dictionary<string, decimal?>();
                foreach (var detail in transferDetails.Where(t => t.OutletId.HasValue).GroupBy(t => new { t.OutletId, t.OutletName }))
                {
                    var outletName = detail.Key.OutletName ?? "ไม่ระบุ";
                    var pricePerHead = await GetLatestOutletPriceAsync(detail.Key.OutletId!.Value, connection);
                    outletPrices[outletName] = pricePerHead;
                }

                // เช็คความสอดคล้องของจำนวนคน
                var uniquePeopleCounts = peopleCounts.Distinct().ToList();
                bool hasInconsistentPeople = uniquePeopleCounts.Count > 1;
                int? consistentPeopleCount = !hasInconsistentPeople && peopleCounts.Count > 0 ? peopleCounts[0] : null;

                // คำนวณราคาต่อหัว
                decimal? costPerHead = null;
                if (consistentPeopleCount.HasValue && consistentPeopleCount > 0)
                {
                    costPerHead = reader.GetDecimal(5) / consistentPeopleCount.Value;
                }
                else if (peopleCounts.Count > 0 && peopleCounts.Sum() > 0)
                {
                    costPerHead = reader.GetDecimal(5) / (decimal)peopleCounts.Average();
                }

                // จัดรูปแบบการแสดงผล "รวมแต่ละใบ"
                string individualCostsDisplay = individualCosts.Count > 0
                    ? string.Join(", ", individualCosts.Select(ic => $"{ic.TransferNo}: {ic.TotalCost:N0}฿"))
                    : "-";

                // 🔥 จัดรูปแบบการแสดงผล "ราคาต่อหัวของ Outlet"
                string outletPricePerHeadDisplay = outletPrices.Count > 0
                    ? string.Join(", ", outletPrices.Select(op =>
                        $"{op.Key}: {(op.Value.HasValue ? $"{op.Value.Value:N4}฿/คน" : "ไม่ระบุ")}"))
                    : "-";

                list.Add(new CombinedTransferListViewModel
                {
                    Id = combinedId,
                    CombinedNo = reader.GetString(1),
                    CreatedDate = reader.GetDateTime(2).ToString("dd/MM/yyyy HH:mm"),
                    CreatedBy = reader.IsDBNull(3) ? "ไม่ระบุ" : reader.GetString(3),
                    CombinedCount = reader.GetInt32(4),
                    TotalCost = reader.GetDecimal(5),
                    Reason = reader.IsDBNull(6) ? null : reader.GetString(6),
                    IsDeleted = reader.GetBoolean(7),
                    TransferNosDisplay = FormatTransferNosDisplay(transferNos),

                    // 🔥 ข้อมูลใหม่
                    PeopleCount = consistentPeopleCount,
                    HasInconsistentPeopleCount = hasInconsistentPeople,
                    AllPeopleCounts = peopleCounts,
                    CombinedCostPerHead = costPerHead,
                    IndividualTransferCostsDisplay = individualCostsDisplay,
                    OutletPricePerHeadDisplay = outletPricePerHeadDisplay
                });
            }

            return list;
        }

        /// <summary>
        /// ดึงรายละเอียดของแต่ละ Transfer ในใบรวม (สำหรับคำนวณข้อมูลสรุป)
        /// </summary>
        private async Task<List<TransferDetailForSummary>> GetTransferDetailsByCombinedIdAsync(int combinedId, SqlConnection connection)
        {
            var details = new List<TransferDetailForSummary>();

            var query = @"
                SELECT 
                    t.Id,
                    t.TransferNo,
                    t.ExpectedPeople,
                    t.OutletId,
                    O.Name AS OutletName,
                    ISNULL((SELECT SUM(ROUND((ti.InitialQuantity + ti.AdditionalQuantity - ISNULL(ti.ReturnedQuantity, 0)) * ISNULL(ti.UnitPrice, 0), 4)) 
                            FROM TransferItems ti WHERE ti.TransferId = t.Id), 0) as TotalCost
                FROM CombinedTransferSource cts
                INNER JOIN Transfer t ON cts.TransferId = t.Id
                LEFT JOIN Outlets O ON t.OutletId = O.Id
                WHERE cts.CombinedTransferId = @CombinedId
                ORDER BY t.TransferNo";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@CombinedId", combinedId);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                details.Add(new TransferDetailForSummary
                {
                    Id = reader.GetInt32(0),
                    TransferNo = reader.GetString(1),
                    PeopleCount = reader.GetInt32(2),
                    OutletId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    OutletName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    TotalCost = reader.GetDecimal(5)
                });
            }

            return details;
        }

        // 🔥 เพิ่ม helper class สำหรับเก็บข้อมูลรายละเอียด Transfer
        private class TransferDetailForSummary
        {
            public int Id { get; set; }
            public string TransferNo { get; set; } = string.Empty;
            public int PeopleCount { get; set; }
            public int? OutletId { get; set; }
            public string? OutletName { get; set; }
            public decimal TotalCost { get; set; }
        }

        /// <summary>
        /// ลบใบรวม (Soft Delete)
        /// </summary>
        public async Task<(bool success, string error)> DeleteCombinedTransferAsync(
            int combinedId,
            string? deletedBy,
            string? reason)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // capture old values (transfer ids) for history
                List<int> oldTransferIds = new();
                var oldQuery = "SELECT TransferId FROM CombinedTransferSource WHERE CombinedTransferId = @CombinedId";
                using (var oldCmd = new SqlCommand(oldQuery, connection, transaction))
                {
                    oldCmd.Parameters.AddWithValue("@CombinedId", combinedId);
                    using var rdr = await oldCmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                    {
                        oldTransferIds.Add(rdr.GetInt32(0));
                    }
                }

                // ← ใหม่: เซ็ต CombinedTransferId = NULL ให้ Transfer ที่เกี่ยวข้อง ก่อน soft-delete
                if (oldTransferIds.Count > 0)
                {
                    var q = "UPDATE Transfer SET CombinedTransferId = NULL WHERE Id IN (" + string.Join(",", oldTransferIds) + ")";
                    using var cmd = new SqlCommand(q, connection, transaction);
                    await cmd.ExecuteNonQueryAsync();
                }

                var query = @"
                    UPDATE CombinedTransfer
                    SET IsDeleted = 1,
                        DeletedDate = @DeletedDate,
                        DeletedBy = @DeletedBy,
                        DeletedReason = @Reason
                    WHERE Id = @Id";

                using var command = new SqlCommand(query, connection, transaction);
                command.Parameters.AddWithValue("@Id", combinedId);
                command.Parameters.AddWithValue("@DeletedDate", DateTime.Now);
                command.Parameters.AddWithValue("@DeletedBy", (object?)deletedBy ?? DBNull.Value);
                command.Parameters.AddWithValue("@Reason", (object?)reason ?? DBNull.Value);

                await command.ExecuteNonQueryAsync();

                // insert history (delete) with old values (JSON)
                var oldJson = oldTransferIds.Count > 0 ? JsonSerializer.Serialize(oldTransferIds) : null;
                await TryInsertHistoryAsync(connection, transaction, combinedId, "Delete", deletedBy ?? string.Empty, reason, oldJson, null);

                await transaction.CommitAsync();

                return (true, "");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, $"เกิดข้อผิดพลาด: {ex.Message}");
            }
        }

        /// <summary>
        /// ดึงรายละเอียดใบรวม
        /// </summary>
        public async Task<CombinedTransferDetailViewModel?> GetCombinedTransferDetailAsync(int combinedId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // ดึงข้อมูลหลัก
            var mainQuery = @"
        SELECT CombinedNo, CreatedDate, CreatedBy, Reason, IsDeleted
        FROM CombinedTransfer
        WHERE Id = @Id";

            CombinedTransferDetailViewModel? detail = null;

            using (var command = new SqlCommand(mainQuery, connection))
            {
                command.Parameters.AddWithValue("@Id", combinedId);
                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    detail = new CombinedTransferDetailViewModel
                    {
                        Id = combinedId,
                        CombinedNo = reader.GetString(0),
                        CreatedDate = reader.GetDateTime(1),
                        CreatedBy = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Reason = reader.IsDBNull(3) ? null : reader.GetString(3)
                    };
                }
            }

            if (detail == null) return null;

            // ดึงรายการ Transfer ที่ถูกรวม
            detail.Transfers = await GetTransferSummariesAsync(combinedId, connection);

            // 🔥 คำนวณข้อมูลสรุป
            detail.TransferCount = detail.Transfers.Count;

            // คำนวณจำนวนคน (Expected)
            var expectedPeople = detail.Transfers.Select(t => t.ExpectedPeople).ToList();
            var uniqueExpected = expectedPeople.Distinct().ToList();
            detail.HasInconsistentPeople = uniqueExpected.Count > 1;
            detail.ConsistentPeopleCount = !detail.HasInconsistentPeople && expectedPeople.Count > 0
                ? expectedPeople[0] : null;
            detail.AllPeopleCounts = expectedPeople;

            // คำนวณจำนวนคน (Actual)
            var actualPeople = detail.Transfers
                .Where(t => t.ActualPeople.HasValue)
                .Select(t => t.ActualPeople!.Value)
                .ToList();

            if (actualPeople.Count > 0)
            {
                var uniqueActual = actualPeople.Distinct().ToList();
                detail.HasInconsistentActualPeople = uniqueActual.Count > 1;
                detail.AllActualPeopleCounts = actualPeople;

                // ✅ เปลี่ยน: ถ้าคนเท่ากัน → ใช้ค่านั้น, ถ้าไม่เท่ากัน → null
                if (!detail.HasInconsistentActualPeople && actualPeople.Count > 0)
                {
                    detail.TotalActualPeople = actualPeople[0];  // ← ใช้ค่าเดียว (ไม่ใช่ Sum)
                }
                else
                {
                    detail.TotalActualPeople = null;  // ← คนไม่เท่ากัน ไม่ระบุค่า
                }
            }

            // คำนวณยอดรวมและราคาต่อหัว
            detail.TotalCost = detail.Transfers.Sum(t => t.TotalCost);

            // ✅ ราคาต่อหัว (Expected) - ใช้เฉพาะเมื่อคนเท่ากัน
            if (detail.ConsistentPeopleCount.HasValue && detail.ConsistentPeopleCount > 0)
            {
                detail.CostPerHead = detail.TotalCost / detail.ConsistentPeopleCount.Value;
            }
            else
            {
                // ถ้าคนไม่เท่ากัน → ไม่คำนวณ CostPerHead
                detail.CostPerHead = null;
            }

            // ✅ ราคาต่อหัว (Actual) - ใช้เฉพาะเมื่อคนจริงเท่ากัน
            if (detail.TotalActualPeople.HasValue && detail.TotalActualPeople > 0)
            {
                detail.ActualCostPerHead = detail.TotalCost / detail.TotalActualPeople.Value;
            }
            else
            {
                detail.ActualCostPerHead = null;
            }

            // 🔥 รวมแต่ละใบ
            var individualCosts = detail.Transfers
                .Select(t => $"{t.TransferNo}: {t.TotalCost:N4}฿")
                .ToList();
            detail.IndividualCostsDisplay = individualCosts.Count > 0
                ? string.Join(", ", individualCosts)
                : "-";

            // 🔥 งบตาม Outlet
            var outletBudgets = detail.Transfers
                .Where(t => t.OutletPricePerHead.HasValue)
                .GroupBy(t => t.OutletName)
                .Select(g => new
                {
                    Outlet = g.Key,
                    Budget = g.First().OutletPricePerHead!.Value
                })
                .ToList();

            detail.OutletBudgetsDisplay = outletBudgets.Count > 0
                ? string.Join(", ", outletBudgets.Select(o => $"{o.Outlet}: {o.Budget:N4}฿/คน"))
                : "-";

            detail.MaxOutletBudget = outletBudgets.Count > 0
                ? outletBudgets.Max(o => o.Budget)
                : null;

            // ดึงรายการสินค้ารวม
            detail.CombinedItems = await GetCombinedItemsAsync(combinedId, connection);

            return detail;
        }

        /// <summary>
        /// ดึงข้อมูลสรุปของแต่ละ Transfer ในใบรวม (เพิ่ม KitchenName + HiddenCostPercentage)
        /// </summary>
        private async Task<List<TransferSummaryViewModel>> GetTransferSummariesAsync(
            int combinedId,
            SqlConnection connection)
        {
            var summaries = new List<TransferSummaryViewModel>();

            var query = @"
        SELECT 
            t.Id,
            t.TransferNo,
            O.Name AS OutletName,
            K.Name AS KitchenName,
            t.UsageDate,
            t.ExpectedPeople,
            t.ActualPeople,
            t.OutletId,
            ISNULL((SELECT SUM(ti.InitialQuantity + ti.AdditionalQuantity - ISNULL(ti.ReturnedQuantity, 0)) 
                    FROM TransferItems ti 
                    WHERE ti.TransferId = t.Id), 0) as TotalQuantity,
            ISNULL((SELECT SUM(ROUND((ti.InitialQuantity + ti.AdditionalQuantity - ISNULL(ti.ReturnedQuantity, 0)) * ISNULL(ti.UnitPrice, 0), 4)) 
                    FROM TransferItems ti 
                    WHERE ti.TransferId = t.Id), 0) as TotalCost,
            ISNULL(t.HiddenCostPercentage, 0) as HiddenCostPercentage
        FROM CombinedTransferSource cts
        INNER JOIN Transfer t ON cts.TransferId = t.Id
        LEFT JOIN Outlets O ON t.OutletId = O.Id
        LEFT JOIN Kitchens K ON t.KitchenId = K.Id
        WHERE cts.CombinedTransferId = @CombinedId
        ORDER BY t.UsageDate, t.TransferNo";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@CombinedId", combinedId);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var expectedPeople = reader.GetInt32(5);
                var actualPeople = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);
                var totalCost = reader.GetDecimal(9);
                var outletId = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7);
                var hiddenCostPercentage = reader.GetDecimal(10); // ← เพิ่มบรรทัดนี้

                decimal? outletPricePerHead = null;
                if (outletId.HasValue)
                {
                    outletPricePerHead = await GetLatestOutletPriceAsync(outletId.Value, connection);
                }

                decimal? costPerPerson = expectedPeople > 0 ? totalCost / expectedPeople : null;

                summaries.Add(new TransferSummaryViewModel
                {
                    Id = reader.GetInt32(0),
                    TransferNo = reader.GetString(1),
                    OutletName = reader.IsDBNull(2) ? "-" : reader.GetString(2),
                    KitchenName = reader.IsDBNull(3) ? "-" : reader.GetString(3),
                    UsageDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                    ExpectedPeople = expectedPeople,
                    ActualPeople = actualPeople,
                    CostPerPerson = costPerPerson,
                    TotalQuantity = reader.GetDecimal(8),
                    TotalCost = totalCost,
                    OutletPricePerHead = outletPricePerHead,
                    HiddenCostPercentage = hiddenCostPercentage // ← เพิ่มบรรทัดนี้
                });
            }

            return summaries;
        }

        /// <summary>
        /// ดึงรายการสินค้ารวมพร้อมที่มา
        /// </summary>
        private async Task<List<CombinedItemViewModel>> GetCombinedItemsAsync(
            int combinedId,
            SqlConnection connection)
        {
            var query = @"
                SELECT 
                    p.Code,
                    p.Name,
                    p.Unit,
                    ti.TransferId,
                    t.TransferNo,
                    (ti.InitialQuantity + ti.AdditionalQuantity) as IssuedQuantity,
                    ti.UnitPrice
                FROM CombinedTransferSource cts
                INNER JOIN Transfer t ON cts.TransferId = t.Id
                INNER JOIN TransferItems ti ON t.Id = ti.TransferId
                INNER JOIN Products p ON ti.ProductCode = p.Code
                WHERE cts.CombinedTransferId = @CombinedId
                ORDER BY p.Code";

            var itemDict = new Dictionary<string, CombinedItemViewModel>();

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@CombinedId", combinedId);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var productCode = reader.GetString(0);
                var productName = reader.GetString(1);
                var unit = reader.GetString(2);
                var transferNo = reader.GetString(4);
                var quantity = reader.GetDecimal(5);
                var unitCost = reader.IsDBNull(6) ? 0m : reader.GetDecimal(6);

                if (!itemDict.TryGetValue(productCode, out var item))
                {
                    item = new CombinedItemViewModel
                    {
                        ProductCode = productCode,
                        ProductName = productName,
                        Unit = unit,
                        TotalQuantity = 0,
                        TotalCost = 0,
                        Sources = new List<ItemSourceInfo>()
                    };
                    itemDict[productCode] = item;
                }

                item.TotalQuantity += quantity;
                item.TotalCost += quantity * unitCost;
                item.Sources.Add(new ItemSourceInfo
                {
                    TransferNo = transferNo,
                    Quantity = quantity,
                    UnitCost = unitCost
                });
            }

            // คำนวณต้นทุนเฉลี่ย
            foreach (var item in itemDict.Values)
            {
                item.AverageUnitCost = item.TotalQuantity > 0 ? item.TotalCost / item.TotalQuantity : 0;
            }

            return itemDict.Values.OrderBy(i => i.ProductCode).ToList();
        }

        /// <summary>
        /// อัพเดทการเลือกใบ Transfer ในใบรวม (สำหรับหน้าแก้ไข)
        /// </summary>
        public async Task<(bool success, string error)> UpdateCombinedTransferSourcesAsync(
            int combinedId,
            List<int> newTransferIds,
            string? modifiedBy,
            string? reason)
        {
            if (newTransferIds == null || newTransferIds.Count == 0)
                return (false, "ไม่มีใบ Transfer ที่เลือก");

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // capture old values for history (transfer ids)
                List<int> oldTransferIds = new();
                var oldQuery = "SELECT TransferId FROM CombinedTransferSource WHERE CombinedTransferId = @CombinedId";
                using (var oldCmd = new SqlCommand(oldQuery, connection, transaction))
                {
                    oldCmd.Parameters.AddWithValue("@CombinedId", combinedId);
                    using var rdr = await oldCmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                    {
                        oldTransferIds.Add(rdr.GetInt32(0));
                    }
                }

                // ลบรายการเดิมทั้งหมด
                var deleteQuery = "DELETE FROM CombinedTransferSource WHERE CombinedTransferId = @CombinedId";
                using (var deleteCmd = new SqlCommand(deleteQuery, connection, transaction))
                {
                    deleteCmd.Parameters.AddWithValue("@CombinedId", combinedId);
                    await deleteCmd.ExecuteNonQueryAsync();
                }

                // เพิ่มรายการใหม่ (ไม่มี CreatedDate column)
                foreach (var transferId in newTransferIds)
                {
                    var insertQuery = @"
                        INSERT INTO CombinedTransferSource (CombinedTransferId, TransferId)
                        VALUES (@CombinedTransferId, @TransferId)";

                    using var insertCmd = new SqlCommand(insertQuery, connection, transaction);
                    insertCmd.Parameters.AddWithValue("@CombinedTransferId", combinedId);
                    insertCmd.Parameters.AddWithValue("@TransferId", transferId);
                    await insertCmd.ExecuteNonQueryAsync();
                }
                // ← ใหม่: ซิงก์คอลัมน์ Transfer.CombinedTransferId
                var removed = oldTransferIds.Except(newTransferIds).ToList();
                var added = newTransferIds.Except(oldTransferIds).ToList();

                if (removed.Count > 0)
                {
                    var q = "UPDATE Transfer SET CombinedTransferId = NULL WHERE Id IN (" + string.Join(",", removed) + ")";
                    using var cmd = new SqlCommand(q, connection, transaction);
                    await cmd.ExecuteNonQueryAsync();
                }

                if (added.Count > 0)
                {
                    var q2 = "UPDATE Transfer SET CombinedTransferId = @CombinedId WHERE Id IN (" + string.Join(",", added) + ")";
                    using var cmd2 = new SqlCommand(q2, connection, transaction);
                    cmd2.Parameters.AddWithValue("@CombinedId", combinedId);
                    await cmd2.ExecuteNonQueryAsync();
                }
                // คำนวณข้อมูลรวมใหม่
                var summaryQuery = @"
                    SELECT 
                        COUNT(DISTINCT t.Id) as TransferCount,
                        SUM(t.ExpectedPeople) as TotalPeople,
                        SUM(ISNULL(ti.TotalQuantity, 0)) as TotalQuantity,
                        SUM(ISNULL(ti.TotalCost, 0)) as TotalCost
                    FROM Transfer t
                    LEFT JOIN (
                        SELECT 
                            ti2.TransferId,
                            SUM(ti2.InitialQuantity + ti2.AdditionalQuantity - ISNULL(ti2.ReturnedQuantity, 0)) AS TotalQuantity,
                            SUM(ROUND((ti2.InitialQuantity + ti2.AdditionalQuantity - ISNULL(ti2.ReturnedQuantity, 0)) * ISNULL(ti2.UnitPrice, 0), 4)) AS TotalCost
                        FROM TransferItems ti2
                        WHERE ti2.TransferId IN (" + string.Join(",", newTransferIds) + @")
                        GROUP BY ti2.TransferId
                    ) ti ON ti.TransferId = t.Id
                    WHERE t.Id IN (" + string.Join(",", newTransferIds) + ")";

                int transferCount = 0;
                int totalPeople = 0;
                decimal totalQuantity = 0;
                decimal totalCost = 0;

                using (var summaryCmd = new SqlCommand(summaryQuery, connection, transaction))
                {
                    using var reader = await summaryCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        transferCount = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                        totalPeople = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                        totalQuantity = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2);
                        totalCost = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);
                    }
                }

                decimal? costPerHead = totalPeople > 0 ? totalCost / totalPeople : null;

                // อัพเดทข้อมูลรวม (รวม Reason ถ้ามี)
                var updateQuery = @"
                    UPDATE CombinedTransfer
                    SET CombinedCount = @CombinedCount,
                        TotalPeople = @TotalPeople,
                        TotalQuantity = @TotalQuantity,
                        TotalCost = @TotalCost,
                        CombinedCostPerHead = @CombinedCostPerHead,
                        Reason = @Reason
                    WHERE Id = @Id";

                using (var updateCmd = new SqlCommand(updateQuery, connection, transaction))
                {
                    updateCmd.Parameters.AddWithValue("@Id", combinedId);
                    updateCmd.Parameters.AddWithValue("@CombinedCount", transferCount);
                    updateCmd.Parameters.AddWithValue("@TotalPeople", totalPeople);
                    updateCmd.Parameters.AddWithValue("@TotalQuantity", totalQuantity);
                    updateCmd.Parameters.AddWithValue("@TotalCost", totalCost);
                    updateCmd.Parameters.AddWithValue("@CombinedCostPerHead", (object?)costPerHead ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@Reason", (object?)reason ?? DBNull.Value);
                    await updateCmd.ExecuteNonQueryAsync();
                }

                // insert history (update) with old/new values and reason - store JSON for OldValues/NewValues
                var oldJson = oldTransferIds.Count > 0 ? JsonSerializer.Serialize(oldTransferIds) : null;
                var newJson = newTransferIds.Count > 0 ? JsonSerializer.Serialize(newTransferIds) : null;
                var description = $"แก้ไขใบรวมTransfer{Environment.NewLine}เหตุผล: {(reason ?? "-")}";
                await TryInsertHistoryAsync(connection, transaction, combinedId, "Update", modifiedBy ?? string.Empty, description, oldJson, newJson);

                await transaction.CommitAsync();
                return (true, "");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, $"เกิดข้อผิดพลาด: {ex.Message}");
            }
        }

        /// <summary>
        /// สร้างใบรวม (Legacy method สำหรับหน้าเก่า)
        /// </summary>
        public async Task<int> CreateCombinedTransferAsync(
            IEnumerable<int> ids,
            int totalPeople,
            decimal totalQty,
            decimal totalCost,
            string createdBy,
            string notes)
        {
            var transferIds = ids.ToList();
            var (success, combinedId, combinedNo, error) = await CreateCombinedTransferAsync(
                transferIds,
                notes,
                createdBy);

            if (!success)
                throw new Exception(error);

            return combinedId;
        }

        /// <summary>
        /// ดึงรายการ Combined ทั้งหมด (Legacy method)
        /// </summary>
        public async Task<List<CombinedTransfer>> GetAllCombinedAsync()
        {
            var list = new List<CombinedTransfer>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    Id, CombinedNo, CreatedDate, CreatedBy, 
                    CombinedCount, TotalQuantity, TotalCost, 
                    TotalPeople, CombinedCostPerHead, Reason, IsDeleted
                FROM CombinedTransfer
                WHERE IsDeleted = 0
                ORDER BY CreatedDate DESC";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                list.Add(new CombinedTransfer
                {
                    Id = reader.GetInt32(0),
                    CombinedNo = reader.GetString(1),
                    CreatedDate = reader.GetDateTime(2),
                    CreatedBy = reader.IsDBNull(3) ? null : reader.GetString(3),
                    CombinedCount = reader.GetInt32(4),
                    TotalQuantity = reader.GetDecimal(5),
                    TotalCost = reader.GetDecimal(6),
                    TotalPeople = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    CombinedCostPerHead = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                    Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
                    IsDeleted = reader.GetBoolean(10)
                });
            }

            return list;
        }

        /// <summary>
        /// ดึง Transfer ที่อยู่ในใบรวม (Legacy method)
        /// </summary>
        public async Task<List<Transfer>> GetCombinedSourcesAsync(int combinedId)
        {
            var transfers = new List<Transfer>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    t.Id, t.TransferNo, t.CreatedDate, t.ExpectedPeople,
                    t.UsageDate, t.OutletId, O.Name AS OutletName, t.OutletPricePerHeadAtSave,
                    t.Status, t.CompletedDate, t.Notes
                FROM CombinedTransferSource cts
                INNER JOIN Transfer t ON cts.TransferId = t.Id
                LEFT JOIN Outlets O ON t.OutletId = O.Id
                WHERE cts.CombinedTransferId = @CombinedId
                ORDER BY t.TransferNo";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@CombinedId", combinedId);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                // Status เป็น string ให้แปลงเป็น enum
                var statusString = reader.GetString(8);
                var status = Enum.TryParse<TransferStatus>(statusString, out var parsedStatus)
                    ? parsedStatus
                    : TransferStatus.Draft;

                transfers.Add(new Transfer
                {
                    Id = reader.GetInt32(0),
                    TransferNo = reader.GetString(1),
                    CreatedDate = reader.GetDateTime(2),
                    ExpectedPeople = reader.GetInt32(3),
                    UsageDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                    OutletId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    OutletName = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Budget = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7),
                    Status = status,
                    CompletedDate = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                    Notes = reader.IsDBNull(10) ? null : reader.GetString(10)
                });
            }

            return transfers;
        }

        /// <summary>
        /// ดึงรายการประวัติการแก้ไขใบรวม
        /// </summary>
        public async Task<List<CombinedTransferHistoryViewModel>> GetCombinedTransferHistoryAsync(int combinedId)
        {
            var list = new List<CombinedTransferHistoryViewModel>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    Id,
                    CombinedTransferId,
                    Action,
                    Description,
                    ModifiedBy,
                    ModifiedDate,
                    OldValues,
                    NewValues
                FROM CombinedTransferHistory
                WHERE CombinedTransferId = @CombinedId
                ORDER BY ModifiedDate DESC";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@CombinedId", combinedId);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new CombinedTransferHistoryViewModel
                {
                    Id = reader.GetInt32(0),
                    CombinedTransferId = reader.GetInt32(1),
                    Action = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    ModifiedBy = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    ModifiedDate = reader.IsDBNull(5) ? DateTime.MinValue : reader.GetDateTime(5),
                    OldValues = reader.IsDBNull(6) ? null : reader.GetString(6),
                    NewValues = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }

            return list;
        }

        // added new method near the other public readers (keep in same partial class)
        public async Task<List<CombinedTransferHistoryViewModel>> GetAllCombinedTransferHistoryAsync()
        {
            var list = new List<CombinedTransferHistoryViewModel>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT 
                    Id,
                    CombinedTransferId,
                    Action,
                    Description,
                    ModifiedBy,
                    ModifiedDate,
                    OldValues,
                    NewValues
                FROM CombinedTransferHistory
                ORDER BY ModifiedDate DESC";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new CombinedTransferHistoryViewModel
                {
                    Id = reader.GetInt32(0),
                    CombinedTransferId = reader.GetInt32(1),
                    Action = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    ModifiedBy = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    ModifiedDate = reader.IsDBNull(5) ? DateTime.MinValue : reader.GetDateTime(5),
                    OldValues = reader.IsDBNull(6) ? null : reader.GetString(6),
                    NewValues = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }

            return list;
        }

        /// <summary>
        /// Resolve a set of Transfer.Id values to their TransferNo.
        /// Returns an empty dictionary when ids is empty or none found.
        /// </summary>
        public async Task<Dictionary<int, string>> GetTransferNosByIdsAsync(IEnumerable<int>? ids)
        {
            var result = new Dictionary<int, string>();
            if (ids == null) return result;

            var idList = ids.Where(i => i > 0).Distinct().ToList();
            if (idList.Count == 0) return result;

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Build IN list safely from ints
            var query = @"SELECT Id, TransferNo FROM Transfer WHERE Id IN (" + string.Join(",", idList) + ")";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32(0);
                var no = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                result[id] = no;
            }

            return result;
        }

        /// <summary>
        /// ดึงรายการ Outlets ทั้งหมดที่ใช้งานอยู่
        /// </summary>
        public async Task<List<Outlet>> GetActiveOutletsAsync()
        {
            var outlets = new List<Outlet>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT Id, Name, PricePerHead, IsActive
                FROM Outlets
                WHERE IsActive = 1
                ORDER BY Name";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                outlets.Add(new Outlet
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    PricePerHead = reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                    IsActive = reader.GetBoolean(3)
                });
            }

            return outlets;
        }

        /// <summary>
        /// ดึงรายการ Kitchens ทั้งหมดที่ใช้งานอยู่
        /// </summary>
        public async Task<List<Kitchen>> GetActiveKitchensAsync()
        {
            var kitchens = new List<Kitchen>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT Id, Name, IsActive
                FROM Kitchens
                WHERE IsActive = 1
                ORDER BY Name";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                kitchens.Add(new Kitchen
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    IsActive = reader.GetBoolean(2)
                });
            }

            return kitchens;
        }

        /// <summary>
        /// ดึงรายการใบ Transfer ที่จบงานแล้ว (รองรับ filter ทั้ง Outlet และ Kitchen)
        /// </summary>
        public async Task<List<SelectableTransferViewModel>> GetAvailableTransfersAsync(
            string? searchTransferNo = null,
            int? outletId = null,
            int? kitchenId = null)
        {
            var transfers = new List<SelectableTransferViewModel>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
        SELECT 
            t.Id,
            t.TransferNo,
            t.CreatedDate,
            t.ExpectedPeople,
            t.ActualPeople,
            t.UsageDate,
            t.OutletId,
            O.Name AS OutletName,
            t.KitchenId,
            K.Name AS KitchenName,
            CASE WHEN ct.Id IS NULL OR ct.IsDeleted = 1 THEN 0 ELSE 1 END as IsAlreadyCombined,
            ISNULL((SELECT SUM(ti.InitialQuantity + ti.AdditionalQuantity - ISNULL(ti.ReturnedQuantity, 0)) 
                    FROM TransferItems ti 
                    WHERE ti.TransferId = t.Id), 0) as TotalQuantity,
            ISNULL((SELECT SUM(ROUND((ti.InitialQuantity + ti.AdditionalQuantity - ISNULL(ti.ReturnedQuantity, 0)) * ISNULL(ti.UnitPrice, 0), 4)) 
                    FROM TransferItems ti 
                    WHERE ti.TransferId = t.Id), 0) as TotalCost,
            ct.Id as BoundCombinedId,
            ct.CombinedNo as BoundCombinedNo
        FROM Transfer t
        LEFT JOIN Outlets O ON t.OutletId = O.Id
        LEFT JOIN Kitchens K ON t.KitchenId = K.Id
        LEFT JOIN CombinedTransferSource cts ON cts.TransferId = t.Id
        LEFT JOIN CombinedTransfer ct ON ct.Id = cts.CombinedTransferId
        WHERE t.Status = 'Completed'
            AND t.IsDeleted = 0
            AND (@SearchTransferNo IS NULL OR t.TransferNo LIKE '%' + @SearchTransferNo + '%')
            AND (@OutletId IS NULL OR t.OutletId = @OutletId)
            AND (@KitchenId IS NULL OR t.KitchenId = @KitchenId)
        ORDER BY t.CreatedDate DESC";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@SearchTransferNo", (object?)searchTransferNo ?? DBNull.Value);
            command.Parameters.AddWithValue("@OutletId", (object?)outletId ?? DBNull.Value);
            command.Parameters.AddWithValue("@KitchenId", (object?)kitchenId ?? DBNull.Value);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var transferId = reader.GetInt32(0);
                var transferNo = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var createdDate = reader.IsDBNull(2) ? DateTime.MinValue : reader.GetDateTime(2);
                var expectedPeople = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                var actualPeople = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
                var usageDateFromDb = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5);
                var outletIdFromDb = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6);
                var outletName = reader.IsDBNull(7) ? "-" : reader.GetString(7);
                var kitchenIdFromDb = reader.IsDBNull(8) ? (int?)null : reader.GetInt32(8);
                var kitchenName = reader.IsDBNull(9) ? "-" : reader.GetString(9);
                var isAlreadyCombined = !reader.IsDBNull(10) && reader.GetInt32(10) == 1;
                var totalQuantity = reader.IsDBNull(11) ? 0m : reader.GetDecimal(11);
                var totalCost = reader.IsDBNull(12) ? 0m : reader.GetDecimal(12);
                var boundCombinedId = reader.IsDBNull(13) ? (int?)null : reader.GetInt32(13);
                var boundCombinedNo = reader.IsDBNull(14) ? null : reader.GetString(14);

                decimal? outletPricePerHead = null;
                if (outletIdFromDb.HasValue)
                {
                    outletPricePerHead = await GetLatestOutletPriceAsync(outletIdFromDb.Value, connection);
                }

                decimal? costPerPerson = expectedPeople > 0 ? totalCost / expectedPeople : null;

                transfers.Add(new SelectableTransferViewModel
                {
                    Id = transferId,
                    TransferNo = transferNo,
                    CreatedDate = createdDate,
                    ExpectedPeople = expectedPeople,
                    ActualPeople = actualPeople,
                    UsageDate = usageDateFromDb,
                    OutletId = outletIdFromDb,
                    OutletName = outletName,
                    KitchenName = kitchenName,
                    CostPerPerson = costPerPerson,
                    TotalQuantity = totalQuantity,
                    TotalCost = totalCost,
                    IsAlreadyCombined = isAlreadyCombined,
                    BoundCombinedId = boundCombinedId,
                    BoundCombinedNo = boundCombinedNo,
                    OutletPricePerHead = outletPricePerHead
                });
            }

            return transfers;
        }

        /// <summary>
        /// จัดรูปแบบการแสดงรหัสใบ Transfer (ถ้ามากกว่า 3 ใบให้แสดงแบบย่อ)
        /// </summary>
        private string FormatTransferNosDisplay(List<string>? transferNos)
        {
            if (transferNos == null || transferNos.Count == 0) return "-";
            if (transferNos.Count <= 3) return string.Join(", ", transferNos);

            return $"{transferNos[0]}, {transferNos[1]} และอีก {transferNos.Count - 2} ใบ";
        }

        /// <summary>
        /// ดึง Transfer IDs ที่อยู่ในใบรวม
        /// </summary>
        public async Task<List<int>> GetTransferIdsByCombinedIdAsync(int combinedId)
        {
            var transferIds = new List<int>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT TransferId
                FROM CombinedTransferSource
                WHERE CombinedTransferId = @CombinedId";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@CombinedId", combinedId);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                transferIds.Add(reader.GetInt32(0));
            }

            return transferIds;
        }

        /// <summary>
        /// ดึงข้อมูล CombinedTransfer ทั้งหมดพร้อม DateTime สำหรับการกรองตามวันที่
        /// </summary>
        public async Task<List<CombinedTransferWithDate>> GetAllCombinedTransfersWithDateAsync()
        {
            var list = new List<CombinedTransferWithDate>();

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"
                SELECT Id, CombinedNo, CreatedDate, CreatedBy, IsDeleted
                FROM CombinedTransfer
                ORDER BY CreatedDate DESC";

            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                list.Add(new CombinedTransferWithDate
                {
                    Id = reader.GetInt32(0),
                    CombinedNo = reader.GetString(1),
                    CreatedDate = reader.GetDateTime(2),
                    CreatedBy = reader.IsDBNull(3) ? null : reader.GetString(3),
                    IsDeleted = reader.IsDBNull(4) ? false : reader.GetBoolean(4)
                });
            }

            return list;
        }
        // (inside CombinedTransferService class) Replace the GetUsageByCategoryAsync method body
        public async Task<List<OutletUsageReportItem>> GetUsageByCategoryAsync(DateTime startDate, DateTime endDate, int? outletId = null)
        {
            var results = new List<OutletUsageReportItem>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Precompute per-CombinedTransfer actual-people consistency:
            // - ConsistentActualPeople: value when every transfer in the combined group has a non-null ActualPeople and all equal.
            // - ActualInconsistent: 1 when ActualPeople values differ across transfers in the combined group.
            var sql = @"
WITH CombinedActual AS (
  SELECT
    ct.Id AS CombinedId,
    CASE 
      WHEN ct.CombinedCount = 3
           AND COUNT(*) = COUNT(t2.ActualPeople)            -- no NULL ActualPeople
           AND COUNT(DISTINCT t2.ActualPeople) = 1           -- all same actual
      THEN MIN(t2.ActualPeople)
      ELSE NULL
    END AS ConsistentActualPeople,
    CASE 
      WHEN ct.CombinedCount = 3
           AND COUNT(DISTINCT t2.ActualPeople) > 1
      THEN 1 ELSE 0
    END AS ActualInconsistent
  FROM CombinedTransfer ct
  JOIN Transfer t2 ON t2.CombinedTransferId = ct.Id
  WHERE t2.UsageDate IS NOT NULL
  GROUP BY ct.Id, ct.CombinedCount
)
SELECT
    CONVERT(date, t.UsageDate) AS UsageDate,
    t.OutletId,
    O.Name AS OutletName,
    p.Category,
    SUM(
        ISNULL(ti.InitialQuantity,0)
      + ISNULL(ti.AdditionalQuantity,0)
      - ISNULL(ti.ReturnedQuantity,0)
    ) AS TotalQuantity,
    SUM(
        ROUND(
            (ISNULL(ti.InitialQuantity,0) + ISNULL(ti.AdditionalQuantity,0) - ISNULL(ti.ReturnedQuantity,0))
            * ISNULL(ti.UnitPrice,0)
        , 4)
    ) AS TotalCost,
    MAX(ca.ConsistentActualPeople) AS ConsistentActualPeople,
    MAX(ca.ActualInconsistent) AS ActualInconsistent,
    MAX(ct.CombinedCostPerHead) AS CombinedCostPerHead
FROM Transfer t
    INNER JOIN CombinedTransfer ct ON ct.Id = t.CombinedTransferId
    LEFT JOIN CombinedActual ca ON ca.CombinedId = ct.Id
    INNER JOIN TransferItems ti ON ti.TransferId = t.Id
    INNER JOIN Products p ON p.Code = ti.ProductCode
    LEFT JOIN Outlets O ON t.OutletId = O.Id
WHERE t.UsageDate IS NOT NULL
  AND CONVERT(date, t.UsageDate) BETWEEN @StartDate AND @EndDate
  AND (@OutletId IS NULL OR t.OutletId = @OutletId)
GROUP BY
    CONVERT(date, t.UsageDate),
    t.OutletId,
    O.Name,
    p.Category
ORDER BY
    CONVERT(date, t.UsageDate),
    O.Name, p.Category;
";

            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@StartDate", startDate.Date);
            cmd.Parameters.AddWithValue("@EndDate", endDate.Date);
            cmd.Parameters.AddWithValue("@OutletId", outletId.HasValue ? (object)outletId.Value : DBNull.Value);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var item = new OutletUsageReportItem
                {
                    UsageDate = reader.IsDBNull(0) ? DateTime.MinValue : reader.GetDateTime(0),
                    OutletId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                    OutletName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Category = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    TotalQuantity = reader.IsDBNull(4) ? 0m : reader.GetDecimal(4),
                    TotalCost = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),

                    // new fields from query:
                    ConsistentActualPeople = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6),
                    ActualPeopleInconsistent = !reader.IsDBNull(7) && reader.GetInt32(7) == 1,

                    CombinedCostPerHead = reader.IsDBNull(8) ? (decimal?)null : reader.GetDecimal(8)
                };

                results.Add(item);
            }

            // compute percent of group (grouped by UsageDate+OutletName)
            var groups = results.GroupBy(r => (r.UsageDate.Date, r.OutletId, r.OutletName));
            foreach (var g in groups)
            {
                var totalGroupCost = g.Sum(x => x.TotalCost);
                foreach (var row in g)
                {
                    row.PercentOfGroup = totalGroupCost > 0 ? Math.Round(row.TotalCost / totalGroupCost * 100m, 4) : 0m;
                }
            }

            return results;
        }
        public async Task<List<OutletCostComparisonItem>> GetOutletCostComparisonAsync(DateTime startDate, DateTime endDate, int? outletId = null)
        {
            var results = new List<OutletCostComparisonItem>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
SELECT
    CONVERT(date, t.UsageDate) AS UsageDate,
    t.OutletId,
    O.Name AS OutletName,
    p.Name AS ProductName,   -- <- return product name
    p.Unit,
    SUM(ISNULL(ti.InitialQuantity,0)) AS EstimatedQuantity,
    SUM(ISNULL(ti.AdditionalQuantity,0)) AS AddedQuantity,
    SUM(ISNULL(ti.ReturnedQuantity,0)) AS ReturnedQuantity,
    SUM(ROUND(ISNULL(ti.InitialQuantity,0) * ISNULL(ti.UnitPrice,0),4)) AS EstimatedCost,
    SUM(ROUND((ISNULL(ti.InitialQuantity,0) + ISNULL(ti.AdditionalQuantity,0) - ISNULL(ti.ReturnedQuantity,0)) * ISNULL(ti.UnitPrice,0),4)) AS ActualCost,
    MAX(ct.TotalPeople) AS TotalPeople
FROM Transfer t
    INNER JOIN TransferItems ti ON ti.TransferId = t.Id
    INNER JOIN Products p ON p.Code = ti.ProductCode
    LEFT JOIN Outlets O ON t.OutletId = O.Id
    LEFT JOIN CombinedTransfer ct ON ct.Id = t.CombinedTransferId
WHERE t.UsageDate IS NOT NULL
  AND CONVERT(date, t.UsageDate) BETWEEN @StartDate AND @EndDate
  AND (@OutletId IS NULL OR t.OutletId = @OutletId)
GROUP BY
    CONVERT(date, t.UsageDate),
    t.OutletId,
    O.Name,
    p.Name, p.Unit
ORDER BY
    CONVERT(date, t.UsageDate),
    O.Name, p.Name;
";

            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@StartDate", startDate.Date);
            cmd.Parameters.AddWithValue("@EndDate", endDate.Date);
            cmd.Parameters.AddWithValue("@OutletId", outletId.HasValue ? (object)outletId.Value : DBNull.Value);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var usageDate = reader.IsDBNull(0) ? DateTime.MinValue : reader.GetDateTime(0);
                var outletIdDb = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
                var outletName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var productName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                var unit = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);

                var estQty = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5);
                var addedQty = reader.IsDBNull(6) ? 0m : reader.GetDecimal(6);
                var returnedQty = reader.IsDBNull(7) ? 0m : reader.GetDecimal(7);
                var estCost = reader.IsDBNull(8) ? 0m : reader.GetDecimal(8);
                var actualCost = reader.IsDBNull(9) ? 0m : reader.GetDecimal(9);
                var totalPeople = reader.IsDBNull(10) ? (int?)null : reader.GetInt32(10);

                var actualQty = estQty + addedQty - returnedQty;

                // Try derive unit price (fallback)
                decimal unitPrice = 0m;
                if (actualQty != 0m)
                    unitPrice = Math.Round(actualCost / actualQty, 4);
                else if (estQty != 0m)
                    unitPrice = Math.Round(estCost / estQty, 4);

                results.Add(new OutletCostComparisonItem
                {
                    UsageDate = usageDate,
                    OutletId = outletIdDb,
                    OutletName = outletName,
                    ProductName = productName,     // <- use ProductName
                    Category = productName,        // keep Category in sync for compatibility
                    Unit = unit,
                    UnitPrice = unitPrice,
                    EstimatedQuantity = estQty,
                    EstimatedCost = estCost,
                    AddedQuantity = addedQty,
                    ReturnedQuantity = returnedQty,
                    ActualQuantity = actualQty,
                    ActualCost = actualCost,
                    TotalPeople = totalPeople
                });
            }

            // compute percent of group by (UsageDate, Outlet)
            var groups = results.GroupBy(r => (r.UsageDate.Date, r.OutletId, r.OutletName));
            foreach (var g in groups)
            {
                var totalGroupCost = g.Sum(x => x.ActualCost);
                foreach (var row in g)
                {
                    row.PercentOfGroup = totalGroupCost > 0 ? Math.Round(row.ActualCost / totalGroupCost * 100m, 4) : 0m;
                }
            }

            return results;
        }
        /// <summary>
        /// ดึงต้นทุนต่อวัน (ตามใบรวม/transfer) สำหรับเดือน/ปีที่เลือก (สามารถกรอง outlet และ category ได้)
        /// คืนค่าเป็นรายการต่อวันต่อ outlet (ถ้ามีข้อมูล)
        /// </summary>
        // (replace the GetDailyCostsByABFAsync method implementation with the following)
        public async Task<List<OutletDailyCostItem>> GetDailyCostsByABFAsync(int year, int month, int? outletId = null, string? category = null)
        {
            var results = new List<OutletDailyCostItem>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
SELECT
    CONVERT(date, t.UsageDate) AS UsageDate,
    t.OutletId,
    O.Name AS OutletName,
    SUM(ROUND((ISNULL(ti.InitialQuantity,0) + ISNULL(ti.AdditionalQuantity,0) - ISNULL(ti.ReturnedQuantity,0)) * ISNULL(ti.UnitPrice,0), 4)) AS TotalCost,

    /* ExpectedPeople: per-combined logic — use representative per combined */
    (
      SELECT ISNULL(SUM(CASE WHEN per_combined.cnt = 1 THEN per_combined.val ELSE 0 END), 0)
      FROM (
        SELECT
            t2.CombinedTransferId,
            COUNT(DISTINCT ISNULL(t2.ExpectedPeople, -999999)) AS cnt,
            MAX(ISNULL(t2.ExpectedPeople, 0)) AS val
        FROM Transfer t2
        INNER JOIN TransferItems ti2 ON ti2.TransferId = t2.Id
        INNER JOIN Products p2 ON p2.Code = ti2.ProductCode
        WHERE t2.CombinedTransferId IS NOT NULL
          AND CONVERT(date, t2.UsageDate) = CONVERT(date, t.UsageDate)
          AND t2.OutletId = t.OutletId
          AND (@Category IS NULL OR p2.Category = @Category)
        GROUP BY t2.CombinedTransferId
      ) AS per_combined
    ) AS ExpectedPeople,

    /* ActualPeople: per-combined consistency check */
    (
      SELECT ISNULL(SUM(CASE WHEN per_combined2.cnt = 1 THEN per_combined2.val ELSE 0 END), 0)
      FROM (
        SELECT
            t3.CombinedTransferId,
            COUNT(DISTINCT ISNULL(t3.ActualPeople, -999999)) AS cnt,
            MAX(ISNULL(t3.ActualPeople, 0)) AS val
        FROM Transfer t3
        INNER JOIN TransferItems ti3 ON ti3.TransferId = t3.Id
        INNER JOIN Products p3 ON p3.Code = ti3.ProductCode
        WHERE t3.CombinedTransferId IS NOT NULL
          AND CONVERT(date, t3.UsageDate) = CONVERT(date, t.UsageDate)
          AND t3.OutletId = t.OutletId
          AND (@Category IS NULL OR p3.Category = @Category)
        GROUP BY t3.CombinedTransferId
      ) AS per_combined2
    ) AS ActualPeople,

    -- Representative hidden percent for date+outlet (renderer will check consistency across outlets)
    MAX(ISNULL(t.HiddenCostPercentage, 0)) AS HiddenCostPercentage,

    -- Meat quantity: product Category/Name contains 'เนื้อ' OR 'แฮม' OR 'ไส้กรอก' (count ALL units regardless)
    SUM(
      CASE
        WHEN (p.Category IS NOT NULL AND (p.Category LIKE N'%เนื้อ%' OR p.Category LIKE N'%แฮม%' OR p.Category LIKE N'%ไส้กรอก%'))
          OR (p.Name IS NOT NULL AND (p.Name LIKE N'%แฮม%' OR p.Name LIKE N'%ไส้กรอก%' OR p.Name LIKE N'%เนื้อ%'))
        THEN (ISNULL(ti.InitialQuantity,0) + ISNULL(ti.AdditionalQuantity,0) - ISNULL(ti.ReturnedQuantity,0))
        ELSE 0
      END
    ) AS MeatQty,

    -- Egg quantity: product Category contains 'ไข่' (count ALL units regardless)
    SUM(
      CASE
        WHEN p.Category IS NOT NULL AND p.Category LIKE N'%ไข่%'
        THEN (ISNULL(ti.InitialQuantity,0) + ISNULL(ti.AdditionalQuantity,0) - ISNULL(ti.ReturnedQuantity,0))
        ELSE 0
      END
    ) AS EggQty,

    SUM(CASE WHEN @Category IS NULL OR p.Category = @Category THEN (ISNULL(ti.InitialQuantity,0) + ISNULL(ti.AdditionalQuantity,0) - ISNULL(ti.ReturnedQuantity,0)) ELSE 0 END) AS CategoryQty,
    MAX(p.Unit) AS Unit
FROM Transfer t
    LEFT JOIN TransferItems ti ON ti.TransferId = t.Id
    LEFT JOIN Products p ON p.Code = ti.ProductCode
    LEFT JOIN Outlets O ON t.OutletId = O.Id
    LEFT JOIN CombinedTransfer ct ON ct.Id = t.CombinedTransferId
WHERE t.UsageDate IS NOT NULL
  AND YEAR(t.UsageDate) = @Year AND MONTH(t.UsageDate) = @Month
  AND (@OutletId IS NULL OR t.OutletId = @OutletId)
GROUP BY
    CONVERT(date, t.UsageDate),
    t.OutletId,
    O.Name
ORDER BY
    CONVERT(date, t.UsageDate)";

            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Year", year);
            cmd.Parameters.AddWithValue("@Month", month);
            cmd.Parameters.AddWithValue("@OutletId", outletId.HasValue ? (object)outletId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Category", string.IsNullOrWhiteSpace(category) ? (object)DBNull.Value : category);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var item = new OutletDailyCostItem
                {
                    UsageDate = reader.IsDBNull(0) ? DateTime.MinValue : reader.GetDateTime(0),
                    OutletId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                    OutletName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    TotalCost = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3),
                    ExpectedPeople = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    ActualPeople = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    HiddenCostPercentage = reader.IsDBNull(6) ? (decimal?)null : reader.GetDecimal(6),
                    MeatQuantity = reader.IsDBNull(7) ? 0m : reader.GetDecimal(7),
                    EggQuantity = reader.IsDBNull(8) ? 0m : reader.GetDecimal(8),
                    CategoryQuantity = reader.IsDBNull(9) ? 0m : reader.GetDecimal(9),
                    Unit = reader.IsDBNull(10) ? null : reader.GetString(10)
                };

                results.Add(item);
            }

            return results;
        }
    }
}