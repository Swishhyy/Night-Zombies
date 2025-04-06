## Configuration
WARNING: Requires Rustkits to function
### Spawn Settings
* **Spawn near players** - Do zombies spawn near players (randomly chosen). If false, zombies will spawn randomly around the map.
* **Min pop for near player spawn** - The minimum number of players on the server required for zombies to spawn near players.
* **Min distance from player** - When spawning near players, this is the minimum distance that zombies are allowed to spawn from the player.
* **Max distance from player** - When spawning near players, this is the maximum distance that zombies are allowed to spawn from the player.
* **Spawn Time** - The in-game time at which zombies will appear.
* **Destroy Time** - The in-game time at which zombies will disappear.

### Spawn Waves
* **Wave Name** - A unique name for the spawn wave.
* **Spawn Time** - The in-game time at which this wave of zombies will appear.
* **Destroy Time** - The in-game time at which zombies in this wave will be despawned.
* **Spawn near players** - Determines whether zombies in this wave will spawn near players (if true) or randomly around the map.
* **Min pop for near player spawn** - The minimum number of players on the server required for a near-player spawn.
* **Min distance from player** - The minimum allowed distance for zombies to spawn from a player.
* **Max distance from player** - The maximum allowed distance for zombies to spawn from a player.

### Wave-Specific Settings
* **Zombie Settings**
  * **Display Name** – The name shown for zombies in this wave.
  * **Scarecrow Population (total amount)** – The total number of zombies to spawn in this wave.
  * **Scarecrow Health** – The health value for each zombie in this wave.
  * **Scarecrow Kits** – A list of kits to assign to zombies (if applicable).
* **Chance Settings**
  * **Chance per cycle** – The percentage chance that zombies in this wave will spawn during each spawn attempt.
  * **Days between spawn** – The number of days between spawn attempts (if you want to throttle spawning over time).

### Monument Settings
* **Monument Zombie Population** – The number of persistent zombies to maintain at each monument.
* **Monument List** – A list of monuments where persistent zombies will be present.
* **Zombie Settings** – Settings for persistent zombies. These settings are similar to the regular scarecrow settings (Display Name, Health, Kits) but do not include the "spawn near players" option. These zombies are always present at the monuments and respawn if killed.

### Destroy Settings
* **Leave Corpse, when destroyed** - Whether corpses remain when zombies naturally despawn (can affect performance when set to true).
* **Leave Corpse, when killed by player** - Whether corpses remain when zombies are killed by players.
* **Half body bag despawn time** - Whether the despawn time for green loot backpacks is halved.
* **Quick destroy corpses** - Whether corpses are cleaned up after 10 seconds.

## Permissions
* **nightzombies.admin**: Allows users to execute the `/forcespawn` or `/despawnall` chat commands.
* **nightzombies.ignore**: Scarecrows will not attack players with this permission.

## Chat Commands
* **/forcespawn**: Forces an immediate zombie spawn across all spawn waves.
* **/despawnall**: Immediately despawns all zombies (and their corpses) across all spawn waves and monuments. Requires the `nightzombies.admin` permission.

### Behaviour Settings
* **Zombies attacked by outpost sentries** - Whether zombies are attacked by sentries at safezones.
* **Ignore Human NPCs** - Whether zombies ignore NPC player characters.
* **Ignored entities (full entity shortname)** - A list of entity prefab names that zombies will never target (must be the full short prefab name).

### Example JSON Configuration
```json
{
  "Spawn Waves": [
    {
      "Wave Name": "Night Wave",
      "Spawn Time": 19.8,
      "Destroy Time": 7.3,
      "Spawn near players": true,
      "Min pop for near player spawn": 10,
      "Min distance from player": 30.0,
      "Max distance from player": 60.0,
      "Zombie Settings": {
        "Display Name": "Scarecrow",
        "Scarecrow Population (total amount)": 50,
        "Scarecrow Health": 200.0,
        "Scarecrow Kits": [ "defaultkit" ]
      },
      "Chance Settings": {
        "Chance per cycle": 100.0,
        "Days betewen spawn": 0
      }
    },
    {
      "Wave Name": "Dawn Wave",
      "Spawn Time": 6.0,
      "Destroy Time": 8.0,
      "Spawn near players": false,
      "Min pop for near player spawn": 0,
      "Min distance from player": 40.0,
      "Max distance from player": 80.0,
      "Zombie Settings": {
        "Display Name": "Dawn Scarecrow",
        "Scarecrow Population (total amount)": 30,
        "Scarecrow Health": 150.0,
        "Scarecrow Kits": [ "dawnkit" ]
      },
      "Chance Settings": {
        "Chance per cycle": 50.0,
        "Days betewen spawn": 1
      }
    },
    {
      "Wave Name": "Midnight Wave",
      "Spawn Time": 0.0,
      "Destroy Time": 1.0,
      "Spawn near players": true,
      "Min pop for near player spawn": 5,
      "Min distance from player": 20.0,
      "Max distance from player": 50.0,
      "Zombie Settings": {
        "Display Name": "Midnight Scarecrow",
        "Scarecrow Population (total amount)": 40,
        "Scarecrow Health": 180.0,
        "Scarecrow Kits": [ "midnightkit" ]
      },
      "Chance Settings": {
        "Chance per cycle": 75.0,
        "Days betewen spawn": 0
      }
    }
  ],
  "Monument Settings": {
    "Monument Zombie Population": 3,
    "Monument List": [
      "airfield", "lighthouse", "powerplant", "trainyard", "militarytunnels",
      "satellitedish", "compound", "oilrig", "submarine", "junkyard",
      "quarry", "banditcamp", "harbor", "water treatment", "warehouse",
      "launchsite", "radardome", "mineshaft", "factory", "boathouse",
      "smokestack", "researchstation", "silobase", "crashsite", "container",
      "dockyard", "garage", "warehouse2", "blockade"
    ],
    "Zombie Settings": {
      "Display Name": "Monument Zombie",
      "Scarecrow Health": 300.0,
      "Scarecrow Kits": [ "monumentkit" ]
    }
  },
  "Destroy Settings": {
    "Leave Corpse, when destroyed": false,
    "Leave Corpse, when killed by player": true,
    "Spawn Loot": true,
    "Half bodybag despawn time": true
  },
  "Behaviour Settings": {
    "Attack sleeping players": false,
    "Zombies attacked by outpost sentries": true,
    "Throw Grenades": true,
    "Ignore Human NPCs": true,
    "Ignored entities (full entity shortname)": [
      "scientistjunkpile.prefab",
      "scarecrow.prefab"
    ]
  },
  "Broadcast Settings": {
    "Broadcast spawn amount": false
  }
}
```

## Notes
* **Monument Zombies**: Persistent zombies (Monument Zombies) are configured under the Monument Settings section. They always exist at specified monuments and will respawn if killed.
* **Automatic Despawning in Water**: Zombies that are fully submerged (their head is below water level) for 10 consecutive seconds will automatically despawn.

## Version Compatibility
These features are part of version 4.1.1. Previous versions (v0.9.0 and v2.0) are still available:
* [v0.9.0 Documentation](https://github.com/0x89A/Night-Zombies/tree/deprecated-v0.9.0)
* [v2.0 Documentation](https://github.com/0x89A/Night-Zombies/tree/deprecated-v2.0)