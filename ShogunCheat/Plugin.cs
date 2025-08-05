using BepInEx;
using BepInEx.Logging;
using CombatEnums;
using HarmonyLib;
using Parameters;
using System.Collections;
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
        #region Main

        private static bool Initialized;
        [HarmonyPatch(typeof(TitleScreenManager), nameof(TitleScreenManager.Awake))]
        [HarmonyPostfix]
        public static void LatePatch()
        {
            if (Initialized)
                return;
            Initialized = true;

            Plugin.Log($"Initialize LatePatch");
            EventsManager.Instance.EnemyDied.AddListener(dropCoin);

            static void dropCoin(Enemy enemy)
            {
                PickupFactory.Instance.InstantiatePickup(PickupEnums.PickupEnum.Coin, enemy.Cell);
                Plugin.Log($"+1 coin");
            }
        }

        #endregion

        #region Upgrades

        // TODO: make after-boss shops less like to spawn sacrifice

        //[HarmonyPatch(typeof(MapManager), nameof(MapManager.Awake))]
        //[HarmonyPostfix]
        public static void UpgradesSandbox()
        {
            try
            {
                Plugin.Log($"Shop components: {MapManager.Instance.map.shopComponentsLeft.Join(f => f.name)}");
                _ = MapManager.Instance.map.MapLocations[0].location;

                //TilesFactory.Instance.Create(AttackEnum.TwinTessen, 4);
            } catch (Exception e)
            {
                Plugin.Log($"Exception in Sandbox {e}");
            }
        }

        //[HarmonyPatch(typeof(Progression), nameof(Progression.PickRoomVariant))]
        //[HarmonyPostfix]
        public static void DebugRooms(Location location, int iRoom, ref int __result)
        {
            try
            {
                // TODO: ShopLocation init, replace sacrifice after boss battles
                var sb = new StringBuilder();
                int num = location.NVariantsForRoom(iRoom);
                sb.Append($"Pick rooms ({num}): ");
                for (int i = 0; i < num; i++)
                {
                    var room = location.GetRoom(iRoom, i);
                    if (room is ShopRoom shop && location is ShopLocation shopLocation)
                        sb.Append($"shop={room.Id}:{room.name}:{shopLocation.leftShopComponent.GetComponent<TileUpgradeInShop>()?.tileUpgrades[0].tileUpgradeEnum}, ");
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
                __instance.upgradesAndProbabilities[upgrade] *= 2f;
                if (!TilesManager.Instance.Deck.Any(a => a.Attack.AttackEffect is AttackEffectEnum.Shockwave))
                    __instance.upgradesAndProbabilities[upgrade] *= 10f;
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
            Plugin.Log($"Shop Gold={__instance.basePrice} - {__instance.tileUpgrades.Join(f => f.tileUpgradeEnum.ToString())}");

            if (rewardSaveData == null)
            {
                var upgrades = __instance.tileUpgrades.ToList();

                if (upgrades.Count >= 2 && !TilesManager.Instance.Deck.Any(a => a.Attack is KunaiAttack && a.Attack.AttackEffect is AttackEffectEnum.None && a.Attack.Cooldown >= 5))
                    upgrades.RemoveAll(f => f.tileUpgradeEnum is TileUpgradeEnum.PerfectStrike);

                if (upgrades.Count >= 2)
                    upgrades.RemoveAll(f => f.tileUpgradeEnum is TileUpgradeEnum.Sacrifice);

                if (upgrades.Count >= 2)
                    upgrades.RemoveAll(f => f.tileUpgradeEnum is TileUpgradeEnum.Cooldown_m1);

                if (upgrades.Count > 0)
                    __instance.tileUpgrades = upgrades.ToArray();
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

        // TODO: remove
        //[HarmonyPatch(typeof(StatsTileUpgrade), nameof(StatsTileUpgrade.Upgrade))]
        //[HarmonyPrefix]
        //public static void ChangeTileUpgradeCooldown(StatsTileUpgrade __instance)
        //{
        //    if (__instance.tileUpgradeEnum is TileUpgradeEnum.Attack_p2_Cooldown_p3)
        //    {
        //        __instance.cooldownDelta = 1;
        //    }
        //}

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

        [HarmonyPatch(typeof(CrossbowAttack), nameof(CrossbowAttack.InitialCooldown), MethodType.Getter)]
        [HarmonyPostfix]
        public static void CrossbowCooldown(ref int __result)
        {
            __result = 8;
        }

        [HarmonyPatch(typeof(CrossbowAttack), nameof(CrossbowAttack.Attack))]
        [HarmonyPostfix]
        public static void CrossbowReload(CrossbowAttack __instance)
        {
            __instance.Reload(true);
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
            if (__instance.FirstTurn || __instance.previousAction is ActionEnum.Attack)
                __result = ActionEnum.Wait;
            else if (__instance.AttackQueue.NTiles == 0)
                __result = __instance.PlayTile(AttackEnum.CorruptedBarrage, __instance.AttackEffect);
            else if (__instance.AttackQueue.HasOffensiveAttack)
                __result = ActionEnum.Attack;
            else
                __result = ActionEnum.Wait;
            return false;
        }

        #endregion
    }
}
