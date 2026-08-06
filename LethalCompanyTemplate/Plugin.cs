using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using System.Collections;

namespace MonsterOverdoseCompany
{
    [BepInPlugin("com.votre_pseudo.monsteroverdosecompany", "Monster-Overdose-Company", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private readonly Harmony harmony = new Harmony("com.votre_pseudo.monsteroverdosecompany");
        public static Plugin Instance;

        void Awake()
        {
            Instance = this;
            Logger.LogInfo("[Monster-Overdose-Company] Mod chargé avec succès ! Préparez-vous au chaos.");
            harmony.PatchAll();
        }
    }

    // ==========================================
    // 1. RÈGLE : BONUS DE +20% DE SCRAP
    // ==========================================
    [HarmonyPatch(typeof(RoundManager))]
    public class ScrapBonusPatch
    {
        [HarmonyPatch("SpawnScrapInLevel")]
        [HarmonyPrefix]
        static void BoostScrapAmount(RoundManager __instance)
        {
            if (__instance.currentLevel != null)
            {
                __instance.currentLevel.minScrap = Mathf.RoundToInt(__instance.currentLevel.minScrap * 1.20f);
                __instance.currentLevel.maxScrap = Mathf.RoundToInt(__instance.currentLevel.maxScrap * 1.20f);
                Debug.Log($"[Monster-Overdose-Company] Bonus de scrap (+20%) appliqué ! Max scrap: {__instance.currentLevel.maxScrap}");
            }
        }
    }

    // ==========================================
    // 2. DÉCLENCHEURS (ENTRÉE ET SORTIE COMPLEXE)
    // ==========================================
    [HarmonyPatch(typeof(EntranceTeleport))]
    public class EntrancePatch
    {
        [HarmonyPatch("TeleportPlayer")]
        [HarmonyPostfix]
        static void Postfix(bool ___isEntranceToBuilding)
        {
            // Entrée dans le complexe
            if (___isEntranceToBuilding && !ChaosManager.hasPlayerEntered)
            {
                ChaosManager.hasPlayerEntered = true;
                Debug.Log("[Monster-Overdose-Company] Joueur entre ! Chrono activé.");
            }
            // Sortie du complexe
            else if (!___isEntranceToBuilding && ChaosManager.hasPlayerEntered && !RobotManager.hasSequenceStarted)
            {
                RobotManager.hasSequenceStarted = true;
                Plugin.Instance.StartCoroutine(RobotManager.WakeUpRobotsSequence());
                Debug.Log("[Monster-Overdose-Company] Joueur sort ! Lancement du réveil progressif des 25 robots !");
            }
        }
    }

    // ==========================================
    // 3. GESTION DES 25 ROBOTS (ZONE SÉCURISÉE VAISSEAU DE 20M)
    // ==========================================
    public class RobotManager
    {
        public static List<RadMechAI> spawnedRobots = new List<RadMechAI>();
        public static bool hasSequenceStarted = false;

        public static void InitRobots(RoundManager manager)
        {
            spawnedRobots.Clear();
            hasSequenceStarted = false;

            // Cherche le prefab du RadMech / Old Bird
            SpawnableEnemyWithRarity robotEnemy = manager.currentLevel.OutsideEnemies.Find(e => e.enemyType.enemyName.ToLower().Contains("radmech"));
            if (robotEnemy == null) return;

            // Repère la position exacte du vaisseau (zone d'atterrissage)
            Vector3 shipPosition = Vector3.zero;
            GameObject shipObj = GameObject.FindWithTag("Ship");
            if (shipObj != null)
            {
                shipPosition = shipObj.transform.position;
            }
            else if (StartOfRound.Instance != null && StartOfRound.Instance.elevatorTransform != null)
            {
                shipPosition = StartOfRound.Instance.elevatorTransform.position;
            }

            int spawnedCount = 0;
            int attempts = 0;

            // Génère 25 robots désactivés à l'extérieur
            while (spawnedCount < 25 && attempts < 200)
            {
                attempts++;
                Vector3 randomPoint = manager.outsideRadius * Random.insideUnitSphere;
                NavMeshHit hit;

                if (NavMesh.SamplePosition(randomPoint, out hit, 50f, NavMesh.AllAreas))
                {
                    // RÈGLE SÉCURITÉ : Interdiction stricte de spawner à moins de 20 mètres du vaisseau
                    float distanceToShip = Vector3.Distance(hit.position, shipPosition);
                    if (distanceToShip < 20f)
                    {
                        continue; // Zone d'atterrissage préservée, on cherche un autre endroit
                    }

                    GameObject obj = Object.Instantiate(robotEnemy.enemyType.enemyPrefab, hit.position, Quaternion.identity);
                    RadMechAI robot = obj.GetComponent<RadMechAI>();
                    if (robot != null)
                    {
                        // Robot initialement éteint / désactivé
                        robot.inFlight = false;
                        robot.creatureSFX.Stop();
                        spawnedRobots.Add(robot);
                        spawnedCount++;
                    }
                }
            }
            Debug.Log($"[Monster-Overdose-Company] {spawnedCount} robots désactivés générés dehors (zone de 20m autour du vaisseau préservée).");
        }

        // Coroutine pour réveiller 1 robot toutes les 10 secondes
        public static IEnumerator WakeUpRobotsSequence()
        {
            foreach (RadMechAI robot in spawnedRobots)
            {
                if (robot != null && !robot.isEnemyDead)
                {
                    robot.SwitchToBehaviourState(1); 
                    Debug.Log("[Monster-Overdose-Company] Un robot vient de se réveiller !");
                }
                yield return new WaitForSeconds(10f); // Réveil individuel toutes les 10s
            }
        }
    }

    // ==========================================
    // 4. GESTION DU CHAOS ET DES SPAWNS MONSTRES
    // ==========================================
    [HarmonyPatch(typeof(RoundManager))]
    public class ChaosManager
    {
        public static bool hasPlayerEntered = false;
        public static float gameTimer = 0f;
        public static float spawnIntervalTimer = 0f;

        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        static void ResetOnStart(RoundManager __instance)
        {
            hasPlayerEntered = false;
            gameTimer = 0f;
            spawnIntervalTimer = 0f;
            RobotManager.InitRobots(__instance);
        }

        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        static void UpdateChaos(RoundManager __instance)
        {
            if (!hasPlayerEntered || __instance.currentLevel == null) return;

            gameTimer += Time.deltaTime;
            spawnIntervalTimer += Time.deltaTime;

            // Augmentation du cap de monstres (+10 toutes les 2 min, max 60)
            int currentMaxEnemies = 10 + (int)(gameTimer / 120f) * 10;
            if (currentMaxEnemies > 60) currentMaxEnemies = 60;

            __instance.currentLevel.maxEnemyPowerCount = currentMaxEnemies;
            __instance.currentLevel.maxOutsideEnemyPowerCount = currentMaxEnemies;

            // Tentative de spawn toutes les 10s (30% puis 85%)
            if (spawnIntervalTimer >= 10f)
            {
                spawnIntervalTimer = 0f;
                float chance = (gameTimer < 120f) ? 0.30f : 0.85f;

                if (Random.value <= chance)
                {
                    TrySpawnChaosEnemy(__instance);
                }
            }

            // Après 5 minutes : Passage en 100% Hostile
            if (gameTimer >= 300f)
            {
                MakeAllEnemiesHostile();
            }
        }

        static void TrySpawnChaosEnemy(RoundManager manager)
        {
            PlayerControllerB targetPlayer = StartOfRound.Instance.allPlayerScripts[Random.Range(0, StartOfRound.Instance.allPlayerScripts.Length)];
            if (targetPlayer == null || !targetPlayer.isPlayerControlled || targetPlayer.isPlayerDead) return;

            List<SpawnableEnemyWithRarity> allEnemies = new List<SpawnableEnemyWithRarity>();
            if (manager.currentLevel.Enemies != null) allEnemies.AddRange(manager.currentLevel.Enemies);
            if (manager.currentLevel.OutsideEnemies != null) allEnemies.AddRange(manager.currentLevel.OutsideEnemies);

            if (allEnemies.Count == 0) return;

            SpawnableEnemyWithRarity selectedEnemy = allEnemies[Random.Range(0, allEnemies.Count)];
            string enemyName = selectedEnemy.enemyType.enemyName.ToLower();

            bool isRobot = enemyName.Contains("radmech") || enemyName.Contains("vieux gardien") || enemyName.Contains("old bird");
            bool isLeviathan = enemyName.Contains("sandworm") || enemyName.Contains("ver") || enemyName.Contains("leviathan");
            bool isInside = targetPlayer.isInsideFactory;

            if (isInside && isRobot) return;
            if (isLeviathan && gameTimer < 420f) return; // Léviathan uniquement à partir de 7 min

            Vector3 spawnPos = targetPlayer.transform.position + (Random.insideUnitSphere * Random.Range(5f, 30f));

            NavMeshHit hit;
            if (NavMesh.SamplePosition(spawnPos, out hit, 30f, NavMesh.AllAreas))
            {
                GameObject enemyObj = Object.Instantiate(selectedEnemy.enemyType.enemyPrefab, hit.position, Quaternion.identity);
                EnemyAI enemyAI = enemyObj.GetComponent<EnemyAI>();
                if (enemyAI != null)
                {
                    manager.SpawnEnemyOnServer(hit.position, 0f, manager.currentLevel.Enemies.IndexOf(selectedEnemy));
                }
            }
        }

        static void MakeAllEnemiesHostile()
        {
            EnemyAI[] enemies = Object.FindObjectsOfType<EnemyAI>();
            foreach (EnemyAI enemy in enemies)
            {
                if (enemy.isEnemyDead) continue;
                if (enemy is HoarderBugAI bug) bug.AngryAtPlayer(StartOfRound.Instance.allPlayerScripts[0]);
                if (enemy is PufferAI lizard) lizard.creatureSFX.Play(); 
                if (enemy is BaboonBirdAI baboon) baboon.threatened = true;
                if (enemy is CrawlerAI spider) spider.makeClingSound = true;
            }
        }
    }

    // ==========================================
    // 5. RÈGLE LÉVIATHAN (7 MIN & 20M)
    // ==========================================
    [HarmonyPatch(typeof(SandWormAI))]
    public class LeviathanIndoorPatch
    {
        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        static void CustomLeviathanMovement(SandWormAI __instance)
        {
            // Uniquement en intérieur ET après 7 minutes
            if (__instance.isInsideFactory && __instance.targetPlayer != null && ChaosManager.gameTimer >= 420f)
            {
                if (__instance.agent == null)
                {
                    __instance.agent = __instance.gameObject.GetComponent<NavMeshAgent>();
                }

                if (__instance.agent != null && __instance.agent.isOnNavMesh)
                {
                    float distance = Vector3.Distance(__instance.transform.position, __instance.targetPlayer.transform.position);
                    __instance.openDoorSpeed = 0f;

                    // Hors du périmètre de 20m -> Sprint à 80 km/h (22f)
                    if (distance > 20f)
                    {
                        __instance.agent.speed = 22f; 
                        __instance.SetDestinationToPosition(__instance.targetPlayer.transform.position);
                    }
                    // Dans le périmètre de 20m -> Redevient normal (5f)
                    else
                    {
                        __instance.agent.speed = 5f; 
                    }
                }
            }
        }
    }
}
