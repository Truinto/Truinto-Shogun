using BepInEx;
using BepInEx.Logging;
using CombatEnums;
using HarmonyLib;
using Parameters;
using Shared.CollectionNS;
using SkillEnums;
using System.Collections;
using System.Reflection;
using System.Text;
using TileEnums;
using TMPro;
using UnityEngine;
using Utils;

namespace ShogunCheat
{
    [BepInPlugin("Truinto." + ModInfo.MOD_NAME, ModInfo.MOD_NAME, ModInfo.MOD_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public void Awake()
        {
            LogSource = Logger;
            var harmony = new Harmony("Truinto." + ModInfo.MOD_NAME);
            harmony.PatchAll();
            Log($"{ModInfo.MOD_NAME} patched");
        }

        private static ManualLogSource LogSource = null!;
        public static void Log(string msg)
        {
            LogSource.LogInfo(msg);
        }
    }


    [HarmonyPatch]
    public static class Patches
    {
        #region Debug

        [HarmonyPatch(typeof(MapManager), nameof(MapManager.Awake))]
        [HarmonyPostfix]
        public static void Sandbox()
        {
            try
            {
                Plugin.Log($"Shop components: {MapManager.Instance.map.shopComponentsLeft.Join(f => f.name)}");
                _ = MapManager.Instance.map.MapLocations[0].location;

                //TilesFactory.Instance.Create(AttackEnum.TwinTessen, 4);
                //Resources.Load<GameObject>("Agents/Enemies/ThornsEnemy");

                // TODO: Ronin, get Throns
                // startingRandomDeck

            } catch (Exception e)
            {
                Plugin.Log($"Exception in Sandbox {e}");
            }
        }

        [HarmonyPatch(typeof(Progression), nameof(Progression.PickRoomVariant))]
        [HarmonyPostfix]
        public static void DebugRooms(Location location, int iRoom, ref int __result)
        {
            try
            {
                var sb = new StringBuilder();
                int num = location.NVariantsForRoom(iRoom);
                sb.Append($"Pick rooms ({num}): ");
                for (int i = 0; i < num; i++)
                {
                    var room = location.GetRoom(iRoom, i);
                    if (room is ShopRoom shop && location is ShopLocation shopLocation)
                    {
                        sb.Append($"shop={room.Id}:{room.name}:{shopLocation.leftShopComponent.GetComponent<TileUpgradeInShop>()?.tileUpgrades[0].tileUpgradeEnum}, ");
                    }
                    else
                        sb.Append($"room={room.Id}:{room.name}, ");
                }
                sb.Length -= 2;
                Plugin.Log(sb.ToString());
            } catch (Exception e)
            {
                Plugin.Log($"Exception reading room {e}");
            }
        }

        #endregion

        #region BeginRun

        public static Skill[]? AllSkills;

        [HarmonyPatch(typeof(MetricsManager), nameof(MetricsManager.BeginRun))]
        [HarmonyPostfix]
        public static void BeginRun()
        {
            try
            {
                EventsManager.Instance.EnemyDied.AddListener(dropCoin);

                if (!Globals.ContinueRun)
                {
                    AllSkills ??= Resources.LoadAll(SkillsManager.Instance.skillsResourcesPath).SelectNotNull(f => (f as GameObject)?.GetComponent<Skill>()).ToArray();
                    Plugin.Log($"Skills ({AllSkills.Length}): {AllSkills.Join(j => j.SkillEnum.ToString())}");
                    foreach (var freebie in Settings.State.BeginRunWithSkills)
                    {
                        var skill = AllSkills.FirstOrDefault(f => f.SkillEnum == freebie);
                        if (skill != null)
                            SkillsManager.Instance.PickUpSkill(skill);
                    }
                }
            } catch (Exception e)
            {
                Plugin.Log($"{e}");
            }

            static void dropCoin(Enemy enemy)
            {
                PickupFactory.Instance.InstantiatePickup(PickupEnums.PickupEnum.Coin, enemy.Cell);
            }
        }

        #endregion

        #region Upgrades

        [HarmonyPatch(typeof(NewTileReward), nameof(NewTileReward.GetPseudoRandomAttackEnums))]
        [HarmonyPostfix]
        public static void ChangeNewTileRewards(ref AttackEnum[] __result)
        {
            // could change the reward selection here
        }

        [HarmonyPatch(typeof(TileUpgradeReward), nameof(TileUpgradeReward.InitializeUpgradesAndProbabilities))]
        [HarmonyPostfix]
        public static void ChangeTileUpgradeRewards(TileUpgradeReward __instance)
        {
            // remove Attack +1 CD +1
            var type = TileUpgradeEnum.Attack_p1_Cooldown_p1;
            if (__instance.upgrades.TryGetValue(type, out var upgrade))
            {
                __instance.upgrades.Remove(type);
                float probability = __instance.upgradesAndProbabilities[upgrade];
                __instance.upgradesAndProbabilities.Remove(upgrade);

                if (__instance.upgrades.TryGetValue(TileUpgradeEnum.Attack_p1, out upgrade))
                    __instance.upgradesAndProbabilities[upgrade] += probability;
            }

            // remove Sacrifice
            type = TileUpgradeEnum.Sacrifice;
            if (__instance.upgrades.TryGetValue(type, out upgrade))
            {
                __instance.upgrades.Remove(type);
                __instance.upgradesAndProbabilities.Remove(upgrade);
            }

            // remove Perfect Strike (unless you have Kunai)
            if (!TilesManager.Instance.Deck.Any(a => a.Attack is KunaiAttack && a.Attack.AttackEffect is AttackEffectEnum.None && a.Attack.Cooldown >= 5))
            {
                type = TileUpgradeEnum.PerfectStrike;
                if (__instance.upgrades.TryGetValue(type, out upgrade))
                {
                    __instance.upgrades.Remove(type);
                    __instance.upgradesAndProbabilities.Remove(upgrade);
                }
            }

            // improve Attack +2 CD +3 to Attack +2 CD +1
            type = TileUpgradeEnum.Attack_p2_Cooldown_p3;
            if (__instance.upgrades.TryGetValue(type, out upgrade) && upgrade is StatsTileUpgrade statsUpgrade)
            {
                statsUpgrade.cooldownDelta = 1;
            }

            // make shockwave more common
            type = TileUpgradeEnum.Shockwave;
            if (__instance.upgrades.TryGetValue(type, out upgrade))
            {
                if (!TilesManager.Instance.Deck.Any(a => a.Attack.AttackEffect is AttackEffectEnum.Shockwave))
                    __instance.upgradesAndProbabilities[upgrade] *= 2f;
                if (TilesManager.Instance.Deck.Any(a => a.Attack.AttackEffect is AttackEffectEnum.None
                    && a.Attack.AttackEnum is AttackEnum.KiPush or AttackEnum.Tanegashima or AttackEnum.TwinTessen or AttackEnum.DragonPunch or AttackEnum.Kunai or AttackEnum.Thorns))
                    __instance.upgradesAndProbabilities[upgrade] *= 10f;
            }

            // make -4 CD more common
            type = TileUpgradeEnum.Cooldown_m4_Attack_m1;
            if (__instance.upgrades.TryGetValue(type, out upgrade))
            {
                if (TilesManager.Instance.Deck.Average(s => s.Attack.Cooldown) >= 6d)
                    __instance.upgradesAndProbabilities[upgrade] *= 10f;
            }

            // make +2 Attack more common
            type = TileUpgradeEnum.Attack_p2_Cooldown_p3;
            if (__instance.upgrades.TryGetValue(type, out upgrade))
            {
                if (TilesManager.Instance.Deck.Max(s => s.Attack.Value) <= 4)
                    __instance.upgradesAndProbabilities[upgrade] *= 10f;
            }

            // make Free Play more common
            type = TileUpgradeEnum.FreePlay;
            if (__instance.upgrades.TryGetValue(type, out upgrade))
            {
                if (TilesManager.Instance.Deck.Any(a => a.Attack.TileEffect is TileEffectEnum.None
                    && a.Attack.AttackEnum is AttackEnum.SmokeBomb or AttackEnum.BackSmokeBomb or AttackEnum.ShadowDash or AttackEnum.BackShadowDash))
                    __instance.upgradesAndProbabilities[upgrade] *= 5f;
            }
        }

        /// <summary>
        /// Shop Gold=0 - Sacrifice
        /// Shop Gold=5 - WarriorGamble
        /// Shop Gold=10 - UpgradeSlots_p1
        /// Shop Gold=20 - Attack_p1
        /// Shop Gold=20 - Cooldown_m2
        /// Shop Gold=20 - DoubleStrike, Ice, PerfectStrike, Poison, Shockwave, Curse
        /// </summary>
        [HarmonyPatch(typeof(TileUpgradeInShop), nameof(TileUpgradeInShop.Begin))]
        [HarmonyPrefix]
        public static void ChangeTileUpgradeShop(ref RewardSaveData? rewardSaveData, TileUpgradeInShop __instance)
        {
            try
            {
                Plugin.Log($"Shop Gold={__instance.basePrice} - {__instance.tileUpgrades.Join(f => f.tileUpgradeEnum.ToString())}");

                if (rewardSaveData == null)
                {
                    var upgrades = __instance.tileUpgrades.ToList();

                    if (upgrades.Count > 0 && upgrades[0].tileUpgradeEnum is TileUpgradeEnum.Sacrifice)
                    {
                        upgrades[0] = getUpgrade(TileUpgradeEnum.UpgradeSlots_p1);
                        __instance.basePrice = 10;
                    }

                    if (upgrades.Count > 1 && !TilesManager.Instance.Deck.Any(a => a.Attack is KunaiAttack && a.Attack.AttackEffect is AttackEffectEnum.None && a.Attack.Cooldown >= 5))
                        upgrades.RemoveAll(f => f.tileUpgradeEnum is TileUpgradeEnum.PerfectStrike);

                    if (upgrades.Count > 1)
                        upgrades.RemoveAll(f => f.tileUpgradeEnum is TileUpgradeEnum.Sacrifice);

                    if (upgrades.Count > 1)
                        upgrades.RemoveAll(f => f.tileUpgradeEnum is TileUpgradeEnum.Cooldown_m1);

                    if (upgrades.Count > 0)
                        __instance.tileUpgrades = upgrades.ToArray();
                }
            } catch (Exception e)
            {
                Plugin.Log($"{e}");
            }
            return;

            TileUpgrade getUpgrade(TileUpgradeEnum upgrade)
            {
                foreach (var shop in MapManager.Instance.map.shopComponentsLeft)
                {
                    var tileInShop = shop.GetComponent<TileUpgradeInShop>();
                    if (tileInShop == null)
                        continue;
                    foreach (var up in tileInShop.tileUpgrades)
                        if (up.tileUpgradeEnum == upgrade)
                            return up;
                }
                throw new Exception("TileUpgradeEnum in no shops");
            }
        }

        [HarmonyPatch(typeof(TileUpgradeInShop), nameof(TileUpgradeInShop.TileUpgradeHasBeenSelected))]
        [HarmonyPrefix]
        public static bool ChangeTileUpgradeShopCost(TileUpgradeInShop __instance)
        {
            __instance.price.Pay();
            __instance.price.Value += __instance.basePrice > 10 ? __instance.basePrice / 2 : __instance.basePrice;
            //EventsManager.Instance.SaveRunProgress.Invoke();
            return false;
        }

        /// <summary>
        /// Always random attack effect.
        /// </summary>
        [HarmonyPatch(typeof(WarriorGambleUpgrade), nameof(WarriorGambleUpgrade.Upgrade))]
        [HarmonyPrefix]
        public static bool ChangeWarriorGamble(Tile tile, WarriorGambleUpgrade __instance)
        {
            __instance.previousAttack = tile.Attack.AttackEnum;
            var tileContainer = tile.TileContainer;
            int level = tile.Attack.Level;
            int maxLevel = tile.Attack.MaxLevel;
            tile.TileContainer.RemoveTile();
            TilesManager.Instance.Deck.Remove(tile);
            UnityEngine.Object.Destroy(tile.gameObject);
            var tileNew = TilesFactory.Instance.Create(TilesFactory.Instance.PseudoRandomAttackEnumsGenerator.GetNext(f => f != tile.Attack.AttackEnum), maxLevel);
            if (tileNew.Attack.CompatibleEffects.Length > 0)
                tileNew.Attack.AttackEffect = MyRandom.NextRandomUniform(tileNew.Attack.CompatibleEffects);
            for (int i = 0; i < level; i++)
                tileNew.DoRandomUpgrade();
            tileNew.Graphics.UpdateGraphics();
            tileContainer.AddTile(tileNew);
            tileContainer.TeleportTileInContainer();
            tileContainer.Interactable = false;
            tileNew.Interactable = false;
            tileNew.Graphics.ShowLevel(true);
            return false;
        }

        [HarmonyPatch(typeof(StatsTileUpgrade), nameof(StatsTileUpgrade._CanUpgradeTileAndWhy))]
        [HarmonyPostfix]
        public static void AllowNegativeUpgrades1(Tile tile, ref (bool value, string whyLocKey) __result, StatsTileUpgrade __instance)
        {
            if (__instance.attackDelta > 0 && !tile.Attack.HasValue)
                return;
            if (__result.whyLocKey is "CannotUpgrade_MaxCooldown" or "CannotUpgrade_MinAttack" or "CannotUpgrade_NoAttack")
                __result = (true, "Can upgrade. This variable should not be used.");
        }

        [HarmonyPatch(typeof(AddAttackEffectTileUpgrade), nameof(AddAttackEffectTileUpgrade.CanUpgradeTile))]
        [HarmonyPostfix]
        public static void AllowNegativeUpgrades2(Tile tile, ref bool __result, AddAttackEffectTileUpgrade __instance)
        {
            __result = tile.Attack.Level < tile.Attack.MaxLevel && tile.Attack.AttackEffect is AttackEffectEnum.None && tile.Attack.CompatibleEffects.Contains(__instance.effect);
        }

        [HarmonyPatch(typeof(AddTileEffectTileUpgrade), nameof(AddTileEffectTileUpgrade.CanUpgradeTile))]
        [HarmonyPostfix]
        public static void AllowNegativeUpgrades3(Tile tile, ref bool __result, AddTileEffectTileUpgrade __instance)
        {
            __result = tile.Attack.Level < tile.Attack.MaxLevel && tile.Attack.TileEffect is TileEffectEnum.None;
        }

        #endregion

        #region Weapons

        [HarmonyPatch(typeof(Attack), nameof(Attack.Initialize))]
        [HarmonyPrefix]
        public static void MinLevel4(ref int maxLevel)
        {
            if (maxLevel == 0)
                return;

            const int minLevel = 4;
            if (maxLevel < minLevel)
            {
                maxLevel = minLevel;
            }
        }

        [HarmonyPatch(typeof(TetsuboAttack), nameof(TetsuboAttack.InitialCooldown), MethodType.Getter)]
        [HarmonyPostfix]
        public static void TetsuboCooldown(ref int __result)
        {
            __result = 5;
        }

        [HarmonyPatch(typeof(BackStrikeAttack), nameof(BackStrikeAttack.InitialCooldown), MethodType.Getter)]
        [HarmonyPostfix]
        public static void BackStrikeCooldown(ref int __result)
        {
            __result = 1;
        }

        [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.HeroPlayedTile))]
        [HarmonyPostfix]
        public static void CrossbowReloadFree(Tile tile, CombatManager __instance)
        {
            if (tile.Attack.AttackEnum is AttackEnum.Crossbow && tile.Attack.Value == 0)
                __instance.heroPlayedTileInThisUpdate = false;
        }

