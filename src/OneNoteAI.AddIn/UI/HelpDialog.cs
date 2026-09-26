using System;
using System.Drawing;
using System.Windows.Forms;

namespace OneNoteAI.UI
{
    /// <summary>
    /// Classic Windows-style help dialog with left-side topic tree
    /// and right-side content panel. Uses TreeView + RichTextBox layout
    /// similar to traditional CHM help viewers.
    /// </summary>
    public class HelpDialog : DpiAwareForm
    {
        private readonly TreeView _treeTopics;
        private readonly RichTextBox _rtbContent;

        public HelpDialog()
        {
            Text = "OneNote AI Assistant - 帮助";
            ClientSize = new Size(820, 560);
            MinimumSize = new Size(700, 450);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = true;
            TopMost = true;
            Theme.ApplyTo(this);
            Icon = null;

            // ── Header ──
            Panel header = Theme.CreateHeader("使用帮助");
            Panel stripe = Theme.CreateAccentStripe();

            // ── Two-panel layout (no SplitContainer to avoid MinSize bugs) ──
            Panel treePanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 210,
                BackColor = Color.FromArgb(252, 250, 254),
                Padding = new Padding(8, 8, 4, 8)
            };

            Panel splitterBar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 1,
                BackColor = Theme.BgCardBorder
            };

            Label treeHeader = new Label
            {
                Text = "目录",
                Dock = DockStyle.Top,
                Height = 28,
                Font = Theme.FontHeading,
                ForeColor = Theme.Purple,
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(4, 0, 0, 4)
            };

