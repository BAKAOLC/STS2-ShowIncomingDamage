using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace STS2ShowIncomingDamage.Utils
{
    public static class DamageCalculator
    {
        private static readonly Dictionary<Type, bool> PowerHasAfterTurnEndCache = [];
        private static readonly Dictionary<Type, bool> PowerHasBeforeTurnEndEarlyCache = [];
        private static readonly Dictionary<Type, bool> PowerBeforeTurnEndEarlyGivesBlockCache = [];
        private static readonly Dictionary<Type, bool> PowerAfterTurnEndCallsDamageCache = [];
        private static readonly Dictionary<Type, bool> PowerAfterTurnEndCallsHpLossCache = [];
        private static readonly Dictionary<Type, bool> OrbHasBeforeTurnEndCache = [];
        private static readonly Dictionary<Type, bool> OrbPassiveGivesBlockCache = [];
        private static string? _cachedBlockKeyword;

        private static string BlockKeyword =>
            _cachedBlockKeyword ??= ResolveBlockKeyword();

        private static string ResolveBlockKeyword()
        {
            try
            {
                var loc = new LocString("intents", "DEFEND.title");
                var text = loc.GetFormattedText();
                if (!string.IsNullOrEmpty(text)) return text;
            }
            catch
            {
                // ignored
            }

            return "Block";
        }

        public static PlayerDamageInfo CalculateIncomingDamage(Creature playerCreature, CombatState combatState,
            bool includeDetails = false)
        {
            var result = new PlayerDamageInfo
            {
                PlayerDamage = 0,
                PetDamage = 0,
                TotalDamage = 0,
                HasPet = false,
                BlockGain = 0,
                Details = includeDetails ? new() : null,
            };

            if (combatState == null || playerCreature == null || playerCreature.IsDead)
                return result;

            Creature? petCreature = null;
            if (playerCreature.Pets is { Count: > 0 })
            {
                petCreature = playerCreature.Pets.FirstOrDefault(p => p is { IsDead: false });
                result.HasPet = petCreature != null;
            }

            var blockList = new List<(int amount, string source, string sourceType, object? sourceObject)>();
            CollectPowerBlockGain(playerCreature, blockList);
            CollectOrbBlockGain(playerCreature, blockList);
            result.BlockGain = blockList.Sum(b => b.amount);

            var damageList = new List<(int amount, string source, string sourceType, object? sourceObject)>();
            var cardDamages = CalculateTurnEndCardDamage(playerCreature);
            damageList.AddRange(cardDamages);
            var powerDamages = CalculateEndOfTurnPowerDamage(playerCreature);
            damageList.AddRange(powerDamages);

            foreach (var enemy in combatState.Enemies)
            {
                if (enemy.IsDead || enemy.Monster == null)
                    continue;

                foreach (var intent in enemy.Monster.NextMove.Intents)
                {
                    if (intent is not AttackIntent attackIntent)
                        continue;

                    var singleDamage = attackIntent.GetSingleDamage([playerCreature], enemy);
                    var totalDamage = attackIntent.GetTotalDamage([playerCreature], enemy);
                    var hitCount = singleDamage > 0 ? totalDamage / singleDamage : 0;
                    var enemyName = enemy.Name;

                    for (var i = 0; i < hitCount; i++)
                        damageList.Add((singleDamage, enemyName, "Enemy", (enemy, attackIntent, playerCreature)));
                }
            }

            result.TotalDamage = damageList.Sum(d => d.amount);

            if (includeDetails)
            {
                result.Details!.InitialBlock = playerCreature.Block;
                var stepNumber = 1;

                var runningBlock = 0;

                if (playerCreature.Block > 0)
                {
                    runningBlock = playerCreature.Block;
                    result.Details.Steps.Add(new()
                    {
                        StepNumber = stepNumber++,
                        Source = BlockKeyword,
                        SourceType = "Block",
                        IsBlock = true,
                        Amount = playerCreature.Block,
                        BlockAfter = runningBlock,
                    });
                }

                foreach (var (amount, source, sourceType, sourceObject) in blockList)
                {
                    runningBlock += amount;
                    result.Details.Steps.Add(new()
                    {
                        StepNumber = stepNumber++,
                        Source = source,
                        SourceType = sourceType,
                        SourceObject = sourceObject,
                        IsBlock = true,
                        Amount = amount,
                        BlockAfter = runningBlock,
                    });
                }

                var currentBlock = playerCreature.Block + result.BlockGain;
                var currentPetHp = petCreature?.CurrentHp ?? 0;

                foreach (var (damage, source, sourceType, sourceObject) in damageList)
                {
                    var remainingDamage = damage;
                    var blockBefore = currentBlock;
                    var petHpBefore = currentPetHp;

                    var blockedAmount = Math.Min(currentBlock, remainingDamage);
                    currentBlock -= blockedAmount;
                    remainingDamage -= blockedAmount;

                    var petDamageAmount = 0;
                    if (currentPetHp > 0 && remainingDamage > 0)
                    {
                        petDamageAmount = Math.Min(currentPetHp, remainingDamage);
                        currentPetHp -= petDamageAmount;
                        result.PetDamage += petDamageAmount;
                        remainingDamage -= petDamageAmount;
                    }

                    result.PlayerDamage += remainingDamage;

                    result.Details.Steps.Add(new()
                    {
                        StepNumber = stepNumber++,
                        Source = source,
                        SourceType = sourceType,
                        SourceObject = sourceObject,
                        IsBlock = false,
                        Amount = damage,
                        BlockBefore = blockBefore,
                        BlockUsed = blockedAmount,
                        PetHpBefore = petHpBefore,
                        PetDamage = petDamageAmount,
                        PlayerDamage = remainingDamage,
                        BlockAfter = currentBlock,
                        PetHpAfter = currentPetHp,
                    });
                }
            }
            else
            {
                var currentBlock = playerCreature.Block + result.BlockGain;
                var currentPetHp = petCreature?.CurrentHp ?? 0;

                foreach (var (damage, _, _, _) in damageList)
                {
                    var remainingDamage = damage;
                    var blockedAmount = Math.Min(currentBlock, remainingDamage);
                    currentBlock -= blockedAmount;
                    remainingDamage -= blockedAmount;

                    if (currentPetHp > 0 && remainingDamage > 0)
                    {
                        var petDmg = Math.Min(currentPetHp, remainingDamage);
                        currentPetHp -= petDmg;
                        result.PetDamage += petDmg;
                        remainingDamage -= petDmg;
                    }

                    result.PlayerDamage += remainingDamage;
                }
            }

            return result;
        }

        private static List<(int amount, string source, string sourceType, object? sourceObject)>
            CalculateEndOfTurnPowerDamage(Creature creature)
        {
            var damages = new List<(int amount, string source, string sourceType, object? sourceObject)>();

            try
            {
                foreach (var power in creature.Powers)
                {
                    var powerType = power.GetType();
                    if (!HasAfterTurnEndOverride(powerType) || power.Type != PowerType.Debuff)
                        continue;

                    var callsDamage = PowerAfterTurnEndCallsDamage(powerType);
                    var callsHpLoss = PowerAfterTurnEndCallsHpLoss(powerType);

                    if (!callsDamage && !callsHpLoss)
                        continue;

                    var powerName = power.Title.GetFormattedText();

                    if (callsDamage)
                        damages.Add((power.Amount, powerName, "Power", power));

                    if (callsHpLoss)
                        damages.Add((power.Amount, powerName, "Power", power));
                }
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"Failed to calculate end-of-turn power damage: {ex.Message}");
            }

            return damages;
        }

        private static List<(int amount, string source, string sourceType, object? sourceObject)>
            CalculateTurnEndCardDamage(Creature creature)
        {
            var damages = new List<(int amount, string source, string sourceType, object? sourceObject)>();

            try
            {
                if (creature.Player?.PlayerCombatState?.Hand == null)
                    return damages;

                foreach (var card in creature.Player.PlayerCombatState.Hand.Cards)
                {
                    if (!card.HasTurnEndInHandEffect)
                        continue;

                    var cardType = card.GetType();
                    var callsDamage = MethodAnalyzer.MethodCallsMethod(cardType, "OnTurnEndInHand", "Damage");
                    var callsHpLoss = MethodAnalyzer.MethodCallsMethod(cardType, "OnTurnEndInHand", "HpLoss");

                    if (!callsDamage && !callsHpLoss)
                        continue;

                    try
                    {
                        var traverse = Traverse.Create(card);
                        var dynamicVars = traverse.Property("DynamicVars").GetValue();
                        if (dynamicVars == null) continue;

                        var cardName = card.Title;

                        if (callsDamage)
                        {
                            var damageVar = Traverse.Create(dynamicVars).Method("get_Item", "Damage").GetValue();
                            if (damageVar != null)
                            {
                                var baseValue = Traverse.Create(damageVar).Property("BaseValue").GetValue<decimal>();
                                var damageAmount = (int)baseValue;
                                if (damageAmount > 0)
                                    damages.Add((damageAmount, cardName, "Card", card));
                            }
                        }

                        if (callsHpLoss)
                        {
                            var hpLossVar = Traverse.Create(dynamicVars).Method("get_Item", "HpLoss").GetValue();
                            if (hpLossVar != null)
                            {
                                var baseValue = Traverse.Create(hpLossVar).Property("BaseValue").GetValue<decimal>();
                                var hpLossAmount = (int)baseValue;
                                if (hpLossAmount > 0)
                                    damages.Add((hpLossAmount, cardName, "Card", card));
                            }
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"Failed to calculate turn end card damage: {ex.Message}");
            }

            return damages;
        }

        private static void CollectOrbBlockGain(Creature creature,
            List<(int amount, string source, string sourceType, object? sourceObject)> blockList)
        {
            try
            {
                if (creature.Player?.PlayerCombatState?.OrbQueue == null)
                    return;

                blockList.AddRange(
                    (from orb in creature.Player.PlayerCombatState.OrbQueue.Orbs
                        let orbType = orb.GetType()
                        where HasBeforeTurnEndOrbTriggerOverride(orbType) && OrbPassiveGivesBlock(orbType)
                        let blockAmount = (int)orb.PassiveVal
                        let orbName = orb.Title.GetFormattedText()
                        select (blockAmount, orbName, "Orb", orb)).Select(dummy =>
                        ((int amount, string source, string sourceType, object? sourceObject))dummy));
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"Failed to calculate orb block gain: {ex.Message}");
            }
        }

        private static void CollectPowerBlockGain(Creature creature,
            List<(int amount, string source, string sourceType, object? sourceObject)> blockList)
        {
            try
            {
                blockList.AddRange(
                    (from power in creature.Powers
                        let powerType = power.GetType()
                        where HasBeforeTurnEndEarlyOverride(powerType) && PowerBeforeTurnEndEarlyGivesBlock(powerType)
                        let powerName = power.Title.GetFormattedText()
                        select (power.Amount, powerName, "Power", power)).Select(dummy =>
                        ((int amount, string source, string sourceType, object? sourceObject))dummy));
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"Failed to calculate power block gain: {ex.Message}");
            }
        }

        private static bool HasAfterTurnEndOverride(Type powerType)
        {
            if (PowerHasAfterTurnEndCache.TryGetValue(powerType, out var cached))
                return cached;

            var result = MethodAnalyzer.TypeHasMethodOverride(powerType, "AfterTurnEnd", typeof(AbstractModel));
            PowerHasAfterTurnEndCache[powerType] = result;
            return result;
        }

        private static bool HasBeforeTurnEndEarlyOverride(Type powerType)
        {
            if (PowerHasBeforeTurnEndEarlyCache.TryGetValue(powerType, out var cached))
                return cached;

            var result = MethodAnalyzer.TypeHasMethodOverride(powerType, "BeforeTurnEndEarly", typeof(AbstractModel));
            PowerHasBeforeTurnEndEarlyCache[powerType] = result;
            return result;
        }

        private static bool HasBeforeTurnEndOrbTriggerOverride(Type orbType)
        {
            if (OrbHasBeforeTurnEndCache.TryGetValue(orbType, out var cached))
                return cached;

            var result = MethodAnalyzer.TypeHasMethodOverride(orbType, "BeforeTurnEndOrbTrigger", typeof(OrbModel));
            OrbHasBeforeTurnEndCache[orbType] = result;
            return result;
        }

        private static bool OrbPassiveGivesBlock(Type orbType)
        {
            if (OrbPassiveGivesBlockCache.TryGetValue(orbType, out var cached))
                return cached;

            var result = MethodAnalyzer.MethodCallsMethod(orbType, "Passive", "GainBlock");
            OrbPassiveGivesBlockCache[orbType] = result;
            return result;
        }

        private static bool PowerBeforeTurnEndEarlyGivesBlock(Type powerType)
        {
            if (PowerBeforeTurnEndEarlyGivesBlockCache.TryGetValue(powerType, out var cached))
                return cached;

            var result = MethodAnalyzer.MethodCallsMethod(powerType, "BeforeTurnEndEarly", "GainBlock");
            PowerBeforeTurnEndEarlyGivesBlockCache[powerType] = result;
            return result;
        }

        private static bool PowerAfterTurnEndCallsDamage(Type powerType)
        {
            if (PowerAfterTurnEndCallsDamageCache.TryGetValue(powerType, out var cached))
                return cached;

            var result = MethodAnalyzer.MethodCallsMethod(powerType, "AfterTurnEnd", "Damage");
            PowerAfterTurnEndCallsDamageCache[powerType] = result;
            return result;
        }

        private static bool PowerAfterTurnEndCallsHpLoss(Type powerType)
        {
            if (PowerAfterTurnEndCallsHpLossCache.TryGetValue(powerType, out var cached))
                return cached;

            var result = MethodAnalyzer.MethodCallsMethod(powerType, "AfterTurnEnd", "HpLoss");
            PowerAfterTurnEndCallsHpLossCache[powerType] = result;
            return result;
        }

        public struct PlayerDamageInfo
        {
            public int PlayerDamage;
            public int PetDamage;
            public int TotalDamage;
            public bool HasPet;
            public int BlockGain;
            public DetailedCalculation? Details;
        }

        public class DetailedCalculation
        {
            public int InitialBlock { get; set; }
            public List<ExecutionStep> Steps { get; set; } = [];
        }

        public class ExecutionStep
        {
            public int StepNumber { get; set; }
            public string Source { get; set; } = "";
            public string SourceType { get; set; } = "";
            public object? SourceObject { get; set; }
            public bool IsBlock { get; set; }
            public int Amount { get; set; }
            public int BlockBefore { get; set; }
            public int BlockUsed { get; set; }
            public int PetHpBefore { get; set; }
            public int PetDamage { get; set; }
            public int PlayerDamage { get; set; }
            public int BlockAfter { get; set; }
            public int PetHpAfter { get; set; }
        }
    }
}
