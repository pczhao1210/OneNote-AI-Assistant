# OneNote AI Assistant

由 **OneNote MVP** 开发的 Microsoft OneNote AI 智能助手插件。

由于部分地区无法使用 Microsoft 官方的 OneNote Copilot 功能，许多用户在日常笔记工作中无法享受到 AI 带来的效率提升。为了解决这一问题，我们开发了 OneNote AI Assistant —— 一款完全独立的 AI 智能助手插件，让全球所有 OneNote 用户都能在笔记中使用 AI 能力。

本插件不依赖 Microsoft Copilot 服务，通过接入 DeepSeek、OpenAI、Ollama 等 AI 服务（包括本地部署方案），为用户提供灵活、可控、无地域限制的 AI 笔记体验。

## 功能一览

| 功能 | 说明 |
|------|------|
| **摘要** | 对当前页面或整个分区生成 AI 摘要，支持长内容分段处理（map-reduce），输出标准化格式：主题 → 要点 → 结论 |
| **生成** | 根据自然语言指令生成全新内容，自动参考当前页面已有内容作为上下文 |
| **模板** | 从 6 种预设模板快速生成结构化笔记：会议纪要、读书笔记、周报、学习笔记、项目计划、头脑风暴 |
| **改写** | 智能改写选中文本或整页内容，支持自定义改写要求（如"更正式"、"更简洁"、"修正语法"） |
| **问答** | 多轮对话问答，支持**跨页搜索**整个分区并带**来源引用** `[来源：页面名称]`，自动 token 预算管理 |
| **翻译** | 翻译选中文本或整页内容，支持 8+ 目标语言（中/英/日/韩/法/德/西/俄），自动识别选中文本 |
| **标签** | AI 自动分析页面内容，生成关键词标签（#格式）、分类和一句话主题摘要 |
| **提取待办** | 智能待办提取（两步法）：第一步读取 OneNote 原生标记（✅已完成/☐未完成），第二步 AI 分析未标记文本发现潜在行动项，严格区分信息列表和真正待办，自动过滤敏感信息 |
| **设置** | 配置 API Key、模型参数、自定义 Prompt 模板，支持多 AI 服务商切换 |
| **帮助** | 内置 Windows 经典帮助窗口，包含 12 个详细主题的使用说明 |

## 支持的 AI 服务商

| 服务商 | API 地址 | 默认模型 | 说明 |
|--------|----------|----------|------|
| **DeepSeek**（默认） | `https://api.deepseek.com` | `deepseek-chat` | 需要 API Key |
| **OpenAI** | `https://api.openai.com/v1` | `gpt-4o-mini` | 需要 API Key |
| **Ollama**（本地） | `http://localhost:11434/v1` | `qwen2.5:7b` | 无需 API Key，数据完全本地处理 |
| **自定义** | 用户自定义 | 用户自定义 | 任何兼容 OpenAI API 格式的服务 |

## 系统要求

