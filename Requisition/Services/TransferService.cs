using Microsoft.Data.SqlClient;
using Requisition.Helpers;
using Requisition.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Requisition.Services
{
    public class TransferService 
    {
        private readonly string _connectionString;
        private readonly HiddenCostService _hiddenCostService; // ✅ เพิ่ม

        public TransferService()
        {
            _connectionString = ConfigurationHelper.GetConnectionString();
            _hiddenCostService = new HiddenCostService(); // ✅ เพิ่ม
        }

        #region Transfer CRUD

        /// <summary>
        /// ดึงรายการใบโอนทั้งหมด (พร้อมจำนวนรายการ) — now includes OutletId and OutletName
        /// </summary>
        public async Task<List<Models.Transfer>> GetAllTransfersAsync()
        {
            var transfers = new List<Models.Transfer>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string query = @"
SELECT T.Id, T.TransferNo, T.CreatedDate, T.ExpectedPeople, T.ActualPeople, T.Budget, 
       T.UsageDate, T.CreatedBy, T.Status, T.Notes, T.CompletedDate, T.LastModifiedDate, 
       T.IsDeleted, T.DeletedDate, T.DeletedBy, T.DeletedReason,
       T.OutletId, O.Name AS OutletName,
       T.OutletPricePerHeadAtSave, T.OutletPricePerHeadSavedAt,
       T.KitchenId, K.Name AS KitchenName,
       T.HiddenCostPercentage
FROM Transfer T
LEFT JOIN Outlets O ON T.OutletId = O.Id
LEFT JOIN Kitchens K ON T.KitchenId = K.Id
WHERE T.IsDeleted = 0
ORDER BY T.CreatedDate DESC";

            using var command = new SqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            var tempList = new List<Models.Transfer>();

            while (await reader.ReadAsync())
            {
                var transfer = new Models.Transfer
                {
                    Id = reader.GetInt32(0),
                    TransferNo = reader.GetString(1),
                    CreatedDate = reader.GetDateTime(2),
                    ExpectedPeople = reader.GetInt32(3),
                    ActualPeople = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    Budget = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
                    UsageDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    CreatedBy = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Status = Enum.Parse<TransferStatus>(reader.GetString(8)),
                    Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
                    CompletedDate = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                    LastModifiedDate = reader.IsDBNull(11) ? DateTime.MinValue : reader.GetDateTime(11),
                    IsDeleted = reader.IsDBNull(12) ? false : reader.GetBoolean(12),
                    DeletedDate = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
                    DeletedBy = reader.IsDBNull(14) ? null : reader.GetString(14),
                    DeletedReason = reader.IsDBNull(15) ? null : reader.GetString(15),
                    OutletId = reader.IsDBNull(16) ? (int?)null : reader.GetInt32(16),
                    OutletName = reader.IsDBNull(17) ? null : reader.GetString(17),
                    OutletPricePerHeadAtSave = reader.IsDBNull(18) ? null : reader.GetDecimal(18),
                    OutletPricePerHeadSavedAt = reader.IsDBNull(19) ? null : reader.GetDateTime(19),
                    KitchenId = reader.IsDBNull(20) ? (int?)null : reader.GetInt32(20),
                    KitchenName = reader.IsDBNull(21) ? null : reader.GetString(21),
                    HiddenCostPercentage = reader.IsDBNull(22) ? null : reader.GetDecimal(22) // added
                };

                tempList.Add(transfer);
            }
            
            reader.Close();

            foreach (var transfer in tempList)
            {
                transfer.Items = await GetTransferItemsAsync(connection, transfer.Id);
                transfers.Add(transfer);
            }

            return transfers;
        }

        /// <summary>
        /// ดึงข้อมูลใบโอนตาม Id พร้อมรายการสินค้า
        /// </summary>
        public async Task<Models.Transfer?> GetTransferByIdAsync(int id)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
SELECT T.Id, T.TransferNo, T.CreatedDate, T.ExpectedPeople, T.ActualPeople, T.Budget, 
       T.UsageDate, T.CreatedBy, T.Status, T.Notes, 
       T.CompletedDate, T.LastModifiedDate,
       T.IsDeleted, T.DeletedDate, T.DeletedBy, T.DeletedReason,
       T.OutletId, O.Name AS OutletName,
       T.OutletPricePerHeadAtSave, T.OutletPricePerHeadSavedAt,
       T.KitchenId, K.Name AS KitchenName,
       T.HiddenCostPercentage
FROM Transfer T
LEFT JOIN Outlets O ON T.OutletId = O.Id
LEFT JOIN Kitchens K ON T.KitchenId = K.Id
WHERE T.Id = @Id";

    using var command = new SqlCommand(query, connection);
    command.Parameters.AddWithValue("@Id", id);

    using var reader = await command.ExecuteReaderAsync();

    Models.Transfer? transfer = null;

    if (await reader.ReadAsync())
    {
        transfer = new Models.Transfer
        {
            Id = reader.GetInt32(0),
            TransferNo = reader.GetString(1),
            CreatedDate = reader.GetDateTime(2),
            ExpectedPeople = reader.GetInt32(3),
            ActualPeople = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            Budget = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
            UsageDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            CreatedBy = reader.IsDBNull(7) ? null : reader.GetString(7),
            Status = Enum.Parse<TransferStatus>(reader.GetString(8)),
            Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
            CompletedDate = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
            LastModifiedDate = reader.IsDBNull(11) ? DateTime.MinValue : reader.GetDateTime(11),
            IsDeleted = reader.IsDBNull(12) ? false : reader.GetBoolean(12),
            DeletedDate = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
            DeletedBy = reader.IsDBNull(14) ? null : reader.GetString(14),
            DeletedReason = reader.IsDBNull(15) ? null : reader.GetString(15),
            OutletId = reader.IsDBNull(16) ? (int?)null : reader.GetInt32(16),
            OutletName = reader.IsDBNull(17) ? null : reader.GetString(17),
            OutletPricePerHeadAtSave = reader.IsDBNull(18) ? null : reader.GetDecimal(18),
            OutletPricePerHeadSavedAt = reader.IsDBNull(19) ? null : reader.GetDateTime(19),
            KitchenId = reader.IsDBNull(20) ? (int?)null : reader.GetInt32(20),
            KitchenName = reader.IsDBNull(21) ? null : reader.GetString(21),
            HiddenCostPercentage = reader.IsDBNull(22) ? null : reader.GetDecimal(22) // ✅ เพิ่ม
        };
    }

    reader.Close();

    if (transfer != null)
    {
        transfer.Items = await GetTransferItemsAsync(connection, transfer.Id);
    }

    return transfer;
}

        /// <summary>
        /// สร้างใบโอนใหม่ - ✅ ต้องระบุทั้ง OutletId และ KitchenId และดึงค่า HiddenCost
        /// </summary>
        public async Task<Models.Transfer?> CreateTransferAsync(
            int expectedPeople,
            int outletId,
            int kitchenId, // ✅ เพิ่ม parameter
            decimal budget = 0,
            DateTime? usageDate = null, 
            string? createdBy = null, 
            string? notes = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                if (expectedPeople < 1)
                    throw new ArgumentException("จำนวนคนต้องมากกว่า 0");
                
                string transferNo = await GenerateTransferNoAsync(connection, transaction);

                // ✅ ดึงค่าต้นทุนแฝงปัจจุบัน
                decimal hiddenCostPercentage = await _hiddenCostService.GetCurrentPercentageAsync();

                // ✅ เพิ่ม HiddenCostPercentage ใน INSERT
                var insertQuery = @"
                    INSERT INTO Transfer (
                        TransferNo, CreatedDate, ExpectedPeople, Budget, 
                        UsageDate, CreatedBy, Status, Notes, LastModifiedDate, 
                        OutletId, KitchenId, HiddenCostPercentage
                    )
                    OUTPUT INSERTED.Id
                    VALUES (
                        @TransferNo, GETDATE(), @ExpectedPeople, @Budget, 
                        @UsageDate, @CreatedBy, @Status, @Notes, GETDATE(), 
                        @OutletId, @KitchenId, @HiddenCostPercentage
                    )";

                int transferId;
                using (var command = new SqlCommand(insertQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@TransferNo", transferNo);
                    command.Parameters.AddWithValue("@ExpectedPeople", expectedPeople);
                    command.Parameters.AddWithValue("@Budget", budget);
                    command.Parameters.AddWithValue("@UsageDate", (object?)usageDate ?? DBNull.Value);
                    command.Parameters.AddWithValue("@CreatedBy", (object?)createdBy ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Status", TransferStatus.Draft.ToString());
                    command.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
                    command.Parameters.AddWithValue("@OutletId", outletId);
                    command.Parameters.AddWithValue("@KitchenId", kitchenId);
                    command.Parameters.AddWithValue("@HiddenCostPercentage", hiddenCostPercentage); // ✅ เพิ่ม

                    var scalar = await command.ExecuteScalarAsync();
                    if (scalar == null || scalar == DBNull.Value)
                    {
                        transaction.Rollback();
                        throw new InvalidOperationException("Failed to create transfer");
                    }

                    transferId = Convert.ToInt32(scalar);
                }

                // ✅ เพิ่ม Kitchen name ใน History
                string? outletName = null;
                string? kitchenName = null;
                
                using (var nameCmd = new SqlCommand("SELECT Name FROM Outlets WHERE Id = @Id", connection, transaction))
                {
                    nameCmd.Parameters.AddWithValue("@Id", outletId);
                    var nameObj = await nameCmd.ExecuteScalarAsync();
                    outletName = nameObj == null || nameObj == DBNull.Value ? null : nameObj.ToString();
                }

                using (var kitchenCmd = new SqlCommand("SELECT Name FROM Kitchens WHERE Id = @Id", connection, transaction))
                {
                    kitchenCmd.Parameters.AddWithValue("@Id", kitchenId);
                    var kitchenObj = await kitchenCmd.ExecuteScalarAsync();
                    kitchenName = kitchenObj == null || kitchenObj == DBNull.Value ? null : kitchenObj.ToString();
                }

                var outletDisplay = string.IsNullOrWhiteSpace(outletName) ? $"#{outletId}" : outletName;
                var kitchenDisplay = string.IsNullOrWhiteSpace(kitchenName) ? $"#{kitchenId}" : kitchenName;
                
                var description = $"สร้างใบโอนใหม่ - จำนวนคน: {expectedPeople} - Outlet: {outletDisplay} - ห้องครัว: {kitchenDisplay} - ต้นทุนแฝง: {hiddenCostPercentage:0}%";

                await AddHistoryAsync(connection, transaction, transferId, "Created",
                    description, createdBy, null, null);

                transaction.Commit();
                return await GetTransferByIdAsync(transferId);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        /// <summary>
        /// อัพเดทข้อมูลใบTransfer (รองรับการแก้ไข ActualPeople แม้สถานะเป็น Completed)
        /// </summary>
        public async Task<bool> UpdateTransferAsync(Models.Transfer transfer, string? modifiedBy = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                var current = await GetTransferByIdForTransactionAsync(connection, transaction, transfer.Id);
                if (current == null)
                    return false;

                // ✅ แก้ไข: อนุญาตให้ update ActualPeople แม้สถานะจะเป็น Completed
                // แต่ถ้าเป็น Completed จะ update เฉพาะ ActualPeople, Notes
                bool isCompletedUpdate = current.Status == TransferStatus.Completed;

                var oldValues = JsonSerializer.Serialize(current);

                if (isCompletedUpdate)
                {
                    // ✅ สำหรับสถานะ Completed: อัปเดต ActualPeople, ExpectedPeople, Outlet, Kitchen, วันที่ใช้, Notes และ HiddenCostPercentage
                    var updateQuery = @"
        UPDATE Transfer
        SET ActualPeople = @ActualPeople,
            ExpectedPeople = @ExpectedPeople,
            OutletId = @OutletId,
            KitchenId = @KitchenId,
            UsageDate = @UsageDate,
            Notes = @Notes,
            HiddenCostPercentage = @HiddenCostPercentage,
            LastModifiedDate = GETDATE()
        WHERE Id = @Id";

                    using (var command = new SqlCommand(updateQuery, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@Id", transfer.Id);
                        command.Parameters.AddWithValue("@ActualPeople", (object?)transfer.ActualPeople ?? DBNull.Value);
                        command.Parameters.AddWithValue("@ExpectedPeople", transfer.ExpectedPeople);
                        command.Parameters.AddWithValue("@OutletId", (object?)transfer.OutletId ?? DBNull.Value);
                        command.Parameters.AddWithValue("@KitchenId", (object?)transfer.KitchenId ?? DBNull.Value);
                        command.Parameters.AddWithValue("@UsageDate", (object?)transfer.UsageDate ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Notes", (object?)transfer.Notes ?? DBNull.Value);
                        command.Parameters.AddWithValue("@HiddenCostPercentage", (object?)transfer.HiddenCostPercentage ?? DBNull.Value);

                        await command.ExecuteNonQueryAsync();
                    }

                    // บันทึก History
                    List<string> changes = new List<string>();

                    if (current.ActualPeople != transfer.ActualPeople)
                    {
                        changes.Add($"จำนวนคนจริง: {current.ActualPeople ?? 0} → {transfer.ActualPeople ?? 0}");
                    }
                    
                    if (current.ExpectedPeople != transfer.ExpectedPeople)
                    {
                        changes.Add($"จำนวนคนที่คาดหวัง: {current.ExpectedPeople} → {transfer.ExpectedPeople}");
                    }
                    
                    // ✅ เพิ่มการตรวจสอบ Outlet
                    if (current.OutletId != transfer.OutletId)
                    {
                        string oldOutlet = current.OutletName ?? (current.OutletId.HasValue ? $"#{current.OutletId}" : "ไม่ระบุ");
                        string newOutlet = transfer.OutletName ?? (transfer.OutletId.HasValue ? $"#{transfer.OutletId}" : "ไม่ระบุ");
                        changes.Add($"Outlet: {oldOutlet} → {newOutlet}");
                    }
                    
                    // ✅ เพิ่มการตรวจสอบ Kitchen
                    if (current.KitchenId != transfer.KitchenId)
                    {
                        string oldKitchen = current.KitchenName ?? (current.KitchenId.HasValue ? $"#{current.KitchenId}" : "ไม่ระบุ");
                        string newKitchen = transfer.KitchenName ?? (transfer.KitchenId.HasValue ? $"#{transfer.KitchenId}" : "ไม่ระบุ");
                        changes.Add($"ห้องครัว: {oldKitchen} → {newKitchen}");
                    }
                    
                    // ✅ เพิ่มการตรวจสอบวันที่ใช้
                    if (current.UsageDate?.Date != transfer.UsageDate?.Date)
                    {
                        string oldDate = current.UsageDate?.ToString("dd/MM/yyyy") ?? "ไม่ระบุ";
                        string newDate = transfer.UsageDate?.ToString("dd/MM/yyyy") ?? "ไม่ระบุ";
                        changes.Add($"วันที่ใช้: {oldDate} → {newDate}");
                    }
                    
                    // ✅ เพิ่มการตรวจสอบ HiddenCostPercentage
                    if ((current.HiddenCostPercentage ?? 0m) != (transfer.HiddenCostPercentage ?? 0m))
                    {
                        changes.Add($"ต้นทุนแฝง: {(current.HiddenCostPercentage ?? 0m):N4}% → {(transfer.HiddenCostPercentage ?? 0m):N4}%");
                    }
                    
                    if (!string.Equals(current.Notes ?? "", transfer.Notes ?? "", StringComparison.Ordinal))
                    {
                        changes.Add($"หมายเหตุ: \"{current.Notes ?? ""}\" → \"{transfer.Notes ?? ""}\"");
                    }

                    string description = changes.Count > 0 
                        ? $"แก้ไขข้อมูล (Completed):\n- {string.Join("\n- ", changes)}"
                        : "แก้ไขข้อมูล (ไม่มีการเปลี่ยนแปลง)";

                    await AddHistoryAsync(connection, transaction, transfer.Id, "UpdatedCompleted",
                        description, modifiedBy, oldValues, JsonSerializer.Serialize(new {
                            ActualPeople = transfer.ActualPeople,
                            ExpectedPeople = transfer.ExpectedPeople,
                            OutletId = transfer.OutletId,
                            OutletName = transfer.OutletName,
                            KitchenId = transfer.KitchenId,
                            KitchenName = transfer.KitchenName,
                            UsageDate = transfer.UsageDate,
                            Notes = transfer.Notes,
                            HiddenCostPercentage = transfer.HiddenCostPercentage
                        }));

                    transaction.Commit();
                    return true;
                }

                // ✅ สำหรับสถานะอื่นๆ (Draft, InProgress): อัปเดตข้อมูลทั่วไป (รวม HiddenCostPercentage)
                var newStatus = transfer.Status;

                // Get item count
                var itemCountQuery = "SELECT COUNT(*) FROM TransferItems WHERE TransferId = @Id";
                int itemCount = 0;
                using (var countCmd = new SqlCommand(itemCountQuery, connection, transaction))
                {
                    countCmd.Parameters.AddWithValue("@Id", transfer.Id);
                    var result = await countCmd.ExecuteScalarAsync();
                    itemCount = result != null ? Convert.ToInt32(result) : 0;
                }

                if (current.Status == TransferStatus.Draft && itemCount > 0)
                {
                    newStatus = TransferStatus.InProgress;
                }

                var updateQueryNormal = @"
            UPDATE Transfer
            SET Status = @Status,
                ExpectedPeople = @ExpectedPeople,
                UsageDate = @UsageDate,
                OutletId = @OutletId,
                KitchenId = @KitchenId,
                Notes = @Notes,
                HiddenCostPercentage = @HiddenCostPercentage,
                OutletPricePerHeadAtSave = @OutletPricePerHeadAtSave,
                OutletPricePerHeadSavedAt = @OutletPricePerHeadSavedAt,
                LastModifiedDate = GETDATE()
            WHERE Id = @Id";

                using (var command = new SqlCommand(updateQueryNormal, connection, transaction))
                {
                    command.Parameters.AddWithValue("@Id", transfer.Id);
                    command.Parameters.AddWithValue("@Status", newStatus.ToString());
                    command.Parameters.AddWithValue("@ExpectedPeople", transfer.ExpectedPeople);
                    command.Parameters.AddWithValue("@UsageDate", (object?)transfer.UsageDate ?? DBNull.Value);
                    command.Parameters.AddWithValue("@OutletId", (object?)transfer.OutletId ?? DBNull.Value);
                    command.Parameters.AddWithValue("@KitchenId", (object?)transfer.KitchenId ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Notes", (object?)transfer.Notes ?? DBNull.Value);
                    command.Parameters.AddWithValue("@HiddenCostPercentage", (object?)transfer.HiddenCostPercentage ?? DBNull.Value);
                    command.Parameters.AddWithValue("@OutletPricePerHeadAtSave", (object?)transfer.OutletPricePerHeadAtSave ?? DBNull.Value);
                    command.Parameters.AddWithValue("@OutletPricePerHeadSavedAt", (object?)transfer.OutletPricePerHeadSavedAt ?? DBNull.Value);

                    await command.ExecuteNonQueryAsync();
                }

                // ตรวจสอบการเปลี่ยนแปลง
                List<string> normalChanges = new List<string>();
                
                if (current.OutletId != transfer.OutletId)
                {
                    string oldOutlet = current.OutletName ?? (current.OutletId.HasValue ? $"#{current.OutletId}" : "ไม่ระบุ");
                    string newOutlet = transfer.OutletName ?? (transfer.OutletId.HasValue ? $"#{transfer.OutletId}" : "ไม่ระบุ");
                    normalChanges.Add($"Outlet: {oldOutlet} → {newOutlet}");
                }

                if (current.KitchenId != transfer.KitchenId)
                {
                    string oldKitchen = current.KitchenName ?? (current.KitchenId.HasValue ? $"#{current.KitchenId}" : "ไม่ระบุ");
                    string newKitchen = transfer.KitchenName ?? (transfer.KitchenId.HasValue ? $"#{transfer.KitchenId}" : "ไม่ระบุ");
                    normalChanges.Add($"ห้องครัว: {oldKitchen} → {newKitchen}");
                }

                // ✅ Add HiddenCostPercentage to normal changes detection
                if ((current.HiddenCostPercentage ?? 0m) != (transfer.HiddenCostPercentage ?? 0m))
                {
                    normalChanges.Add($"ต้นทุนแฝง: {(current.HiddenCostPercentage ?? 0m):N4}% → {(transfer.HiddenCostPercentage ?? 0m):N4}%");
                }

                string normalDescription = newStatus != current.Status
                    ? $"แก้ไขข้อมูลใบTransfer และเปลี่ยนสถานะจาก {current.StatusText} → {GetStatusText(newStatus)}"
                    : "แก้ไขข้อมูลใบTransfer";

                if (normalChanges.Count > 0)
                {
                    normalDescription += "\n" + string.Join("\n", normalChanges);
                }

                await AddHistoryAsync(connection, transaction, transfer.Id, "UpdatedItem",
                    normalDescription, modifiedBy, oldValues, JsonSerializer.Serialize(new {
                        Status = newStatus.ToString(),
                        ExpectedPeople = transfer.ExpectedPeople,
                        UsageDate = transfer.UsageDate,
                        OutletId = transfer.OutletId,
                        KitchenId = transfer.KitchenId,
                        Notes = transfer.Notes,
                        HiddenCostPercentage = transfer.HiddenCostPercentage,
                        OutletPricePerHeadAtSave = transfer.OutletPricePerHeadAtSave,
                        OutletPricePerHeadSavedAt = transfer.OutletPricePerHeadSavedAt
                    }));

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
        /// ⚠️ แก้ไข: Soft Delete - ไม่ลบจริง แต่ทำเครื่องหมายว่าถูกลบ
        /// </summary>
        public async Task<bool> DeleteTransferAsync(int transferId, string? deletedBy = null, string? reason = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                var transfer = await GetTransferByIdForTransactionAsync(connection, transaction, transferId);

                if (transfer == null)
                    return false;

                // ตรวจสอบว่าสามารถลบได้หรือไม่
                if (transfer.Status != TransferStatus.Draft)
                {
                    throw new InvalidOperationException("สามารถลบได้เฉพาะใบTransferที่เป็นแบบร่างเท่านั้น");
                }

                if (transfer.IsDeleted)
                {
                    throw new InvalidOperationException("ใบTransferนี้ถูกลบไปแล้ว");
                }

                // ⚠️ Soft Delete: อัปเดตสถานะแทนการลบ
                var updateQuery = @"
                    UPDATE Transfer
                    SET IsDeleted = 1,
                        DeletedDate = GETDATE(),
                        DeletedBy = @DeletedBy,
                        DeletedReason = @DeletedReason,
                        LastModifiedDate = GETDATE()
                    WHERE Id = @Id";

                using (var command = new SqlCommand(updateQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@Id", transferId);
                    command.Parameters.AddWithValue("@DeletedBy", (object?)deletedBy ?? DBNull.Value);
                    command.Parameters.AddWithValue("@DeletedReason", (object?)reason ?? DBNull.Value);

                    int rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected == 0)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }

                // บันทึก History
                await AddHistoryAsync(connection, transaction, transferId, "Deleted",
                    $"ลบใบTransfer - เหตุผล: {reason ?? "ไม่ระบุ"}", deletedBy, null, null);

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        #endregion

        #region Requisition Items

        /// <summary>
        /// เพิ่มรายการสินค้าในใบTransfer
        /// </summary>
        public async Task<bool> AddItemAsync(int transferId, TransferItem item, string? modifiedBy = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                var transfer = await GetTransferByIdForTransactionAsync(connection, transaction, transferId);
                // CHANGE HERE
                if (transfer == null || transfer.IsDeleted || transfer.Status == TransferStatus.Completed)
                    return false;

                if (item.InitialQuantity <= 0)
                    throw new InvalidOperationException("จำนวนเบิกต้องมากกว่า 0");

                // Insert Item พร้อม UnitCost
                var insertQuery = @"
                    INSERT INTO TransferItems 
                    (TransferId, ProductCode, ProductName, InitialQuantity, 
                     AdditionalQuantity, ReturnedQuantity, Unit, Notes, UnitPrice, PriceDate)
                    VALUES (@TransferId, @ProductCode, @ProductName, @InitialQuantity, 
                            0, NULL, @Unit, @Notes, @UnitPrice, @PriceDate)";

                using (var command = new SqlCommand(insertQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@TransferId", transferId);
                    command.Parameters.AddWithValue("@ProductCode", item.ProductCode);
                    command.Parameters.AddWithValue("@ProductName", item.ProductName);
                    command.Parameters.AddWithValue("@InitialQuantity", item.InitialQuantity);
                    command.Parameters.AddWithValue("@Unit", (object?)item.Unit ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Notes", (object?)item.Notes ?? DBNull.Value);
                    command.Parameters.AddWithValue("@UnitPrice", (object?)item.UnitPrice ?? DBNull.Value);
                    command.Parameters.AddWithValue("@PriceDate", (object?)item.PriceDate ?? DBNull.Value);

                    await command.ExecuteNonQueryAsync();
                }

                if (transfer.Status == TransferStatus.Draft)
                {
                    await UpdateTransferStatusAsync(connection, transaction, transferId, TransferStatus.InProgress);
                }

                // Detailed description
                string unitPriceText = item.UnitPrice.HasValue ? $"{item.UnitPrice.Value:N4} ฿" : "ไม่ระบุราคา";
                string priceDateText = item.PriceDate.HasValue ? item.PriceDate.Value.ToString("dd/MM/yyyy") : "ไม่ระบุวันที่ราคา";
                string description = $"เพิ่มสินค้า: {item.ProductName} ({item.ProductCode}) จำนวน {item.InitialQuantity} {item.Unit} | ราคาต่อหน่วย: {unitPriceText} | วันที่ราคา: {priceDateText}";

                await AddHistoryAsync(connection, transaction, transferId, "AddedItem",
                    description, modifiedBy, null, JsonSerializer.Serialize(item));

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
        /// เบิกเพิ่ม (เพิ่มจำนวน AdditionalQuantity)
        /// </summary>
        public async Task<bool> AddMoreQuantityAsync(int itemId, decimal additionalQuantity, string? modifiedBy = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                // ดึงข้อมูล Item และตรวจสอบ
                var item = await GetItemByIdForTransactionAsync(connection, transaction, itemId);
                if (item == null)
                    return false;

                var transfer = await GetTransferByIdForTransactionAsync(connection, transaction, item.TransferId);
                if (transfer == null)
                    return false;

                // ⚠️ เปลี่ยนเงื่อนไข: อนุญาตให้เพิ่มเมื่อใบTransferยังไม่จบงานและไม่ถูกลบ
                if (transfer.IsDeleted || transfer.Status == TransferStatus.Completed)
                    return false;

                // Validate ด้วย method ใหม่จาก Model
                if (!item.CanAddMoreQuantity(additionalQuantity, out string? errorMessage))
                    throw new InvalidOperationException(errorMessage);

                decimal oldAdditional = item.AdditionalQuantity;
                decimal newAdditional = oldAdditional + additionalQuantity;

                // Update AdditionalQuantity
                var updateQuery = @"
            UPDATE TransferItems 
            SET AdditionalQuantity = @NewAdditional
            WHERE Id = @Id";

                using (var command = new SqlCommand(updateQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@Id", itemId);
                    command.Parameters.AddWithValue("@NewAdditional", newAdditional);

                    await command.ExecuteNonQueryAsync();
                }

                // Detailed description
                string description = $"เบิกเพิ่ม: {item.ProductName} ({item.ProductCode}) เพิ่ม {additionalQuantity} {item.Unit} → รวม {newAdditional} {item.Unit}";

                // Add History
                await AddHistoryAsync(connection, transaction, item.TransferId, "AddedMore",
                    description, modifiedBy, $"{{\"AdditionalQuantity\": {oldAdditional}}}", $"{{\"AdditionalQuantity\": {newAdditional}}}");

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
        /// แก้ไขรายการสินค้า (เฉพาะจำนวนและหมายเหตุ)
        /// </summary>
        public async Task<bool> UpdateItemAsync(TransferItem item, string? modifiedBy = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                // ตรวจสอบ
                var transfer = await GetTransferByIdForTransactionAsync(connection, transaction, item.TransferId);
                if (transfer == null || transfer.IsDeleted || transfer.Status == TransferStatus.Completed)
                    return false;

                var oldItem = await GetItemByIdForTransactionAsync(connection, transaction, item.Id);
                if (oldItem == null)
                    return false;

                // Validate
                if (item.InitialQuantity <= 0)
                    throw new InvalidOperationException("จำนวนเบิกต้องมากกว่า 0");

                // Update
                var updateQuery = @"
                    UPDATE TransferItems 
                    SET InitialQuantity = @InitialQuantity,
                        Notes = @Notes
                    WHERE Id = @Id";

                using (var command = new SqlCommand(updateQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@Id", item.Id);
                    command.Parameters.AddWithValue("@InitialQuantity", item.InitialQuantity);
                    command.Parameters.AddWithValue("@Notes", (object?)item.Notes ?? DBNull.Value);

                    await command.ExecuteNonQueryAsync();
                }

                // Detailed description comparing old -> new
                decimal oldQty = oldItem.InitialQuantity;
                decimal newQty = item.InitialQuantity;
                string description = $"แก้ไขสินค้า: {item.ProductName} ({item.ProductCode}) จำนวน {oldQty} → {newQty} {item.Unit}";

                // Add History
                await AddHistoryAsync(connection, transaction, item.TransferId, "UpdatedItem",
                    description, modifiedBy, JsonSerializer.Serialize(oldItem), JsonSerializer.Serialize(item));

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
        /// ลบรายการสินค้า
        /// </summary>
        public async Task<bool> RemoveItemAsync(int itemId, string? modifiedBy = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                var item = await GetItemByIdForTransactionAsync(connection, transaction, itemId);
                if (item == null)
                    return false;

                var transfer = await GetTransferByIdForTransactionAsync(connection, transaction, item.TransferId);
                if (transfer == null || transfer.IsDeleted || transfer.Status == TransferStatus.Completed)
                    return false;

                // Delete
                var deleteQuery = "DELETE FROM TransferItems WHERE Id = @Id";

                using (var command = new SqlCommand(deleteQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@Id", itemId);
                    await command.ExecuteNonQueryAsync();
                }

                // Detailed description
                string description = $"ลบสินค้า: {item.ProductName} ({item.ProductCode}) จำนวนเดิม {item.InitialQuantity} {item.Unit}";

                // Add History
                await AddHistoryAsync(connection, transaction, item.TransferId, "RemovedItem",
                    description, modifiedBy, JsonSerializer.Serialize(item), null);

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        #endregion
        #region Return Items (NEW - ส่วนที่เพิ่มใหม่)

        /// <summary>
        /// คืนของแบบแยก view (สามารถคืนหลายครั้งได้)
        /// </summary>
        public async Task<bool> ReturnItemAsync(int itemId, decimal returnQuantity, string? returnedBy = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                // ดึงข้อมูล Item
                var item = await GetItemByIdForTransactionAsync(connection, transaction, itemId);
                if (item == null)
                    throw new InvalidOperationException("ไม่พบรายการสินค้า");

                // ตรวจสอบว่าใบTransferยังแก้ไขได้อยู่
                var transfer = await GetTransferByIdForTransactionAsync(connection, transaction, item.TransferId);
                if (transfer == null)
                    throw new InvalidOperationException("ไม่พบใบTransfer");

                if (transfer.Status == TransferStatus.Completed)
                    throw new InvalidOperationException("ไม่สามารถคืนของได้ เนื่องจากใบTransferจบงานแล้ว");

                // Validate ด้วย method จาก Model
                if (!item.CanReturnQuantity(returnQuantity, out string? errorMessage))
                    throw new InvalidOperationException(errorMessage);

                // คำนวณค่าใหม่
                decimal oldReturned = item.ReturnedQuantity ?? 0;
                decimal newReturned = oldReturned + returnQuantity;
                decimal newRemaining = item.TotalIssuedQuantity - newReturned;

                // บันทึกข้อมูลเก่าสำหรับ history
                var oldValues = new
                {
                    ReturnedQuantity = oldReturned,
                    RemainingQuantity = item.RemainingQuantity
                };

                // Update ฐานข้อมูล
                var updateQuery = @"
                    UPDATE TransferItems 
                    SET ReturnedQuantity = @Returned
                    WHERE Id = @Id";

                using (var command = new SqlCommand(updateQuery, connection, transaction))
                {
                    command.Parameters.AddWithValue("@Id", itemId);
                    command.Parameters.AddWithValue("@Returned", newReturned);
                    await command.ExecuteNonQueryAsync();
                }

                // Detailed description
                string description = $"คืน: {item.ProductName} ({item.ProductCode}) จำนวน {returnQuantity:N4} {item.Unit} | ก่อนคืน: {oldReturned:N4} | หลังคืน: {newReturned:N4} | คงเหลือ: {newRemaining:N4}";

                // บันทึกประวัติ
                var newValues = new
                {
                    ReturnedQuantity = newReturned,
                    RemainingQuantity = newRemaining
                };

                await AddHistoryAsync(connection, transaction, item.TransferId, "Returned",
                    description,
                    returnedBy,
                    JsonSerializer.Serialize(oldValues),
                    JsonSerializer.Serialize(newValues));

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
        /// คืนของหลายรายการพร้อมกัน
        /// </summary>
        public async Task<bool> ReturnMultipleItemsAsync(Dictionary<int, decimal> returnQuantities, string? returnedBy = null)
        {
            if (returnQuantities == null || returnQuantities.Count == 0)
                throw new ArgumentException("ต้องระบุรายการที่ต้องการคืนอย่างน้อย 1 รายการ");

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                int? transferId = null;
                var returnedItems = new List<string>();

                foreach (var kvp in returnQuantities)
                {
                    int itemId = kvp.Key;
                    decimal quantity = kvp.Value;

                    // ดึงข้อมูล Item
                    var item = await GetItemByIdForTransactionAsync(connection, transaction, itemId);
                    if (item == null)
                        throw new InvalidOperationException($"ไม่พบรายการสินค้า ID: {itemId}");

                    // เก็บ transferId เพื่อตรวจสอบว่าเป็นใบโอนเดียวกัน
                    if (transferId == null)
                    {
                        transferId = item.TransferId;
                    }
                    else if (transferId != item.TransferId)
                    {
                        throw new InvalidOperationException("รายการสินค้าต้องอยู่ในใบTransferเดียวกัน");
                    }

                    // Validate
                    if (!item.CanReturnQuantity(quantity, out string? errorMessage))
                        throw new InvalidOperationException($"{item.ProductName}: {errorMessage}");

                    // คำนวณค่าใหม่
                    decimal oldReturned = item.ReturnedQuantity ?? 0;
                    decimal newReturned = oldReturned + quantity;

                    // Update
                    var updateQuery = @"
                        UPDATE TransferItems 
                        SET ReturnedQuantity = @Returned
                        WHERE Id = @Id";

                    using (var command = new SqlCommand(updateQuery, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@Id", itemId);
                        command.Parameters.AddWithValue("@Returned", newReturned);
                        await command.ExecuteNonQueryAsync();
                    }

                    returnedItems.Add($"{item.ProductName} ({quantity:N4} {item.Unit})");
                }

                // ตรวจสอบใบโอน
                if (transferId == null)
                    throw new InvalidOperationException("ไม่พบใบtransfer");

                var transfer = await GetTransferByIdForTransactionAsync(connection, transaction, transferId.Value);
                if (transfer == null)
                    throw new InvalidOperationException("ไม่พบใบtransfer");

                if (transfer.Status == TransferStatus.Completed)
                    throw new InvalidOperationException("ไม่สามารถคืนของได้ เนื่องจากใบtransferจบงานแล้ว");

                // บันทึกประวัติ
                string itemsList = string.Join(", ", returnedItems);
                string description = $"คืนสินค้าหลายรายการ: {itemsList}";

                await AddHistoryAsync(connection, transaction, transferId.Value, "ReturnedMultiple",
                    description, returnedBy, null, JsonSerializer.Serialize(returnQuantities));

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
        /// ดึงรายการสินค้าที่สามารถคืนได้ (มี Remaining > 0)
        /// </summary>
        public async Task<List<TransferItem>> GetReturnableItemsAsync(int transferId)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var items = await GetTransferItemsAsync(connection, transferId);
            
            // กรองเฉพาะที่ CanReturn = true
            return items.Where(item => item.CanReturn).ToList();
        }

        #endregion

        #region Complete Requisition

        /// <summary>
        /// จบงาน (Complete) พร้อมบันทึกจำนวนคืน
        /// </summary>
        public async Task<bool> CompleteTransferAsync(int transferId, Dictionary<int, decimal>? returnedQuantities = null, string? completedBy = null, int? actualPeople = null, string? reason = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                var transfer = await GetTransferByIdForTransactionAsync(connection, transaction, transferId);
                if (transfer == null)
                    return false;

                if (transfer.Status == TransferStatus.Completed)
                    throw new InvalidOperationException("ใบtransferจบงานแล้ว");

                var returnedDetails = new List<string>();

                // Apply returned quantities if provided
                if (returnedQuantities != null && returnedQuantities.Count > 0)
                {
                    foreach (var kvp in returnedQuantities)
                    {
                        int itemId = kvp.Key;
                        decimal returnQty = kvp.Value;

                        if (returnQty > 0)
                        {
                            var item = await GetItemByIdForTransactionAsync(connection, transaction, itemId);
                            if (item != null)
                            {
                                if (!item.CanReturnQuantity(returnQty, out string? errorMessage))
                                    throw new InvalidOperationException($"{item.ProductName}: {errorMessage}");

                                decimal newReturned = (item.ReturnedQuantity ?? 0m) + returnQty;

                                var updateItemQuery = @"
                            UPDATE TransferItems 
                            SET ReturnedQuantity = @Returned
                            WHERE Id = @Id";

                                using var cmdItem = new SqlCommand(updateItemQuery, connection, transaction);
                                cmdItem.Parameters.AddWithValue("@Id", itemId);
                                cmdItem.Parameters.AddWithValue("@Returned", newReturned);
                                await cmdItem.ExecuteNonQueryAsync();

                                returnedDetails.Add($"{item.ProductName} ({returnQty:N4} {item.Unit})");
                            }
                        }
                    }
                }

                // Recompute total returned
                decimal totalReturned = 0;
                var allItems = await GetTransferItemsForTransactionAsync(connection, transaction, transferId);
                foreach (var item in allItems)
                {
                    totalReturned += item.ReturnedQuantity ?? 0m;
                }

                // Update transfer: set Completed, CompletedDate, ActualPeople (new column) and LastModifiedDate
                var updateTransferQuery = @"
            UPDATE Transfer
            SET Status = @Status,
                CompletedDate = GETDATE(),
                ActualPeople = @ActualPeople,
                LastModifiedDate = GETDATE()
            WHERE Id = @Id";

                using (var updateCmd = new SqlCommand(updateTransferQuery, connection, transaction))
                {
                    updateCmd.Parameters.AddWithValue("@Id", transferId);
                    updateCmd.Parameters.AddWithValue("@Status", TransferStatus.Completed.ToString());
                    updateCmd.Parameters.AddWithValue("@ActualPeople", (object?)actualPeople ?? DBNull.Value);
                    await updateCmd.ExecuteNonQueryAsync();
                }

                // Build description including actual people and optional reason
                string returnedPart = returnedDetails.Any()
                    ? $" คืนรวม {totalReturned:N4} หน่วย: {string.Join(", ", returnedDetails)}"
                    : $" คืนรวม {totalReturned:N4} หน่วย";

                string actualPeoplePart = actualPeople.HasValue ? $" | ผู้เข้าร่วมจริง: {actualPeople.Value}" : "";
                string reasonPart = !string.IsNullOrWhiteSpace(reason) ? $" | เหตุผล: {reason}" : "";

                string description = $"จบงาน{returnedPart}{actualPeoplePart}{reasonPart}";

                // Structured new values for history
                var newValuesObj = new
                {
                    Completed = true,
                    ActualPeople = actualPeople,
                    Returned = returnedQuantities,
                    Reason = reason
                };

                await AddHistoryAsync(connection, transaction, transferId, "Completed",
                    description,
                    completedBy,
                    null,
                    JsonSerializer.Serialize(newValuesObj));

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        #endregion

        #region History

        /// <summary>
        /// ดึงประวัติการแก้ไขของใบTransfer
        /// </summary>
        public async Task<List<TransferHistory>> GetTransferHistoryAsync(int transferId)
        {
            var histories = new List<TransferHistory>();

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT Id, TransferId, ModifiedDate, ModifiedBy, Action, 
                       Description, OldValues, NewValues
                FROM TransferHistory
                WHERE TransferId = @TransferId
                ORDER BY ModifiedDate DESC";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@TransferId", transferId);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                histories.Add(new TransferHistory
                {
                    Id = reader.GetInt32(0),
                    TransferId = reader.GetInt32(1),
                    ModifiedDate = reader.GetDateTime(2),
                    ModifiedBy = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Action = reader.GetString(4),
                    Description = reader.IsDBNull(5) ? null : reader.GetString(5),
                    OldValues = reader.IsDBNull(6) ? null : reader.GetString(6),
                    NewValues = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }

            return histories;
        }

        #endregion

        #region Helper Methods

        private async Task<string> GenerateTransferNoAsync(SqlConnection connection, SqlTransaction transaction)
        {
            // Use Gregorian (ค.ศ.) year-month-day explicitly via InvariantCulture
            string today = DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            string prefix = $"TRF-{today}-";

            // หาเลขที่ล่าสุดของวันนี้
            var query = @"
        SELECT TOP 1 TransferNo 
        FROM Transfer 
        WHERE TransferNo LIKE @Prefix + '%'
        ORDER BY TransferNo DESC";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@Prefix", prefix);

            var lastNo = await command.ExecuteScalarAsync() as string;

            int sequence = 1;
            if (!string.IsNullOrEmpty(lastNo))
            {
                // Extract sequence number
                string lastSeq = lastNo.Substring(prefix.Length);
                if (int.TryParse(lastSeq, out int lastSeqNum))
                {
                    sequence = lastSeqNum + 1;
                }
            }

            return $"{prefix}{sequence:D3}";
        }

        private async Task<List<TransferItem>> GetTransferItemsAsync(SqlConnection connection, int transferId)
        {
            var items = new List<TransferItem>();

            var query = @"
        SELECT Id, TransferId, ProductCode, ProductName, 
               InitialQuantity, AdditionalQuantity, ReturnedQuantity, 
               Unit, Notes, UnitPrice, PriceDate
        FROM TransferItems
        WHERE TransferId = @TransferId
        ORDER BY Id";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@TransferId", transferId);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                items.Add(new TransferItem
                {
                    Id = reader.GetInt32(0),
                    TransferId = reader.GetInt32(1),
                    ProductCode = reader.GetString(2),
                    ProductName = reader.GetString(3),
                    InitialQuantity = reader.GetDecimal(4),
                    AdditionalQuantity = reader.GetDecimal(5),
                    ReturnedQuantity = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                    Unit = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Notes = reader.IsDBNull(8) ? null : reader.GetString(8),
                    // ⚠️ เพิ่มฟิลด์ใหม่
                    UnitPrice = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                    PriceDate = reader.IsDBNull(10) ? null : reader.GetDateTime(10)
                });
            }

            return items;
        }

        private async Task<List<TransferItem>> GetTransferItemsForTransactionAsync(
            SqlConnection connection, SqlTransaction transaction, int transferId)
        {
            var items = new List<TransferItem>();

            var query = @"
        SELECT Id, TransferId, ProductCode, ProductName, 
               InitialQuantity, AdditionalQuantity, ReturnedQuantity, 
               Unit, Notes
        FROM TransferItems
        WHERE TransferId = @TransferId
        ORDER BY Id";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@TransferId", transferId);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                items.Add(new TransferItem
                {
                    Id = reader.GetInt32(0),
                    TransferId = reader.GetInt32(1),
                    ProductCode = reader.GetString(2),
                    ProductName = reader.GetString(3),
                    InitialQuantity = reader.GetDecimal(4),
                    AdditionalQuantity = reader.GetDecimal(5),
                    ReturnedQuantity = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                    Unit = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Notes = reader.IsDBNull(8) ? null : reader.GetString(8)
                });
            }

            return items;
        }

        private async Task<Models.Transfer?> GetTransferByIdForTransactionAsync(
    SqlConnection connection, SqlTransaction transaction, int id)
        {
            var query = @"
SELECT T.Id, T.TransferNo, T.CreatedDate, T.UsageDate, T.OutletId, T.CreatedBy, 
       T.Status, T.CompletedDate, T.ActualPeople, T.Notes, T.LastModifiedDate,
       T.IsDeleted, T.DeletedDate, T.DeletedBy, T.DeletedReason,
       O.Name AS OutletName,
       T.OutletPricePerHeadAtSave, T.OutletPricePerHeadSavedAt,
       T.KitchenId, K.Name AS KitchenName
FROM Transfer T
LEFT JOIN Outlets O ON T.OutletId = O.Id
LEFT JOIN Kitchens K ON T.KitchenId = K.Id
WHERE T.Id = @Id";

    using var command = new SqlCommand(query, connection, transaction);
    command.Parameters.AddWithValue("@Id", id);

    using var reader = await command.ExecuteReaderAsync();

    if (await reader.ReadAsync())
    {
        return new Models.Transfer
        {
            Id = reader.GetInt32(0),
            TransferNo = reader.GetString(1),
            CreatedDate = reader.GetDateTime(2),
            UsageDate = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
            OutletId = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
            CreatedBy = reader.IsDBNull(5) ? null : reader.GetString(5),
            Status = (TransferStatus)Enum.Parse(typeof(TransferStatus), reader.GetString(6)),
            CompletedDate = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            ActualPeople = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
            LastModifiedDate = reader.GetDateTime(10),
            IsDeleted = reader.GetBoolean(11),
            DeletedDate = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
            DeletedBy = reader.IsDBNull(13) ? null : reader.GetString(13),
            DeletedReason = reader.IsDBNull(14) ? null : reader.GetString(14),
            OutletName = reader.IsDBNull(15) ? null : reader.GetString(15),
            OutletPricePerHeadAtSave = reader.IsDBNull(16) ? null : reader.GetDecimal(16),
            OutletPricePerHeadSavedAt = reader.IsDBNull(17) ? null : reader.GetDateTime(17),
            KitchenId = reader.IsDBNull(18) ? (int?)null : reader.GetInt32(18),
            KitchenName = reader.IsDBNull(19) ? null : reader.GetString(19)
        };
    }

    return null;
}

        private async Task<TransferItem?> GetItemByIdForTransactionAsync(
            SqlConnection connection, SqlTransaction transaction, int id)
        {
            var query = @"
                SELECT Id, TransferId, ProductCode, ProductName, 
                       InitialQuantity, AdditionalQuantity, ReturnedQuantity, 
                       Unit, Notes
                FROM TransferItems
                WHERE Id = @Id";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@Id", id);

            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new TransferItem
                {
                    Id = reader.GetInt32(0),
                    TransferId = reader.GetInt32(1),
                    ProductCode = reader.GetString(2),
                    ProductName = reader.GetString(3),
                    InitialQuantity = reader.GetDecimal(4),
                    AdditionalQuantity = reader.GetDecimal(5),
                    ReturnedQuantity = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                    Unit = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Notes = reader.IsDBNull(8) ? null : reader.GetString(8)
                };
            }

            return null;
        }

        private async Task UpdateTransferStatusAsync(
            SqlConnection connection, SqlTransaction transaction, int transferId, TransferStatus status)
        {
            var query = "UPDATE Transfer SET Status = @Status, LastModifiedDate = GETDATE() WHERE Id = @Id";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@Id", transferId);
            command.Parameters.AddWithValue("@Status", status.ToString());

            await command.ExecuteNonQueryAsync();
        }

        private async Task AddHistoryAsync(
            SqlConnection connection, SqlTransaction transaction,
            int transferId, string action, string? description,
            string? modifiedBy, string? oldValues, string? newValues)
        {
            var query = @"
                INSERT INTO TransferHistory 
                (TransferId, ModifiedDate, ModifiedBy, Action, Description, OldValues, NewValues)
                VALUES (@TransferId, GETDATE(), @ModifiedBy, @Action, @Description, @OldValues, @NewValues)";

            using var command = new SqlCommand(query, connection, transaction);
            command.Parameters.AddWithValue("@TransferId", transferId);
            command.Parameters.AddWithValue("@ModifiedBy", (object?)modifiedBy ?? DBNull.Value);
            command.Parameters.AddWithValue("@Action", action);
            command.Parameters.AddWithValue("@Description", (object?)description ?? DBNull.Value);
            command.Parameters.AddWithValue("@OldValues", (object?)oldValues ?? DBNull.Value);
            command.Parameters.AddWithValue("@NewValues", (object?)newValues ?? DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }

        // ⚠️ เพิ่ม helper method นี้
        private static string GetStatusText(TransferStatus status)
        {
            return status switch
            {
                TransferStatus.Draft => "แบบร่าง",
                TransferStatus.InProgress => "กำลังดำเนินการ",
                TransferStatus.Completed => "จบงานแล้ว",
                _ => "ไม่ทราบสถานะ"
            };
        }

        #endregion

        /// <summary>
        /// บันทึกรายการใหม่หลายตัวพร้อมกัน พร้อมบันทึกประวัติแบบละเอียด
        /// </summary>
        public async Task<bool> AddMultipleItemsAsync(int transferId, List<TransferItem> items, string? modifiedBy = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                var transfer = await GetTransferByIdForTransactionAsync(connection, transaction, transferId);
                if (transfer == null)
                    return false;

                // เดิมใช้ !requisition.CanEdit → จำกัดเฉพาะ Draft
                // ปรับใหม่ให้เพิ่มรายการได้ถ้า Status เป็น Draft หรือ InProgress และยังไม่ถูกลบ
                if (transfer.IsDeleted ||
                    (transfer.Status != TransferStatus.Draft && transfer.Status != TransferStatus.InProgress))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"❌ AddMultipleItemsAsync: Transfer {transferId} not allowed. Status={transfer.Status}, IsDeleted={transfer.IsDeleted}");
                    return false;
                }

                var addedItems = new List<string>();

                foreach (var item in items)
                {
                    if (item.InitialQuantity <= 0)
                        throw new InvalidOperationException($"จำนวนเบิกของ {item.ProductName} ต้องมากกว่า 0");

                    var insertQuery = @"
                INSERT INTO TransferItems 
                (TransferId, ProductCode, ProductName, InitialQuantity,
                 AdditionalQuantity, ReturnedQuantity, Unit, Notes, UnitPrice, PriceDate)
                VALUES (@TransferId, @ProductCode, @ProductName, @InitialQuantity,
                        0, NULL, @Unit, @Notes, @UnitPrice, @PriceDate)";

                    using (var command = new SqlCommand(insertQuery, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@TransferId", transferId);
                        command.Parameters.AddWithValue("@ProductCode", item.ProductCode ?? string.Empty);
                        command.Parameters.AddWithValue("@ProductName", item.ProductName ?? string.Empty);
                        command.Parameters.AddWithValue("@InitialQuantity", item.InitialQuantity);
                        command.Parameters.AddWithValue("@Unit", (object?)item.Unit ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Notes", (object?)item.Notes ?? DBNull.Value);
                        command.Parameters.AddWithValue("@UnitPrice", (object?)item.UnitPrice ?? DBNull.Value);
                        command.Parameters.AddWithValue("@PriceDate", (object?)item.PriceDate ?? DBNull.Value);

                        await command.ExecuteNonQueryAsync();
                    }

                    addedItems.Add($"{item.ProductName} ({item.ProductCode}) จำนวน {item.InitialQuantity} {item.Unit}");
                }

                // เปลี่ยนสถานะเป็น InProgress เฉพาะกรณีเดิมเป็น Draft
                if (transfer.Status == TransferStatus.Draft)
                {
                    var updateStatusQuery = @"
                UPDATE Transfer 
                SET Status = @Status, 
                    LastModifiedDate = GETDATE()
                WHERE Id = @Id";

                    using (var statusCmd = new SqlCommand(updateStatusQuery, connection, transaction))
                    {
                        statusCmd.Parameters.AddWithValue("@Status", TransferStatus.InProgress.ToString());
                        statusCmd.Parameters.AddWithValue("@Id", transferId);
                        await statusCmd.ExecuteNonQueryAsync();
                    }

                    System.Diagnostics.Debug.WriteLine("✅ AddMultipleItemsAsync: เปลี่ยนสถานะ Draft → InProgress");
                }

                string itemsList = string.Join("\n- ", addedItems);
                string description = transfer.Status == TransferStatus.Draft
                    ? $"เพิ่มรายการสินค้า {items.Count} รายการ และเปลี่ยนสถานะเป็น 'กำลังดำเนินการ':\n- {itemsList}"
                    : $"เพิ่มรายการสินค้า {items.Count} รายการ:\n- {itemsList}";

                await AddHistoryAsync(connection, transaction, transferId, "AddedMultipleItems",
                    description, modifiedBy, null, JsonSerializer.Serialize(items));

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
        /// อัปเดตรายการหลายตัวพร้อมกัน ไม่บันทึกประวัติ (ใช้สำหรับ batch update)
        /// </summary>
        public async Task<bool> UpdateMultipleItemsAsync(int transferId, List<TransferItem> items, string? modifiedBy = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                // ตรวจสอบ
                var transfer = await GetTransferByIdForTransactionAsync(connection, transaction, transferId);
                if (transfer == null || transfer.IsDeleted || transfer.Status == TransferStatus.Completed)
                    return false;

                var updatedItems = new List<string>();

                foreach (var item in items)
                {
                    // Validate
                    if (item.InitialQuantity <= 0)
                        throw new InvalidOperationException($"จำนวนเบิกของ {item.ProductName} ต้องมากกว่า 0");

                    // Update
                    var updateQuery = @"
                UPDATE TransferItems 
                SET InitialQuantity = @InitialQuantity
                WHERE Id = @Id";

                    using (var command = new SqlCommand(updateQuery, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@Id", item.Id);
                        command.Parameters.AddWithValue("@InitialQuantity", item.InitialQuantity);

                        await command.ExecuteNonQueryAsync();
                    }

                    updatedItems.Add($"{item.ProductName} ({item.ProductCode}) → {item.InitialQuantity} {item.Unit}");
                }

                // ไม่บันทึกประวัติสำหรับการ update (ตามข้อ 2)

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
        /// ลบรายการหลายตัวพร้อมกัน ไม่บันทึกประวัติ (ใช้สำหรับ batch delete)
        /// </summary>
        public async Task<bool> RemoveMultipleItemsAsync(int transferId, List<int> itemIds, string? modifiedBy = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                var transfer = await GetTransferByIdForTransactionAsync(connection, transaction, transferId);
                if (transfer == null || transfer.IsDeleted || transfer.Status == TransferStatus.Completed)
                    return false;

                foreach (var itemId in itemIds)
                {
                    // Delete
                    var deleteQuery = "DELETE FROM TransferItems WHERE Id = @Id AND TransferId = @TransferId";

                    using (var command = new SqlCommand(deleteQuery, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@Id", itemId);
                        command.Parameters.AddWithValue("@TransferId", transferId);
                        await command.ExecuteNonQueryAsync();
                    }
                }

                // ไม่บันทึกประวัติสำหรับการลบ (ตามข้อ 3)

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
        /// คำนวณยอดเงินรวมของใบTransfer (รวมราคาสินค้าปัจจุบัน)
        /// </summary>
        public async Task<decimal> CalculateTotalCostAsync(int transferId)
        {
            var transfer = await GetTransferByIdAsync(transferId);
            if (transfer == null || transfer.Items.Count == 0)
                return 0;

            var productService = new ProductService();
            decimal totalCost = 0;

            foreach (var item in transfer.Items)
            {
                // ดึงข้อมูลสินค้าเพื่อเอาราคา
                var product = await productService.GetProductByCodeAsync(item.ProductCode);
                if (product?.Price != null)
                {
                    decimal quantity = item.InitialQuantity + item.AdditionalQuantity;
                    totalCost += quantity * product.Price.Value;
                }
            }

            return totalCost;
        }

        // Add this public helper to the TransferService class (near other public methods)
        public async Task<bool> AddHistoryEntryAsync(int transferId, string action, string description, string? modifiedBy = null, string? oldValues = null, string? newValues = null)
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                // reuse the existing private AddHistoryAsync helper
                await AddHistoryAsync(connection, transaction, transferId, action, description, modifiedBy, oldValues, newValues);
                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}
