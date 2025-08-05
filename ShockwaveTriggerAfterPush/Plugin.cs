using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections;
using TileEnums;
using TMPro;
using UnityEngine;
using Utils;

namespace ShockwaveTriggerAfterPush
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
        #region Shockwave

        /// <summary>
        /// Push coroutine that updates position for all units (even dead ones).
        /// </summary>
        public static IEnumerator PushTarget(Agent __instance, Dir dir)
        {
            float bounceHitAnimationTime = 0.025f;
            float bounceTime = 0.2f;
            Cell moveToCell = __instance.Cell.LastFreeCellInDirection(dir);
            Cell cell = moveToCell.Neighbour(dir, 1);
            var collisionTarget = cell?.Agent;
            if (__instance.IsAlive)
                __instance.Cell = moveToCell;
            else
                __instance._cell = moveToCell; // no not use setter, since agent is already off the board
            Vector3 position = __instance.transform.position;
            Vector3 to = (cell?.Agent != null ? ((moveToCell.transform.position + cell.transform.position) / 2f) : moveToCell.transform.position);
            SoundEffectsManager.Instance.Play("Dash");
            float time = Vector3.Distance(position, to) / 15f;
            yield return __instance.StartCoroutine(__instance.MoveToCoroutine(position, to, time, 0f, createDustEffect: true, createDashEffect: true));
            if (collisionTarget != null)
            {
                SoundEffectsManager.Instance.Play("CombatHit");
                EffectsManager.Instance.ScreenShake();
                collisionTarget.ReceiveAttack(new Hit(1, isDirectional: false, isCollision: true), __instance);
                __instance.ReceiveAttack(new Hit(1, isDirectional: false, isCollision: true), collisionTarget);
                yield return new WaitForSeconds(bounceHitAnimationTime);
                yield return __instance.StartCoroutine(__instance.MoveToCoroutine(to, moveToCell.transform.position, bounceTime));
            }
            yield return null;
        }

        /// <summary>
        /// Shockwave also damages groups of 1.
        /// </summary>
        [HarmonyPatch(typeof(ShockwaveEffect), nameof(ShockwaveEffect.Initialize))]
        [HarmonyPrefix]
        public static bool ShockwaveDamageTarget(Agent[] targets, ShockwaveEffect __instance)
        {
            var hitList = new List<Agent>();
            var waveDirection = new List<Dir>();
            foreach (var mainTarget in targets)
            {
                var direction = Globals.Hero.Cell.DirectionToOtherCell(mainTarget.Cell);
                for (int i = 0; i < 2; i++)
                {
                    if (i == 1)
                        direction = DirUtils.Opposite(direction);
                    Agent target = mainTarget;
                    while (true)
                    {
                        if (!hitList.Contains(target))
                        {
                            hitList.Add(target);
                            waveDirection.Add(direction);
                        }
                        var cellNeighbour = target.Cell.Neighbour(direction, 1);
                        if (cellNeighbour == null || cellNeighbour.Agent == null || cellNeighbour.Agent is Hero)
                            break;
                        target = cellNeighbour.Agent;
                    }
                }
            }

            SoundEffectsManager.Instance.Play("Shockwave");
            for (int i = 0; i < hitList.Count; i++)
            {
                hitList[i].ReceiveAttack(new Hit(__instance.damage, isDirectional: false), null);
                var go = UnityEngine.Object.Instantiate(__instance.shockwaveArcEffect, hitList[i].transform.position, Quaternion.identity);
                if (waveDirection[i] == Dir.Left)
                    go.transform.localScale = new Vector3(-1f, 1f, 1f);
            }
            __instance.StartCoroutine(__instance.WaitAndDestroy(2f));
            return false;
        }

        // GrapplingHookAttack
        // BaseSmokeBombAttack

        [HarmonyPatch(typeof(DragonPunchAttack), nameof(DragonPunchAttack.ApplyEffect))]
        [HarmonyPrefix]
        public static bool ShockwavePushDragonPunch(DragonPunchAttack __instance)
        {
            __instance.inProgress = false;
            __instance.target = __instance.AgentInRange(__instance.Attacker);
            if (__instance.target == null)
            {
                SoundEffectsManager.Instance.Play("MissHit");
                return false;
            }
            if (__instance.target is Enemy || __instance.target.IsAlive)
            {
                if (!__instance.target.Movable)
                    __instance.HitTarget(__instance.target);
                else
                    __instance.StartCoroutine(performAttack(__instance));
            }
            return false;

            static IEnumerator performAttack(DragonPunchAttack __instance)
            {
                __instance.inProgress = true;
                yield return null;
                __instance.target.ReceiveAttack(new Hit(__instance.Value, __instance.IsDirectional, false, true, __instance.IsNonLethal, __instance.TechNameAndStats), __instance.Attacker);
                EffectsManager.Instance.ScreenShake();
                SoundEffectsManager.Instance.Play("CombatHit");
                yield return __instance.StartCoroutine(PushTarget(__instance.target, __instance.Attacker.FacingDir));
                Attack.ProcessAttackEffects(__instance.target, __instance.AttackEffect);
                __instance.inProgress = false;
            }
        }

        [HarmonyPatch(typeof(TwinTessenAttack), nameof(TwinTessenAttack.ApplyEffect))]
        [HarmonyPrefix]
        public static bool ShockwavePushTwinTessen(TwinTessenAttack __instance)
        {
            __instance.inProgress = false;
            SoundEffectsManager.Instance.Play("TwinTessenAttack");
            __instance.targets = __instance.AgentsInRange(__instance.Attacker);
            if (__instance.targets.Length == 0)
            {
                SoundEffectsManager.Instance.Play("MissHit");
                return false;
            }

            if (__instance.gameObject.activeSelf)
                __instance.StartCoroutine(performPushing(__instance));
            return false;

            static IEnumerator performPushing(TwinTessenAttack __instance)
            {
                __instance.inProgress = true;
                yield return null;
                var movableTargets = new List<Agent>();
                foreach (Agent target in __instance.targets)
                {
                    target.ReceiveAttack(new Hit(__instance.Value, __instance.IsDirectional, false, true, __instance.IsNonLethal, __instance.TechNameAndStats), __instance.Attacker);
                    EffectsManager.Instance.ScreenShake();
                    SoundEffectsManager.Instance.Play("CombatHit");
                    if (!target.Movable)
                        Attack.ProcessAttackEffects(target, __instance.AttackEffect);
                    else
                        movableTargets.Add(target);
                }
                var coroutines = new IEnumerator[movableTargets.Count];
                for (int i = 0; i < movableTargets.Count; i++)
                {
                    Dir dir = (movableTargets[i].Cell.IndexInGrid > __instance.Attacker.Cell.IndexInGrid) ? Dir.Right : Dir.Left;
                    coroutines[i] = PushTarget(movableTargets[i], dir);
                    __instance.StartCoroutine(coroutines[i]);
                }
                while (!coroutines.All(c => c.Current == null))
                    yield return null;
                foreach (var target in movableTargets)
                    Attack.ProcessAttackEffects(target, __instance.AttackEffect);
                __instance.inProgress = false;
            }
        }

        [HarmonyPatch(typeof(TanegashimaAttack), nameof(TanegashimaAttack.ApplyEffect))]
        [HarmonyPrefix]
        public static bool ShockwaveTanegashima(TanegashimaAttack __instance)
        {
            var target = __instance.AgentInRange(__instance.Attacker);
            SoundEffectsManager.Instance.Play("TanegashimaFire");
            var gameObject = EffectsManager.Instance.CreateInGameEffect("TanegashimaSmokeEffect", __instance.Attacker.transform.position);
            if (__instance.Attacker.FacingDir == Dir.Left)
                gameObject.transform.localScale = new Vector3(-1f, 1f, 1f);
            var bulletEffect = EffectsManager.Instance.CreateInGameEffect("FastBulletEffect", __instance.Attacker.transform.position).GetComponent<FastBulletEffect>();
            var vectorFrom = __instance.Attacker.transform.position + Vector3.up * 0.5f;
            var vectorTo = (target == null) ? (__instance.Attacker.transform.position + Vector3.up * 0.5f + DirUtils.ToVec(__instance.Attacker.FacingDir) * 10f) : (target.transform.position + Vector3.up * 0.5f);
            bulletEffect.Initialize(vectorFrom, vectorTo);
            __instance.runningCoroutinesCounter = 0;
            if (target != null)
            {
                if (!target.Movable || !__instance.gameObject.activeSelf)
                    __instance.HitTarget(target);
                else
                    __instance.StartCoroutine(Recoil(__instance, true, target, __instance.Attacker.FacingDir));
            }
            if (__instance.gameObject.activeSelf)
                __instance.StartCoroutine(Recoil(__instance, false, __instance.Attacker, DirUtils.Opposite(__instance.Attacker.FacingDir)));
            return false;

            static IEnumerator Recoil(TanegashimaAttack __instance, bool doAttackEffects, Agent agent, Dir direction)
            {
                __instance.runningCoroutinesCounter++;
                yield return new WaitForSeconds(0.05f);
                if (doAttackEffects)
                {
                    agent.ReceiveAttack(new Hit(__instance.Value, __instance.IsDirectional, false, true, __instance.IsNonLethal, __instance.TechNameAndStats), __instance.Attacker);
                    EffectsManager.Instance.ScreenShake();
                    SoundEffectsManager.Instance.Play("CombatHit");
                }
                Cell cell = agent.Cell.Neighbour(direction, 1);
                if (cell == null)
                {
                    __instance.runningCoroutinesCounter--;
                    yield break;
                }
                Agent targetRecoilAgent = cell.Agent;
                if (targetRecoilAgent == null)
                {
                    if (agent.IsAlive)
                        agent.Cell = cell;
                    else
                        agent._cell = cell; // no not use setter, since agent is already off the board
                    float time = Vector3.Distance(agent.transform.position, cell.transform.position) / __instance.recoilSpeed;
                    yield return __instance.StartCoroutine(agent.MoveToCoroutine(agent.transform.position, cell.transform.position, time, 0f, createDustEffect: true));
                    if (agent == Globals.Hero)
                    {
                        EventsManager.Instance.HeroPerformedMoveAttack.Invoke();
                    }
                }
                else
                {
                    Vector3 from = agent.transform.position;
                    Vector3 position = cell.transform.position;
                    Vector3 hitPoint = (from + position) / 2f;
                    float time2 = Vector3.Distance(from, hitPoint) / __instance.recoilSpeed;
                    yield return __instance.StartCoroutine(agent.MoveToCoroutine(from, hitPoint, time2));
                    SoundEffectsManager.Instance.Play("CombatHit");
                    EffectsManager.Instance.ScreenShake();
                    targetRecoilAgent.ReceiveAttack(new Hit(1, isDirectional: true, isCollision: true, synergizeWithSkills: false), agent);
                    if (doAttackEffects)
                        agent.ReceiveAttack(new Hit(1, isDirectional: true, isCollision: true, synergizeWithSkills: false), targetRecoilAgent);
                    yield return new WaitForSeconds(__instance.bounceHitAnimationTime);
                    yield return __instance.StartCoroutine(agent.MoveToCoroutine(hitPoint, from, __instance.bounceTime));
                }
                if (doAttackEffects)
                    Attack.ProcessAttackEffects(agent, __instance.AttackEffect);
                yield return new WaitForSeconds(0.15f);
                __instance.runningCoroutinesCounter--;
            }
        }

        [HarmonyPatch(typeof(KiPushAttack), nameof(KiPushAttack.PerformAttack))]
        [HarmonyPrefix]
        public static bool ShockwavePushKiPush(ref IEnumerator __result, KiPushAttack __instance)
        {
            __result = patch(__instance);
            return false;
            static IEnumerator patch(KiPushAttack __instance)
            {
                __instance.Attacker.AttackInProgress = true;
                __instance.pushInProgress = false;
                Agent target = __instance.AgentInRange(__instance.Attacker);
                yield return new WaitForSeconds(__instance.anticipationTime);
                SoundEffectsManager.Instance.Play("KiPush");
                if (target == null)
                {
                    yield return new WaitForSeconds(0.2f);
                }
                else
                {
                    if (Cell.Distance(__instance.Attacker.Cell, target.Cell) > 1)
                        yield return new WaitForSeconds(__instance.propagationTime);
                    EffectsManager.Instance.CreateInGameEffect("KiPushEffect", target.transform.position, __instance.Attacker.FacingDir == Dir.Left);
                    yield return new WaitForSeconds(__instance.hitDelay);
                    target.ReceiveAttack(new Hit(__instance.Value, __instance.IsDirectional, isCollision: false, synergizeWithSkills: true, __instance.IsNonLethal, __instance.TechNameAndStats), __instance.Attacker);
                    EffectsManager.Instance.ScreenShake();
                    SoundEffectsManager.Instance.Play("CombatHit");
                    if (target.Movable)
                    {
                        __instance.pushInProgress = true;
                        yield return __instance.StartCoroutine(PushTarget(target, __instance.Attacker.FacingDir));
                        __instance.pushInProgress = false;
                    }
                    Attack.ProcessAttackEffects(target, __instance.AttackEffect);
                }
                yield return new WaitForSeconds(0.1f);
                __instance.Attacker.AttackInProgress = false;
            }
        }

        #endregion
    }
}