- Windows 10 或更高版本
- Microsoft OneNote（Microsoft 365 / Office 2019 或更高版本）
- [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- AI 服务商的 API Key（Ollama 本地部署无需 Key）

## 安装

1. **编译插件**（Visual Studio 2019+ 或 Build Tools）：
   - 打开 `OneNoteAI.sln`
   - 以 Release 模式编译

2. **安装插件**：
   - 运行安装程序 `src/OneNoteAI.Installer/Output/OneNoteAISetup.exe`
   - 或手动通过 `regasm` 注册

3. **重启 OneNote** — Ribbon 栏出现「AI 助手」标签页（10 个按钮）

4. **配置 API Key**：
   - 点击 Ribbon 的「设置」按钮
   - 输入 API Key，选择服务商
   - 调整模型和参数偏好

## 使用方法

1. 打开任意 OneNote 页面
2. 在 Ribbon 栏「AI 助手」标签页选择功能：

| 按钮 | 操作 |
|------|------|
| **摘要** | 选择范围（当前页/分区）→ 自动生成结构化摘要 |
| **生成** | 输入指令 → AI 生成内容 → 可插入页面 |
| **模板** | 选择模板类型 → 输入关键信息 → AI 按模板结构展开 |
| **改写** | 选中文本（或整页）→ 输入改写要求 → 查看结果 |
| **问答** | 选择范围 → 输入问题 → AI 回答并标注来源 → 可继续追问 |
| **翻译** | 选中文本（或整页）→ 输入目标语言 → 查看译文 |
| **标签** | 直接点击 → 自动分析并生成标签/分类/主题 |
| **提取待办** | 选择范围 → 显示原生标记 + AI 发现的行动项 |
| **设置** | 配置 API Key、模型、温度、自定义 Prompt |
| **帮助** | 打开内置帮助窗口，查看详细使用说明 |

3. 在结果对话框中：
   - 点击「插入页面」将结果写入当前 OneNote 页面
   - 点击「重新生成」重试
   - 点击「继续提问」进行多轮对话（问答功能）
   - 点击「复制」复制到剪贴板

## 项目结构

```
OneNote AI Assistant v2.0
├── AddIn/              # COM 插件入口 & Ribbon 回调
├── AI/                 # AI 客户端（OpenAI 兼容）、流式输出、Token 估算
│   ├── DeepseekClient     # HTTP 请求 & SSE 流式解析
│   ├── PromptTemplates    # 标准化输出模板（摘要/问答/待办等）
│   ├── ContentChunker     # 长文本分段处理
│   └── TokenEstimator     # Token 计数估算
├── Features/           # 功能命令
│   ├── SummarizeCommand   # 摘要（单页/分区 map-reduce）
│   ├── GenerateCommand    # 内容生成
│   ├── TemplateCommand    # 模板生成（6种预设）
│   ├── RewriteCommand     # 改写
│   ├── QACommand          # 问答（跨页搜索 + 来源引用 + 多轮对话）
│   ├── TranslateCommand   # 翻译
│   ├── TagCommand         # 自动标签/分类
│   └── ExtractTodosCommand # 待办提取（原生标记 + AI）
├── OneNote/            # OneNote COM 互操作
│   ├── OneNoteProvider    # 页面/分区读取
│   ├── PageParser         # XML 解析（含 Tag 标记识别）
│   └── PageWriter         # 页面写入（Markdown→HTML 转换）
├── UI/                 # 用户界面（OneNote 紫色主题）
│   ├── Theme              # 共享主题类（调色板/字体/按钮工厂）
│   ├── ResultDialog       # 结果展示（流式渲染 + Markdown 格式化）
│   ├── PromptDialog       # 输入对话框
│   ├── ScopeDialog        # 范围选择
│   ├── SettingsDialog     # 设置面板
│   ├── HelpDialog         # 帮助窗口（经典目录树+内容面板）
│   └── ProgressOverlay    # 进度叠加层
├── Settings/           # 加密设置管理（DPAPI）
├── Logging/            # 文件日志
└── Ribbon/             # 自定义 Ribbon XML & 图标
```

## 技术栈

- **语言**: C# 9.0
- **框架**: .NET Framework 4.8
- **运行时**: COM Add-in (IDTExtensibility2 + IRibbonExtensibility)
- **OneNote 互操作**: Microsoft.Office.Interop.OneNote (v15.0)
- **AI 接口**: OpenAI-compatible Chat Completions API
- **HTTP**: System.Net.Http（SSE 流式传输）
- **JSON**: Newtonsoft.Json 13.0.3
- **安全**: DPAPI 加密（API Key 本地存储）
- **安装**: Inno Setup 6
- **编译**: Visual Studio 2019+ / MSBuild 16.11

## 开发者

- **开发者**: OneNote_MVP
- **GitHub**: [github.com/oldding/OneNote-AI-Assistant](https://github.com/oldding/OneNote-AI-Assistant)

## 许可证

Private — All rights reserved.
