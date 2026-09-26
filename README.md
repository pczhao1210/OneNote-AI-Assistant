**English** | [中文](README.zh-CN.md)

# OneNote AI Assistant

A Microsoft OneNote AI-powered assistant add-in developed by **OneNote MVP**.

Since Microsoft's official OneNote Copilot is not available in many regions, users worldwide cannot benefit from AI-powered productivity features in their daily note-taking. OneNote AI Assistant is a fully independent AI plugin that brings AI capabilities to all OneNote users — no Copilot subscription required.

This plugin connects to DeepSeek, OpenAI, Ollama, or any OpenAI-compatible API, providing flexible, controllable, and region-free AI note experiences.

![OneNote AI Assistant Demo](docs/demo.png)

## Features

| Feature | Description |
|---------|-------------|
| **Summary** | Generate AI summaries for current page or entire section. Supports map-reduce for long content. Structured output: Topic → Key Points → Conclusion |
| **Generate** | Create new content from natural language instructions, automatically referencing existing page content |
| **Template** | Quick structured generation from 6 built-in templates: Meeting Notes, Book Notes, Weekly Report, Study Notes, Project Plan, Brainstorming |
| **Rewrite** | Rewrite selected text or full page with custom instructions (e.g., "more formal", "more concise") |
| **Q&A** | Multi-turn Q&A with **cross-page search** across the entire section, **source citations** `[Source: Page Name]`, and automatic token budget management |
| **Translate** | Translate selected text or full page to 8+ target languages (EN/ZH/JA/KO/FR/DE/ES/RU) |
| **Tag** | AI auto-generates keyword tags (#format), category, and one-line topic summary |
| **Extract Todos** | Smart two-step extraction: Step 1 reads OneNote native tags (✅/☐), Step 2 uses AI to discover hidden action items. Strictly distinguishes info lists from real todos. Auto-filters sensitive data |
| **Settings** | Configure API Key, model parameters, custom prompt templates, multi-provider switching |
| **Help** | Built-in Windows-style help window with 12 detailed topics |

## Supported AI Providers

| Provider | Base URL | Default Model | Notes |
|----------|----------|---------------|-------|
| **DeepSeek** (default) | `https://api.deepseek.com` | `deepseek-chat` | API key required |
| **OpenAI** | `https://api.openai.com/v1` | `gpt-4o-mini` | API key required |
| **Ollama** (local) | `http://localhost:11434/v1` | `qwen2.5:7b` | No API key, fully local |
| **Custom** | User-defined | User-defined | Any OpenAI-compatible API |

## Requirements

- Windows 10 or later
- Microsoft OneNote (Microsoft 365 / Office 2019+)
- [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- API key from your chosen provider (not required for Ollama)

### High-DPI displays

Add-in dialogs render text and controls at the display's native DPI, including
4K displays at 150%, 200% or 300% scaling. Moving a dialog between monitors
updates its layout and fonts. Per-monitor V2 support requires Windows 10
version 1703 or later (including Windows 11); version 1607 uses per-monitor V1,
and older versions retain the host's DPI mode with a warning in the add-in log.
This applies only to the add-in's UI thread, without changing OneNote's DPI
settings or modifying `OneNote.exe.config` / `dllhost.exe.config`.
Ribbon icons use 128-pixel source artwork instead of enlarged 32-pixel images.
After installing an updated build, fully exit and restart OneNote.

## Installation

1. **Build** (Visual Studio 2019+ or Build Tools):
   - Open `OneNoteAI.sln`
   - Build in Release mode

2. **Install**:
   - Run `src/OneNoteAI.Installer/Output/OneNoteAISetup.exe`
   - Or manually register via `regasm`

3. **Restart OneNote** — "AI Assistant" ribbon tab appears (10 buttons)

4. **Configure**:
   - Click **Settings** in the ribbon
   - Enter your API key
   - Choose provider and adjust parameters

## Usage

1. Open any OneNote page
2. Use the ribbon commands:

| Button | Action |
|--------|--------|
| **Summary** | Select scope (page/section) → Auto-generate structured summary |
| **Generate** | Enter instruction → AI generates content → Insert to page |
| **Template** | Choose template → Enter key info → AI expands to full document |
| **Rewrite** | Select text → Enter requirements → Review result |
| **Q&A** | Select scope → Ask question → AI answers with citations → Follow-up |
| **Translate** | Select text → Enter target language → View translation |
| **Tag** | Click → Auto-analyze → Generate tags/category/topic |
| **Todos** | Select scope → Shows native tags + AI-discovered action items |
| **Settings** | Configure API key, model, temperature, custom prompts |
| **Help** | Open built-in help with detailed documentation |

3. In the result dialog:
   - **Insert** — write result to current OneNote page
   - **Regenerate** — try again with same prompt
   - **Follow-up** — continue multi-turn conversation (Q&A)
   - **Copy** — copy to clipboard

## Internationalization

The plugin automatically detects your system language:
- **Chinese system** → Chinese UI and AI prompts
- **English system** → English UI and AI prompts
- Manual override available in Settings

## Architecture

```
OneNote AI Assistant v2.0
├── AddIn/              # COM add-in entry & ribbon callbacks
├── AI/                 # AI client (OpenAI-compatible), streaming, token estimation
│   ├── DeepseekClient     # HTTP + SSE streaming
│   ├── PromptTemplates    # Bilingual structured output templates
│   ├── ContentChunker     # Long text segmentation
│   └── TokenEstimator     # Token counting
├── Features/           # Commands
│   ├── SummarizeCommand   # Summary (single page / section map-reduce)
│   ├── GenerateCommand    # Content generation
│   ├── TemplateCommand    # Template generation (6 presets)
│   ├── RewriteCommand     # Rewrite
│   ├── QACommand          # Q&A (cross-page + citations + multi-turn)
│   ├── TranslateCommand   # Translation
│   ├── TagCommand         # Auto-tag/classify
│   └── ExtractTodosCommand # Todo extraction (native tags + AI)
├── OneNote/            # OneNote COM interop
│   ├── OneNoteProvider    # Page/section reading
│   ├── PageParser         # XML parsing (incl. Tag recognition)
│   └── PageWriter         # Page writing (Markdown → HTML)
├── UI/                 # User interface (OneNote purple theme)
│   ├── Theme              # Shared theme (palette/fonts/button factory)
│   ├── Strings            # i18n string resources (zh/en)
│   ├── ResultDialog       # Streaming result display
│   ├── HelpDialog         # Help window (tree + content)
│   └── ...                # PromptDialog, ScopeDialog, Settings, Progress
├── Settings/           # Encrypted settings (DPAPI)
├── Logging/            # File-based logging
└── Ribbon/             # Custom ribbon XML & icons
```

## Tech Stack

- **Language**: C# 9.0
- **Framework**: .NET Framework 4.8
- **Runtime**: COM Add-in (IDTExtensibility2 + IRibbonExtensibility)
- **OneNote**: Microsoft.Office.Interop.OneNote (v15.0)
- **AI**: OpenAI-compatible Chat Completions API
- **HTTP**: System.Net.Http (SSE streaming)
- **JSON**: Newtonsoft.Json 13.0.3
- **Security**: DPAPI encryption (local API key storage)
- **Installer**: Inno Setup 6
- **Build**: Visual Studio 2019+ / MSBuild 16.11

## Developer

- **Author**: OneNote_MVP
- **GitHub**: [github.com/oldding/OneNote-AI-Assistant](https://github.com/oldding/OneNote-AI-Assistant)

## License

Private — All rights reserved.
