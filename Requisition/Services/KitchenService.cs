using Microsoft.Data.SqlClient;
using Requisition.Helpers;
using Requisition.Models;
using System.Text.Json;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Requisition.Services
{
    public class KitchenService
    {
        private readonly string _connectionString;

        public KitchenService()
        {
            _connectionString = ConfigurationHelper.GetConnectionString();
        }

        // DTO for history returned to UI
        public class HistoryRecord
        {
            public int Id { get; set; }
            public int? KitchenId { get; set; }
            public string? KitchenName { get; set; }
            public string ActionType { get; set; } = string.Empty;
            public string? ActionBy { get; set; }
            public DateTime ActionDate { get; set; }
            public string? OldValues { get; set; }
            public string? NewValues { get; set; }
            public string? ChangedFields { get; set; }
        }

        private record ChangedField(string Field, string DisplayName, string? OldValue, string? NewValue);

        public async Task<List<Kitchen>> GetAllAsync()
        {
            var list = new List<Kitchen>();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"SELECT Id, Name, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate
                                       FROM Kitchens
                                       ORDER BY IsActive DESC, Name", conn);

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new Kitchen
                {
                    Id = rdr.GetInt32(0),
                    Name = rdr.IsDBNull(1) ? null : rdr.GetString(1),
                    IsActive = !rdr.IsDBNull(2) && rdr.GetBoolean(2),
                    CreatedBy = rdr.IsDBNull(3) ? null : rdr.GetString(3),
                    CreatedDate = rdr.IsDBNull(4) ? DateTime.MinValue : rdr.GetDateTime(4),
                    ModifiedBy = rdr.IsDBNull(5) ? null : rdr.GetString(5),
                    ModifiedDate = rdr.IsDBNull(6) ? null : rdr.GetDateTime(6)
                });
            }

            return list;
        }

        public async Task<int> CreateAsync(Kitchen model, string? actionBy = null)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var tran = conn.BeginTransaction();

            try
            {
                var cmd = new SqlCommand(@"
                    INSERT INTO Kitchens (Name, IsActive, CreatedBy, CreatedDate)
                    OUTPUT INSERTED.Id
                    VALUES (@Name, 1, @CreatedBy, SYSUTCDATETIME())", conn, tran);

                cmd.Parameters.AddWithValue("@Name", (object?)model.Name ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CreatedBy", (object?)actionBy ?? DBNull.Value);

                var result = await cmd.ExecuteScalarAsync();
                if (result == null || result == DBNull.Value)
                    throw new InvalidOperationException("Failed to insert Kitchen: no Id returned.");
                var inserted = Convert.ToInt32(result);

                var changed = new List<ChangedField>();
                changed.Add(new ChangedField("Name", "ชื่อห้องครัว", null, model.Name));

                var changedJson = JsonSerializer.Serialize(changed);

                await AddHistoryAsync(conn, tran, inserted, "Created", actionBy, null, JsonSerializer.Serialize(model), changedJson);

                tran.Commit();
                return inserted;
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }

        public async Task UpdateAsync(Kitchen model, string? actionBy = null)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var tran = conn.BeginTransaction();

            try
            {
                Kitchen? old = null;
                var readCmd = new SqlCommand(@"SELECT Id, Name, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate
                                               FROM Kitchens WHERE Id = @Id", conn, tran);
                readCmd.Parameters.AddWithValue("@Id", model.Id);
                using var rdr = await readCmd.ExecuteReaderAsync();
                if (await rdr.ReadAsync())
                {
                    old = new Kitchen
                    {
                        Id = rdr.GetInt32(0),
                        Name = rdr.IsDBNull(1) ? null : rdr.GetString(1),
                        IsActive = !rdr.IsDBNull(2) && rdr.GetBoolean(2),
                        CreatedBy = rdr.IsDBNull(3) ? null : rdr.GetString(3),
                        CreatedDate = rdr.IsDBNull(4) ? DateTime.MinValue : rdr.GetDateTime(4),
                        ModifiedBy = rdr.IsDBNull(5) ? null : rdr.GetString(5),
                        ModifiedDate = rdr.IsDBNull(6) ? null : rdr.GetDateTime(6)
                    };
                }
                rdr.Close();

                var updateCmd = new SqlCommand(@"
                    UPDATE Kitchens
                    SET Name = @Name,
                        ModifiedBy = @ModifiedBy,
                        ModifiedDate = SYSUTCDATETIME()
                    WHERE Id = @Id", conn, tran);

                updateCmd.Parameters.AddWithValue("@Name", (object?)model.Name ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@ModifiedBy", (object?)actionBy ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@Id", model.Id);

                await updateCmd.ExecuteNonQueryAsync();

                var changedList = new List<ChangedField>();
                if (old != null)
                {
                    if (!string.Equals(old.Name, model.Name, StringComparison.Ordinal))
                        changedList.Add(new ChangedField("Name", "ชื่อห้องครัว", old.Name, model.Name));

                    if (old.IsActive != model.IsActive)
                        changedList.Add(new ChangedField("IsActive", "สถานะ", old.IsActive ? "ใช้งาน" : "ปิด", model.IsActive ? "ใช้งาน" : "ปิด"));
                }

                var changedJson = changedList.Count > 0 ? JsonSerializer.Serialize(changedList) : null;

                await AddHistoryAsync(conn, tran, model.Id, "Updated", actionBy,
                    old != null ? JsonSerializer.Serialize(old) : null,
                    JsonSerializer.Serialize(model),
                    changedJson);

                tran.Commit();
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }

        public async Task DeleteAsync(int id, string? actionBy = null)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var tran = conn.BeginTransaction();

            try
            {
                Kitchen? old = null;
                var readCmd = new SqlCommand(@"SELECT Id, Name, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate
                                               FROM Kitchens WHERE Id = @Id", conn, tran);
                readCmd.Parameters.AddWithValue("@Id", id);
                using var rdr = await readCmd.ExecuteReaderAsync();
                if (await rdr.ReadAsync())
                {
                    old = new Kitchen
                    {
                        Id = rdr.GetInt32(0),
                        Name = rdr.IsDBNull(1) ? null : rdr.GetString(1),
                        IsActive = !rdr.IsDBNull(2) && rdr.GetBoolean(2),
                        CreatedBy = rdr.IsDBNull(3) ? null : rdr.GetString(3),
                        CreatedDate = rdr.IsDBNull(4) ? DateTime.MinValue : rdr.GetDateTime(4),
                        ModifiedBy = rdr.IsDBNull(5) ? null : rdr.GetString(5),
                        ModifiedDate = rdr.IsDBNull(6) ? null : rdr.GetDateTime(6)
                    };
                }
                rdr.Close();

                var delCmd = new SqlCommand(@"
                    UPDATE Kitchens
                    SET IsActive = 0, ModifiedBy = @ModifiedBy, ModifiedDate = SYSUTCDATETIME()
                    WHERE Id = @Id", conn, tran);

                delCmd.Parameters.AddWithValue("@ModifiedBy", (object?)actionBy ?? DBNull.Value);
                delCmd.Parameters.AddWithValue("@Id", id);

                await delCmd.ExecuteNonQueryAsync();

                var changedList = new List<ChangedField>();
                if (old != null)
                    changedList.Add(new ChangedField("IsActive", "สถานะ", old.IsActive ? "ใช้งาน" : "ปิด", "ปิด"));

                var changedJson = JsonSerializer.Serialize(changedList);

                await AddHistoryAsync(conn, tran, id, "Deleted", actionBy, old != null ? JsonSerializer.Serialize(old) : null, null, changedJson);

                tran.Commit();
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }

        private async Task AddHistoryAsync(SqlConnection conn, SqlTransaction tran, int? kitchenId, string actionType,
            string? actionBy, string? oldValues, string? newValues, string? changedFields, string? remarks = null)
        {
            var cmd = new SqlCommand(@"
                INSERT INTO KitchenHistory (KitchenId, ActionType, ActionBy, ActionDate, OldValues, NewValues, ChangedFields, Remarks)
                VALUES (@KitchenId, @ActionType, @ActionBy, SYSUTCDATETIME(), @OldValues, @NewValues, @ChangedFields, @Remarks)", conn, tran);

            cmd.Parameters.AddWithValue("@KitchenId", (object?)kitchenId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ActionType", actionType);
            cmd.Parameters.AddWithValue("@ActionBy", (object?)actionBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OldValues", (object?)oldValues ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@NewValues", (object?)newValues ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ChangedFields", (object?)changedFields ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Remarks", (object?)remarks ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<HistoryRecord>> GetHistoryAsync(int kitchenId)
        {
            var list = new List<HistoryRecord>();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
                SELECT Id, ActionType, ActionBy, ActionDate, OldValues, NewValues, ChangedFields
                FROM KitchenHistory
                WHERE KitchenId = @Id
                ORDER BY ActionDate DESC", conn);
            cmd.Parameters.AddWithValue("@Id", kitchenId);

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new HistoryRecord
                {
                    Id = rdr.GetInt32(0),
                    ActionType = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1),
                    ActionBy = rdr.IsDBNull(2) ? null : rdr.GetString(2),
                    ActionDate = rdr.IsDBNull(3) ? DateTime.MinValue : rdr.GetDateTime(3),
                    OldValues = rdr.IsDBNull(4) ? null : rdr.GetString(4),
                    NewValues = rdr.IsDBNull(5) ? null : rdr.GetString(5),
                    ChangedFields = rdr.IsDBNull(6) ? null : rdr.GetString(6)
                });
            }

            return list;
        }

        public async Task<List<HistoryRecord>> GetAllHistoryAsync()
        {
            var list = new List<HistoryRecord>();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
                SELECT h.Id, h.KitchenId, k.Name, h.ActionType, h.ActionBy, h.ActionDate, h.OldValues, h.NewValues, h.ChangedFields
                FROM KitchenHistory h
                LEFT JOIN Kitchens k ON k.Id = h.KitchenId
                ORDER BY h.ActionDate DESC", conn);

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new HistoryRecord
                {
                    Id = rdr.GetInt32(0),
                    KitchenId = rdr.IsDBNull(1) ? null : (int?)rdr.GetInt32(1),
                    KitchenName = rdr.IsDBNull(2) ? null : rdr.GetString(2),
                    ActionType = rdr.IsDBNull(3) ? string.Empty : rdr.GetString(3),
                    ActionBy = rdr.IsDBNull(4) ? null : rdr.GetString(4),
                    ActionDate = rdr.IsDBNull(5) ? DateTime.MinValue : rdr.GetDateTime(5),
                    OldValues = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                    NewValues = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                    ChangedFields = rdr.IsDBNull(8) ? null : rdr.GetString(8)
                });
            }

            return list;
        }

        public async Task ToggleStatusAsync(Kitchen kitchen, string reason, string actionBy)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var tran = conn.BeginTransaction();

            try
            {
                // read old values
                Kitchen? old = null;
                var readCmd = new SqlCommand(@"SELECT Id, Name, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate
                                               FROM Kitchens WHERE Id = @Id", conn, tran);
                readCmd.Parameters.AddWithValue("@Id", kitchen.Id);
                using var rdr = await readCmd.ExecuteReaderAsync();
                if (await rdr.ReadAsync())
                {
                    old = new Kitchen
                    {
                        Id = rdr.GetInt32(0),
                        Name = rdr.IsDBNull(1) ? null : rdr.GetString(1),
                        IsActive = !rdr.IsDBNull(2) && rdr.GetBoolean(2),
                        CreatedBy = rdr.IsDBNull(3) ? null : rdr.GetString(3),
                        CreatedDate = rdr.IsDBNull(4) ? DateTime.MinValue : rdr.GetDateTime(4),
                        ModifiedBy = rdr.IsDBNull(5) ? null : rdr.GetString(5),
                        ModifiedDate = rdr.IsDBNull(6) ? null : rdr.GetDateTime(6)
                    };
                }
                rdr.Close();

                // อัปเดตสถานะ
                var updateCmd = new SqlCommand(@"
                    UPDATE Kitchens
                    SET IsActive = @IsActive,
                        ModifiedBy = @ModifiedBy,
                        ModifiedDate = SYSUTCDATETIME()
                    WHERE Id = @Id", conn, tran);

                updateCmd.Parameters.AddWithValue("@IsActive", kitchen.IsActive);
                updateCmd.Parameters.AddWithValue("@ModifiedBy", (object?)actionBy ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@Id", kitchen.Id);

                await updateCmd.ExecuteNonQueryAsync();

                // สร้างข้อมูลการเปลี่ยนแปลงพิเศษ
                string statusAction = kitchen.IsActive ? "เปิดการใช้งาน" : "ปิดการใช้งาน";
                var changedList = new List<ChangedField>();

                if (old != null)
                {
                    changedList.Add(new ChangedField("IsActive", "สถานะ",
                        old.IsActive ? "เปิดใช้งาน" : "ปิดใช้งาน",
                        kitchen.IsActive ? "เปิดใช้งาน" : "ปิดใช้งาน"));
                }

                changedList.Add(new ChangedField("Reason", "เหตุผล", null, reason));

                var changedJson = JsonSerializer.Serialize(changedList);

                // บันทึกประวัติพร้อมเหตุผล
                await AddHistoryAsync(conn, tran, kitchen.Id, statusAction, actionBy,
                    old != null ? JsonSerializer.Serialize(old) : null,
                    JsonSerializer.Serialize(kitchen),
                    changedJson,
                    reason);

                tran.Commit();
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }
    }
}
