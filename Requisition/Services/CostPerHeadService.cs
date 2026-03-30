using Requisition.Helpers;
using Requisition.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text.Json;
using System.Threading.Tasks;

namespace Requisition.Services
{
    public class CostPerHeadService
    {
        private readonly string _connectionString;

        public CostPerHeadService()
        {
            _connectionString = ConfigurationHelper.GetConnectionString();
        }

        // DTO for history returned to UI
        public class HistoryRecord
        {
            public int Id { get; set; }
            public int? OutletId { get; set; }            // optional for global history
            public string? OutletName { get; set; }       // optional display name
            public string ActionType { get; set; } = string.Empty;
            public string? ActionBy { get; set; }
            public DateTime ActionDate { get; set; }
            public string? OldValues { get; set; }
            public string? NewValues { get; set; }
            public string? ChangedFields { get; set; } // JSON list of changed fields
        }

        // Helper type for building changed fields
        private record ChangedField(string Field, string DisplayName, string? OldValue, string? NewValue);

        public async Task<List<Outlet>> GetAllAsync()
        {
            var list = new List<Outlet>();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"SELECT Id, Name, PricePerHead, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate
                                       FROM Outlets
                                       ORDER BY IsActive DESC, Name", conn);

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var isActive = !rdr.IsDBNull(3) && rdr.GetBoolean(3);
                var outlet = new Outlet
                {
                    Id = rdr.GetInt32(0),
                    Name = rdr.IsDBNull(1) ? null : rdr.GetString(1),
                    PricePerHead = rdr.IsDBNull(2) ? null : rdr.GetDecimal(2),
                    IsActive = isActive,
                    CreatedBy = rdr.IsDBNull(4) ? null : rdr.GetString(4),
                    CreatedDate = rdr.IsDBNull(5) ? DateTime.MinValue : rdr.GetDateTime(5),
                    ModifiedBy = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                    ModifiedDate = rdr.IsDBNull(7) ? null : rdr.GetDateTime(7)
                };

                // Diagnostic log: show DB value per row
                System.Diagnostics.Debug.WriteLine($"CostPerHeadService.GetAllAsync: Id={outlet.Id}, Name='{outlet.Name}', DB_IsActive={isActive}");

                list.Add(outlet);
            }

            return list;
        }

        public async Task<int> CreateAsync(Outlet model, string? actionBy = null)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var tran = conn.BeginTransaction();

