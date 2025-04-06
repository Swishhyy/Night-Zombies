using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using Physics = UnityEngine.Physics;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Configuration;
using ConVar;
using Pool = Facepunch.Pool;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Night Zombies", "0x89A", "5.0.1")]
    [Description("Spawns and kills zombies at set times")]
    class NightZombies : RustPlugin
    {
        private const string DeathSound = "assets/prefabs/npc/murderer/sound/death.prefab";
        private const string RemoveMeMethodName = nameof(DroppedItemContainer.RemoveMe);
        private const int GrenadeItemId = 1840822026;
        
        private static NightZombies _instance;
        private static Configuration _config;
        private DynamicConfigFile _dataFile;
        
        [PluginReference("Kits")]
        private Plugin _kits;
        
        [PluginReference("Vanish")]
        private Plugin _vanish;
        
        // The top-level spawn controller now manages multiple spawn wave controllers.
        private SpawnController _spawnController;
        // New persistent (monument) zombie controller.
        private MonumentController _monumentController;
        
        #region -Init-
        
        private void Init()
        {
            _instance = this;
            
            // Register permissions
            permission.RegisterPermission("nightzombies.admin", this);
            permission.RegisterPermission("nightzombies.ignore", this);
            
            // Create our multi-wave controller and persistent zombie controller.
            _spawnController = new SpawnController();
            _monumentController = new MonumentController();
            
            // Read saved number of days since last spawn.
            _dataFile = Interface.Oxide.DataFileSystem.GetFile("NightZombies-daysSinceSpawn");
            try
            {
                _spawnController.DaysSinceLastSpawn = _dataFile.ReadObject<int>();
            }
            catch
            {
                PrintWarning("Failed to load saved days since last spawn, defaulting to 0");
                _spawnController.DaysSinceLastSpawn = 0;
            }
            
            if (_config.Behaviour.SentriesAttackZombies)
                Unsubscribe(nameof(OnTurretTarget));
            if (_config.Destroy.SpawnLoot)
                Unsubscribe(nameof(OnCorpsePopulate));
            if (_config.Behaviour.Ignored.Count == 0 && !_config.Behaviour.IgnoreHumanNpc && _config.Behaviour.AttackSleepers)
                Unsubscribe(nameof(OnNpcTarget));
        }
        
        private void OnServerInitialized()
        {
            if (!_kits?.IsLoaded ?? false)
                PrintWarning("Kits is not loaded, custom kits will not work");
            
            // Start time check for each spawn wave.
            if (_config.SpawnWaves != null && _config.SpawnWaves.Count > 0)
            {
                TOD_Sky.Instance.Components.Time.OnMinute += _spawnController.TimeTick;
                TOD_Sky.Instance.Components.Time.OnDay += OnDay;
            }
            
            // Initialize persistent monument zombies.
            _monumentController.Initialize();
        }
        
        private void Unload()
        {
            TOD_Sky.Instance.Components.Time.OnMinute -= _spawnController.TimeTick;
            TOD_Sky.Instance.Components.Time.OnDay -= OnDay;
            
            _dataFile.WriteObject(_spawnController.DaysSinceLastSpawn);
            _spawnController?.Shutdown();
            _monumentController?.Shutdown();
            
            _config = null;
            _instance = null;
        }
        
        private void OnDay() => _spawnController.DaysSinceLastSpawn++;
        
        #endregion
        
        #region -Chat Commands-
        
        [ChatCommand("forcespawn")]
        private void ForceSpawnCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "nightzombies.admin"))
            {
                player.ChatMessage("You do not have permission to use this command.");
                return;
            }
            
            _spawnController.ForceSpawn();
            player.ChatMessage("Forced spawn initiated. Zombies will vanish in 10 minutes.");
        }
        
        [ChatCommand("despawnall")]
        private void DespawnAllCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, "nightzombies.admin"))
            {
                player.ChatMessage("You do not have permission to use this command.");
                return;
            }
            
            _spawnController.DespawnAll();
            _monumentController.DespawnAll();
            player.ChatMessage("All zombies and corpses have been despawned.");
        }
        
        #endregion
        
        #region -Oxide Hooks-
        
        private object OnNpcTarget(ScarecrowNPC npc, BaseEntity target)
        {
            return CanAttack(target);
        }
        
        private object OnTurretTarget(NPCAutoTurret turret, ScarecrowNPC entity)
        {
            if (entity == null)
                return null;
            return true;
        }
        
        private object OnPlayerDeath(ScarecrowNPC scarecrow, HitInfo info)
        {
            Effect.server.Run(DeathSound, scarecrow.transform.position);
            _spawnController.ZombieDied(scarecrow);
            // Monument zombies are persistent and handled separately.
            
            if (_config.Destroy.LeaveCorpseKilled)
                return null;
            
            NextTick(() =>
            {
                if (scarecrow == null || scarecrow.IsDestroyed)
                    return;
                scarecrow.AdminKill();
            });
            return true;
        }
        
        private BaseCorpse OnCorpsePopulate(ScarecrowNPC npcPlayer, NPCPlayerCorpse corpse)
        {
            return corpse;
        }
        
        private void OnEntitySpawned(NPCPlayerCorpse corpse)
        {
            // Default to first wave's display name.
            corpse.playerName = _config.SpawnWaves[0].Zombies.DisplayName;
        }
        
        private void OnEntitySpawned(DroppedItemContainer container)
        {
            if (!_config.Destroy.HalfBodybagDespawn)
                return;
            NextTick(() =>
            {
                if (container != null && container.playerName == _config.SpawnWaves[0].Zombies.DisplayName)
                {
                    container.CancelInvoke(RemoveMeMethodName);
                    container.Invoke(RemoveMeMethodName, container.CalculateRemovalTime() / 2);
                }
            });
        }
        
        #endregion
        
        #region -Helpers-
        
        private object CanAttack(BaseEntity target)
        {
            // If target is a player with the "nightzombies.ignore" permission, never attack them.
            if (target is BasePlayer player && permission.UserHasPermission(player.UserIDString, "nightzombies.ignore"))
                return true;
            
            if (_config.Behaviour.Ignored.Contains(target.ShortPrefabName) ||
                (_config.Behaviour.IgnoreHumanNpc && HumanNPCCheck(target)) ||
                (!_config.Behaviour.AttackSleepers && target is BasePlayer player2 && player2.IsSleeping()))
                return true;
            
            return null;
        }
        
        private bool HumanNPCCheck(BaseEntity target)
        {
            return target is BasePlayer player && !player.userID.IsSteamId() &&
                   target is not ScientistNPC && target is not ScarecrowNPC;
        }
        
        #endregion
        
        #region -Classes-
        
        // Top-level spawn controller managing multiple spawn wave controllers.
        private class SpawnController
        {
            public int DaysSinceLastSpawn;
            private List<SpawnWaveController> waveControllers = new List<SpawnWaveController>();
            
            public SpawnController()
            {
                if (_config.SpawnWaves == null || _config.SpawnWaves.Count == 0)
                    _config.SpawnWaves = new List<Configuration.SpawnWave> { new Configuration.SpawnWave() };
                foreach (var wave in _config.SpawnWaves)
                {
                    waveControllers.Add(new SpawnWaveController(wave));
                }
            }
            
            public void TimeTick()
            {
                foreach (var wave in waveControllers)
                    wave.TimeTick();
            }
            
            public void ForceSpawn()
            {
                foreach (var wave in waveControllers)
                    wave.ForceSpawn();
            }
            
            public void DespawnAll()
            {
                foreach (var wave in waveControllers)
                    wave.DespawnAll();
            }
            
            public void ZombieDied(ScarecrowNPC zombie)
            {
                foreach (var wave in waveControllers)
                {
                    if (wave.RemoveZombie(zombie))
                        break;
                }
            }
            
            public void Shutdown()
            {
                foreach (var wave in waveControllers)
                    wave.Shutdown();
            }
        }
        
        // Controller for a single spawn wave.
        private class SpawnWaveController
        {
            private readonly Configuration.SpawnWave waveConfig;
            private readonly int spawnLayerMask = LayerMask.GetMask("Default", "Tree", "Construction", "World", "Vehicle_Detailed", "Deployed");
            private readonly WaitForSeconds waitTenthSecond = new WaitForSeconds(0.1f);
            private bool _spawned;
            private Coroutine _currentCoroutine;
            private readonly List<ScarecrowNPC> zombies = new List<ScarecrowNPC>();
            
            // Dictionary to track how long each zombie has been fully submerged.
            private Dictionary<ScarecrowNPC, float> waterTimes = new Dictionary<ScarecrowNPC, float>();
            // Timer to check water status every second.
            private Timer waterTimer;
            // Head offset used to determine if a zombie is fully underwater.
            private const float ZombieHeadOffset = 1.8f;
            
            public SpawnWaveController(Configuration.SpawnWave config)
            {
                waveConfig = config;
                waterTimer = _instance.timer.Every(1f, () => CheckWaterStatus());
            }
            
            private bool IsSpawnTime
            {
                get
                {
                    if (waveConfig.SpawnTime > waveConfig.DestroyTime)
                        return Env.time >= waveConfig.SpawnTime || Env.time < waveConfig.DestroyTime;
                    else
                        return Env.time <= waveConfig.SpawnTime || Env.time > waveConfig.DestroyTime;
                }
            }
            
            private bool IsDestroyTime
            {
                get
                {
                    if (waveConfig.SpawnTime > waveConfig.DestroyTime)
                        return Env.time >= waveConfig.DestroyTime && Env.time < waveConfig.SpawnTime;
                    else
                        return Env.time <= waveConfig.DestroyTime && Env.time > waveConfig.SpawnTime;
                }
            }
            
            private bool CanSpawn()
            {
                return !_spawned && Random.Range(0f, 100f) < waveConfig.Chance.Chance;
            }
            
            public void TimeTick()
            {
                if (CanSpawn() && IsSpawnTime)
                    _currentCoroutine = ServerMgr.Instance.StartCoroutine(SpawnZombies());
                else if (zombies.Count > 0 && IsDestroyTime && _spawned)
                    _currentCoroutine = ServerMgr.Instance.StartCoroutine(RemoveZombies());
            }
            
            private IEnumerator SpawnZombies()
            {
                _spawned = true;
                for (int i = 0; i < waveConfig.Zombies.Population; i++)
                {
                    SpawnZombie();
                    yield return waitTenthSecond;
                }
                _currentCoroutine = null;
            }
            
            private IEnumerator RemoveZombies(bool shuttingDown = false)
            {
                if (zombies.Count == 0)
                    yield break;
                if (_currentCoroutine != null)
                    ServerMgr.Instance.StopCoroutine(_currentCoroutine);
                foreach (ScarecrowNPC zombie in zombies.ToArray())
                {
                    if (zombie == null || zombie.IsDestroyed)
                        continue;
                    zombie.AdminKill();
                    yield return !shuttingDown ? waitTenthSecond : null;
                }
                zombies.Clear();
                _spawned = false;
                _currentCoroutine = null;
            }
            
            public void ForceSpawn()
            {
                _spawned = true;
                for (int i = 0; i < waveConfig.Zombies.Population; i++)
                    SpawnZombie();
                _instance.timer.Once(600f, () =>
                {
                    foreach (ScarecrowNPC zombie in zombies.ToArray())
                    {
                        if (zombie != null && !zombie.IsDestroyed)
                        {
                            zombie.AdminKill();
                            zombies.Remove(zombie);
                        }
                    }
                    _spawned = false;
                });
            }
            
            public void DespawnAll()
            {
                if (_currentCoroutine != null)
                    ServerMgr.Instance.StopCoroutine(_currentCoroutine);
                foreach (ScarecrowNPC zombie in zombies.ToArray())
                {
                    if (zombie != null && !zombie.IsDestroyed)
                        zombie.AdminKill();
                }
                zombies.Clear();
                _spawned = false;
            }
            
            public bool RemoveZombie(ScarecrowNPC zombie)
            {
                if (zombies.Contains(zombie))
                {
                    zombies.Remove(zombie);
                    if (IsSpawnTime)
                        SpawnZombie();
                    return true;
                }
                return false;
            }
            
            public void Shutdown()
            {
                if (waterTimer != null)
                    waterTimer.Destroy();
                ServerMgr.Instance.StartCoroutine(RemoveZombies(true));
            }
            
            #region -Util-
            
            private void SpawnZombie()
            {
                if (zombies.Count >= waveConfig.Zombies.Population)
                    return;
                Vector3 position = (waveConfig.SpawnNearPlayers && BasePlayer.activePlayerList.Count >= waveConfig.MinNearPlayers && GetRandomPlayer(out BasePlayer player))
                    ? GetRandomPositionAroundPlayer(player)
                    : GetRandomPosition();
                ScarecrowNPC zombie = GameManager.server.CreateEntity("assets/prefabs/npc/scarecrow/scarecrow.prefab", position) as ScarecrowNPC;
                if (zombie == null)
                    return;
                zombie.Spawn();
                zombie.displayName = waveConfig.Zombies.DisplayName;
                if (zombie.TryGetComponent(out BaseNavigator navigator))
                {
                    navigator.ForceToGround();
                    navigator.PlaceOnNavMesh(0);
                }
                float health = waveConfig.Zombies.Health;
                zombie.SetMaxHealth(health);
                zombie.SetHealth(health);
                if (_instance._kits != null && waveConfig.Zombies.Kits.Count > 0)
                {
                    zombie.inventory.containerWear.Clear();
                    ItemManager.DoRemoves();
                    _instance._kits.Call("GiveKit", zombie, waveConfig.Zombies.Kits.GetRandom());
                }
                if (!_config.Behaviour.ThrowGrenades)
                {
                    foreach (Item item in zombie.inventory.FindItemsByItemID(GrenadeItemId))
                        item.Remove();
                    ItemManager.DoRemoves();
                }
                zombies.Add(zombie);
            }
            
            private bool GetRandomPlayer(out BasePlayer player)
            {
                List<BasePlayer> players = Pool.GetList<BasePlayer>();
                foreach (BasePlayer bplayer in BasePlayer.activePlayerList)
                {
                    if (bplayer.IsFlying || _instance._vanish?.Call<bool>("IsInvisible", bplayer) == true)
                        continue;
                    players.Add(bplayer);
                }
                player = players.GetRandom();
                Pool.FreeList(ref players);
                return player;
            }
            
            private Vector3 GetRandomPosition()
            {
                Vector3 position = Vector3.zero;
                for (int i = 0; i < 6; i++)
                {
                    float x = Random.Range(-TerrainMeta.Size.x / 2, TerrainMeta.Size.x / 2),
                          z = Random.Range(-TerrainMeta.Size.z / 2, TerrainMeta.Size.z / 2),
                          y = TerrainMeta.HeightMap.GetHeight(new Vector3(x, 0, z));
                    position = new Vector3(x, y + 0.5f, z);
                    if (!AntiHack.TestInsideTerrain(position) && !IsInObject(position) && !IsInOcean(position))
                        break;
                }
                if (position == Vector3.zero)
                    position.y = TerrainMeta.HeightMap.GetHeight(0, 0);
                return position;
            }
            
            private Vector3 GetRandomPositionAroundPlayer(BasePlayer player)
            {
                Vector3 playerPos = player.transform.position;
                Vector3 position = Vector3.zero;
                float maxDist = waveConfig.MaxDistance;
                for (int i = 0; i < 6; i++)
                {
                    position = new Vector3(Random.Range(playerPos.x - maxDist, playerPos.x + maxDist), 0,
                                           Random.Range(playerPos.z - maxDist, playerPos.z + maxDist));
                    position.y = TerrainMeta.HeightMap.GetHeight(position);
                    if (!AntiHack.TestInsideTerrain(position) && !IsInObject(position) && !IsInOcean(position) &&
                        Vector3.Distance(playerPos, position) > waveConfig.MinDistance)
                        break;
                }
                if (position == Vector3.zero)
                    position = GetRandomPosition();
                return position;
            }
            
            private bool IsInObject(Vector3 position)
            {
                return Physics.OverlapSphere(position, 0.5f, spawnLayerMask).Length > 0;
            }
            
            private bool IsInOcean(Vector3 position)
            {
                return WaterLevel.GetWaterDepth(position, true, true) > 0.25f;
            }
            
            private void Broadcast(string key, params object[] values)
            {
                foreach (BasePlayer player in BasePlayer.activePlayerList)
                    player.ChatMessage(string.Format(_instance.GetMessage(key, player.UserIDString), values));
            }
            
            #endregion
            
            // Check water status: if a zombie's head is fully underwater (i.e. water depth at its feet exceeds ZombieHeadOffset)
            // continuously for 10 seconds, despawn it.
            private void CheckWaterStatus()
            {
                foreach (var zombie in new List<ScarecrowNPC>(zombies))
                {
                    if (zombie == null || zombie.IsDestroyed)
                    {
                        waterTimes.Remove(zombie);
                        continue;
                    }
                    
                    if (WaterLevel.GetWaterDepth(zombie.transform.position, true, true) > ZombieHeadOffset)
                    {
                        if (waterTimes.ContainsKey(zombie))
                            waterTimes[zombie] += 1f;
                        else
                            waterTimes[zombie] = 1f;
                        
                        if (waterTimes[zombie] >= 10f)
                        {
                            zombie.AdminKill();
                            zombies.Remove(zombie);
                            waterTimes.Remove(zombie);
                        }
                    }
                    else
                    {
                        if (waterTimes.ContainsKey(zombie))
                            waterTimes.Remove(zombie);
                    }
                }
            }
        }
        
        // New controller for persistent monument zombies.
        private class MonumentController
        {
            // Dictionary mapping each monument name to its list of persistent zombies.
            private Dictionary<string, List<ScarecrowNPC>> monumentZombies = new Dictionary<string, List<ScarecrowNPC>>();
            private Timer checkTimer;
            
            public void Initialize()
            {
                // For each configured monument, spawn the specified number of zombies.
                foreach (string monument in _config.Monument.Monuments)
                {
                    monumentZombies[monument] = new List<ScarecrowNPC>();
                    for (int i = 0; i < _config.Monument.Population; i++)
                    {
                        SpawnZombieAtMonument(monument);
                    }
                }
                // Check every 60 seconds to ensure persistent zombies are present.
                checkTimer = _instance.timer.Every(60f, () => CheckMonumentZombies());
            }
            
            private void SpawnZombieAtMonument(string monument)
            {
                Vector3 pos = GetMonumentPosition(monument);
                ScarecrowNPC zombie = GameManager.server.CreateEntity("assets/prefabs/npc/scarecrow/scarecrow.prefab", pos) as ScarecrowNPC;
                if (zombie == null)
                    return;
                zombie.Spawn();
                zombie.displayName = _config.Monument.Zombies.DisplayName;
                float health = _config.Monument.Zombies.Health;
                zombie.SetMaxHealth(health);
                zombie.SetHealth(health);
                if (_instance._kits != null && _config.Monument.Zombies.Kits.Count > 0)
                {
                    zombie.inventory.containerWear.Clear();
                    ItemManager.DoRemoves();
                    _instance._kits.Call("GiveKit", zombie, _config.Monument.Zombies.Kits.GetRandom());
                }
                monumentZombies[monument].Add(zombie);
            }
            
            // Helper to get a spawn position for a monument.
            private Vector3 GetMonumentPosition(string monument)
            {
                // In a real implementation, you'd use actual monument coordinates.
                // For this example, we return a random position.
                float x = Random.Range(-100, 100);
                float z = Random.Range(-100, 100);
                float y = TerrainMeta.HeightMap.GetHeight(new Vector3(x, 0, z)) + 1;
                return new Vector3(x, y, z);
            }
            
            private void CheckMonumentZombies()
            {
                // For each monument, ensure the number of zombies is maintained.
                foreach (var kvp in monumentZombies)
                {
                    string monument = kvp.Key;
                    List<ScarecrowNPC> zombies = kvp.Value;
                    zombies.RemoveAll(z => z == null || z.IsDestroyed);
                    int missing = _config.Monument.Population - zombies.Count;
                    for (int i = 0; i < missing; i++)
                    {
                        SpawnZombieAtMonument(monument);
                    }
                }
            }
            
            public void DespawnAll()
            {
                foreach (var kvp in monumentZombies)
                {
                    foreach (var zombie in kvp.Value.ToArray())
                    {
                        if (zombie != null && !zombie.IsDestroyed)
                            zombie.AdminKill();
                    }
                    kvp.Value.Clear();
                }
            }
            
            public void Shutdown()
            {
                if (checkTimer != null)
                    checkTimer.Destroy();
                DespawnAll();
            }
        }
        
        #endregion
        
        #region -Configuration-
        
        private class Configuration
        {
            [JsonProperty("Spawn Waves")]
            public List<SpawnWave> SpawnWaves = new List<SpawnWave>();
            
            [JsonProperty("Monument Settings")]
            public MonumentSettings Monument = new MonumentSettings();
            
            [JsonProperty("Destroy Settings")]
            public DestroySettings Destroy = new DestroySettings();
            
            [JsonProperty("Behaviour Settings")]
            public BehaviourSettings Behaviour = new BehaviourSettings();
            
            [JsonProperty("Broadcast Settings")]
            public ChatSettings Broadcast = new ChatSettings();
            
            public class SpawnWave
            {
                [JsonProperty("Wave Name")]
                public string WaveName = "Default Wave";
                
                [JsonProperty("Spawn Time")]
                public float SpawnTime = 19.8f;
                
                [JsonProperty("Destroy Time")]
                public float DestroyTime = 7.3f;
                
                [JsonProperty("Spawn near players")]
                public bool SpawnNearPlayers = false;
                
                [JsonProperty("Min pop for near player spawn")]
                public int MinNearPlayers = 10;
                
                [JsonProperty("Min distance from player")]
                public float MinDistance = 30.0f;
                
                [JsonProperty("Max distance from player")]
                public float MaxDistance = 60.0f;
                
                [JsonProperty("Zombie Settings")]
                public SpawnSettings.ZombieSettings Zombies = new SpawnSettings.ZombieSettings();
                
                [JsonProperty("Chance Settings")]
                public SpawnSettings.ChanceSetings Chance = new SpawnSettings.ChanceSetings();
            }
            
            public class SpawnSettings
            {
                public class ZombieSettings
                {
                    [JsonProperty("Display Name")]
                    public string DisplayName = "Scarecrow";
                    
                    [JsonProperty("Scarecrow Population (total amount)")]
                    public int Population = 50;
                    
                    [JsonProperty("Scarecrow Health")]
                    public float Health = 200f;
                    
                    [JsonProperty("Scarecrow Kits")]
                    public List<string> Kits = new List<string>();
                }
                
                public class ChanceSetings
                {
                    [JsonProperty("Chance per cycle")]
                    public float Chance = 100f;
                    
                    [JsonProperty("Days betewen spawn")]
                    public int Days = 0;
                }
            }
            
            public class MonumentSettings
            {
                [JsonProperty("Monument Zombie Population")]
                public int Population = 3;
                
                [JsonProperty("Monument List")]
                public List<string> Monuments = new List<string>
                {
                    "airfield", "lighthouse", "powerplant", "trainyard", "militarytunnels",
                    "satellitedish", "compound", "oilrig", "submarine", "junkyard",
                    "quarry", "banditcamp", "harbor", "water treatment", "warehouse",
                    "launchsite", "radardome", "mineshaft", "factory", "boathouse",
                    "smokestack", "researchstation", "silobase", "crashsite", "container",
                    "dockyard", "garage", "warehouse2", "blockade"
                };
                
                [JsonProperty("Zombie Settings")]
                public ZombieSettings Zombies = new ZombieSettings();
                
                public class ZombieSettings
                {
                    [JsonProperty("Display Name")]
                    public string DisplayName = "Monument Zombie";
                    
                    [JsonProperty("Scarecrow Health")]
                    public float Health = 300f;
                    
                    [JsonProperty("Scarecrow Kits")]
                    public List<string> Kits = new List<string> { "monumentkit" };
                }
            }
            
            public class DestroySettings
            {
                [JsonProperty("Leave Corpse, when destroyed")]
                public bool LeaveCorpse = false;
                
                [JsonProperty("Leave Corpse, when killed by player")]
                public bool LeaveCorpseKilled = true;
                
                [JsonProperty("Spawn Loot")]
                public bool SpawnLoot = true;
                
                [JsonProperty("Half bodybag despawn time")]
                public bool HalfBodybagDespawn = true;
            }
            
            public class BehaviourSettings
            {
                [JsonProperty("Attack sleeping players")]
                public bool AttackSleepers = false;
                
                [JsonProperty("Zombies attacked by outpost sentries")]
                public bool SentriesAttackZombies = true;
                
                [JsonProperty("Throw Grenades")]
                public bool ThrowGrenades = true;
                
                [JsonProperty("Ignore Human NPCs")]
                public bool IgnoreHumanNpc = true;
                
                [JsonProperty("Ignored entities (full entity shortname)")]
                public List<string> Ignored = new List<string>();
            }
            
            public class ChatSettings
            {
                [JsonProperty("Broadcast spawn amount")]
                public bool DoBroadcast = false;
            }
        }
        
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
                SaveConfig();
            }
            catch
            {
                PrintError("Failed to load _config, using default values");
                LoadDefaultConfig();
            }
        }
        
        protected override void LoadDefaultConfig() => _config = new Configuration
        {
            Behaviour = new Configuration.BehaviourSettings
            {
                Ignored = new List<string>
                {
                    "scientistjunkpile.prefab",
                    "scarecrow.prefab"
                }
            },
            SpawnWaves = new List<Configuration.SpawnWave>
            {
                // Example wave 1
                new Configuration.SpawnWave
                {
                    WaveName = "Night Wave",
                    SpawnTime = 19.8f,
                    DestroyTime = 7.3f,
                    SpawnNearPlayers = true,
                    MinNearPlayers = 10,
                    MinDistance = 30.0f,
                    MaxDistance = 60.0f,
                    Zombies = new Configuration.SpawnSettings.ZombieSettings
                    {
                        DisplayName = "Scarecrow",
                        Population = 50,
                        Health = 200f,
                        Kits = new List<string> { "defaultkit" }
                    },
                    Chance = new Configuration.SpawnSettings.ChanceSetings
                    {
                        Chance = 100f,
                        Days = 0
                    }
                },
                // Example wave 2
                new Configuration.SpawnWave
                {
                    WaveName = "Dawn Wave",
                    SpawnTime = 6.0f,
                    DestroyTime = 8.0f,
                    SpawnNearPlayers = false,
                    MinNearPlayers = 0,
                    MinDistance = 40.0f,
                    MaxDistance = 80.0f,
                    Zombies = new Configuration.SpawnSettings.ZombieSettings
                    {
                        DisplayName = "Dawn Scarecrow",
                        Population = 30,
                        Health = 150f,
                        Kits = new List<string> { "dawnkit" }
                    },
                    Chance = new Configuration.SpawnSettings.ChanceSetings
                    {
                        Chance = 50f,
                        Days = 1
                    }
                },
                // Additional example wave 3
                new Configuration.SpawnWave
                {
                    WaveName = "Midnight Wave",
                    SpawnTime = 0.0f,
                    DestroyTime = 1.0f,
                    SpawnNearPlayers = true,
                    MinNearPlayers = 5,
                    MinDistance = 20.0f,
                    MaxDistance = 50.0f,
                    Zombies = new Configuration.SpawnSettings.ZombieSettings
                    {
                        DisplayName = "Midnight Scarecrow",
                        Population = 40,
                        Health = 180f,
                        Kits = new List<string> { "midnightkit" }
                    },
                    Chance = new Configuration.SpawnSettings.ChanceSetings
                    {
                        Chance = 75f,
                        Days = 0
                    }
                }
            },
            Monument = new Configuration.MonumentSettings
            {
                Population = 3,
                Monuments = new List<string>
                {
                    "airfield", "lighthouse", "powerplant", "trainyard", "militarytunnels",
                    "satellitedish", "compound", "oilrig", "submarine", "junkyard",
                    "quarry", "banditcamp", "harbor", "water treatment", "warehouse",
                    "launchsite", "radardome", "mineshaft", "factory", "boathouse",
                    "smokestack", "researchstation", "silobase", "crashsite", "container",
                    "dockyard", "garage", "warehouse2", "blockade"
                },
                Zombies = new Configuration.MonumentSettings.ZombieSettings
                {
                    DisplayName = "Monument Zombie",
                    Health = 300f,
                    Kits = new List<string> { "monumentkit" }
                }
            }
        };
        
        protected override void SaveConfig() => Config.WriteObject(_config);
        
        #endregion
        
        #region -Localisation-
        
        private string GetMessage(string key, string userId = null)
        {
            return lang.GetMessage(key, this, userId);
        }
        
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["ChatBroadcast"] = "[Night Zombies] Spawned {0} zombies",
                ["ChatBroadcastSeparate"] = "[Night Zombies] Spawned {0} murderers | Spawned {1} scarecrows"
            }, this);
        }
        
        #endregion
    }
}
