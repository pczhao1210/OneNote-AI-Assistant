# OneNote AI Assistant

A Microsoft OneNote COM Add-in that integrates AI (DeepSeek / OpenAI / Ollama) to provide an intelligent AI Assistant experience directly inside OneNote.

## Features

| Feature | Description |
|---------|-------------|
| **Summarize** | Generate AI summaries of the current page or an entire section. Supports map-reduce for long content. |
| **Q&A** | Multi-turn Q&A with **cross-page search** across the entire section and **source citations**. |
| **Generate** | Create new content from natural language instructions, referencing existing page content. |
| **Template** | Quick structured generation from 6 built-in templates (meeting notes, book notes, weekly report, study notes, project plan, brainstorming). |
| **Rewrite** | Rewrite selected text or entire pages (e.g., "more formal", "more concise"). |
| **Translate** | Translate selected text or full page to 8+ target languages. |
| **Tag** | AI auto-generates tags, category, and one-line topic summary for the current page. |
| **Extract Todos** | Extract action items and to-do items from pages or entire sections. |

## Supported AI Providers

| Provider | Base URL | Default Model |
|----------|----------|---------------|
| **DeepSeek** (default) | `https://api.deepseek.com` | `deepseek-chat` |
| **OpenAI** | `https://api.openai.com/v1` | `gpt-4o-mini` |
| **Ollama** (local) | `http://localhost:11434/v1` | `qwen2.5:7b` |
| **Custom** | User-defined | User-defined |

All providers use the OpenAI-compatible chat completions API format.

## Prerequisites

- Windows 10 or later
- Microsoft OneNote (Microsoft 365 / Office 2019 or later)
- [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- An API key from your chosen provider (not required for Ollama)

## Installation

1. **Build the add-in** using Visual Studio 2019+ (or Build Tools):
   - Open `OneNoteAI.sln`
   - Build in Release mode

2. **Register the add-in**:
   - Run the installer from `src/OneNoteAI.Installer/Output/`
   - Or manually register via `regasm`

3. **Restart OneNote** — the "AI 助手" ribbon tab will appear.

4. **Configure**:
   - Click **Settings** in the ribbon
   - Select your AI provider
   - Enter your API key
   - Adjust model/temperature as needed

## Usage

1. Open any OneNote page
2. Use the ribbon commands:
   - **摘要** — summarize the current page or section
   - **生成** — create new content with an AI prompt
   - **模板** — generate from predefined templates
   - **改写** — rewrite selected text or the whole page
   - **问答** — ask questions (single page or cross-section with citations)
   - **翻译** — translate selected text or full page
   - **标签** — auto-generate tags and classification
   - **提取待办** — extract action items from notes
3. Review the streaming AI response
4. Click **Insert** to add to your page, or **Regenerate** for a new response

## Architecture

```
OneNote AI Assistant Add-in (v2.0)
├── AddIn/              # COM add-in entry point & ribbon callbacks
├── AI/                 # AI client (OpenAI-compatible), streaming, token estimation
├── Features/           # Commands: Summarize, Q&A, Generate, Template,
│                       #   Rewrite, Translate, Tag, ExtractTodos
├── OneNote/            # OneNote COM interop, page parsing & writing
├── UI/                 # Windows Forms dialogs (ResultDialog, Settings, etc.)
├── Settings/           # Encrypted settings, multi-provider config
├── Logging/            # File-based logging
└── Ribbon/             # Custom ribbon XML & icons
```

## Tech Stack

- **Language**: C# 9.0
- **Framework**: .NET Framework 4.8
- **Runtime**: COM Add-in (IDTExtensibility2)
- **OneNote Interop**: Microsoft.Office.Interop.OneNote
- **AI**: OpenAI-compatible API (DeepSeek / OpenAI / Ollama)
- **HTTP**: System.Net.Http (streaming SSE)
- **JSON**: Newtonsoft.Json 13.0.3
- **Build**: Visual Studio 2019+ / MSBuild

## License

Private — All rights reserved.
