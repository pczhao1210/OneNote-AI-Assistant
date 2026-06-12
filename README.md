# OneNote AI Assistant

A Microsoft OneNote COM Add-in that integrates DeepSeek AI to provide an intelligent AI Assistant experience directly inside OneNote.

## Features

| Feature | Description |
|---------|-------------|
| **Summarize** | Generate AI summaries of the current page or an entire section. Supports map-reduce for long content. Results can be inserted back into the page. |
| **Q&A** | Multi-turn question-answering grounded in your page content. Maintains conversation history for follow-up questions. |
| **Generate** | Create new content from natural language instructions, optionally referencing existing page content as context. |
| **Rewrite** | Rewrite selected text or entire pages according to your instructions (e.g., "more formal", "more concise"). |
| **Extract Todos** | Extract action items and to-do items from pages or entire sections. |

## Prerequisites

- Windows 10 or later
- Microsoft OneNote (Microsoft 365 / Office 2019 or later)
- [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- A [DeepSeek](https://platform.deepseek.com/) API key

## Installation

1. **Build the add-in** using Visual Studio 2022 (or later):
   - Open `OneNoteAI.sln`
   - Build the solution in Release mode

2. **Register the add-in**:
   - Run the built installer from `src/OneNoteAI.Installer/Output/`
   - Or manually register via `regasm`

3. **Restart OneNote** — the AI Assistant ribbon tab will appear.

4. **Configure your API key**:
   - Click the **Settings** button in the AI Assistant ribbon
   - Enter your DeepSeek API key
   - Adjust model/temperature/language preferences as needed

## Usage

1. Open any OneNote page
2. Click one of the Assistant commands in the ribbon:
   - **Summarize** — summarize the current page or section
   - **Q&A** — ask questions about the page content
   - **Generate** — create new content with an AI prompt
   - **Rewrite** — rewrite selected text or the whole page
   - **Extract Todos** — extract action items from notes
3. Review the AI response in the streaming dialog
4. Click **Insert** to add the result to your page, or **Regenerate** for a new response

## Architecture

```
OneNote AI Assistant Add-in
├── AddIn/              # COM add-in entry point & ribbon callbacks
├── AI/                 # DeepSeek API client, streaming, token estimation
├── Features/           # Command implementations (Summarize, Q&A, Generate, Rewrite, ExtractTodos)
├── OneNote/            # OneNote COM interop, page parsing & writing
├── UI/                 # Windows Forms dialogs (ResultDialog, Settings, etc.)
├── Settings/           # Encrypted settings management (DPAPI)
├── Logging/            # File-based logging
└── Ribbon/             # Custom ribbon XML & icons
```

## Tech Stack

- **Language**: C#
- **Framework**: .NET Framework 4.8
- **Runtime**: COM Add-in (IDTExtensibility2)
- **OneNote Interop**: Microsoft.Office.Interop.OneNote
- **AI Provider**: DeepSeek API (OpenAI-compatible)
- **HTTP**: System.Net.Http
- **JSON**: Newtonsoft.Json 13.0.3
- **Build**: Visual Studio 2022

## License

Private — All rights reserved.
