using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections;
using TileEnums;
using TMPro;
using UnityEngine;
using Utils;

namespace BossHealth
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
        #region Boss Health

        public static TextMeshProUGUI? BossHp;

        [HarmonyPatch(typeof(CorruptedSoulBossRoom), nameof(CorruptedSoulBossRoom.Begin))]
        [HarmonyPostfix]
        public static void Postfix(CorruptedSoulBossRoom __instance)
        {
            __instance.bossHealthBar.gameObject.SetActive(value: true);
        }

        [HarmonyPatch(typeof(BossHealthBar), nameof(BossHealthBar.UpdateHealth))]
        [HarmonyPostfix]
        public static void Postfix(int hp)
        {
            BossHp?.SetText(hp.ToString());
        }

        [HarmonyPatch(typeof(BossHealthBar), nameof(BossHealthBar.Initialize))]
        [HarmonyPostfix]
        public static void Patch(BossHealthBar __instance)
        {
            try
            {
                foreach (Transform item in __instance.transform)
                {
                    if (item.gameObject.name == "Bar")
                    {
                        var text = UnityEngine.Object.Instantiate(__instance.bossName);
                        text.transform.SetParent(__instance.bossName.transform.parent);
                        text.transform.localScale = __instance.bossName.transform.localScale;
                        text.transform.position = new Vector3(item.gameObject.transform.position.x + __instance.width + text.bounds.size.x / 2f + __instance.width / 10f, item.gameObject.transform.position.y, item.gameObject.transform.position.y + 10f);
                        text.gameObject.layer = __instance.bossName.gameObject.layer;
                        text.SetAllDirty();
                        BossHp = text;
                    }
                }
            } catch (Exception ex)
            {
                Plugin.Log(ex.ToString());
            }
        }

        #endregion
    }
}
