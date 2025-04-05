## Configuration

### Spawn Settings
* **Spawn near players** - Do zombies spawn near to players (randomly chosen), if false zombies will spawn randomly around the map.
* **Min pop for near player spawn** - The minimum number of players on the server for zombies to spawn near players.
* **Min distance from player** - When spawning near players, this is the minimum distance that zombies are allowed to spawn from the player.
* **Max distance from player** - When spawning near players, this is the maximum distance that zombies are allowed to spawn from the player.
* **Spawn Time** - The in-game time at which zombies will appear.
* **Destroy Time** - The in-game time at which zombies will disappear.

### Spawn Waves
* **Wave Name** - A unique name for the spawn wave.
* **Spawn Time** - The in-game time at which this wave of zombies will appear.
* **Destroy Time** - The in-game time at which zombies in this wave will be despawned.
* **Spawn near players** - Determines whether zombies in this wave will spawn near players (if true) or randomly around the map.
* **Min pop for near player spawn** - Minimum number of players on the server for a near-player spawn to occur.
* **Min distance from player** - The minimum allowed distance for zombies to spawn from a player.
* **Max distance from player** - The maximum allowed distance for zombies to spawn from a player.

### Wave-Specific Settings
* Zombie Settings
  * **Display Name** – The name shown for zombies in this wave.
  * **Scarecrow Population (total amount)** – Total number of zombies to spawn in this wave.
  * **Scarecrow Health** – Health value for each zombie in this wave.
  * **Scarecrow Kits** – A list of kits to assign to zombies (if applicable).
* Chance Settings
  * **Chance per cycle** – The percentage chance that zombies in this wave will spawn each spawn attempt.
  * **Days betewen spawn** – The number of days between spawn attempts (if you want to throttle spawning over time).

### Destroy Settings
* **Leave Corpse, when destroyed** - Are corpses left when zombies disappear (can affect performance when set to true).
* **Leave Corpse, when killed by player** - Are corpses left when zombies are killed by players.
* **Half body bag despawn time** - Is the despawn time for green loot backpacks halved.
* **Quick destroy corpses** - Are corpses cleaned up after 10 seconds.

## Permissions
* **nightzombies.admin**: Allows users to execute the `/forcespawn` chat command.
* **nightzombies.ignore**: Scarecrows will not attack players with this permission.

## Chat Commands
* **/forcespawn**: Forces an immediate zombie spawn.
* **/despawnall**: Immediately despawns all zombies (and their corpses) across all spawn waves. Requires the nightzombies.admin permission.

### Behaviour Settings
* **Zombies attacked by outpost sentries** - Are zombies attacked by the sentries at safezones.
* **Ignore Human NPCs** - Do zombies ignore npc player characters.
* **Ignored entities (full entity shortname)** - Zombies will not target entities in this list, must be the full short prefab name.

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
**These are likely broken after the large AI update 2nd December 2021**

The 0.9.0 and 2.0 versions are still available to download if you want them.

* You can find v0.9.0 and its documentation [here](https://github.com/0x89A/Night-Zombies/tree/deprecated-v0.9.0)
* You can find v2.0 and its documentation [here](https://github.com/0x89A/Night-Zombies/tree/deprecated-v2.0)