        [HarmonyPatch(typeof(CrossbowAttack), nameof(CrossbowAttack.Attack))]
        [HarmonyPostfix]
        public static void CrossbowReloadSprite1(CrossbowAttack __instance)
        {
            if (__instance.TileEffect != TileEffectEnum.FreePlay)
                __instance.tile.Graphics.background.sprite = TilesFactory.Instance.Sprites.TileBackgroundSprites[__instance.Value == 0 ? TileEffectEnum.FreePlay : TileEffectEnum.None];
            else
                __instance.Reload(true);
        }

        [HarmonyPatch(typeof(CrossbowAttack), nameof(CrossbowAttack.Reload))]
        [HarmonyPostfix]
        public static void CrossbowReloadSprite2(CrossbowAttack __instance)
        {
            if (__instance.TileEffect != TileEffectEnum.FreePlay)
                __instance.tile.Graphics.background.sprite = TilesFactory.Instance.Sprites.TileBackgroundSprites[__instance.Value == 0 ? TileEffectEnum.FreePlay : TileEffectEnum.None];
        }

        [HarmonyPatch(typeof(CrossbowAttack), nameof(CrossbowAttack.Initialize))]
        [HarmonyPostfix]
        public static void CrossbowReloadSprite3(CrossbowAttack __instance)
        {
            if (__instance.TileEffect != TileEffectEnum.FreePlay)
                __instance.tile.Graphics.background.sprite = TilesFactory.Instance.Sprites.TileBackgroundSprites[__instance.Value == 0 ? TileEffectEnum.FreePlay : TileEffectEnum.None];
        }

