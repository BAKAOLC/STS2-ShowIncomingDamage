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
        private static readonly Dictionary<Type, bool> OrbEvokeGivesBlockCache = [];
        private static readonly Dictionary<Type, bool> RelicHasBeforeTurnEndCache = [];
        private static readonly Dictionary<Type, bool> RelicBeforeTurnEndGivesBlockCache = [];
        private static readonly Dictionary<Type, bool> RelicHasBeforeTurnEndVeryEarlyCache = [];
        private static readonly Dictionary<Type, bool> RelicBeforeTurnEndVeryEarlyChecksBlockCache = [];
        private static readonly Dictionary<Type, bool> RelicBeforeTurnEndCallsDamageCache = [];
        private static readonly Dictionary<Type, bool> RelicBeforeTurnEndCallsHpLossCache = [];
        private static readonly Dictionary<Type, bool> RelicAfterTurnEndCallsDamageCache = [];
        private static readonly Dictionary<Type, bool> RelicAfterTurnEndCallsHpLossCache = [];
        private static readonly Dictionary<Type, bool> RelicHasAfterTurnEndCache = [];
        private static readonly Dictionary<Type, bool> RelicModifiesOrbPassiveTriggerCache = [];
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
            CollectRelicBlockGain(playerCreature, blockList);
            result.BlockGain = blockList.Sum(b => b.amount);

            var damageList = new List<(int amount, string source, string sourceType, object? sourceObject)>();
            var cardDamages = CalculateTurnEndCardDamage(playerCreature);
            damageList.AddRange(cardDamages);
            var powerDamages = CalculateEndOfTurnPowerDamage(playerCreature);
            damageList.AddRange(powerDamages);
            CollectRelicDamage(playerCreature, damageList);

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

                var orbs = creature.Player.PlayerCombatState.OrbQueue.Orbs;
                if (orbs.Count == 0)
                    return;

                foreach (var orb in orbs)
                {
                    var orbType = orb.GetType();

                    if (!HasBeforeTurnEndOrbTriggerOverride(orbType) || !OrbPassiveGivesBlock(orbType))
                        continue;

                    var blockAmount = (int)orb.PassiveVal;
                    var orbName = orb.Title.GetFormattedText();

                    var triggerCount = CalculateOrbPassiveTriggerCount(creature, orb);

                    for (var t = 0; t < triggerCount; t++)
                        blockList.Add((blockAmount, orbName, "Orb", orb));
                }

                CollectOrbEvokeBlockGain(creature, blockList);
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"Failed to calculate orb block gain: {ex.Message}");
            }
        }

        private static void CollectOrbEvokeBlockGain(Creature creature,
            List<(int amount, string source, string sourceType, object? sourceObject)> blockList)
        {
            try
            {
                foreach (var power in creature.Powers)
                {
                    var powerType = power.GetType();
                    if (!HasAfterTurnEndOverride(powerType))
                        continue;

                    if (!MethodAnalyzer.MethodCallsMethod(powerType, "AfterTurnEnd", "EvokeLast"))
                        continue;

                    var orbs = creature.Player?.PlayerCombatState?.OrbQueue?.Orbs;
                    if (orbs == null || orbs.Count == 0)
                        continue;

                    var evokeCount = Math.Min(power.Amount, orbs.Count);
                    for (var i = 0; i < evokeCount; i++)
                    {
                        var orbIndex = orbs.Count - 1 - i;
                        if (orbIndex < 0) break;

                        var orb = orbs[orbIndex];
                        var orbType = orb.GetType();

                        if (!OrbEvokeGivesBlock(orbType)) continue;
                        var blockAmount = (int)orb.EvokeVal;
                        if (blockAmount <= 0) continue;
                        var orbName = orb.Title.GetFormattedText();
                        blockList.Add((blockAmount, $"{orbName} (Evoke)", "Orb", orb));
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"Failed to calculate orb evoke block gain: {ex.Message}");
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

        private static void CollectRelicBlockGain(Creature creature,
            List<(int amount, string source, string sourceType, object? sourceObject)> blockList)
        {
            try
            {
                if (creature.Player?.Relics == null)
                    return;

                foreach (var relic in creature.Player.Relics)
                {
                    if (relic.IsMelted || relic.IsUsedUp)
                        continue;

                    var relicType = relic.GetType();

                    if (!RelicHasBeforeTurnEndOverride(relicType) || !RelicBeforeTurnEndGivesBlock(relicType)) continue;
                    if (RelicHasBeforeTurnEndVeryEarlyOverride(relicType) &&
                        RelicBeforeTurnEndVeryEarlyChecksBlock(relicType))
                        if (creature.Block > 0)
                            continue;

                    var blockAmount = GetRelicBlockAmount(relic);
                    if (blockAmount <= 0) continue;
                    var relicName = relic.Title.GetFormattedText();
                    blockList.Add((blockAmount, relicName, "Relic", relic));
                }
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"Failed to calculate relic block gain: {ex.Message}");
            }
        }

        private static void CollectRelicDamage(Creature creature,
            List<(int amount, string source, string sourceType, object? sourceObject)> damageList)
        {
            try
            {
                if (creature.Player?.Relics == null)
                    return;

                foreach (var relic in creature.Player.Relics)
                {
                    if (relic.IsMelted || relic.IsUsedUp)
                        continue;

                    var relicType = relic.GetType();

                    if (RelicHasBeforeTurnEndOverride(relicType))
                        if (RelicBeforeTurnEndCallsDamage(relicType) || RelicBeforeTurnEndCallsHpLoss(relicType))
                        {
                            var damageAmount = GetRelicDamageAmount(relic);
                            if (damageAmount > 0)
                            {
                                var relicName = relic.Title.GetFormattedText();
                                damageList.Add((damageAmount, relicName, "Relic", relic));
                            }
                        }

                    if (!RelicHasAfterTurnEndOverride(relicType)) continue;
                    {
                        if (!RelicAfterTurnEndCallsDamage(relicType) && !RelicAfterTurnEndCallsHpLoss(relicType))
                            continue;
                        var damageAmount = GetRelicDamageAmount(relic);
                        if (damageAmount <= 0) continue;
                        var relicName = relic.Title.GetFormattedText();
                        damageList.Add((damageAmount, relicName, "Relic", relic));
                    }
                }
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"Failed to calculate relic damage: {ex.Message}");
            }
        }

        private static int GetRelicBlockAmount(RelicModel relic)
        {
            try
            {
                var traverse = Traverse.Create(relic);
                var dynamicVars = traverse.Property("DynamicVars").GetValue();
                if (dynamicVars == null) return 0;

                var blockVar = Traverse.Create(dynamicVars).Property("Block").GetValue();
                if (blockVar == null) return 0;

                var intValue = Traverse.Create(blockVar).Property("IntValue").GetValue<int>();
                return intValue;
            }
            catch
            {
                return 0;
            }
        }

        private static int GetRelicDamageAmount(RelicModel relic)
        {
            try
            {
                var traverse = Traverse.Create(relic);
                var dynamicVars = traverse.Property("DynamicVars").GetValue();
                if (dynamicVars == null) return 0;

                var damageVar = Traverse.Create(dynamicVars).Property("Damage").GetValue();
                if (damageVar != null)
                {
                    var intValue = Traverse.Create(damageVar).Property("IntValue").GetValue<int>();
                    if (intValue > 0) return intValue;
                }

                var hpLossVar = Traverse.Create(dynamicVars).Property("HpLoss").GetValue();
                if (hpLossVar == null) return 0;
                {
                    var intValue = Traverse.Create(hpLossVar).Property("IntValue").GetValue<int>();
                    if (intValue > 0) return intValue;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private static bool RelicHasBeforeTurnEndOverride(Type relicType)
        {
            if (RelicHasBeforeTurnEndCache.TryGetValue(relicType, out var cached))
                return cached;

            var result = MethodAnalyzer.TypeHasMethodOverride(relicType, "BeforeTurnEnd", typeof(AbstractModel));
            RelicHasBeforeTurnEndCache[relicType] = result;
            return result;
        }

        private static bool RelicBeforeTurnEndGivesBlock(Type relicType)
        {
            if (RelicBeforeTurnEndGivesBlockCache.TryGetValue(relicType, out var cached))
                return cached;

            var result = MethodAnalyzer.MethodCallsMethod(relicType, "BeforeTurnEnd", "GainBlock");
            RelicBeforeTurnEndGivesBlockCache[relicType] = result;
            return result;
        }

        private static bool RelicHasBeforeTurnEndVeryEarlyOverride(Type relicType)
        {
            if (RelicHasBeforeTurnEndVeryEarlyCache.TryGetValue(relicType, out var cached))
                return cached;

            var result =
                MethodAnalyzer.TypeHasMethodOverride(relicType, "BeforeTurnEndVeryEarly", typeof(AbstractModel));
            RelicHasBeforeTurnEndVeryEarlyCache[relicType] = result;
            return result;
        }

        private static bool RelicBeforeTurnEndVeryEarlyChecksBlock(Type relicType)
        {
            if (RelicBeforeTurnEndVeryEarlyChecksBlockCache.TryGetValue(relicType, out var cached))
                return cached;

            var result = MethodAnalyzer.MethodCallsMethod(relicType, "BeforeTurnEndVeryEarly", "get_Block");
            RelicBeforeTurnEndVeryEarlyChecksBlockCache[relicType] = result;
            return result;
        }

        private static bool RelicBeforeTurnEndCallsDamage(Type relicType)
        {
            if (RelicBeforeTurnEndCallsDamageCache.TryGetValue(relicType, out var cached))
                return cached;

            var result = MethodAnalyzer.MethodCallsMethod(relicType, "BeforeTurnEnd", "Damage");
            RelicBeforeTurnEndCallsDamageCache[relicType] = result;
            return result;
        }

        private static bool RelicBeforeTurnEndCallsHpLoss(Type relicType)
        {
            if (RelicBeforeTurnEndCallsHpLossCache.TryGetValue(relicType, out var cached))
                return cached;

            var result = MethodAnalyzer.MethodCallsMethod(relicType, "BeforeTurnEnd", "HpLoss");
            RelicBeforeTurnEndCallsHpLossCache[relicType] = result;
            return result;
        }

        private static bool RelicHasAfterTurnEndOverride(Type relicType)
        {
            if (RelicHasAfterTurnEndCache.TryGetValue(relicType, out var cached))
                return cached;

            var result = MethodAnalyzer.TypeHasMethodOverride(relicType, "AfterTurnEnd", typeof(AbstractModel));
            RelicHasAfterTurnEndCache[relicType] = result;
            return result;
        }

        private static bool RelicAfterTurnEndCallsDamage(Type relicType)
        {
            if (RelicAfterTurnEndCallsDamageCache.TryGetValue(relicType, out var cached))
                return cached;

            var result = MethodAnalyzer.MethodCallsMethod(relicType, "AfterTurnEnd", "Damage");
            RelicAfterTurnEndCallsDamageCache[relicType] = result;
            return result;
        }

        private static bool RelicAfterTurnEndCallsHpLoss(Type relicType)
        {
            if (RelicAfterTurnEndCallsHpLossCache.TryGetValue(relicType, out var cached))
                return cached;

            var result = MethodAnalyzer.MethodCallsMethod(relicType, "AfterTurnEnd", "HpLoss");
            RelicAfterTurnEndCallsHpLossCache[relicType] = result;
            return result;
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

        private static bool OrbEvokeGivesBlock(Type orbType)
        {
            if (OrbEvokeGivesBlockCache.TryGetValue(orbType, out var cached))
                return cached;

            var result = MethodAnalyzer.MethodCallsMethod(orbType, "Evoke", "GainBlock");
            OrbEvokeGivesBlockCache[orbType] = result;
            return result;
        }

        private static int CalculateOrbPassiveTriggerCount(Creature creature, OrbModel orb)
        {
            var triggerCount = 1;

            try
            {
                if (creature.Player?.Relics == null)
                    return triggerCount;

                triggerCount = creature.Player.Relics.Where(relic => relic is { IsMelted: false, IsUsedUp: false })
                    .Where(relic => RelicModifiesOrbPassiveTrigger(relic.GetType())).Aggregate(triggerCount,
                        (current, relic) => relic.ModifyOrbPassiveTriggerCounts(orb, current));
            }
            catch (Exception ex)
            {
                Main.Logger.Error($"Failed to calculate orb passive trigger count: {ex.Message}");
            }

            return triggerCount;
        }

        private static bool RelicModifiesOrbPassiveTrigger(Type relicType)
        {
            if (RelicModifiesOrbPassiveTriggerCache.TryGetValue(relicType, out var cached))
                return cached;

            var result =
                MethodAnalyzer.TypeHasMethodOverride(relicType, "ModifyOrbPassiveTriggerCounts", typeof(RelicModel));
            RelicModifiesOrbPassiveTriggerCache[relicType] = result;
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
