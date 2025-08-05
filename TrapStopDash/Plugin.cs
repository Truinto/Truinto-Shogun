using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections;
using TileEnums;
using TMPro;
using UnityEngine;
using Utils;

namespace TrapStopDash
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
        #region Trap

        /// <summary>
        /// Make ShadowDash move to last cell, if direction has no agents at all.
        /// </summary>
        public static void ShadowDashTarget(BaseShadowDashAttack __instance)
        {
            __instance.Attacker.AttackInProgress = true;
            __instance.StartCoroutine(__instance.DashThrough(__instance.Attacker.Cell, __instance.Attacker.Cell.LastFreeCellInDirection(__instance.Direction), __instance.Direction));
        }

        public static void TrapTargetCell(Agent attacker, Dir direction, out Cell? trapCell)
        {
            trapCell = null;
            if (attacker is Hero)
                return;

            var traps = CombatSceneManager.Instance.Room.transform.GetComponentsInChildren<Trap>().Select(s => s.transform.position);
            if (!traps.Any())
                return;

            var currentCell = attacker.Cell;
            while (true)
            {
                currentCell = currentCell.Neighbour(direction, 1);
                if (currentCell == null || currentCell.Agent != null)
                    break;
                if (traps.Contains(currentCell.transform.position))
                {
                    trapCell = currentCell;
                    break;
                }
            }
        }

        [HarmonyPatch(typeof(BaseDashAttack), nameof(BaseDashAttack.Begin))]
        [HarmonyPrefix]
        public static bool TrapStopDash(Agent attacker, ref bool __result, BaseDashAttack __instance)
        {
            __instance.Attacker = attacker;

            TrapTargetCell(__instance.Attacker, __instance.Direction, out var trapCell);
            if (trapCell == null)
                return true;

            __instance.StartCoroutine(__instance.Dash(trapCell));
            __result = true;
            return false;
        }

        [HarmonyPatch(typeof(BaseChargeAttack), nameof(BaseChargeAttack.Begin))]
        [HarmonyPrefix]
        public static bool TrapStopCharge(Agent attacker, ref bool __result, BaseChargeAttack __instance)
        {
            __instance.Attacker = attacker;

            TrapTargetCell(__instance.Attacker, __instance.Direction, out var trapCell);
            if (trapCell == null)
                return true;
            if (trapCell.Neighbour(__instance.Direction, 1).Agent is Enemy)
                return true;

            __instance.Attacker.AttackInProgress = true;
            __instance.Attacker.SetIdleAnimation(false);
            __instance.StartCoroutine(__instance.DashOnly(trapCell));
            __result = true;
            return false;
        }

        [HarmonyPatch(typeof(BaseShadowDashAttack), nameof(BaseShadowDashAttack.Begin))]
        [HarmonyPrefix]
        public static bool TrapStopShadowDash(Agent attacker, ref bool __result, BaseShadowDashAttack __instance)
        {
            __instance.Attacker = attacker;

            TrapTargetCell(__instance.Attacker, __instance.Direction, out var trapCell);
            if (trapCell == null)
            {
                if (__instance.AgentsInRange(__instance.Attacker).Length == 0)
                {
                    ShadowDashTarget(__instance);
                    __result = true;
                    return false;
                }
                return true;
            }

            __instance.Attacker.AttackInProgress = true;
            __instance.StartCoroutine(__instance.DashThrough(__instance.Attacker.Cell, trapCell, __instance.Direction));
            __result = true;
            return false;
        }

        [HarmonyPatch(typeof(BaseSmokeBombAttack), nameof(BaseSmokeBombAttack.Begin))]
        [HarmonyPrefix]
        public static bool TrapStopSmokeBomb(Agent attacker, ref bool __result, BaseSmokeBombAttack __instance)
        {
            __instance.Attacker = attacker;

            var direction = __instance.Range[0] > 0 ? __instance.Attacker.FacingDir : DirUtils.Opposite(__instance.Attacker.FacingDir);

            TrapTargetCell(__instance.Attacker, direction, out var trapCell);
            if (trapCell == null)
                return true;

            __instance.Attacker.AttackInProgress = true;
            __instance.StartCoroutine(smokeBomb(__instance, trapCell));
            __result = true;
            return false;

            static IEnumerator smokeBomb(BaseSmokeBombAttack __instance, Cell targetCell)
            {
                __instance.Attacker.AttackInProgress = true;
                EffectsManager.Instance.CreateInGameEffect("SmokeBombEffect", __instance.Attacker.Cell.transform.position);
                EffectsManager.Instance.CreateInGameEffect("SmokeBombEffect", targetCell.transform.position);
                yield return new WaitForSeconds(__instance.preSwapTime);
                __instance.Attacker.Cell = targetCell;
                __instance.Attacker.transform.position = __instance.Attacker.Cell.transform.position;
                if (__instance.Attacker.IsAlive)
                    __instance.Attacker.Animator.SetTrigger("TriggerIdle");
                yield return new WaitForSeconds(__instance.postSwapTime);
                __instance.Attacker.AttackInProgress = false;
            }
        }

        #endregion
    }
}
