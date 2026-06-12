using Microsoft.Office.Core;

namespace OneNoteAI.AddIn
{
    internal static class RibbonState
    {
        public static IRibbonUI RibbonUI { get; set; }

        public static void InvalidateRibbon()
        {
            if (RibbonUI != null)
            {
                RibbonUI.Invalidate();
            }
        }
    }
}