        [HarmonyPatch(typeof(BlazingSuiseiAttack), nameof(BlazingSuiseiAttack.PerformAttack))]
        [HarmonyPrefix]
        public static bool BlazingSuiseiEffect(ref IEnumerator __result, BlazingSuiseiAttack __instance)
        {
            __result = patch(__instance);
            return false;
            static IEnumerator patch(BlazingSuiseiAttack __instance)
            {
                __instance.Attacker.AttackInProgress = true;
                yield return new WaitForSeconds(__instance.agentThrowAnimationTime);
                __instance.blazingSuisei = EffectsManager.Instance.CreateInGameEffect("BlazingSuiseiEffect", __instance.Attacker.AgentGraphics.transform).GetComponent<TetheredWeaponEffect>();
                __instance.blazingSuisei.transform.localPosition = __instance.relativeClazingSuiseiPosition;
                SoundEffectsManager.Instance.Play("MissHit");
                Agent target = __instance.AgentInRange(__instance.Attacker);
                float throwDistance = (target == null) ? (__instance.Range.Max() * TechParams.effectiveGridCellSize) : Mathf.Abs(__instance.blazingSuisei.transform.position.x - target.transform.position.x);
                yield return __instance.blazingSuisei.PerformWeaponMove(0f, throwDistance, __instance.slowSpeed, __instance.accellerationTime);
                if (target != null)
                {
                    __instance.HitTarget(target);
                    var agentLeft = target.Cell.Neighbour(Dir.Left, 1)?.Agent;
                    var agentRight = target.Cell.Neighbour(Dir.Right, 1)?.Agent;
                    if (!target.IsAlive && agentLeft is not Hero && agentRight is not Hero)
                    {
                        EffectsManager.Instance.CreateInGameEffect("ExplosionEffect", target.Cell.transform);
                        if (agentLeft != null)
                        {
                            __instance.HitTarget(agentLeft);
                            //cell2.Agent.ReceiveAttack(new Hit(2, isDirectional: false, isCollision: false, synergizeWithSkills: false), __instance.Attacker);
                        }
                        if (agentRight != null)
                        {
                            __instance.HitTarget(agentRight);
                            //cell3.Agent.ReceiveAttack(new Hit(2, isDirectional: false, isCollision: false, synergizeWithSkills: false), __instance.Attacker);
                        }
                        if (__instance.blazingSuisei == null)
                            yield break;
                        __instance.blazingSuisei.SetHeadActive(visible: false);
                    }
                }
                yield return new WaitForSeconds(__instance.impactPauseTime);
                yield return __instance.blazingSuisei.PerformWeaponMove(throwDistance, 0f, __instance.fastSpeed, __instance.accellerationTime);
                __instance.blazingSuisei.DisappearAndDestroy();
                __instance.Attacker.AttackInProgress = false;
            }
        }

