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

        // Stored under %AppData%: the install folder is under Program Files, which normal users can't write to,
        // so saving next to the exe silently failed and settings were lost on every restart.
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScrapIt", "SerialToTcp");
        public static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

        // Where versions up to 1.0 kept it; read once as a fallback so existing mappings carry over.
        private static readonly string LegacySettingsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        /// <summary>Loads settings. <paramref name="warning"/> is set if something went wrong the user should know about.</summary>
        public static AppSettings Load(out string? warning)
        {
            warning = null;
            string path = File.Exists(SettingsPath) ? SettingsPath
                        : File.Exists(LegacySettingsPath) ? LegacySettingsPath
                        : "";
            if (path == "") return new AppSettings();

            try
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                settings.Mappings ??= new();
                if (path == LegacySettingsPath)
                {
                    settings.Save(out _);
                    warning = $"Imported settings from {LegacySettingsPath}; they are now stored in {SettingsPath}.";
                }
                return settings;
            }
            catch (Exception ex)
            {
                // Keep the broken file rather than letting the next Save overwrite it with an empty list.
                string backup = path + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try { File.Copy(path, backup, overwrite: true); } catch { backup = "(backup failed)"; }
                warning = $"Could not read settings from {path} ({ex.Message}). Starting with empty settings; old file saved as {backup}.";
                return new AppSettings();
            }
        }

        /// <summary>Saves settings; returns false and an error message instead of throwing.</summary>
        public bool Save(out string? error)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(this, options);
                // Write-then-rename so a crash or power cut mid-save can't leave a half-written file.
                var tmp = SettingsPath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, SettingsPath, overwrite: true);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = $"Could not save settings to {SettingsPath}: {ex.Message}";
                return false;
            }
        }
    }
}
