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

namespace ThornsMelee
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

        #region Throns

        /// <summary>
        /// Throns can be used in melee and entangles the enemy (freeze).
        /// </summary>
        [HarmonyPatch(typeof(ThornsAttack), nameof(ThornsAttack.Begin))]
        [HarmonyPrefix]
        public static bool ThronsMelee(Agent attacker, ThornsAttack __instance, ref bool __result)
        {
            __instance.AnimationTrigger = "";
            __instance.Attacker = attacker;
            if (__instance.Attacker.CellInFront.Agent is not Enemy target || target is ThornsEnemy)
                return true;

            __instance.AnimationTrigger = "EarthImpale";
            __instance.StartCoroutine(PerformAttack(__instance, target));
            __result = true;
            return false;

            static IEnumerator PerformAttack(ThornsAttack __instance, Agent target)
            {
                __instance.Attacker.AttackInProgress = true;
                //var sfx = EffectsManager.Instance.CreateInGameEffect("EarthImpaleEffect", __instance.Attacker.Cell.transform);
                //if (target.Cell.IndexInGrid < __instance.Attacker.Cell.IndexInGrid)
                //    sfx.transform.localScale = new Vector3(-1f, 1f, 1f);
                SoundEffectsManager.Instance.Play("EarthImpalePreAttack");
                yield return new WaitForSeconds(0.3f);
                //SoundEffectsManager.Instance.Play("EarthImpaleAttack");
                yield return new WaitForSeconds(0.1f);
                __instance.HitTarget(target);
                if (target.IsAlive)
                    target.ApplyIceStatus(4);
                yield return new WaitForSeconds(0.3f);
                __instance.Attacker.AttackInProgress = false;
            }
        }

        /// <summary>
        /// Grappler take damage, when they grapple thorns.
        /// </summary>
        [HarmonyPatch(typeof(ThornsEnemy), nameof(ThornsEnemy.ReceiveAttack))]
        [HarmonyPrefix]
        public static bool ThronsGrappled(Hit hit, Agent attacker, ThornsEnemy __instance)
        {
            if (attacker is GrapplerEnemy && attacker.Cell.Distance(__instance.Cell) > 1)
                return false;
            return true;
        }

        #endregion
    }
}