        [HarmonyPatch(typeof(ChakramAttack), nameof(ChakramAttack.CompatibleEffects), MethodType.Getter)]
        [HarmonyPrefix]
        public static void ChakramDoubleStrike(ChakramAttack __instance)
        {
            if (!TilesFactory.Instance.Sprites.TileSymbolSprites.ContainsKey((AttackEnum.Chakram, AttackEffectEnum.DoubleStrike)))
            {
                TilesFactory.Instance.Sprites.TileSymbolSprites[(AttackEnum.Chakram, AttackEffectEnum.DoubleStrike)]
                    = TilesFactory.Instance.Sprites.TileSymbolSprites[(AttackEnum.Chakram, AttackEffectEnum.Curse)];
                __instance.CompatibleEffects =
                    [
                        AttackEffectEnum.Ice,
                        AttackEffectEnum.Shockwave,
                        AttackEffectEnum.Poison,
                        AttackEffectEnum.PerfectStrike,
                        AttackEffectEnum.Curse,
                        AttackEffectEnum.DoubleStrike,
                    ];
            }
        }

        #endregion

        #region Enemies

        [HarmonyPatch(typeof(Enemy), nameof(Enemy.CreateAndAddTileToAttackQueue))]
        [HarmonyPrefix]
        public static void EnemyDamageOverride(ref AttackEnum attackEnum, ref AttackEffectEnum attackEffect, ref int? valueOverride, Enemy __instance)
        {
            if (__instance is CorruptedProgenyEnemy)
                valueOverride = 1;
        }

        [HarmonyPatch(typeof(CorruptedProgenyEnemy), nameof(CorruptedProgenyEnemy.AIPickAction))]
        [HarmonyPrefix]
        public static bool CorruptedProgenyActions(CorruptedProgenyEnemy __instance, ref ActionEnum __result)
        {
            if (__instance.FirstTurn)
                __result = ActionEnum.Wait;
            else if (__instance.previousAction is ActionEnum.Attack)
                __result = ActionEnum.Wait;
            else if (__instance.AttackQueue.NTiles == 0)
                __result = __instance.PlayTile(AttackEnum.CorruptedBarrage, __instance.AttackEffect);
            else if (__instance.AttackQueue.HasOffensiveAttack)
            {
                __result = ActionEnum.Attack;
                __instance.ApplyPoisonStatus(1);
            }
            else
                __result = ActionEnum.Wait;
            return false;
        }

        #endregion
    }
}
