using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2ShowIncomingDamage.Patching.Core;
using STS2ShowIncomingDamage.Patching.Models;
using STS2ShowIncomingDamage.Utils;

namespace STS2ShowIncomingDamage.Patches
{
    /// <summary>
    ///     Combat event hooks: refresh damage display on combat setup, turn start, and intent refresh.
    /// </summary>
    public class CombatEventsPatch : IModPatches
    {
        private static bool _subscribed;

        public static void AddTo(ModPatcher patcher)
        {
            patcher.RegisterPatch<SetUpCombat>();
            patcher.RegisterPatch<RefreshIntents>();
        }

        private static void EnsureSubscribed()
        {
            if (_subscribed) return;
            _subscribed = true;
            CombatManager.Instance.CombatSetUp += _ => DamageDisplayService.RefreshAll();
            CombatManager.Instance.TurnStarted += _ => DamageDisplayService.RefreshAll();
            CombatManager.Instance.CombatEnded += _ => DamageDisplayService.HideAll();
        }

        public class SetUpCombat : IPatchMethod
        {
            public static string PatchId => "combat_setup";
            public static string Description => "Refresh damage display and subscribe to events on combat setup";

            public static ModPatchTarget[] GetTargets()
            {
                return
                [
                    new(typeof(CombatManager), nameof(CombatManager.SetUpCombat)),
                ];
            }

            public static void Postfix()
            {
                EnsureSubscribed();
                DamageDisplayService.RefreshAll();
            }
        }

        public class RefreshIntents : IPatchMethod
        {
            public static string PatchId => "creature_refresh_intents";
            public static string Description => "Update damage display when creature intents are refreshed";

            public static ModPatchTarget[] GetTargets()
            {
                return
                [
                    new(typeof(NCreature), nameof(NCreature.RefreshIntents)),
                ];
            }

            public static void Postfix()
            {
                EnsureSubscribed();
                DamageDisplayService.RefreshAll();
            }
        }
    }
}