            try
            {
                var cmd = new SqlCommand(@"
                    INSERT INTO Outlets (Name, PricePerHead, IsActive, CreatedBy, CreatedDate)
                    OUTPUT INSERTED.Id
                    VALUES (@Name, @Price, 1, @CreatedBy, SYSUTCDATETIME())", conn, tran);

                cmd.Parameters.AddWithValue("@Name", (object?)model.Name ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Price", (object?)model.PricePerHead ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CreatedBy", (object?)actionBy ?? DBNull.Value);

                var result = await cmd.ExecuteScalarAsync();
                if (result == null || result == DBNull.Value)
                    throw new InvalidOperationException("Failed to insert Outlet: no Id returned.");
                var inserted = Convert.ToInt32(result);

                // build changed fields (all non-null fields for create)
                var changed = new List<ChangedField>();
                changed.Add(new ChangedField("Name", "ชื่อ", null, model.Name));
                changed.Add(new ChangedField("PricePerHead", "ราคาต่อหัว", null, model.PricePerHead?.ToString("N4")));

                var changedJson = JsonSerializer.Serialize(changed);

                // history
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

        public async Task UpdateAsync(Outlet model, string? actionBy = null)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var tran = conn.BeginTransaction();

            try
            {
                // read old values
                Outlet? old = null;
                var readCmd = new SqlCommand(@"SELECT Id, Name, PricePerHead, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate
                                               FROM Outlets WHERE Id = @Id", conn, tran);
                readCmd.Parameters.AddWithValue("@Id", model.Id);
                using var rdr = await readCmd.ExecuteReaderAsync();
                if (await rdr.ReadAsync())
                {
                    old = new Outlet
                    {
                        Id = rdr.GetInt32(0),
                        Name = rdr.IsDBNull(1) ? null : rdr.GetString(1),
                        PricePerHead = rdr.IsDBNull(2) ? null : rdr.GetDecimal(2),
                        IsActive = !rdr.IsDBNull(3) && rdr.GetBoolean(3),
                        CreatedBy = rdr.IsDBNull(4) ? null : rdr.GetString(4),
                        CreatedDate = rdr.IsDBNull(5) ? DateTime.MinValue : rdr.GetDateTime(5),
                        ModifiedBy = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                        ModifiedDate = rdr.IsDBNull(7) ? null : rdr.GetDateTime(7)
                    };
                }
                rdr.Close();

                var updateCmd = new SqlCommand(@"
                    UPDATE Outlets
                    SET Name = @Name,
                        PricePerHead = @Price,
                        ModifiedBy = @ModifiedBy,
                        ModifiedDate = SYSUTCDATETIME()
                    WHERE Id = @Id", conn, tran);

                updateCmd.Parameters.AddWithValue("@Name", (object?)model.Name ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@Price", (object?)model.PricePerHead ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@ModifiedBy", (object?)actionBy ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@Id", model.Id);

                await updateCmd.ExecuteNonQueryAsync();

                // compute changed fields by comparing old and new
                var changedList = new List<ChangedField>();
                if (old != null)
                {
                    if (!string.Equals(old.Name, model.Name, StringComparison.Ordinal))
                        changedList.Add(new ChangedField("Name", "ชื่อ", old.Name, model.Name));

                    var oldPrice = old.PricePerHead?.ToString("N4");
                    var newPrice = model.PricePerHead?.ToString("N4");
                    if (!string.Equals(oldPrice, newPrice, StringComparison.Ordinal))
                        changedList.Add(new ChangedField("PricePerHead", "ราคาต่อหัว", oldPrice, newPrice));

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
                // read old values
                Outlet? old = null;
                var readCmd = new SqlCommand(@"SELECT Id, Name, PricePerHead, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate
                                               FROM Outlets WHERE Id = @Id", conn, tran);
                readCmd.Parameters.AddWithValue("@Id", id);
                using var rdr = await readCmd.ExecuteReaderAsync();
                if (await rdr.ReadAsync())
                {
                    old = new Outlet
                    {
                        Id = rdr.GetInt32(0),
                        Name = rdr.IsDBNull(1) ? null : rdr.GetString(1),
                        PricePerHead = rdr.IsDBNull(2) ? null : rdr.GetDecimal(2),
                        IsActive = !rdr.IsDBNull(3) && rdr.GetBoolean(3),
                        CreatedBy = rdr.IsDBNull(4) ? null : rdr.GetString(4),
                        CreatedDate = rdr.IsDBNull(5) ? DateTime.MinValue : rdr.GetDateTime(5),
                        ModifiedBy = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                        ModifiedDate = rdr.IsDBNull(7) ? null : rdr.GetDateTime(7)
                    };
                }
                rdr.Close();

                var delCmd = new SqlCommand(@"
                    UPDATE Outlets
                    SET IsActive = 0, ModifiedBy = @ModifiedBy, ModifiedDate = SYSUTCDATETIME()
                    WHERE Id = @Id", conn, tran);

                delCmd.Parameters.AddWithValue("@ModifiedBy", (object?)actionBy ?? DBNull.Value);
                delCmd.Parameters.AddWithValue("@Id", id);

                await delCmd.ExecuteNonQueryAsync();

                // record that IsActive changed to false
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

        private async Task AddHistoryAsync(SqlConnection conn, SqlTransaction tran, int? outletId, string actionType,
            string? actionBy, string? oldValues, string? newValues, string? changedFields, string? remarks = null)
        {
            var cmd = new SqlCommand(@"
                INSERT INTO OutletHistory (OutletId, ActionType, ActionBy, ActionDate, OldValues, NewValues, ChangedFields, Remarks)
                VALUES (@OutletId, @ActionType, @ActionBy, SYSUTCDATETIME(), @OldValues, @NewValues, @ChangedFields, @Remarks)", conn, tran);

            cmd.Parameters.AddWithValue("@OutletId", (object?)outletId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ActionType", actionType);
            cmd.Parameters.AddWithValue("@ActionBy", (object?)actionBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@OldValues", (object?)oldValues ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@NewValues", (object?)newValues ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ChangedFields", (object?)changedFields ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Remarks", (object?)remarks ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<HistoryRecord>> GetHistoryAsync(int outletId)
        {
            var list = new List<HistoryRecord>();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
                SELECT Id, ActionType, ActionBy, ActionDate, OldValues, NewValues, ChangedFields
                FROM OutletHistory
                WHERE OutletId = @Id
                ORDER BY ActionDate DESC", conn);
            cmd.Parameters.AddWithValue("@Id", outletId);

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

        // New: get all history across outlets (includes outlet name when available)
        public async Task<List<HistoryRecord>> GetAllHistoryAsync()
        {
            var list = new List<HistoryRecord>();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var cmd = new SqlCommand(@"
                SELECT h.Id, h.OutletId, d.Name, h.ActionType, h.ActionBy, h.ActionDate, h.OldValues, h.NewValues, h.ChangedFields
                FROM OutletHistory h
                LEFT JOIN Outlets d ON d.Id = h.OutletId
                ORDER BY h.ActionDate DESC", conn);

            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                list.Add(new HistoryRecord
                {
                    Id = rdr.GetInt32(0),
                    OutletId = rdr.IsDBNull(1) ? null : (int?)rdr.GetInt32(1),
                    OutletName = rdr.IsDBNull(2) ? null : rdr.GetString(2),
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

        public async Task ToggleStatusAsync(Outlet outlet, string reason, string actionBy)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();
            using var tran = conn.BeginTransaction();

            try
            {
                // read old values
                Outlet? old = null;
                var readCmd = new SqlCommand(@"SELECT Id, Name, PricePerHead, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate
                                               FROM Outlets WHERE Id = @Id", conn, tran);
                readCmd.Parameters.AddWithValue("@Id", outlet.Id);
                using var rdr = await readCmd.ExecuteReaderAsync();
                if (await rdr.ReadAsync())
                {
                    old = new Outlet
                    {
                        Id = rdr.GetInt32(0),
                        Name = rdr.IsDBNull(1) ? null : rdr.GetString(1),
                        PricePerHead = rdr.IsDBNull(2) ? null : rdr.GetDecimal(2),
                        IsActive = !rdr.IsDBNull(3) && rdr.GetBoolean(3),
                        CreatedBy = rdr.IsDBNull(4) ? null : rdr.GetString(4),
                        CreatedDate = rdr.IsDBNull(5) ? DateTime.MinValue : rdr.GetDateTime(5),
                        ModifiedBy = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                        ModifiedDate = rdr.IsDBNull(7) ? null : rdr.GetDateTime(7)
                    };
                }
                rdr.Close();

                // Diagnostic: log old vs requested new value
                System.Diagnostics.Debug.WriteLine($"ToggleStatusAsync: OutletId={outlet.Id}, OldIsActive={(old != null ? old.IsActive.ToString() : "null")}, RequestedNewIsActive={outlet.IsActive}");

                // อัปเดตสถานะ
                var updateCmd = new SqlCommand(@"
                    UPDATE Outlets
                    SET IsActive = @IsActive,
                        ModifiedBy = @ModifiedBy,
                        ModifiedDate = SYSUTCDATETIME()
                    WHERE Id = @Id", conn, tran);

                updateCmd.Parameters.AddWithValue("@IsActive", outlet.IsActive);
                updateCmd.Parameters.AddWithValue("@ModifiedBy", (object?)actionBy ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@Id", outlet.Id);

                await updateCmd.ExecuteNonQueryAsync();

                // สร้างข้อมูลการเปลี่ยนแปลงพิเศษ
                string statusAction = outlet.IsActive ? "เปิดการใช้งาน" : "ปิดการใช้งาน";
                var changedList = new List<ChangedField>();

                if (old != null)
                {
                    changedList.Add(new ChangedField("IsActive", "สถานะ", 
                        old.IsActive ? "เปิดใช้งาน" : "ปิดใช้งาน",
                        outlet.IsActive ? "เปิดใช้งาน" : "ปิดใช้งาน"));
                }

                changedList.Add(new ChangedField("Reason", "เหตุผล", null, reason));

                var changedJson = JsonSerializer.Serialize(changedList);

                // บันทึกประวัติพร้อมเหตุผล
                await AddHistoryAsync(conn, tran, outlet.Id, statusAction, actionBy,
                    old != null ? JsonSerializer.Serialize(old) : null,
                    JsonSerializer.Serialize(outlet),
                    changedJson,
                    reason);

                System.Diagnostics.Debug.WriteLine($"ToggleStatusAsync: Commit OutletId={outlet.Id}, NewIsActive={outlet.IsActive}");

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
