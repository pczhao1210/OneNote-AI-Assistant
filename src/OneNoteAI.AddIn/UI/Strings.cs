using System;
using System.Globalization;
using OneNoteAI.Settings;

namespace OneNoteAI.UI
{
    /// <summary>
    /// Internationalization (i18n) string resource provider.
    /// Supports Chinese (zh) and English (en). Defaults to system UI culture
    /// unless the user has explicitly chosen a language in Settings.
    /// </summary>
    public static class Strings
    {
        private static string _forcedLang; // null = auto, "zh" or "en"

        /// <summary>Current effective language code ("zh" or "en").</summary>
        public static string Lang
        {
            get
            {
                if (!string.IsNullOrEmpty(_forcedLang))
                    return _forcedLang;

                // Check user setting
                try
                {
                    string setting = SettingsManager.Current?.Language;
                    if (!string.IsNullOrWhiteSpace(setting) && !setting.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    {
                        return setting.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
                    }
                }
                catch { }

                // Fall back to system culture
                string culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                return culture == "zh" ? "zh" : "en";
            }
        }

        public static bool IsChinese { get { return Lang == "zh"; } }

        public static void SetLanguage(string lang) { _forcedLang = lang; }

        // ── Ribbon labels ──
        public static string RibbonTab        { get { return IsChinese ? "AI 助手" : "AI Assistant"; } }
        public static string RibbonGroup      { get { return IsChinese ? "AI 助手" : "AI Assistant"; } }
        public static string BtnSummarize     { get { return IsChinese ? "摘要" : "Summary"; } }
        public static string BtnGenerate      { get { return IsChinese ? "生成" : "Generate"; } }
        public static string BtnTemplate      { get { return IsChinese ? "模板" : "Template"; } }
        public static string BtnRewrite       { get { return IsChinese ? "改写" : "Rewrite"; } }
        public static string BtnQA            { get { return IsChinese ? "问答" : "Q&A"; } }
        public static string BtnTranslate     { get { return IsChinese ? "翻译" : "Translate"; } }
        public static string BtnTag           { get { return IsChinese ? "标签" : "Tag"; } }
        public static string BtnExtractTodos  { get { return IsChinese ? "提取待办" : "Todos"; } }
        public static string BtnSettings      { get { return IsChinese ? "设置" : "Settings"; } }
        public static string BtnHelp          { get { return IsChinese ? "帮助" : "Help"; } }

        // ── Common buttons ──
        public static string OK               { get { return IsChinese ? "确定" : "OK"; } }
        public static string Cancel           { get { return IsChinese ? "取消" : "Cancel"; } }
        public static string Close            { get { return IsChinese ? "关闭" : "Close"; } }
        public static string Insert           { get { return IsChinese ? "📥 插入页面" : "📥 Insert"; } }
        public static string Copy             { get { return IsChinese ? "📋 复制" : "📋 Copy"; } }
        public static string Regenerate       { get { return IsChinese ? "↻ 重新生成" : "↻ Retry"; } }
        public static string FollowUp         { get { return IsChinese ? "💬 继续提问" : "💬 Follow-up"; } }

        // ── Dialogs ──
        public static string AppTitle         { get { return "OneNote AI Assistant"; } }
        public static string ConfigApiFirst   { get { return IsChinese ? "请先在设置中配置 API Key。" : "Please configure your API Key in Settings first."; } }
        public static string PageEmpty        { get { return IsChinese ? "当前页面没有内容。" : "The current page has no content."; } }
        public static string PageFetchFail    { get { return IsChinese ? "获取页面失败：" : "Failed to get page: "; } }
        public static string OperationCancel  { get { return IsChinese ? "操作已取消。" : "Operation cancelled."; } }

        // ── Scope dialog ──
        public static string ScopeTitle       { get { return IsChinese ? "选择范围" : "Select Scope"; } }
        public static string ScopeHeader      { get { return IsChinese ? "请选择处理范围：" : "Select processing scope:"; } }
        public static string ScopeCurrentPage { get { return IsChinese ? "仅当前页面" : "Current page only"; } }
        public static string ScopeSection(string name, int count)
        {
            return IsChinese
                ? string.Format("当前分区「{0}」（共 {1} 页）", name, count)
                : string.Format("Section \"{0}\" ({1} pages)", name, count);
        }
        public static string ScopeHint        { get { return IsChinese ? "提示：分区范围会逐页处理后汇总，耗时与页数成正比。" : "Tip: Section scope processes each page individually. Time increases with page count."; } }

        // ── Summarize ──
        public static string SummarizeTitle   { get { return IsChinese ? "摘要结果" : "Summary Result"; } }
        public static string SummarizeFail    { get { return IsChinese ? "摘要生成失败：" : "Summary generation failed: "; } }

        // ── Generate ──
        public static string GenerateTitle    { get { return IsChinese ? "生成结果" : "Generation Result"; } }
        public static string GeneratePrompt   { get { return IsChinese ? "请输入生成指令：" : "Enter your generation instruction:"; } }
        public static string GeneratePlaceholder { get { return IsChinese ? "例如：写一篇关于XX的笔记..." : "e.g., Write a note about..."; } }

        // ── Rewrite ──
        public static string RewriteTitle     { get { return IsChinese ? "改写结果" : "Rewrite Result"; } }
        public static string RewritePrompt    { get { return IsChinese ? "请输入改写要求（留空则默认润色）：" : "Enter rewrite instructions (leave empty for default polishing):"; } }

        // ── Q&A ──
        public static string QATitle          { get { return IsChinese ? "问答（多轮）" : "Q&A (Multi-turn)"; } }
        public static string QACrossTitle(string section, int pages)
        {
            return IsChinese
                ? string.Format("跨页问答 - {0}（{1}页）", section, pages)
                : string.Format("Cross-page Q&A - {0} ({1} pages)", section, pages);
        }
        public static string QAPromptLabel    { get { return IsChinese ? "请输入您的问题：" : "Enter your question:"; } }
        public static string QAPlaceholder    { get { return IsChinese ? "在此输入您的问题..." : "Type your question here..."; } }
        public static string QAFollowLabel    { get { return IsChinese ? "请输入追问：" : "Enter follow-up question:"; } }

        // ── Translate ──
        public static string TranslateTitle   { get { return IsChinese ? "翻译结果" : "Translation Result"; } }
        public static string TranslateLangPrompt { get { return IsChinese ? "请输入目标语言（如：英文、中文、日文）：" : "Enter target language (e.g., English, Chinese, Japanese):"; } }
        public static string TranslatePlaceholder { get { return IsChinese ? "英文" : "Chinese"; } }

        // ── Tag ──
        public static string TagTitle         { get { return IsChinese ? "自动标签与分类" : "Auto Tag & Classify"; } }

        // ── Template ──
        public static string TemplateChoose   { get { return IsChinese ? "选择模板" : "Choose Template"; } }
        public static string TemplateChooseLabel { get { return IsChinese ? "请选择要使用的笔记模板：" : "Select a note template:"; } }

        // ── Todos ──
        public static string TodosTitle       { get { return IsChinese ? "待办提取结果" : "Todo Extraction"; } }
        public static string TodosNativeHeader { get { return IsChinese ? "## 已有标记（OneNote 原生）\n" : "## Tagged Items (OneNote Native)\n"; } }
        public static string TodosStats(int pending, int completed, int total)
        {
            return IsChinese
                ? string.Format("**统计：** {0} 项未完成，{1} 项已完成（共 {2} 项）", pending, completed, total)
                : string.Format("**Stats:** {0} pending, {1} completed ({2} total)", pending, completed, total);
        }
        public static string TodosNoneFound   { get { return IsChinese ? "（未发现需要执行的待办事项。当前内容为信息记录/推荐列表。）" : "(No actionable todos found. Content appears to be informational/reference list.)"; } }

        // ── Help ──
        public static string HelpTitle        { get { return IsChinese ? "使用帮助" : "Help"; } }

        // ── Settings ──
        public static string SettingsTitle    { get { return IsChinese ? "设置" : "Settings"; } }
        public static string SettingsLangLabel { get { return IsChinese ? "界面语言：" : "UI Language:"; } }
        public static string SettingsLangAuto  { get { return IsChinese ? "自动（跟随系统）" : "Auto (follow system)"; } }

        // ── Progress ──
        public static string ProgressAI       { get { return IsChinese ? "AI 正在处理..." : "AI is processing..."; } }
        public static string ProgressCancel   { get { return IsChinese ? "取消生成" : "Cancel"; } }
    }
}
