namespace OneNoteAI.OneNote
{
    /// <summary>
    /// Scope of content fed into an AI feature.
    /// Future: add <c>CurrentNotebook</c> once section-level rollout is verified.
    /// </summary>
    public enum ScopeKind
    {
        CurrentPage = 0,
        CurrentSection = 1
    }
}
