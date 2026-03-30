using Microsoft.Data.SqlClient;
using Requisition.Helpers;
using Requisition.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Requisition.Services
{
    public class TemplateService
    {
        private readonly string _connectionString;

        public TemplateService()
        {
            _connectionString = ConfigurationHelper.GetConnectionString();
        }

        public record HistoryRecord
        {
            public int TemplateId { get; init; }
            public DateTime ActionDate { get; init; }
            public string ActionType { get; init; } = string.Empty;
            public string ActionBy { get; init; } = string.Empty;
            public string? OldValues { get; init; }
            public string? NewValues { get; init; }
            public string? ChangedFields { get; init; }
            public string? ChangedSummary { get; init; } // New property to capture summary
        }

        // Build ChangedFields JSON (includes OutletId and ingredient adds/removes / name change).
        private string BuildChangedFieldsJsonForCreate(Template template)
        {
            // For create: record outlet (id + name if available) plus ingredients as "added"
            var added = (template.Ingredients ?? new List<TemplateIngredient>())
                .Select(i => new { i.ProductCode, i.ProductName })
                .ToList();

            var list = new List<object>();

            if (template.OutletId.HasValue || !string.IsNullOrEmpty(template.OutletName))
            {
                var dr = new { Id = template.OutletId, Name = template.OutletName };
                list.Add(new { DisplayName = "Outlet", Field = "Outlet", OldValue = (string?)null, NewValue = JsonSerializer.Serialize(dr) });
            }

            if (!string.IsNullOrEmpty(template.Name))
            {
                list.Add(new { DisplayName = "ชื่อ Template", Field = "Name", OldValue = (string?)null, NewValue = template.Name });
            }

            list.Add(new { DisplayName = "วัตถุดิบเพิ่ม", Field = "IngredientsAdded", OldValue = (string?)null, NewValue = JsonSerializer.Serialize(added) });

            return JsonSerializer.Serialize(list);
        }

        private string BuildChangedFieldsJsonForUpdate(Template oldT, Template updated)
        {
            var changes = new List<object>();

            // Name change
            if (!string.Equals(oldT.Name, updated.Name, StringComparison.Ordinal))
            {
                changes.Add(new { DisplayName = "ชื่อ Template", Field = "Name", OldValue = oldT.Name, NewValue = updated.Name });
            }

            // Outlet change (record id + name for readability)
            if (oldT.OutletId != updated.OutletId || !string.Equals(oldT.OutletName, updated.OutletName, StringComparison.Ordinal))
            {
                var oldDr = new { Id = oldT.OutletId, Name = oldT.OutletName };
                var newDr = new { Id = updated.OutletId, Name = updated.OutletName };
                changes.Add(new { DisplayName = "Outlet", Field = "Outlet", OldValue = JsonSerializer.Serialize(oldDr), NewValue = JsonSerializer.Serialize(newDr) });
            }

            // Ingredients: compute added and removed by ProductCode
            var oldCodes = new HashSet<string>(oldT.Ingredients.Select(i => i.ProductCode), StringComparer.OrdinalIgnoreCase);
            var newCodes = new HashSet<string>(updated.Ingredients.Select(i => i.ProductCode), StringComparer.OrdinalIgnoreCase);

            var addedCodes = newCodes.Except(oldCodes).ToList();
            var removedCodes = oldCodes.Except(newCodes).ToList();

            if (addedCodes.Any())
            {
                var added = updated.Ingredients
                    .Where(i => addedCodes.Contains(i.ProductCode))
                    .Select(i => new { i.ProductCode, i.ProductName })
                    .ToList();

                changes.Add(new { DisplayName = "วัตถุดิบเพิ่ม", Field = "IngredientsAdded", OldValue = (string?)null, NewValue = JsonSerializer.Serialize(added) });
            }

            if (removedCodes.Any())
            {
                var removed = oldT.Ingredients
                    .Where(i => removedCodes.Contains(i.ProductCode))
                    .Select(i => new { i.ProductCode, i.ProductName })
                    .ToList();

                changes.Add(new { DisplayName = "วัตถุดิบถูกลบ", Field = "IngredientsRemoved", OldValue = JsonSerializer.Serialize(removed), NewValue = (string?)null });
            }

            return changes.Count == 0 ? string.Empty : JsonSerializer.Serialize(changes);
        }

        private string BuildChangedFieldsJsonForDelete(Template existing)
        {
            var removed = (existing.Ingredients ?? new List<TemplateIngredient>())
                .Select(i => new { i.ProductCode, i.ProductName })
                .ToList();

            var list = new List<object>
            {
                new { DisplayName = "วัตถุดิบถูกลบ (เมื่อทำการลบ Template)", Field = "IngredientsRemoved", OldValue = JsonSerializer.Serialize(removed), NewValue = (string?)null },
                new { DisplayName = "Deleted Template", Field = "IsDeleted", OldValue = JsonSerializer.Serialize(new { existing.Id, existing.Name }), NewValue = (string?)null }
            };
            return JsonSerializer.Serialize(list);
        }

        // one-line summary
        private string BuildSummary(Template? oldT, Template? updated)
        {
            var parts = new List<string>();
            if (oldT == null)
            {
                var added = (updated?.Ingredients ?? new List<TemplateIngredient>()).Select(i => $"{i.ProductCode} - {i.ProductName}").ToList();
                if (added.Any()) parts.Add("Added: " + string.Join(", ", added));
                if (!string.IsNullOrEmpty(updated?.Name)) parts.Add($"Name: {updated.Name}");
                if (updated?.OutletId != null) parts.Add($"OutletId: {updated.OutletId}");
            }
            else
            {
                if (!string.Equals(oldT.Name, updated?.Name, StringComparison.Ordinal))
                    parts.Add($"Name: {oldT.Name} → {updated?.Name}");

                if (oldT.OutletId != updated?.OutletId)
                    parts.Add($"OutletId: {oldT.OutletId} → {updated?.OutletId}");

                var oldCodes = new HashSet<string>(oldT.Ingredients.Select(i => i.ProductCode), StringComparer.OrdinalIgnoreCase);
                var newCodes = new HashSet<string>(updated!.Ingredients.Select(i => i.ProductCode), StringComparer.OrdinalIgnoreCase);

                var added = updated.Ingredients.Where(i => !oldCodes.Contains(i.ProductCode)).Select(i => $"{i.ProductCode} - {i.ProductName}").ToList();
                var removed = oldT.Ingredients.Where(i => !newCodes.Contains(i.ProductCode)).Select(i => $"{i.ProductCode} - {i.ProductName}").ToList();

                if (added.Any()) parts.Add("Added: " + string.Join(", ", added));
                if (removed.Any()) parts.Add("Removed: " + string.Join(", ", removed));
            }

            var summary = string.Join("; ", parts);
            if (summary.Length > 1000) summary = summary.Substring(0, 1000);
            return summary;
        }

        // Use direct SQL (no stored procedures) matching existing DB schema
        public async Task<List<Template>> GetAllAsync()
        {
            var list = new List<Template>();

            const string sql = @"
                SELECT T.Id, T.Name, T.IngredientsJson, T.CreatedDate, T.LastModifiedDate, T.ModificationCount, T.IsDeleted, T.CreatedBy, T.LastModifiedBy, T.OutletId,
                       O.Name AS OutletName
                FROM dbo.Templates T
                LEFT JOIN dbo.Outlets O ON O.Id = T.OutletId
                ORDER BY T.IsDeleted ASC, T.CreatedDate DESC";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var ingredientsJson = reader.IsDBNull(2) ? null : reader.GetString(2);
                List<TemplateIngredient> ingredients = new();
                if (!string.IsNullOrWhiteSpace(ingredientsJson))
                {
                    try { ingredients = JsonSerializer.Deserialize<List<TemplateIngredient>>(ingredientsJson) ?? new(); }
                    catch { ingredients = new(); }
                }

                var createdDate = reader.IsDBNull(3) ? DateTime.MinValue : reader.GetDateTime(3);

                list.Add(new Template
                {
                    Id = reader.GetInt32(0),
                    Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Ingredients = ingredients,
                    CreatedDate = createdDate,
                    LastModifiedDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                    ModificationCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    IsDeleted = reader.IsDBNull(6) ? false : reader.GetBoolean(6),
                    OutletId = reader.IsDBNull(9) ? (int?)null : reader.GetInt32(9),
                    OutletName = reader.IsDBNull(10) ? null : reader.GetString(10)
                });
            }

            return list;
        }

        public async Task<Template?> GetByIdAsync(int id)
        {
            const string sql = @"
                SELECT T.Id, T.Name, T.IngredientsJson, T.CreatedDate, T.LastModifiedDate, T.ModificationCount, T.IsDeleted, T.CreatedBy, T.LastModifiedBy, T.OutletId,
                       O.Name AS OutletName
                FROM dbo.Templates T
                LEFT JOIN dbo.Outlets O ON O.Id = T.OutletId
                WHERE T.Id = @Id";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Id", id);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var ingredientsJson = reader.IsDBNull(2) ? null : reader.GetString(2);
                List<TemplateIngredient> ingredients = new();
                if (!string.IsNullOrWhiteSpace(ingredientsJson))
                {
                    try { ingredients = JsonSerializer.Deserialize<List<TemplateIngredient>>(ingredientsJson) ?? new(); }
                    catch { ingredients = new(); }
                }

                var createdDate = reader.IsDBNull(3) ? DateTime.MinValue : reader.GetDateTime(3);

                return new Template
                {
                    Id = reader.GetInt32(0),
                    Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Ingredients = ingredients,
                    CreatedDate = createdDate,
                    LastModifiedDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                    ModificationCount = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    IsDeleted = reader.IsDBNull(6) ? false : reader.GetBoolean(6),
                    OutletId = reader.IsDBNull(9) ? (int?)null : reader.GetInt32(9),
                    OutletName = reader.IsDBNull(10) ? null : reader.GetString(10)
                };
            }

            return null;
        }

        // Create using direct INSERT; write history row into TemplateModificationHistory
        public async Task<int> CreateAsync(Template template, string createdBy, string? source = "UI")
        {
            if (template == null) throw new ArgumentNullException(nameof(template));

            var ingredientsJson = JsonSerializer.Serialize(template.Ingredients ?? new List<TemplateIngredient>());
            var changedJson = BuildChangedFieldsJsonForCreate(template);
            var summary = BuildSummary(null, template);

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // Insert template
            const string insertSql = @"
                INSERT INTO dbo.Templates
                    (Name, IngredientsJson, CreatedDate, LastModifiedDate, ModificationCount, IsDeleted, CreatedBy, LastModifiedBy, OutletId)
                OUTPUT INSERTED.Id
                VALUES
                    (@Name, @IngredientsJson, SYSUTCDATETIME(), NULL, 0, 0, @CreatedBy, NULL, @OutletId);";

            await using (var tx = (SqlTransaction)await conn.BeginTransactionAsync())
            {
                try
                {
                    await using (var cmd = new SqlCommand(insertSql, conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@Name", template.Name ?? string.Empty);
                        cmd.Parameters.AddWithValue("@IngredientsJson", (object?)ingredientsJson ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@CreatedBy", (object?)createdBy ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@OutletId", (object?)template.OutletId ?? DBNull.Value);

                        var result = await cmd.ExecuteScalarAsync();
                        int newId = Convert.ToInt32(result);
                        template.Id = newId;
                        template.CreatedDate = DateTime.UtcNow;

                        // Insert history row
                        const string histSql = @"
                            INSERT INTO dbo.TemplateModificationHistory
                                (TemplateId, ActionDate, ActionType, ActionBy, OldValues, NewValues, ChangesDescription, Source, ChangesSummary)
                            VALUES
                                (@TemplateId, SYSUTCDATETIME(), 'Created', @ActionBy, NULL, @NewValues, @ChangesDescription, @Source, @ChangesSummary);";

                        await using (var hist = new SqlCommand(histSql, conn, tx))
                        {
                            hist.Parameters.AddWithValue("@TemplateId", newId);
                            hist.Parameters.AddWithValue("@ActionBy", (object?)createdBy ?? DBNull.Value);
                            hist.Parameters.AddWithValue("@NewValues", (object?)ingredientsJson ?? DBNull.Value);
                            hist.Parameters.AddWithValue("@ChangesDescription", (object?)changedJson ?? DBNull.Value);
                            hist.Parameters.AddWithValue("@Source", (object?)source ?? DBNull.Value);
                            hist.Parameters.AddWithValue("@ChangesSummary", (object?)summary ?? DBNull.Value);

                            await hist.ExecuteNonQueryAsync();
                        }

                        await tx.CommitAsync();
                        return newId;
                    }
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            }
        }

        // Update using direct UPDATE; write history into TemplateModificationHistory
        public async Task<bool> UpdateAsync(Template template, string modifiedBy, string? source = "UI")
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (template.Id <= 0) return false;

            var existing = await GetByIdAsync(template.Id);
            var changedJson = existing != null ? BuildChangedFieldsJsonForUpdate(existing, template) : string.Empty;
            var ingredientsJson = JsonSerializer.Serialize(template.Ingredients ?? new List<TemplateIngredient>());
            var summary = BuildSummary(existing, template);

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            const string updateSql = @"
                UPDATE dbo.Templates
                SET Name = @Name,
                    IngredientsJson = @IngredientsJson,
                    OutletId = @OutletId,
                    LastModifiedDate = SYSUTCDATETIME(),
                    LastModifiedBy = @ModifiedBy,
                    ModificationCount = ISNULL(ModificationCount,0) + 1
                WHERE Id = @TemplateId;";

            await using (var tx = (SqlTransaction)await conn.BeginTransactionAsync())
            {
                try
                {
                    await using (var cmd = new SqlCommand(updateSql, conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@TemplateId", template.Id);
                        cmd.Parameters.AddWithValue("@Name", template.Name ?? string.Empty);
                        cmd.Parameters.AddWithValue("@IngredientsJson", (object?)ingredientsJson ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ModifiedBy", (object?)modifiedBy ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@OutletId", (object?)template.OutletId ?? DBNull.Value);

                        await cmd.ExecuteNonQueryAsync();
                    }

                    // Insert history row
                    const string histSql = @"
                        INSERT INTO dbo.TemplateModificationHistory
                            (TemplateId, ActionDate, ActionType, ActionBy, OldValues, NewValues, ChangesDescription, Source, ChangesSummary)
                        VALUES
                            (@TemplateId, SYSUTCDATETIME(), 'Updated', @ActionBy, @OldValues, @NewValues, @ChangesDescription, @Source, @ChangesSummary);";

                    await using (var hist = new SqlCommand(histSql, conn, tx))
                    {
                        var oldVals = existing != null ? JsonSerializer.Serialize(existing.Ingredients) : null;
                        hist.Parameters.AddWithValue("@TemplateId", template.Id);
                        hist.Parameters.AddWithValue("@ActionBy", (object?)modifiedBy ?? DBNull.Value);
                        hist.Parameters.AddWithValue("@OldValues", (object?)oldVals ?? DBNull.Value);
                        hist.Parameters.AddWithValue("@NewValues", (object?)ingredientsJson ?? DBNull.Value);
                        hist.Parameters.AddWithValue("@ChangesDescription", (object?)changedJson ?? DBNull.Value);
                        hist.Parameters.AddWithValue("@Source", (object?)source ?? DBNull.Value);
                        hist.Parameters.AddWithValue("@ChangesSummary", (object?)summary ?? DBNull.Value);

                        await hist.ExecuteNonQueryAsync();
                    }

                    await tx.CommitAsync();
                    return true;
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            }
        }

        public async Task<bool> DeleteAsync(int id, string deletedBy, string? source = "UI")
        {
            var existing = await GetByIdAsync(id);
            var changedJson = existing != null ? BuildChangedFieldsJsonForDelete(existing) : string.Empty;
            var summary = BuildSummary(existing, null);

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            const string delSql = @"
                UPDATE dbo.Templates
                SET IsDeleted = 1,
                    LastModifiedDate = SYSUTCDATETIME(),
                    LastModifiedBy = @DeletedBy
                WHERE Id = @Id;";

            await using (var tx = (SqlTransaction)await conn.BeginTransactionAsync())
            {
                try
                {
                    await using (var cmd = new SqlCommand(delSql, conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@DeletedBy", (object?)deletedBy ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Id", id);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    const string histSql = @"
                        INSERT INTO dbo.TemplateModificationHistory
                            (TemplateId, ActionDate, ActionType, ActionBy, OldValues, NewValues, ChangesDescription, Source, ChangesSummary)
                        VALUES
                            (@TemplateId, SYSUTCDATETIME(), 'Deleted', @ActionBy, @OldValues, NULL, @ChangesDescription, @Source, @ChangesSummary);";

                    await using (var hist = new SqlCommand(histSql, conn, tx))
                    {
                        var oldVals = existing != null ? JsonSerializer.Serialize(existing.Ingredients) : null;
                        hist.Parameters.AddWithValue("@TemplateId", id);
                        hist.Parameters.AddWithValue("@ActionBy", (object?)deletedBy ?? DBNull.Value);
                        hist.Parameters.AddWithValue("@OldValues", (object?)oldVals ?? DBNull.Value);
                        hist.Parameters.AddWithValue("@ChangesDescription", (object?)changedJson ?? DBNull.Value);
                        hist.Parameters.AddWithValue("@Source", (object?)source ?? DBNull.Value);
                        hist.Parameters.AddWithValue("@ChangesSummary", (object?)summary ?? DBNull.Value);

                        await hist.ExecuteNonQueryAsync();
                    }

                    await tx.CommitAsync();
                    return true;
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            }
        }

        public async Task<List<HistoryRecord>> GetHistoryAsync(int templateId)
        {
            var history = new List<HistoryRecord>();

            const string sql = @"
                SELECT TemplateId, ActionDate, ActionType, ActionBy, OldValues, NewValues, ChangesDescription, ChangesSummary
                FROM dbo.TemplateModificationHistory
                WHERE TemplateId = @TemplateId
                ORDER BY ActionDate DESC";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@TemplateId", templateId);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                history.Add(new HistoryRecord
                {
                    TemplateId = reader.GetInt32(0),
                    ActionDate = reader.IsDBNull(1) ? DateTime.MinValue : reader.GetDateTime(1),
                    ActionType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    ActionBy = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    OldValues = reader.IsDBNull(4) ? null : reader.GetString(4),
                    NewValues = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ChangedFields = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ChangedSummary = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }

            return history;
        }

        public async Task<List<HistoryRecord>> GetAllHistoryAsync()
        {
            var history = new List<HistoryRecord>();

            const string sql = @"
                SELECT TemplateId, ActionDate, ActionType, ActionBy, OldValues, NewValues, ChangesDescription, ChangesSummary
                FROM dbo.TemplateModificationHistory
                ORDER BY ActionDate DESC";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                history.Add(new HistoryRecord
                {
                    TemplateId = reader.GetInt32(0),
                    ActionDate = reader.IsDBNull(1) ? DateTime.MinValue : reader.GetDateTime(1),
                    ActionType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    ActionBy = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    OldValues = reader.IsDBNull(4) ? null : reader.GetString(4),
                    NewValues = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ChangedFields = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ChangedSummary = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }

            return history;
        }

        public async Task<bool> ToggleStatusAsync(int templateId, string reason, string actionBy, string? source = "UI")
        {
            var existing = await GetByIdAsync(templateId);
            if (existing == null) return false;

            // สลับสถานะ IsDeleted
            bool newStatus = !existing.IsDeleted;
            string actionType = newStatus ? "ปิดการใช้งาน" : "เปิดการใช้งาน";

            // สร้าง ChangedFields JSON - ใช้คำว่า "ใช้งาน" และ "ปิด" ให้สั้นกระชับ
            var changedList = new List<object>
            {
                new
                {
                    DisplayName = "สถานะ",
                    Field = "IsDeleted",
                    OldValue = existing.IsDeleted ? "ปิด" : "ใช้งาน",
                    NewValue = newStatus ? "ปิด" : "ใช้งาน"
                },
                new
                {
                    DisplayName = "เหตุผล",
                    Field = "Reason",
                    OldValue = (string?)null,
                    NewValue = reason
                }
            };

            var changedJson = JsonSerializer.Serialize(changedList);
            var summary = $"สถานะ: {(existing.IsDeleted ? "ปิด" : "ใช้งาน")} → {(newStatus ? "ปิด" : "ใช้งาน")}, เหตุผล: {reason}";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            const string updateSql = @"
                UPDATE dbo.Templates
                SET IsDeleted = @IsDeleted,
                    LastModifiedDate = SYSUTCDATETIME(),
                    LastModifiedBy = @ModifiedBy
                WHERE Id = @Id;";

            await using (var tx = (SqlTransaction)await conn.BeginTransactionAsync())
            {
                try
                {
                    await using (var cmd = new SqlCommand(updateSql, conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@IsDeleted", newStatus);
                        cmd.Parameters.AddWithValue("@ModifiedBy", (object?)actionBy ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Id", templateId);

                        await cmd.ExecuteNonQueryAsync();
                    }

                    // บันทึกประวัติพร้อมเหตุผล
                    const string histSql = @"
                        INSERT INTO dbo.TemplateModificationHistory
                            (TemplateId, ActionDate, ActionType, ActionBy, OldValues, NewValues, ChangesDescription, Source, ChangesSummary)
                        VALUES
                            (@TemplateId, SYSUTCDATETIME(), @ActionType, @ActionBy, @OldValues, @NewValues, @ChangesDescription, @Source, @ChangesSummary);";

                    await using (var hist = new SqlCommand(histSql, conn, tx))
                    {
                        var oldVals = JsonSerializer.Serialize(existing);
                        hist.Parameters.AddWithValue("@TemplateId", templateId);
                        hist.Parameters.AddWithValue("@ActionType", actionType);
                        hist.Parameters.AddWithValue("@ActionBy", (object?)actionBy ?? DBNull.Value);
                        hist.Parameters.AddWithValue("@OldValues", (object?)oldVals ?? DBNull.Value);
                        hist.Parameters.AddWithValue("@NewValues", (object?)oldVals ?? DBNull.Value);
                        hist.Parameters.AddWithValue("@ChangesDescription", (object?)changedJson ?? DBNull.Value);
                        hist.Parameters.AddWithValue("@Source", (object?)source ?? DBNull.Value);
                        hist.Parameters.AddWithValue("@ChangesSummary", (object?)summary ?? DBNull.Value);

                        await hist.ExecuteNonQueryAsync();
                    }

                    await tx.CommitAsync();
                    return true;
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            }
        }
    }
}
