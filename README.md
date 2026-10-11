# RandomEvents

**server-side mod** - only the host needs it installed.

Makes random events happen. 

- **Compatiblity with other Mods:**
  - [InLobbyConfig](https://thunderstore.io/package/KingEnderBrine/InLobbyConfig/)

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/E1E71PHUJ2)

## Random Events

| Event Name | Announcement | Description | Condition |
|------------|--------------|-------------|-----------|
| BulletHell | Projectile density dialed to eleven | Increases the fire rate of enemies but lowers their damage. |  |
| CasinoItem | Ancient relic cycling through its many powers | Gives all players 10 items of random type. The type of the item changes every 10 seconds. |  |
| EquipmentOnly | Equipment-exclusive combat | Gives all players a random equipment temporarily with reduced cooldown but disables all skills. | Events "SkillsOnly", "FuelArray" inactive. Artifact of Enigma inactive. |
| ExplodingCorpses | Exploding corpses | All enemies explode upon death. | Players have sufficient means to do ranged damage. |
| FallingEnemies | Enemies fall down from the heavens | Enemies fall from the sky. Only walking enemies spawn. |  |
| FuelArray | Stay above 50% health or face disaster | All players get a Fuel Array equipment temporarily but take reduced damage from all sources. | Event "FuelArray" inactive. |
| Ghosts | Ghostly apparitions haunt the battlefield | Some enemies spawn as incorporeal ghosts. They disappear after some time. | Currently not a boss wave. |
| Haste | The world accelerates | All beings become faster. |  |
| ItemZone | Zone fuels your arsenal | Spawns item zones which grant players standing in them temporary items. | Simulacrum mode only |
| MonsterTransform | Players embrace their monstrous forms | Players transform into monsters. | Event "SkillsOnly" inactive and currently not a boss wave. |
| RandomArtifact |  | Activates up to three random artifacts temporarily. The follwing artifacts are blacklisted: Death, Delusion, Devotion, Evolution, Metamorphosis, Rebirth, Sacrifice, Vengeance, Prestige. | There is at least one artifact which can be enabled and is not blacklisted. |
| RandomTeams | Enemies turn against each other | Makes some enemies fight each other as well as the players. |  |
| SkillsOnly | Primary sealed, skills unleashed | Makes players unable to use primary skills but other skills have reduced cooldown. | Events "EquipmentOnly", "MonsterTransform" inactive and none of the players have a bandolier in their inventories (workaround to make it work server-side only). |
| SmallArena | Confined space | Halves the battle zone radius but makes the wave also easier. | Simulacrum mode only |
| StrongEnemies | Fear the few | Only strong enemies spawn. | Event "WeakEnemies" inactive. |
| WeakEnemies | Horde of the many | Only weak enemies spawn. | Event "StrongEnemies" inactive. |
| Zombies | The dead refuse to rest | Spawns some infected enemies. Infected enemies infect other nearby enemies upon death and revive once with reduced health and speed. | Currently not a boss wave. |
