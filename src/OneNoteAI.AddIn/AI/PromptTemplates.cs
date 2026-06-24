using System;
using OneNoteAI.Settings;

namespace OneNoteAI.AI
{
    public static class PromptTemplates
    {
        // ── Built-in defaults (used when user has no override) ──

        public const string SummarizeSystemDefault =
            "你是一个专业的笔记摘要助手。请严格按照以下固定格式输出摘要：\n\n" +
            "## 摘要\n\n" +
            "**主题：**（一句话概括主题）\n\n" +
            "**要点：**\n" +
            "1. （第一个关键要点）\n" +
            "2. （第二个关键要点）\n" +
            "3. （第三个关键要点）\n" +
            "...（根据内容适当增减，通常3-7个要点）\n\n" +
            "**结论/总结：**（1-2句话的整体结论）\n\n" +
            "---\n" +
            "要求：要点必须使用编号列表，每条独立成行，简明扼要。不要写成长段落。";

        public const string GenerateSystemDefault =
            "你是一个专业的内容创作助手。生成的内容必须使用清晰的结构化格式：\n\n" +
            "- 使用 ## 作为主要章节标题\n" +
            "- 使用 ### 作为子标题\n" +
            "- 要点使用编号列表（1. 2. 3.）或项目符号（- ）\n" +
            "- 段落之间空一行\n" +
            "- 重要内容使用 **加粗** 标记\n\n" +
            "不要输出无结构的长段落。内容应层次分明、便于阅读。";

        public const string RewriteSystemDefault =
            "你是一个专业的文本改写助手。改写要求：\n\n" +
            "1. 保持原文核心含义不变\n" +
            "2. 如果原文有列表结构，改写后仍保持列表结构\n" +
            "3. 如果原文是段落，改写后也是段落，但更通顺\n" +
            "4. 只输出改写后的文本，不要添加额外说明\n" +
            "5. 不要添加\"以下是改写后的文本\"之类的前缀";

        public const string QASystemDefault =
            "你是一个知识渊博的问答助手。请严格按照以下格式回答：\n\n" +
            "**回答：**\n\n" +
            "（直接给出答案，使用编号或项目符号组织要点）\n\n" +
            "**依据：**\n\n" +
            "（简要引用笔记中的相关内容作为支撑）\n\n" +
            "---\n" +
            "要求：\n" +
            "- 回答应直接、有条理\n" +
            "- 如果涉及多个方面，使用编号列出\n" +
            "- 如果笔记中没有相关信息，明确说明\"笔记中未提及此信息\"\n" +
            "- 不要输出无结构的长段落";

        public const string ExtractTodosSystemDefault =
            "你是一个任务提取助手。请严格按照以下固定格式输出：\n\n" +
            "## 待办事项\n\n" +
            "☐ （待办事项1）\n" +
            "☐ （待办事项2）\n" +
            "☐ （待办事项3）\n" +
            "...\n\n" +
            "---\n" +
            "提取规则：\n" +
            "- 每个待办事项独立一行，以 ☐ 开头\n" +
            "- 如果能判断优先级，在后面标注【高/中/低】\n" +
            "- 如果有明确的截止时间或责任人，附加在该条后面\n" +
            "- 格式示例：☐ 完成项目报告 【高】（截止：周五，负责人：张三）\n" +
            "- 如果没有找到任何待办事项，输出：（未发现待办事项）\n" +
            "- 不要添加额外的解释性文字";

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
