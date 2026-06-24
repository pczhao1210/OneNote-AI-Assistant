using System;
using OneNoteAI.Settings;

namespace OneNoteAI.AI
{
    public static class PromptTemplates
    {
        // ── Built-in defaults (used when user has no override) ──
        public const string SummarizeSystemDefault = "你是一个专业的笔记摘要助手。请根据用户提供的笔记内容，生成简洁准确的摘要。摘要应突出要点，保留关键信息，使用清晰的中文表达。";
        public const string GenerateSystemDefault = "你是一个专业的内容创作助手。请根据用户的指令，生成高质量的内容。内容应结构清晰、逻辑连贯，使用专业准确的中文表达。";
        public const string RewriteSystemDefault = "你是一个专业的文本改写助手。请根据用户的要求改写给定的文本。改写后的文本应保持原意，但在表达方式、语气或风格上进行改进。";
        public const string QASystemDefault = "你是一个知识渊博的问答助手。请根据给定的笔记内容回答用户的问题。回答应准确、有条理，如果笔记中没有相关信息，请如实告知。";
        public const string ExtractTodosSystemDefault = "你是一个任务提取助手。请从用户提供的笔记内容中识别并提取所有待办事项、任务和行动项。以清晰的列表形式输出，每个待办事项独立一行，以\"☐\"开头。";

        // ── Live system prompts (read user override if set, else default) ──
        public static string SummarizeSystem
        {
            get { return Pick(SettingsManager.Current.PromptOverrides?.Summarize, SummarizeSystemDefault); }
        }

        public static string GenerateSystem
        {
            get { return Pick(SettingsManager.Current.PromptOverrides?.Generate, GenerateSystemDefault); }
        }

        public static string RewriteSystem
        {
            get { return Pick(SettingsManager.Current.PromptOverrides?.Rewrite, RewriteSystemDefault); }
        }

        public static string QASystem
        {
            get { return Pick(SettingsManager.Current.PromptOverrides?.QA, QASystemDefault); }
        }

        public static string ExtractTodosSystem
        {
            get { return Pick(SettingsManager.Current.PromptOverrides?.ExtractTodos, ExtractTodosSystemDefault); }
        }

        private static string Pick(string overrideText, string fallback)
        {
            return string.IsNullOrWhiteSpace(overrideText) ? fallback : overrideText;
        }

        // ── User prompt builders ──

        /// Build user prompt for summarize feature
        public static string BuildSummarizePrompt(string noteContent, bool isSectionSummary = false)
        {
            if (isSectionSummary)
            {
                return string.Format("请对以下多个笔记页面的内容生成一个综合摘要：\n\n{0}", noteContent);
            }

            return string.Format("请对以下笔记内容生成摘要：\n\n{0}", noteContent);
        }

        /// Build user prompt for generate feature
        public static string BuildGeneratePrompt(string userInstruction, string existingContent = null)
        {
            if (string.IsNullOrWhiteSpace(existingContent))
            {
                return string.Format("请根据以下指令生成内容：\n\n{0}", userInstruction);
            }

            return string.Format("参考以下现有笔记内容：\n\n{0}\n\n请根据以下指令生成新内容：\n\n{1}", existingContent, userInstruction);
        }

        /// Build user prompt for rewrite feature
        public static string BuildRewritePrompt(string originalText, string rewriteInstruction = null)
        {
            string instruction = string.IsNullOrWhiteSpace(rewriteInstruction)
                ? "请改写以下文本，使其更加清晰、专业："
                : string.Format("请按照以下要求改写文本：{0}\n\n原文：", rewriteInstruction);

            return string.Format("{0}\n\n{1}", instruction, originalText);
        }

        /// Build user prompt for QA feature
        public static string BuildQAPrompt(string noteContent, string question)
        {
            return string.Format("以下是笔记内容：\n\n{0}\n\n请回答以下问题：\n{1}", noteContent, question);
        }

        /// Build user prompt for extract todos feature
        public static string BuildExtractTodosPrompt(string noteContent)
        {
            return string.Format("请从以下笔记内容中提取所有待办事项和行动项：\n\n{0}", noteContent);
        }

        /// Get system prompt by task type
        public static string GetSystemPrompt(string taskType)
        {
            switch ((taskType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "summarize":
                    return SummarizeSystem;
                case "generate":
                    return GenerateSystem;
                case "rewrite":
                    return RewriteSystem;
                case "qa":
                    return QASystem;
                case "extract-todos":
                    return ExtractTodosSystem;
                default:
                    return GenerateSystem;
            }
        }
    }
}
