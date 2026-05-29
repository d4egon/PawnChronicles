using Verse;
using UnityEngine;

namespace PawnChronicles
{
    /// <summary>
    /// Mod entry point. Registers settings and provides the global settings
    /// accessor used throughout the codebase.
    /// </summary>
    public class PawnChroniclesMod : Mod
    {
        public static PawnChroniclesSettings Settings { get; private set; } = null!;

        public PawnChroniclesMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<PawnChroniclesSettings>();
        }

        public override string SettingsCategory() => "Pawn Chronicles";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            PawnChroniclesSettingsWindow.DrawSettings(inRect, Settings);
        }
    }
}
