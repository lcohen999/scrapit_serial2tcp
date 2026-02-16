using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SerialToTcp
{
    public class PortMapping
    {
        public string ComPort { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        public int TcpPort { get; set; } = 4001;
    }

    public class AppSettings
    {
        public List<PortMapping> Mappings { get; set; } = new();
        public bool StartMinimized { get; set; } = false;
        public bool AutoStart { get; set; } = false;

        private static readonly string SettingsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}
