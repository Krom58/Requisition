using System;
using System.IO;
using System.Text.Json;

namespace Requisition.Helpers
{
    public class AppSettings
    {
        public ConnectionStrings ConnectionStrings { get; set; } = new();
    }

    public class ConnectionStrings
    {
        public string DefaultConnection { get; set; } = string.Empty;
    }

    public static class ConfigurationHelper
    {
        private static AppSettings? _settings;

        public static string GetConnectionString()
        {
            if (_settings == null)
            {
                LoadSettings();
            }

            // ✅ เพิ่ม MultipleActiveResultSets=True
            var baseConnectionString = _settings?.ConnectionStrings.DefaultConnection ??
                   "Server=localhost;Database=RequisitionDB;Integrated Security=True;TrustServerCertificate=True;";
            
            // ตรวจสอบว่ามี MultipleActiveResultSets อยู่แล้วหรือไม่
            if (!baseConnectionString.Contains("MultipleActiveResultSets", StringComparison.OrdinalIgnoreCase))
            {
                baseConnectionString += "MultipleActiveResultSets=True;";
            }
            
            return baseConnectionString;
        }

        private static void LoadSettings()
        {
            try
            {
                var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    _settings = JsonSerializer.Deserialize<AppSettings>(json);
                }
                else
                {
                    _settings = new AppSettings();
                }
            }
            catch
            {
                _settings = new AppSettings();
            }
        }
    }
}