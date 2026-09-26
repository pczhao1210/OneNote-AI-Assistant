using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OneNoteAI.Knowledge;
using OneNoteAI.Mcp;

namespace OneNoteAI.Conversation
{
    public sealed class EvidenceSource
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public NoteChunk Note { get; set; }
        public ToolExecution Execution { get; set; }
        public string Text => Note?.Text ?? Execution?.Result?.ToString(Formatting.Indented) ?? "";
        public override string ToString() => "[" + Id + "] " + Title;
    }

    public sealed class EvidenceRegistry
    {
        private int _notes;
        private int _external;
        private readonly List<EvidenceSource> _sources = new List<EvidenceSource>();
        public IReadOnlyList<EvidenceSource> Sources => _sources;

        public EvidenceSource Add(NoteChunk chunk)
        {
            var source = new EvidenceSource { Id = "S" + (++_notes), Title = chunk.Title + " / " + chunk.Path, Note = chunk };
            _sources.Add(source);
            return source;
        }

        public EvidenceSource Add(ToolExecution execution)
        {
            var source = new EvidenceSource { Id = "M" + (++_external),
                Title = execution.ServerName + " / " + execution.ToolName + " (" + execution.Status + ")", Execution = execution };
            _sources.Add(source);
            return source;
        }

        public static string Format(EvidenceSource source)
        {
            if (source.Note != null)
                return new JObject { ["id"] = source.Id, ["kind"] = "OneNote passage", ["title"] = source.Note.Title,
                    ["path"] = source.Note.Path, ["text"] = source.Note.Text }.ToString(Formatting.None);
            return new JObject { ["id"] = source.Id, ["kind"] = "MCP result / operation receipt",
                ["server"] = source.Execution.ServerName, ["tool"] = source.Execution.ToolName, ["callId"] = source.Execution.CallId,
                ["status"] = source.Execution.Status, ["retrievedAt"] = source.Execution.Time, ["result"] = source.Execution.Result }.ToString(Formatting.None);
        }

        public void Clear() { _sources.Clear(); _notes = 0; _external = 0; }
    }
}
