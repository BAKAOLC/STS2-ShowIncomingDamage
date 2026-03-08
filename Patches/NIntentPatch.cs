using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2ShowIncomingDamage.Patching.Models;

namespace STS2ShowIncomingDamage.Patches
{
    /// <summary>
    ///     Enemy intent display: append total damage for multi-hit attacks.
    /// </summary>
    public class NIntentPatch : IPatchMethod
    {
        public static string PatchId => "intent_update_visuals_total";
        public static string Description => "Show total damage for multi-hit attack intents";
        public static bool IsCritical => false;

        public static ModPatchTarget[] GetTargets()
        {
            return
            [
                new(typeof(NIntent), "UpdateVisuals"),
            ];
        }

        // ReSharper disable InconsistentNaming
        public static void Postfix(
                AbstractIntent ____intent,
                Creature ____owner,
                IEnumerable<Creature> ____targets,
                MegaRichTextLabel ____valueLabel)
            // ReSharper restore InconsistentNaming
        {
            try
            {
                if (____intent is not AttackIntent attackIntent) return;
                if (____targets == null || ____owner == null || ____valueLabel == null) return;

                var targets = ____targets as Creature[] ?? [.. ____targets];
                var singleDamage = attackIntent.GetSingleDamage(targets, ____owner);
                var totalDamage = attackIntent.GetTotalDamage(targets, ____owner);

                if (singleDamage <= 0 || totalDamage <= singleDamage) return;

                var text = ____valueLabel.Text?.Trim() ?? "";
                if (!text.Contains('('))
                    ____valueLabel.Text = $"{text} ({totalDamage})";
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"Failed to update intent total damage display: {ex.Message}");
            }
        }
    }
}
