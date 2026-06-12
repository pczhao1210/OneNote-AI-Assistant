using System;
using System.IO;
using Newtonsoft.Json;

namespace OneNoteAI.Settings
{
    public class AppSettings
    {
        [JsonProperty("apiKey")]
        public string ApiKey { get; set; }

        [JsonProperty("apiBaseUrl")]
        public string ApiBaseUrl { get; set; } = "https://api.deepseek.com";

        [JsonProperty("defaultModel")]
        public string DefaultModel { get; set; } = "deepseek-chat";

        [JsonProperty("autoSelectModel")]
        public bool AutoSelectModel { get; set; } = true;

        [JsonProperty("temperature")]
        public double Temperature { get; set; } = 0.7;

        [JsonProperty("maxTokens")]
        public int MaxTokens { get; set; } = 4096;

        [JsonProperty("language")]
        public string Language { get; set; } = "zh-CN";

        [JsonProperty("promptOverrides")]
        public PromptOverrides PromptOverrides { get; set; } = new PromptOverrides();
    }

    /// <summary>
    /// User-customizable system prompts. Any field left null/empty means
    /// the built-in default in <see cref="OneNoteAI.AI.PromptTemplates"/>
    /// will be used.
    /// </summary>
    public class PromptOverrides
    {
        [JsonProperty("summarize")]
        public string Summarize { get; set; }

        [JsonProperty("generate")]
        public string Generate { get; set; }

        [JsonProperty("rewrite")]
        public string Rewrite { get; set; }

        [JsonProperty("qa")]
        public string QA { get; set; }

        [JsonProperty("extractTodos")]
        public string ExtractTodos { get; set; }
    }

    public class SettingsManager
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OneNoteAI");

        private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

        private static AppSettings _current;
        private static readonly object _lock = new object();

        public static AppSettings Current
        {
            get
            {
                if (_current == null)
                {
                    Load();
                }

                return _current;
            }
        }

        public static void Load()
        {
            lock (_lock)
            {
                if (!File.Exists(SettingsFile))
                {
                    _current = CreateDefaultSettings();
                    return;
                }

                try
                {
                    string json = File.ReadAllText(SettingsFile);
                    AppSettings settings = JsonConvert.DeserializeObject<AppSettings>(json);
                    _current = settings ?? CreateDefaultSettings();
                    ApplyDefaults(_current);
                }
                catch
                {
                    _current = CreateDefaultSettings();
                }
            }
        }

        public static void Save()
        {
            lock (_lock)
            {
                if (_current == null)
                {
                    _current = CreateDefaultSettings();
                }

                ApplyDefaults(_current);
                Directory.CreateDirectory(SettingsDir);

                string json = JsonConvert.SerializeObject(_current, Formatting.Indented);
                File.WriteAllText(SettingsFile, json);
            }
        }

        /// <summary>
        /// Get decrypted API key
        /// </summary>
        public static string GetApiKey()
        {
            string encrypted = Current.ApiKey;
            if (string.IsNullOrEmpty(encrypted))
            {
                return string.Empty;
            }

            try
            {
                return EncryptionHelper.Decrypt(encrypted);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Set and encrypt API key
        /// </summary>
        public static void SetApiKey(string plainKey)
        {
            Current.ApiKey = EncryptionHelper.Encrypt(plainKey);
            Save();
        }

        /// <summary>
        /// Check if API key is configured
        /// </summary>
        public static bool HasApiKey()
        {
            return !string.IsNullOrEmpty(GetApiKey());
        }

        private static AppSettings CreateDefaultSettings()
        {
            return new AppSettings();
        }

        private static void ApplyDefaults(AppSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.ApiBaseUrl))
            {
                settings.ApiBaseUrl = "https://api.deepseek.com";
            }

            if (string.IsNullOrWhiteSpace(settings.DefaultModel))
            {
                settings.DefaultModel = "deepseek-chat";
            }

            if (settings.Temperature < 0.0 || settings.Temperature > 2.0)
            {
                settings.Temperature = 0.7;
            }

            if (settings.MaxTokens < 512 || settings.MaxTokens > 8192)
            {
                settings.MaxTokens = 4096;
            }

            if (string.IsNullOrWhiteSpace(settings.Language))
            {
                settings.Language = "zh-CN";
            }

            if (settings.PromptOverrides == null)
            {
                settings.PromptOverrides = new PromptOverrides();
            }
        }
    }
}
