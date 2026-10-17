# RandomEvents

**server-side mod** - only the host needs it installed.

Makes random events happen. In a normal run, every minute brings a chance of one to three events, which last 45 seconds. In Simulacrum, events can start with a wave and end with it.

- **Compatibility with other Mods:**
  - [InLobbyConfig](https://thunderstore.io/package/KingEnderBrine/InLobbyConfig/)

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/E1E71PHUJ2)

## Random Events

| Event Name | Announcement | Description | Condition |
|------------|--------------|-------------|-----------|
| BulletHell | Projectile density dialed to eleven | Increases the fire rate of enemies but lowers their damage. |  |
| CasinoItem | Ancient relic cycling through its many powers | Gives all players 10 items of random type. The type of the item changes every 10 seconds. |  |
| ChestMimics | Not every chest is what it seems | Chests bought with gold may release a monster instead of their item, a boss for a legendary or boss item. The chest's items are lost. | The stage has an unopened chest bought with gold. |
| CursedLoot | Spoils come at a price | Monsters drop an item when they die, which its finder keeps until the event ends. Each time a player has taken damage from monsters adding up to 10% of their base health, they get a hex that disables some of their items until the event ends. | Hallowed Concepts is enabled. |
| DroneEscort | Backup has arrived | Gives each player a random drone that can be found on stages. The drones disappear when the event ends. |  |
| EquipmentOnly | Equipment-exclusive combat | Gives all players a random equipment temporarily with reduced cooldown but disables all skills. | Events "SkillsOnly", "FuelArray" inactive. Artifact of Enigma inactive. |
| ExplodingCorpses | Exploding corpses | All enemies explode upon death. | Players have sufficient means to do ranged damage. |
| FallingEnemies | Enemies fall down from the heavens | Enemies fall from the sky. Only walking enemies spawn. |  |
| FocusFire | Focus your fire | All monsters have ten times their health, but each hit from a player leaves Symbiotic Scorpion's debuff on them, and each stack makes them take 10% more damage, compounding. |  |
| FuelArray | Stay above 50% health or face disaster | All players are healed to full health and shield, then get a Fuel Array equipment temporarily but take reduced damage from all sources. | Event "EquipmentOnly" inactive. |
| Ghosts | Ghostly apparitions haunt the battlefield | Some enemies spawn as incorporeal ghosts. They disappear after some time. | Currently not a boss wave. |
| Haste | The world accelerates | All beings become faster. |  |
| HotPotato | Hot potato | A random player carries a ticking bomb, marked with lunar marks and small blasts that speed up as it nears its explosion, and passes it on by touching another player. After 20 seconds it explodes, dealing half of the carrier's health to them and hurting monsters nearby. Then another player gets a new bomb. | At least two players are alive. |
| Hunted | The hunt is on | All monsters target one random player, who gets 100 armor. When that player dies, the monsters hunt another one. | At least two players are alive and event "StealthMission" inactive. |
| InventoryRoulette | Spin the inventory roulette | Every player gets the items of another player until the event ends. Items picked up meanwhile stay with whoever picked them up; items used up meanwhile are gone for their owner. Equipment stays. | At least two players are alive and event "SurvivorSwap" inactive. |
| ItemKits | Item kit: Glass Cannon | Gives all players the items of a random kit temporarily, e.g. Glass Cannon or Bounce House. | At least one kit has items. |
| ItemRush | Grab them before they do | Items appear around the players, which their finder keeps until the event ends. Monsters can pick them up too and keep them until the event ends, without dropping them when they die. Only items fair for monsters appear, see the setting Unsafe Items, and no lunar or void items. | At least one item is not in the setting Unsafe Items. |
| ItemZone | Zone fuels your arsenal | Spawns item zones which grant players standing in them temporary items. | Simulacrum mode only |
| LinkedSouls | Your souls are bound together | Damage a player takes is split evenly among all living players. Each share is reduced by that player's own armor. | At least two players are alive. |
| Mitosis | Divide and multiply | Only monsters with a weaker relative on the stage spawn, e.g. Elder Lemurians when there are also Lemurians. When they die, they split into two of the weaker monster. | The stage's monsters include such a pair, and events "StrongEnemies" and "WeakEnemies" inactive. |
| MonsterTransform | Players embrace their monstrous forms | Players transform into monsters. | Events "SkillsOnly" and "SurvivorSwap" inactive and currently not a boss wave. |
| RandomArtifact |  | Activates up to three random artifacts temporarily. The following artifacts are blacklisted: Death, Delusion, Devotion, Evolution, Metamorphosis, Prestige, Rebirth, Sacrifice, Vengeance. | There is at least one artifact which can be enabled and is not blacklisted. |
| RandomTeams | Enemies turn against each other | Makes some enemies fight each other as well as the players. |  |
| RedLightGreenLight | Red light, green light | Alternates between green and red light, announced in chat. During red light, all AI-controlled characters are rooted, and players who move get a Tonic Affliction every second until the event ends. Players who stand still can't be hurt. |  |
| RitualHelmets | Skulls on the charge | Only monsters that can sprint spawn, and all monsters get an always charged Ritual Helmet and sprint wherever they can. Players take 66% less damage. | Hallowed Concepts is enabled and the stage has monsters that can sprint. |
| SecondWind | Fragile, but never finished | Revives dead players. All other players drop to 10% of their health until the event ends, but anyone who dies during the event is revived once with full health. |  |
| SkillsOnly | Primary sealed, skills unleashed | Makes players unable to use primary skills but other skills have reduced cooldown. | Events "EquipmentOnly", "MonsterTransform" inactive and none of the players have a Bandolier. |
| SmallArena | Confined space | Halves the battle zone radius but makes the wave also easier. | Simulacrum mode only |
| StealthMission | Vanish from their sight | Cloaks all players, so monsters lose track of them. Attacking reveals a player for 2 seconds. | Event "Hunted" inactive. |
| StrongEnemies | Fear the few | Only strong enemies spawn. | Events "WeakEnemies" and "Mitosis" inactive. |
| SupplyDrop | Supplies inbound | Drops a random Captain beacon (Healing, Shocking, Resupply, Hacking or Defense) next to each player. The beacons disappear when the event ends. |  |
| SurvivorSwap | Walk in another's shoes | In multiplayer, every player plays the survivor of another player, with that player's loadout and items. Alone, the player becomes a random other survivor. Everyone gets their own back when the event ends. | Events "MonsterTransform" and "InventoryRoulette" inactive and currently not a boss wave. |
| TheAlpha | The pack has a leader | Makes a random monster the alpha, with ten times its health and a boss health bar. While it lives, all other monsters are in a frenzy like from Gorag's Opus: +100% attack speed and +50% movement speed. |  |
| TimeWarp | Time slows for your foes | Slows all enemies to about a third of their speed, almost twice as much as Chronobauble. |  |
| UmbraAmbush | Umbras emerge from the shadows | Each player is hunted by an enemy survivor. It has ten times the health and a kit of safe items as big as that player's items per tier, picked to suit its survivor. The hunters disappear when the event ends. |  |
| VengefulSpirits | Spirits stand guard | All monsters get a Vengeful Spirit: a spirit that takes half of their damage. Killing the spirit makes its monster vengeful, with more attack speed and shorter cooldowns. Half as many monsters spawn. The items are removed when the event ends. | Hallowed Concepts is enabled. |
| WeakEnemies | Horde of the many | Only weak enemies spawn. | Events "StrongEnemies" and "Mitosis" inactive. |
| Zombies | The dead refuse to rest | Spawns some infected enemies. Infected enemies infect other nearby enemies upon death and revive once with reduced health and speed. | Currently not a boss wave. |
