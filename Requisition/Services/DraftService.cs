using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Requisition.Models;

namespace Requisition.Services
{
    public class DraftService
    {
        private readonly string _basePath;

        public DraftService()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _basePath = Path.Combine(local, "Requisition", "Drafts");
            Directory.CreateDirectory(_basePath);
        }

        private string GetPath(string key) => Path.Combine(_basePath, $"{key}.json");

        // key: "transfer-{id}" or "transfer-new-{user}" for new transfer drafts
        public async Task SaveDraftAsync(string key, TransferDraft draft)
        {
            var path = GetPath(key);
            var json = JsonSerializer.Serialize(draft, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }

        public async Task<TransferDraft?> LoadDraftAsync(string key)
        {
            var path = GetPath(key);
            if (!File.Exists(path)) return null;
            try
            {
                var json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<TransferDraft>(json);
            }
            catch
            {
                // ignore malformed file, treat as no-draft
                return null;
            }
        }

        public Task DeleteDraftAsync(string key)
        {
            var path = GetPath(key);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }
    }
}
