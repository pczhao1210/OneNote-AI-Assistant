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
| **Q&A Assistant** | Document-first, multi-turn answers and passage search. Independent **semantic + OneNote Search** retrieval within selected notebooks, section groups or sections; use enabled Remote HTTPS MCP only when external help is needed |
| **Translate** | Translate selected text or full page to 8+ target languages (EN/ZH/JA/KO/FR/DE/ES/RU) |
| **Tag** | AI auto-generates keyword tags (#format), category, and one-line topic summary |
| **Extract Todos** | Smart two-step extraction: Step 1 reads OneNote native tags (✅/☐), Step 2 uses AI to discover hidden action items. Strictly distinguishes info lists from real todos. Auto-filters sensitive data |
| **Settings** | Configure API Key, model parameters, custom prompt templates, multi-provider switching |
| **Help** | Built-in Windows-style help window with 12 detailed topics |

## Supported AI Providers

| Provider | Base URL | Default Model | Notes |
|----------|----------|---------------|-------|
| **DeepSeek** (default) | `https://api.deepseek.com` | `deepseek-chat` | API key required |
| **OpenAI** | `https://api.openai.com/v1` | `gpt-4.1-mini` | API key required |
| **Qwen** | `https://dashscope.aliyuncs.com/compatible-mode/v1` | `qwen-plus` | API key required |
| **Zhipu** | `https://open.bigmodel.cn/api/paas/v4` | `glm-4.5-air` | API key required |
| **Moonshot** | `https://api.moonshot.cn/v1` | `moonshot-v1-8k` | API key required |
| **MiniMax** | `https://api.minimax.chat/v1` | `MiniMax-Text-01` | API key required |
| **Gemini** | `https://generativelanguage.googleapis.com/v1beta/openai` | `gemini-2.5-flash` | OpenAI-compatible endpoint |
| **Claude** | `https://api.anthropic.com` | `claude-sonnet-4-20250514` | Native Messages API |
| **OpenRouter** | `https://openrouter.ai/api/v1` | `openai/gpt-4.1-mini` | API key required |
| **Ollama** (local) | `http://localhost:11434/v1` | `qwen2.5:7b` | Chat can stay local; cloud Embeddings/MCP are separate |
| **Custom** | User-defined | User-defined | Any OpenAI-compatible API |

### New models without code changes

The model field is editable, not limited to its suggestions. Enter the provider's model ID and endpoint; disable automatic model selection when you want to keep a specific DeepSeek model.

Two compatibility controls in **Settings → API / Model** are saved per provider and also apply to connection tests, knowledge answers and other commands:

- **Output limit field**: Auto, `max_tokens`, or `max_completion_tokens`. Auto uses the completion field for OpenAI and recognized OpenAI reasoning model names; other compatible providers keep `max_tokens`. Claude always uses its native `max_tokens`.
- **Temperature field**: Auto, Send, or Omit. Auto omits it for recognized GPT-5/o-series reasoning names. For a new model or custom deployment alias, select its documented field and choose Omit if temperature is unsupported.

For the error “`max_tokens` is not supported; use `max_completion_tokens`”, select `max_completion_tokens`. If the model rejects temperature, select Omit. No endpoint or model is silently switched, and failed requests are not automatically resubmitted. This configures Chat Completions/Claude Messages; Responses-only APIs are not supported.

## Requirements

- Windows 10 or later
- Desktop Microsoft OneNote (Microsoft 365 / Office 2019+), x86 or x64; not OneNote for Windows 10/UWP or the web app
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
The same scaling applies to the Q&A assistant, index settings, MCP
connections, tool catalogs, approval prompts and source viewers.
After installing an updated build, fully exit and restart OneNote.

## Installation

1. **Build and package** using the developer commands below, or obtain a trusted installer.

2. **Install**:
   - Close OneNote and run `src\OneNoteAI.Installer\Output\OneNoteAISetup-2.1.8.exe`
   - The installer includes managed dependencies and both SQLite native architectures, and registers both COM views on x64 Windows

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
| **Q&A Assistant** | Choose document scope → Ask for a document-first answer, optionally supplemented by MCP when needed → Inspect sources → Follow up |
| **Translate** | Select text → Enter target language → View translation |
| **Tag** | Click → Auto-analyze → Generate tags/category/topic |
| **Todos** | Select scope → Shows native tags + AI-discovered action items |
| **Settings** | Configure API key, model, temperature, custom prompts |
| **Help** | Open built-in help with detailed documentation |

3. Other commands use the result dialog:
   - **Insert** — write result to current OneNote page
   - **Regenerate** — try again with same prompt
   - **Copy** — copy to clipboard

## Q&A Assistant and document retrieval

Open **Q&A Assistant** in the ribbon. Send a message to have the chat model synthesize document evidence, not just list search hits. **Current page** works without an Embedding key or index. Index settings, updates and search-only **Find passages (no model)** are in the **...** menu. For cross-page retrieval:

1. Open **Index / MCP settings**. Configure a separate HTTPS Embedding endpoint and API key. Presets are `text-embedding-3-small` (1536 dimensions, default) and `text-embedding-3-large` (3072). Dimensions `0` means the model default; a custom model needs its actual dimensions.
2. Select the notebooks, section groups or sections you authorize for indexing, then save. Parent consent includes future descendants; identifiers, not display names, define scope. Click **Update index** to upload authorized text for Embeddings.
3. Choose **Selected note scope** and a query scope within that consent. Ask a question or find passages. Semantic recall and native OneNote Search run independently, then merge; neither is restricted to the other's hits.

When an authorized scope is configured, new Q&A windows default to **Selected note scope**; saving the first authorized scope also switches an open window to this mode. Without authorization, the default remains **Current page**. You can explicitly select Current page to restrict a query to that page. The scope banner and each turn identify the effective mode; the activity tab reports accessible pages, retrieved pages/passages and the evidence retained within the model budget. Scope size is not a claim that every page was read.

**Index / MCP settings → Retrieved passage limit** accepts **1-100, default 16**, for current-page and cross-page answers as well as Find passages. Save to apply; no reindexing is needed. This is an upper limit, not a guaranteed count: deduplication, source checks and the model context budget may reduce the passages actually used. Higher limits may increase latency and token usage; they do not imply a complete review of the selected scope.

No separately deployed vector database is needed. SQLite stores text, provenance, queue state and vectors; section/hash shards use HNSW with a bounded in-memory cache. Updates reuse unchanged vectors and resume persisted batches. Optional automatic refresh runs every five minutes **only while the Q&A window is open**. Changing the Embedding endpoint/model/dimensions requires reindexing and may incur new charges.

OneNote hierarchy timestamps can differ from page-content timestamps. Reads compare each revision source separately and verify content stability. Index refresh checks accessible authorized pages locally, including pages whose hierarchy time did not change; only changed content needs new Embeddings. This local scan can take time for large scopes.

Answers cite `[S1]` note sources separately from `[M1]` MCP results. Open **Sources** and select a turn to inspect its references, then double-click a source to navigate to the note or inspect the tool receipt. **Activity** expands retrieval and tool progress without taking over the conversation. Each follow-up retrieves fresh evidence; changing scope or service identity resets the conversation.

**Enter** sends, **Shift+Enter** inserts a newline, and **Ctrl+Enter** queues a message. During an answer, Enter keeps the draft and prompts you to queue instead. Queued messages run serially after successful answers and retain the scope, active-page ID and settings captured when queued. Cancellation or failure pauses the queue; resume or remove pending messages explicitly. Changing scope/settings or starting a new chat clears queued messages, and closing the window cancels active work. Cancelling cannot roll back an already dispatched MCP operation. Chinese IME confirmation is not treated as Send.

The composer has one circular send/cancel control with a white vector icon and a distinct disabled state. The note sidebar can be collapsed. Native rich text renders Markdown tables (including cell alignment and formatting), headings, nested lists, task lists, emphasis, links and fenced code blocks. **Copy answer** copies selected text, or the latest answer with source labels and notices, as both text and RTF for formatted paste. It does not write to OneNote or revalidate historical sources; incomplete answers are marked. The background UI message window stays hidden and is excluded from Alt+Tab.

**Index / MCP settings → Answer display → Enable Mermaid diagram previews** is off by default. Enabling it checks for Microsoft Edge WebView2 Runtime; without it, normal rich-text answers still work. Complete `mermaid` code blocks then offer **View diagram**, opening a separate local preview. Mermaid assets ship with the add-in; the preview blocks external resource requests, navigation and diagram actions. Invalid syntax shows an error and retains the source. WebView2 is never installed automatically.

Answer windows explicitly use the modern Windows RichEdit control, independent of OneNote's COM-host defaults. This keeps wrapped table text inside its cells and preserves Chinese diagram-link labels without exposing internal link targets.

The offline Mermaid 12.0.0 bundle is vendored unchanged from `https://cdn.jsdelivr.net/npm/mermaid@12.0.0/dist/mermaid.min.js` (SHA256 `28FCA7AE6EBC7ED7BB63BDE63136A74BFEF14F296A57E403657EEB8B32836073`); retain its bundled notices when updating it.

**Documents first, MCP on demand.** Each turn starts with the selected document evidence. If it suffices, the model answers without connecting to MCP, listing remote tools or executing them. Only the local discovery gateway is initially exposed. For an out-of-document question, essential evidence gap, necessary verification/freshness check or explicitly requested external action, the model can request discovery with a focused query and brief reason. The reason appears in the activity view, then relevant tools become available. Every follow-up makes this decision again; earlier MCP use does not automatically enable remote tools for the next turn.

Prompts distinguish document facts, external additions and inferences, explain conflicts and respect documents-only requests. Missing local evidence must not be disguised as a complete review using external sources. With MCP disabled or unavailable, the answer should state its limits rather than claim external verification. **Settings → Prompt templates → Q&A Assistant** customizes answer style; grounding, scope, citation and on-demand tool rules remain part of the runtime prompt.

Coverage, failed pages and single-path degradation are reported. Without indexed pages, cross-page search can use OneNote Search alone. A small set of retrieved passages is **not an exhaustive notebook review**, and “no verified passages” does not mean an entire notebook has no answer. Long current pages also use selected passages; use the existing Summary command for page/section summarization.

## Remote HTTPS MCP

In **Index / MCP settings → Remote HTTPS MCP**, add a Streamable HTTP endpoint. JSON and SSE responses are supported through the official MCP C# SDK. Plain HTTP, stdio and legacy standalone HTTP+SSE transports are not supported. Authentication can be none, Bearer, an API-key header, or OAuth.

For **Bearer**, paste the access token issued by that MCP service into **Bearer token**, without the `Bearer ` prefix. The client sends `Authorization: Bearer <token>`. It is not your chat or Embedding key. The API-key-header field is unused in this mode; OAuth obtains tokens through browser login instead.

OAuth uses the system browser, PKCE and a temporary `http://127.0.0.1:<port>/callback/` redirect. The remote service and its authorization endpoints must use HTTPS. Some services require a registered client ID/secret or a real client metadata document URL; otherwise the server must support dynamic registration. Use **Sign out locally** to remove cached authorization, not to revoke permissions at the provider.

The directory exposes tools, resources and prompts. You can inspect schemas, run tools manually, read remote resources and retrieve prompts. Tools default to **Auto approve, including create/update/delete operations**. Set individual tools to **Require approval** or disable them; save settings to apply policy edits to conversations. New tools also default to Auto approve. Only enable trusted servers.

MCP is off by default in each Q&A window. Select servers and enable **MCP only when needed** to allow document-first, on-demand discovery and calls; it does not require MCP for every answer. Automatic calls require native tool calling in the chosen chat model; turn off that capability in settings for unsupported models and use the manual tool interface instead. Tool schemas, history and results share the context budget; set the actual context-window size for custom models. Manual directory browsing and tool execution remain explicit actions, outside automatic answer routing.

The activity view distinguishes rejection, tool errors, returned results and **unknown outcome**. Cancellation is not rollback. A response lost after dispatch stops the tool loop and is not automatically replayed: inspect the remote system before retrying. External results remain session-local unless explicitly saved to OneNote. Image/audio/binary content is identified as omitted, not silently treated as text.

## Privacy and local state

- Chat receives the question, selected evidence and any selected tool schemas/results. Embeddings receive authorized note text when indexing and search questions during semantic retrieval. Choosing local Ollama for chat does **not** make cloud Embeddings or MCP local.
- `%LOCALAPPDATA%\OneNoteAI\Knowledge` contains a **plaintext note/vector cache**, restricted with a current-user directory ACL; this is not database encryption. Keys and OAuth tokens use Windows DPAPI. `%APPDATA%\OneNoteAI\settings.json` stores settings and encrypted credentials.
- **Clear local index** deletes cached note/vector/index data, not OneNote content or OAuth credentials. Locked/missing notes become ineligible for retrieval; temporary unavailability is not treated as permanent deletion. Use Clear local index to remove all cached content, including unavailable sources. Uninstall preserves user state.

## Internationalization

The plugin automatically detects your system language:
- **Chinese system** → Chinese UI and AI prompts
- **English system** → English UI and AI prompts
- The persisted `language` setting accepts `auto`, `zh-CN` or `en`; the settings window preserves it

## Architecture

```
src\OneNoteAI.AddIn
├── AddIn/              # COM add-in entry & ribbon callbacks
├── AI/                 # AI client (OpenAI-compatible), streaming, token estimation
│   ├── DeepseekClient     # OpenAI/Claude native streaming and tool calls
│   ├── EmbeddingClient    # Separate Embedding API, validated normalized vectors
│   ├── ContextBudget      # Whole-request budget including tool schemas/results
│   ├── PromptTemplates    # Bilingual structured output templates
│   ├── ContentChunker     # Long text segmentation
│   └── TokenEstimator     # Token counting
├── Features/           # Commands
│   ├── SummarizeCommand   # Summary (single page / section map-reduce)
│   ├── GenerateCommand    # Content generation
│   ├── TemplateCommand    # Template generation (6 presets)
│   ├── RewriteCommand     # Rewrite
│   ├── QACommand          # Q&A Assistant entry point
│   ├── TranslateCommand   # Translation
│   ├── TagCommand         # Auto-tag/classify
│   └── ExtractTodosCommand # Todo extraction (native tags + AI)
├── Knowledge/          # Consent, chunking, SQLite, incremental indexing, HNSW, hybrid recall
├── Mcp/                # HTTPS transport, OAuth, discovery, schema/approval/dispatch
├── Conversation/       # Evidence registry, fresh retrieval, native model/tool loop
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

- **Language**: C# 12 (pinned compiler)
- **Framework**: .NET Framework 4.8
- **Runtime**: COM Add-in (IDTExtensibility2 + IRibbonExtensibility)
- **OneNote**: Microsoft.Office.Interop.OneNote (v15.0)
- **AI**: OpenAI-compatible Chat Completions, Claude Messages, OpenAI-compatible Embeddings
- **HTTP**: System.Net.Http (SSE streaming)
- **JSON**: Newtonsoft.Json 13.0.3
- **Index**: System.Data.SQLite.Core 1.0.119, HNSW 25.3.56901
- **MCP**: ModelContextProtocol.Core 2.2.0, JsonSchema.Net 7.3.4
- **Credentials**: DPAPI encryption; the note cache is not encrypted
- **Installer**: Inno Setup 6.5+
- **Build**: Visual Studio 2022 / current MSBuild 17 (17.14 recommended), NuGet PackageReference with lock files

## Build and test

On Windows, install Visual Studio/Build Tools with .NET desktop development, desktop OneNote and the Office/OneNote interop assemblies. Reference assemblies and the C# compiler are restored through NuGet. From a Developer PowerShell at the repository root:

```powershell
msbuild .\OneNoteAI.sln -restore -p:RestoreLockedMode=true -p:Configuration=Release
.\tests\OneNoteAI.Tests\bin\Release\OneNoteAI.Tests.exe
```

To run only the DPI and UI regression scenarios (Windows 10 1703+):

```powershell
.\tests\OneNoteAI.Tests\bin\Release\OneNoteAI.Tests.exe High-DPI WinForms
.\tests\OneNoteAI.Tests\bin\Release\OneNoteAI.Tests.exe --legacy-rich-edit "COM-host rich-text" "Markdown tables" "High-DPI"
```

The DPI scenario covers English and Chinese dialogs at 100%-300% scaling,
native font sizes, grid headers and user-resized splitters. It also moves a
hidden test window between available monitors to check native DPI notifications.

The executable suite uses synthetic notes, HTTP responses and isolated settings/cache directories, without real notes, paid API calls or remote writes. It covers provider drafts, parsing, streaming, scopes, resumable indexing, native tool loops, MCP policies/OAuth and WinForms. ANN cases include 5,000 vectors each at 1536 and 3072 dimensions. Arguments select scenario names, for example `OneNoteAI.Tests.exe MCP OAuth`.

For an isolated x86 run:

```powershell
msbuild .\OneNoteAI.sln -p:Configuration=Release -p:PlatformTarget=x86 -p:OutputPath=bin\Release-x86\
.\tests\OneNoteAI.Tests\bin\Release-x86\OneNoteAI.Tests.exe
```

Keep the AnyCPU Release output for packaging. Install Inno Setup and ensure `Languages\ChineseSimplified.isl` is present ([official translations](https://jrsoftware.org/files/istrans/)), then compile:

```powershell
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' .\src\OneNoteAI.Installer\setup.iss
```

Real OneNote COM hosting, browser OAuth with individual providers and real remote MCP services still need installation-level testing. Synthetic ANN checks do not establish large-library latency, total-process memory limits or retrieval quality on a labeled corpus.

## Developer

- **Author**: OneNote_MVP
- **GitHub**: [github.com/oldding/OneNote-AI-Assistant](https://github.com/oldding/OneNote-AI-Assistant)

## License

Private — All rights reserved.

Bundled dependency licenses and notices are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) and included in the installer.
