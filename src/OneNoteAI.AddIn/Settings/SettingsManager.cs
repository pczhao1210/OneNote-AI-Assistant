using System;
using System.IO;
using Newtonsoft.Json;

namespace OneNoteAI.Settings
{
    /// <summary>
    /// Supported AI provider presets. The user can also choose "Custom"
    /// and provide their own base URL.
    /// </summary>
    public enum AiProvider
    {
        DeepSeek = 0,
        OpenAI = 1,
        Ollama = 2,
        Custom = 3,
        Qwen = 4,
        Zhipu = 5,
        Moonshot = 6,
        MiniMax = 7,
        Gemini = 8,
        Claude = 9,
        OpenRouter = 10
    }

    public class ProviderSettings
    {
        [JsonProperty("apiKey")]
        public string ApiKey { get; set; }

        [JsonProperty("apiBaseUrl")]
        public string ApiBaseUrl { get; set; }

        [JsonProperty("defaultModel")]
        public string DefaultModel { get; set; }
    }

    public class AppSettings
    {
        [JsonProperty("provider")]
        public AiProvider Provider { get; set; } = AiProvider.DeepSeek;

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
        public string Language { get; set; } = "auto";

        [JsonProperty("promptOverrides")]
        public PromptOverrides PromptOverrides { get; set; } = new PromptOverrides();

        // Each provider keeps its own encrypted key and last-used endpoint/model.
        // The three legacy fields above mirror the active entry for backward compatibility.
        [JsonProperty("providerSettings")]
        public System.Collections.Generic.Dictionary<AiProvider, ProviderSettings> ProviderSettings { get; set; }
            = new System.Collections.Generic.Dictionary<AiProvider, ProviderSettings>();

        /// <summary>
        /// Returns the effective API base URL based on the selected provider.
        /// If the user has set a custom URL, it takes precedence.
        /// </summary>
        public string GetEffectiveBaseUrl()
        {
            if (!string.IsNullOrWhiteSpace(ApiBaseUrl))
            {
                return ApiBaseUrl;
            }

            switch (Provider)
            {
                case AiProvider.OpenAI:
                    return "https://api.openai.com/v1";
                case AiProvider.Qwen:
                    return "https://dashscope.aliyuncs.com/compatible-mode/v1";
                case AiProvider.Zhipu:
                    return "https://open.bigmodel.cn/api/paas/v4";
                case AiProvider.Moonshot:
                    return "https://api.moonshot.cn/v1";
                case AiProvider.MiniMax:
                    return "https://api.minimax.chat/v1";
                case AiProvider.Gemini:
                    return "https://generativelanguage.googleapis.com/v1beta/openai";
                case AiProvider.Claude:
                    return "https://api.anthropic.com";
                case AiProvider.Ollama:
                    return "http://localhost:11434/v1";
                case AiProvider.OpenRouter:
                    return "https://openrouter.ai/api/v1";
                case AiProvider.DeepSeek:
                default:
                    return "https://api.deepseek.com";
            }
        }

        /// <summary>
        /// Returns the default model name for the selected provider.
        /// </summary>
        public string GetDefaultModelForProvider()
        {
            switch (Provider)
            {
                case AiProvider.OpenAI:
                    return "gpt-4.1-mini";
                case AiProvider.Qwen:
                    return "qwen-plus";
                case AiProvider.Zhipu:
                    return "glm-4.5-air";
                case AiProvider.Moonshot:
                    return "moonshot-v1-8k";
                case AiProvider.MiniMax:
                    return "MiniMax-Text-01";
                case AiProvider.Gemini:
                    return "gemini-2.5-flash";
                case AiProvider.Claude:
                    return "claude-sonnet-4-20250514";
                case AiProvider.Ollama:
                    return "qwen2.5:7b";
                case AiProvider.OpenRouter:
                    return "openai/gpt-4.1-mini";
                case AiProvider.DeepSeek:
                default:
                    return "deepseek-chat";
            }
        }
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
            ProviderSettings profile = GetActiveProviderSettings();
            profile.ApiKey = Current.ApiKey;
            Save();
        }

        public static ProviderSettings GetActiveProviderSettings()
        {
            AppSettings settings = Current;
            if (settings.ProviderSettings == null)
            {
                settings.ProviderSettings = new System.Collections.Generic.Dictionary<AiProvider, ProviderSettings>();
            }
            ProviderSettings profile;
            if (!settings.ProviderSettings.TryGetValue(settings.Provider, out profile) || profile == null)
            {
                profile = new ProviderSettings
                {
                    ApiKey = settings.ApiKey,
                    ApiBaseUrl = settings.ApiBaseUrl,
                    DefaultModel = settings.DefaultModel
                };
                settings.ProviderSettings[settings.Provider] = profile;
            }
            return profile;
        }

        public static void SwitchProvider(AiProvider provider)
        {
            AppSettings settings = Current;
            ProviderSettings oldProfile = GetActiveProviderSettings();
            oldProfile.ApiKey = settings.ApiKey;
            oldProfile.ApiBaseUrl = settings.ApiBaseUrl;
            oldProfile.DefaultModel = settings.DefaultModel;

            settings.Provider = provider;
            ProviderSettings profile;
            if (!settings.ProviderSettings.TryGetValue(provider, out profile) || profile == null)
            {
                profile = new ProviderSettings();
                settings.ProviderSettings[provider] = profile;
            }
            settings.ApiKey = profile.ApiKey;
            settings.ApiBaseUrl = string.IsNullOrWhiteSpace(profile.ApiBaseUrl) ? settings.GetEffectiveBaseUrl() : profile.ApiBaseUrl;
            settings.DefaultModel = string.IsNullOrWhiteSpace(profile.DefaultModel) ? settings.GetDefaultModelForProvider() : profile.DefaultModel;
        }

        /// <summary>
        /// Check if API key is configured
        /// </summary>
        public static bool HasApiKey()
        {
            return Current.Provider == AiProvider.Ollama || !string.IsNullOrEmpty(GetApiKey());
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

            ProviderSettings profile = GetActiveProviderSettings();
            profile.ApiKey = settings.ApiKey;
            profile.ApiBaseUrl = settings.ApiBaseUrl;
            profile.DefaultModel = settings.DefaultModel;
        }
    }
}
