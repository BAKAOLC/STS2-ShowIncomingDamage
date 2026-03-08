using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2ShowIncomingDamage.Patching.Models;
using STS2ShowIncomingDamage.Utils;

namespace STS2ShowIncomingDamage.Patches
{
    /// <summary>
    ///     Periodically refresh damage display (every 250ms).
    /// </summary>
    public class RecalcPeriodicPatch : IPatchMethod
    {
        public static string PatchId => "intent_process_periodic";
        public static string Description => "Periodically refresh damage display during combat";
        public static bool IsCritical => false;

        public static ModPatchTarget[] GetTargets()
        {
            return
            [
                new(typeof(NIntent), "_Process", [typeof(double)]),
            ];
        }

        public static void Postfix()
        {
            if (DamageDisplayService.ShouldPeriodicRefresh())
                DamageDisplayService.RefreshAll();
        }
    }
}
