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
    [Info("Night Zombies", "0x89A", "4.0.0")]
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
        
        // The top-level spawn controller now manages multiple wave controllers
        private SpawnController _spawnController;
        
        #region -Init-
        
        private void Init()
        {
            _instance = this;
            
            // Register permissions
            permission.RegisterPermission("nightzombies.admin", this);
            permission.RegisterPermission("nightzombies.ignore", this);
            
            // Create our multi-wave controller
            _spawnController = new SpawnController();
            
            // Read saved number of days since last spawn
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
            {
                Unsubscribe(nameof(OnTurretTarget));
            }
            
            if (_config.Destroy.SpawnLoot)
            {
                Unsubscribe(nameof(OnCorpsePopulate));
            }
            
            if (_config.Behaviour.Ignored.Count == 0 && !_config.Behaviour.IgnoreHumanNpc && _config.Behaviour.AttackSleepers)
            {
                Unsubscribe(nameof(OnNpcTarget));
            }
        }
        
        private void OnServerInitialized()
        {
            if (!_kits?.IsLoaded ?? false)
            {
                PrintWarning("Kits is not loaded, custom kits will not work");
            }
            
            // Start time check for each wave
            if (_config.SpawnWaves != null && _config.SpawnWaves.Count > 0)
            {
                TOD_Sky.Instance.Components.Time.OnMinute += _spawnController.TimeTick;
                TOD_Sky.Instance.Components.Time.OnDay += OnDay;
            }
        }
        
        private void Unload()
        {
            TOD_Sky.Instance.Components.Time.OnMinute -= _spawnController.TimeTick;
            TOD_Sky.Instance.Components.Time.OnDay -= OnDay;
            
            _dataFile.WriteObject(_spawnController.DaysSinceLastSpawn);
            _spawnController?.Shutdown();
            
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
        
        #endregion
        
        #region -Oxide Hooks-
        
        private object OnNpcTarget(ScarecrowNPC npc, BaseEntity target)
        {
            return CanAttack(target);
        }
        
        private object OnTurretTarget(NPCAutoTurret turret, ScarecrowNPC entity)
        {
            if (entity == null)
            {
                return null;
            }
            return true;
        }
        
        private object OnPlayerDeath(ScarecrowNPC scarecrow, HitInfo info)
        {
            Effect.server.Run(DeathSound, scarecrow.transform.position);
            _spawnController.ZombieDied(scarecrow);
            
            if (_config.Destroy.LeaveCorpseKilled)
            {
                return null;
            }
            
            NextTick(() =>
            {
                if (scarecrow == null || scarecrow.IsDestroyed)
                {
                    return;
                }
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
            if (corpse.playerName == "Scarecrow")
            {
                corpse.playerName = _config.SpawnWaves[0].Zombies.DisplayName; // Default to first wave's name
            }
        }
        
        private void OnEntitySpawned(DroppedItemContainer container)
        {
            if (!_config.Destroy.HalfBodybagDespawn)
            {
                return;
            }
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
            if (target is BasePlayer player && permission.UserHasPermission(player.UserIDString, "nightzombies.ignore"))
            {
                return true;
            }
            
            if (_config.Behaviour.Ignored.Contains(target.ShortPrefabName) ||
                (_config.Behaviour.IgnoreHumanNpc && HumanNPCCheck(target)) ||
                (!_config.Behaviour.AttackSleepers && target is BasePlayer player2 && player2.IsSleeping()))
            {
                return true;
            }
            
            return null;
        }
        
        private bool HumanNPCCheck(BaseEntity target)
        {
            return target is BasePlayer player && !player.userID.IsSteamId() && target is not ScientistNPC &&
                   target is not ScarecrowNPC;
        }
        
        #endregion
        
        #region -Classes-
        
        // Top-level spawn controller managing multiple spawn wave controllers
        private class SpawnController
        {
            public int DaysSinceLastSpawn;
            private List<SpawnWaveController> waveControllers = new List<SpawnWaveController>();
            
            public SpawnController()
            {
                // If no waves defined, create a default wave.
                if (_config.SpawnWaves == null || _config.SpawnWaves.Count == 0)
                {
                    _config.SpawnWaves = new List<Configuration.SpawnWave> { new Configuration.SpawnWave() };
                }
                foreach (var wave in _config.SpawnWaves)
                {
                    waveControllers.Add(new SpawnWaveController(wave));
                }
            }
            
            public void TimeTick()
            {
                foreach (var wave in waveControllers)
                {
                    wave.TimeTick();
                }
            }
            
            public void ForceSpawn()
            {
                foreach (var wave in waveControllers)
                {
                    wave.ForceSpawn();
                }
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
                {
                    wave.Shutdown();
                }
            }
        }
        
        // Controller for a single spawn wave
        private class SpawnWaveController
        {
            private readonly Configuration.SpawnWave waveConfig;
            private readonly int spawnLayerMask = LayerMask.GetMask("Default", "Tree", "Construction", "World", "Vehicle_Detailed", "Deployed");
            private readonly WaitForSeconds waitTenthSecond = new WaitForSeconds(0.1f);
            private bool _spawned;
            private Coroutine _currentCoroutine;
            private readonly List<ScarecrowNPC> zombies = new List<ScarecrowNPC>();
            
            public SpawnWaveController(Configuration.SpawnWave config)
            {
                waveConfig = config;
            }
            
            private bool IsSpawnTime
            {
                get
                {
                    // Use the spawn and destroy times defined for this wave.
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
                // Check chance per cycle. (Days can be added if desired.)
                return !_spawned && Random.Range(0f, 100f) < waveConfig.Chance.Chance;
            }
            
            public void TimeTick()
            {
                if (CanSpawn() && IsSpawnTime)
                {
                    _currentCoroutine = ServerMgr.Instance.StartCoroutine(SpawnZombies());
                }
                else if (zombies.Count > 0 && IsDestroyTime && _spawned)
                {
                    _currentCoroutine = ServerMgr.Instance.StartCoroutine(RemoveZombies());
                }
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
                {
                    SpawnZombie();
                }
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
            
            public bool RemoveZombie(ScarecrowNPC zombie)
            {
                if (zombies.Contains(zombie))
                {
                    zombies.Remove(zombie);
                    if (IsSpawnTime)
                    {
                        SpawnZombie();
                    }
                    return true;
                }
                return false;
            }
            
            public void Shutdown()
            {
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
                    {
                        item.Remove();
                    }
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
                {
                    position.y = TerrainMeta.HeightMap.GetHeight(0, 0);
                }
                return position;
            }
            
            private Vector3 GetRandomPositionAroundPlayer(BasePlayer player)
            {
                Vector3 playerPos = player.transform.position;
                Vector3 position = Vector3.zero;
                float maxDist = waveConfig.MaxDistance;
                for (int i = 0; i < 6; i++)
                {
                    position = new Vector3(Random.Range(playerPos.x - maxDist, playerPos.x + maxDist), 0, Random.Range(playerPos.z - maxDist, playerPos.z + maxDist));
                    position.y = TerrainMeta.HeightMap.GetHeight(position);
                    if (!AntiHack.TestInsideTerrain(position) && !IsInObject(position) && !IsInOcean(position) &&
                        Vector3.Distance(playerPos, position) > waveConfig.MinDistance)
                        break;
                }
                if (position == Vector3.zero)
                {
                    position = GetRandomPosition();
                }
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
                {
                    player.ChatMessage(string.Format(_instance.GetMessage(key, player.UserIDString), values));
                }
            }
            
            #endregion
        }
        
        #endregion
        
        #region -Configuration-
        
        private class Configuration
        {
            [JsonProperty("Spawn Waves")]
            public List<SpawnWave> SpawnWaves = new List<SpawnWave>();
            
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
                new Configuration.SpawnWave()
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