            _treeTopics = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(252, 250, 254),
                Font = Theme.FontContent,
                ForeColor = Theme.TextPrimary,
                ItemHeight = 26,
                ShowLines = true,
                ShowRootLines = true,
                HideSelection = false,
                FullRowSelect = true
            };
            _treeTopics.AfterSelect += OnTopicSelected;

            treePanel.Controls.Add(_treeTopics);
            treePanel.Controls.Add(treeHeader);

            // ── Right: Content area ──
            Panel contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(16, 12, 16, 12)
            };

            _rtbContent = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                Font = Theme.FontContent,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            contentPanel.Controls.Add(_rtbContent);

            // ── Footer ──
            Panel footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = Theme.BgPage
            };
            footer.Paint += delegate(object s, PaintEventArgs pe)
            {
                using (Pen pen = new Pen(Theme.BgCardBorder))
                    pe.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };

            Button btnClose = Theme.CreateSecondaryButton("关闭");
            btnClose.Location = new Point(footer.Width - 130, 8);
            btnClose.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            btnClose.Click += delegate { Close(); };
            footer.Controls.Add(btnClose);

            // ── Assembly (order matters for Dock: Fill added first) ──
            Controls.Add(contentPanel);
            Controls.Add(splitterBar);
            Controls.Add(treePanel);
            Controls.Add(footer);
            Controls.Add(header);
            Controls.Add(stripe);
            CancelButton = btnClose;

            BuildTopicTree();
            if (_treeTopics.Nodes.Count > 0)
            {
                _treeTopics.SelectedNode = _treeTopics.Nodes[0];
            }
        }

        private void BuildTopicTree()
        {
            bool zh = Strings.IsChinese;

            TreeNode root = new TreeNode(zh ? "快速入门" : "Quick Start") { Tag = "quickstart" };
            _treeTopics.Nodes.Add(root);

            TreeNode features = new TreeNode(zh ? "功能说明" : "Features");
            features.Nodes.Add(new TreeNode(zh ? "摘要" : "Summary") { Tag = "summarize" });
            features.Nodes.Add(new TreeNode(zh ? "生成" : "Generate") { Tag = "generate" });
            features.Nodes.Add(new TreeNode(zh ? "模板" : "Template") { Tag = "template" });
            features.Nodes.Add(new TreeNode(zh ? "改写" : "Rewrite") { Tag = "rewrite" });
            features.Nodes.Add(new TreeNode(zh ? "问答" : "Q&A") { Tag = "qa" });
            features.Nodes.Add(new TreeNode(zh ? "翻译" : "Translate") { Tag = "translate" });
            features.Nodes.Add(new TreeNode(zh ? "标签" : "Tag") { Tag = "tag" });
            features.Nodes.Add(new TreeNode(zh ? "提取待办" : "Todos") { Tag = "todos" });
            _treeTopics.Nodes.Add(features);
            features.Expand();

            _treeTopics.Nodes.Add(new TreeNode(zh ? "设置说明" : "Settings") { Tag = "settings" });
            _treeTopics.Nodes.Add(new TreeNode(zh ? "多模型支持" : "Providers") { Tag = "providers" });
            _treeTopics.Nodes.Add(new TreeNode(zh ? "常见问题" : "FAQ") { Tag = "faq" });
            _treeTopics.Nodes.Add(new TreeNode(zh ? "关于" : "About") { Tag = "about" });
        }

        protected override void OnDpiScaleChanged(EventArgs e)
        {
            base.OnDpiScaleChanged(e);
            _treeTopics.ItemHeight = ScaleLogical(26);
            string topic = _treeTopics.SelectedNode?.Tag as string;
            if (!string.IsNullOrEmpty(topic))
            {
                ShowTopic(topic);
            }
        }

        private void OnTopicSelected(object sender, TreeViewEventArgs e)
        {
            string tag = e.Node.Tag as string;
            if (string.IsNullOrEmpty(tag))
            {
                _rtbContent.Clear();
                return;
            }
            ShowTopic(tag);
        }

        private void ShowTopic(string topic)
        {
            _rtbContent.Clear();
            string content = Strings.IsChinese ? GetTopicZh(topic) : GetTopicEn(topic);
            // Simple renderer: parse lines for basic formatting
            string[] lines = content.Split('\n');
            foreach (string line in lines)
            {
                string trimmed = line.TrimEnd('\r');
                if (trimmed.StartsWith("# "))
                {
                    AppendStyled(trimmed.Substring(2) + "\n", Theme.FontTitle, Theme.Purple);
                }
                else if (trimmed.StartsWith("## "))
                {
                    AppendStyled(trimmed.Substring(3) + "\n", Theme.FontHeading, Theme.Purple);
                }
                else if (trimmed.StartsWith("- "))
                {
                    AppendStyled("  •  ", Theme.FontContent, Theme.Purple);
                    AppendStyled(trimmed.Substring(2) + "\n", Theme.FontContent, Theme.TextPrimary);
                }
                else
                {
                    AppendStyled(trimmed + "\n", Theme.FontContent, Theme.TextPrimary);
                }
            }
            _rtbContent.SelectionStart = 0;
            _rtbContent.ScrollToCaret();
        }

        private void AppendStyled(string text, Font font, Color color)
        {
            _rtbContent.SelectionStart = _rtbContent.TextLength;
            _rtbContent.SelectionLength = 0;
            using (Font scaledFont = CreateDpiFont(font))
            {
                _rtbContent.SelectionFont = scaledFont;
            }
            _rtbContent.SelectionColor = color;
            _rtbContent.AppendText(text);
        }

        private static string GetTopicZh(string topic)
        {
            switch (topic)
            {
                case "quickstart": return
@"# 快速入门

欢迎使用 OneNote AI Assistant！以下是快速上手步骤：

## 第一步：配置 API Key
- 点击 Ribbon 栏的「设置」按钮
- 在 API 设置中输入您的 DeepSeek API Key
- 点击「测试连接」确认连接正常
- 点击「确定」保存

## 第二步：开始使用
- 打开任意 OneNote 页面
- 在 Ribbon 栏找到「AI 助手」标签页
- 选择需要的功能（摘要、生成、问答等）
- 在弹出的对话框中查看 AI 结果
- 点击「插入页面」将结果写入笔记

## 小提示
- 所有功能都支持「重新生成」，不满意可以重试
- 问答功能支持多轮对话（「继续提问」按钮）
- 翻译和改写功能会自动识别选中的文本";

                case "summarize": return
@"# 摘要

对当前页面或整个分区的内容生成 AI 摘要。

## 使用方法
- 点击「摘要」按钮
- 选择范围：「仅当前页面」或「整个分区」
- AI 会自动分析内容并生成结构化摘要

## 输出格式
- 主题：一句话概括
- 要点：编号列出关键信息（3-7条）
- 结论：整体总结

## 适用场景
- 快速回顾长篇笔记的核心内容
- 为整个分区生成概览（自动逐页分析后汇总）
- 分享笔记前生成简要说明";

                case "generate": return
@"# 生成

根据您的指令，使用 AI 生成全新内容。

## 使用方法
- 点击「生成」按钮
- 输入生成指令（如：写一篇关于XX的笔记）
- AI 会参考当前页面已有内容作为上下文
- 结果可插入到当前页面

## 使用技巧
- 指令越具体，生成效果越好
- 例如：「写一份关于Python基础的学习笔记，包括数据类型、控制流、函数」
- 而不是：「写点东西」";

                case "template": return
@"# 模板

从预设模板快速生成结构化笔记。

## 可用模板
- 会议纪要：基本信息 → 讨论要点 → 决议 → 待办
- 读书笔记：书籍信息 → 核心观点 → 摘录 → 感悟
- 周报：本周完成 → 进行中 → 问题 → 下周计划
- 学习笔记：概念 → 框架 → 重点难点 → 练习
- 项目计划：概述 → 里程碑 → 任务分解 → 风险
- 头脑风暴：创意列表 → 延伸思考 → 可行性分析

## 使用方法
- 点击「模板」按钮
- 从列表中选择模板类型
- 输入关键信息（几句话即可）
- AI 会按固定章节结构展开成完整笔记";

                case "rewrite": return
@"# 改写

使用 AI 改写或润色文本。

## 使用方法
- 先在 OneNote 中选中要改写的文本
- 点击「改写」按钮
- 输入改写要求（如：更正式、更简洁、翻译成英文）
- 如果没有选中文本，将改写整个页面

## 改写模式示例
- 「更正式」→ 将口语化表达改为书面语
- 「更简洁」→ 删减冗余，保留核心信息
- 「更详细」→ 展开要点，补充细节
- 「修正语法」→ 修复错别字和语法错误";

                case "qa": return
@"# 问答

基于笔记内容进行智能问答，支持跨页搜索。

## 使用方法
- 点击「问答」按钮
- 选择范围：「仅当前页面」或「整个分区」
- 输入您的问题
- AI 基于笔记内容回答，并标注信息来源

## 跨页问答（分区模式）
- AI 会读取分区内所有页面
- 回答会标注来源：[来源：页面名称]
- 自动管理 token 预算，防止超限
- 支持多轮追问（「继续提问」按钮）

## 适用场景
- 从大量笔记中快速找到特定信息
- 对比分析不同页面的内容
- 基于笔记内容进行推理和总结";

                case "translate": return
@"# 翻译

使用 AI 翻译选中文本或整个页面。

## 使用方法
- 如需翻译部分内容，先选中文本
- 点击「翻译」按钮
- 输入目标语言（如：英文、日文、法文）
- 翻译结果可插入到页面

## 支持的语言
- 英文、中文、日文、韩文
- 法文、德文、西班牙文、俄文
- 也可输入其他语言名称（如：阿拉伯文、葡萄牙文）";

                case "tag": return
@"# 标签

AI 自动分析页面内容，生成标签和分类。

## 使用方法
- 打开要分析的页面
- 点击「标签」按钮
- AI 会自动生成：
  - 标签：3-8 个关键词标签（#格式）
  - 分类：归入最合适的类别
  - 主题：一句话概括

## 输出示例
  标签：#OneNote #插件开发 #AI #C# #COM
  分类：技术/开发
  主题：OneNote AI 智能助手插件的开发记录";

                case "todos": return
@"# 提取待办

智能提取待办事项，结合 OneNote 原生标记和 AI 分析。

## 工作原理（两步法）
- 第一步：读取 OneNote 原生标记（复选框、勾选等）
  - 显示已完成项（✅）和未完成项（☐）
  - 准确识别标记状态
- 第二步：AI 分析未标记的文本
  - 只从未标记内容中发现隐含的行动项
  - 严格区分「信息列表」和「真正的待办」
  - 不会重复已标记的内容

## 使用方法
- 点击「提取待办」按钮
- 选择范围：当前页面或整个分区
- 查看分类结果并插入页面

## 安全保护
- 自动过滤 API Key、密码等敏感信息
- 敏感内容不会发送给 AI";

                case "settings": return
@"# 设置说明

## API 设置
- API Key：您的 DeepSeek API 密钥（sk- 开头）
- API Base URL：默认 https://api.deepseek.com
- 测试连接：验证 API Key 是否有效

## 模型设置
- 自动选择模型：根据内容长度自动选用合适模型
- 默认模型：deepseek-chat（通用）或 deepseek-reasoner（推理）
- 温度：0.0-2.0，越高越有创意，越低越稳定
- 最大 Token：单次生成的最大长度

## Prompt 模板
- 可自定义每个功能的 AI 系统提示词
- 留空则使用内置默认提示词
- 点击「恢复默认」可还原";

                case "providers": return
@"# 多模型支持

本插件支持多种 AI 服务提供商：

## DeepSeek（默认）
- Base URL：https://api.deepseek.com
- 模型：deepseek-chat、deepseek-reasoner
- 需要 API Key（在 platform.deepseek.com 申请）

## OpenAI
- Base URL：https://api.openai.com/v1
- 模型：gpt-4o-mini、gpt-4o 等
- 需要 API Key

## Ollama（本地）
- Base URL：http://localhost:11434/v1
- 模型：qwen2.5:7b 等本地模型
- 无需 API Key，数据完全本地处理
- 需先安装 Ollama 并下载模型

## 自定义
- 任何兼容 OpenAI API 格式的服务
- 在设置中修改 Base URL 和模型名称即可";

                case "faq": return
@"# 常见问题

## Q：插件安装后 OneNote 中看不到 AI 助手标签？
- 确保已关闭并重新打开 OneNote
- 检查 OneNote → 文件 → 选项 → 加载项 中是否已启用

## Q：提示「API Key 未配置」？
- 点击「设置」按钮输入您的 API Key
- 确保 Key 以 sk- 开头

## Q：生成速度很慢？
- 检查网络连接（需要访问 api.deepseek.com）
- 内容较长时 AI 处理时间会更长
- 可在设置中降低「最大 Token」数

## Q：插入页面后格式不对？
- 插件会自动将 Markdown 转为 OneNote 支持的格式
- 标题、加粗、列表等都会正确显示

## Q：如何使用本地模型（不联网）？
- 安装 Ollama（ollama.com）
- 下载模型：ollama pull qwen2.5:7b
- 在设置中将 Base URL 改为 http://localhost:11434/v1
- 清空 API Key";

                case "about": return
@"# 关于 OneNote AI Assistant

版本：2.0.0

## 开发背景

本插件由 OneNote MVP 开发。

由于部分地区无法使用 Microsoft 官方的 OneNote Copilot 功能，许多用户在日常笔记工作中无法享受到 AI 带来的效率提升。为了解决这一问题，我们开发了 OneNote AI Assistant —— 一款完全独立的 AI 智能助手插件，让全球所有 OneNote 用户都能在笔记中使用 AI 能力。

本插件不依赖 Microsoft Copilot 服务，通过接入 DeepSeek、OpenAI、Ollama 等第三方 AI 服务（包括本地部署方案），为用户提供灵活、可控、无地域限制的 AI 笔记体验。

## 主要功能
- 智能摘要、内容生成、模板创作
- 跨页问答（带来源引用）
- 智能翻译、自动标签
- 待办提取（原生标记 + AI 发现）
- 多模型支持（DeepSeek / OpenAI / Ollama）

## 技术栈
- C# / .NET Framework 4.8
- COM Add-in (IDTExtensibility2)
- OneNote Interop API
- OpenAI-compatible Chat API

## 联系方式
- GitHub：github.com/oldding/OneNote-AI-Assistant
- 开发者：OneNote_MVP";

                default: return "请从左侧目录选择一个主题。";
            }
        }

        private static string GetTopicEn(string topic)
        {
            switch (topic)
            {
                case "quickstart": return
@"# Quick Start

Welcome to OneNote AI Assistant!

## Step 1: Configure API Key
- Click the Settings button in the Ribbon
- Enter your DeepSeek API Key in API Settings
- Click Test Connection to verify
- Click OK to save

## Step 2: Start Using
- Open any OneNote page
- Find the AI Assistant tab in the Ribbon
- Choose a feature (Summary, Generate, Q&A, etc.)
- Review the AI result in the dialog
- Click Insert to add the result to your page

## Tips
- All features support Regenerate if you want a different result
- Q&A supports multi-turn conversation (Follow-up button)
- Translate and Rewrite auto-detect selected text";

                case "summarize": return
@"# Summary

Generate AI summaries for the current page or entire section.

## How to Use
- Click the Summary button
- Choose scope: Current Page or Entire Section
- AI analyzes content and generates a structured summary

## Output Format
- Topic: one-line overview
- Key Points: numbered list (3-7 items)
- Conclusion: overall summary";

                case "generate": return
@"# Generate

Create new content using AI based on your instructions.

## How to Use
- Click the Generate button
- Enter your instruction (e.g., write a note about...)
- AI references existing page content as context
- Result can be inserted into the current page";

                case "template": return
@"# Template

Quickly generate structured notes from preset templates.

## Available Templates
- Meeting Notes: Info → Discussion → Decisions → Action Items
- Book Notes: Book Info → Key Ideas → Quotes → Reflections
- Weekly Report: Completed → In Progress → Issues → Next Week
- Study Notes: Concepts → Framework → Key Points → Practice
- Project Plan: Overview → Milestones → Tasks → Risks
- Brainstorming: Ideas → Extensions → Feasibility → Recommendations

## How to Use
- Click Template button → Select a template → Enter key info → AI expands";

                case "rewrite": return
@"# Rewrite

Rewrite or polish text using AI.

## How to Use
- Select text in OneNote first (or it will rewrite the entire page)
- Click the Rewrite button
- Enter instructions (e.g., more formal, more concise)
- Review and insert the result";

                case "qa": return
@"# Q&A

Intelligent Q&A based on your notes, with cross-page search.

## How to Use
- Click the Q&A button
- Choose scope: Current Page or Entire Section
- Enter your question
- AI answers based on note content with source citations

## Cross-page Mode
- AI reads all pages in the section
- Answers include source: [Source: Page Name]
- Automatic token budget management
- Supports multi-turn follow-up questions";

                case "translate": return
@"# Translate

Translate selected text or full page using AI.

## Supported Languages
- English, Chinese, Japanese, Korean
- French, German, Spanish, Russian
- Or enter any other language name";

                case "tag": return
@"# Auto Tag

AI analyzes page content and generates tags and classification.

## Output
- Tags: 3-8 keyword tags in # format
- Category: best-fit category
- Topic: one-line summary";

                case "todos": return
@"# Extract Todos

Smart todo extraction combining OneNote native tags and AI analysis.

## How It Works (Two-step)
- Step 1: Read OneNote native tags (checkboxes, etc.)
  - Shows completed (check) and pending (box) items
- Step 2: AI analyzes untagged text
  - Finds hidden action items
  - Strictly distinguishes info lists from real todos
  - Does not duplicate tagged items

## Security
- API keys and passwords are automatically filtered
- Sensitive content is never sent to AI";

                case "settings": return
@"# Settings

## API Settings
- API Key: your AI provider key (starts with sk-)
- Base URL: default https://api.deepseek.com
- Test Connection: verify your key

## Model Settings
- Auto-select model: picks best model based on content length
- Temperature: 0.0-2.0 (higher = more creative)
- Max Tokens: maximum generation length

## Language
- Auto: follows system language
- Chinese / English: manual override";

                case "providers": return
@"# Multi-Provider Support

## DeepSeek (Default)
- URL: https://api.deepseek.com
- Models: deepseek-chat, deepseek-reasoner

## OpenAI
- URL: https://api.openai.com/v1
- Models: gpt-4o-mini, gpt-4o, etc.

## Ollama (Local)
- URL: http://localhost:11434/v1
- No API key needed, fully local processing
- Install Ollama first, then pull a model

## Custom
- Any OpenAI-compatible API endpoint";

                case "faq": return
@"# FAQ

## Q: AI Assistant tab not showing in OneNote?
- Close and reopen OneNote
- Check File → Options → Add-ins

## Q: API Key not configured error?
- Click Settings and enter your key (starts with sk-)

## Q: Generation is slow?
- Check network connection
- Reduce Max Tokens in settings

## Q: How to use local models (offline)?
- Install Ollama (ollama.com)
- Run: ollama pull qwen2.5:7b
- Set Base URL to http://localhost:11434/v1";

                case "about": return
@"# About OneNote AI Assistant

Version: 2.0.0

## Background

Developed by OneNote MVP.

Since OneNote Copilot is not available in certain regions, many users cannot benefit from AI-powered productivity features in their daily note-taking. To address this, we developed OneNote AI Assistant — a fully independent AI plugin that brings AI capabilities to all OneNote users worldwide.

This plugin does not rely on Microsoft Copilot. It connects to DeepSeek, OpenAI, Ollama, or any OpenAI-compatible API, providing flexible, controllable, region-free AI note experiences.

## Contact
- GitHub: github.com/oldding/OneNote-AI-Assistant
- Developer: OneNote_MVP";

                default: return "Select a topic from the left panel.";
            }
        }
    }
}
