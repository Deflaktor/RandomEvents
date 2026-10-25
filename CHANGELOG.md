# 1.2.0

- Add 48 events: BlightedCorpses, Bouncy, ChestMimics, CursedSpoils, Deathmatch, DroneEscort, DroneUprising, EliteSurge, ExperimentalEquipment, FocusFire, FromTheBrink, GiantBalloons, HotPotato, HuggingTime, Hunted, IndirectDamage, InventoryRoulette, ItemKits, ItemRush, LavaFloor, Leashed, LinkedSouls, LynxPups, MirroredEnemies, Mitosis, NeutroniumWeights, Ossification, RedLightGreenLight, ReducedGravity, RitualHelmets, SafeZone, Shielded, Slippery, StealthMission, SupplyDrop, SurvivorSwap, SwarmQueen, TagKing, TagYoureIt, TheAlpha, Thieves, TimeWarp, UmbraAmbush, UnitedWeStand, Vampires, VengefulSpirits, VoidCore and VoidInfection
- Update for the latest game version
- Fix players staying monsters after the MonsterTransform event
- Fix players sometimes keeping an event's equipment after the event
- Fix the event's equipment stopping working after picking up equipment
- Events end with their stage, so random artifacts and monster bodies no longer carry over into the next stage or into ProperSave's save
- Fix events of a quit run carrying over into the next run
- Fix RandomArtifact turning off artifacts it did not turn on
- Fix an error when RandomArtifact ends with the run
- Fix RandomArtifact picking Artifact of Prestige, or Artifact of Enigma together with EquipmentOnly
- Fix RandomArtifact turning on Artifact of Kin or Artifact of Dissonance together with an event that changes which monsters spawn, which undid that event's change
- Fix errors when a player leaves during the MonsterTransform event
- ItemZone: every player now sees the zones. Each has a Lunar Bud showing its item and pulses when it gives items: 6 every second instead of 1 six times a second
- Fix ItemZone placing zones outside the safe zone during SmallArena
- CasinoItem gives its first item right away, then a new one every 10 seconds
- Items that events give fly to the player with the sound of an item being handed over, unless many fly at once
- CasinoItem also gives meals and Hallowed items while their expansions are enabled
- EquipmentOnly can happen together with ExperimentalEquipment and then hands out its equipment, unless that is Foreign Fruit or Gnarled Woodsprite
- FallingEnemies no longer drops allies from the sky
- BulletHell no longer gives Void Jailers Alien Heads
- BulletHell's monsters keep full-sized health bars, as it no longer weakens them with Tonic Afflictions
- Simulacrum's artifact waves are only left out while RandomArtifact is enabled
- Fix events never ending when the Event Duration is at least the Event Timer
- Fix WeakEnemies stopping the other events on stages with three monster types
- Fix an event's change to the spawning monsters, e.g. StrongEnemies', staying after it ends while another such event still runs
- Fix the teleporter boss coming from an event's monsters, or not appearing, when the teleporter is activated during StrongEnemies, WeakEnemies or FallingEnemies
- Fix StrongEnemies' extra spawns staying for the rest of the Simulacrum wave
- Fix WeakEnemies spawning the strongest elites in Simulacrum, and StrongEnemies fewer elites, also for the rest of the wave
- Fix WeakEnemies' Simulacrum waves stopping to spawn and ending early, mostly after a moment without enemies
- Fix skills staying disabled after EquipmentOnly, and Predatory Instincts' buff ending early after ExplodingCorpses
- EquipmentOnly's Molotovs burst in the air next to flying monsters, dealing 800% damage to all monsters within 6 m
- Fix infected monsters still reviving after Zombies ends
- Revived zombies count toward the Simulacrum wave
- Ghosts fade within 40 seconds at most
- Fix the monster family message repeating when FallingEnemies, StrongEnemies or WeakEnemies ends
- SkillsOnly also removes the equipment of players who respawn during the event
- SkillsOnly turns each Eclipse Lite into 2 Topaz Brooches, as the fast cooldowns would make its barrier endless
- Fix the event announcement failing while no player is alive
- Fix each event announcement dropping an item below the players
- Fix a Lesser Wisp appearing near the players with each event announcement
- Every player hears the event announcement's sound
- Each announced event shows a popup with its description
- FuelArray heals players to full health and shield when it starts
- Fix temporary heresy items, e.g. SkillsOnly's Visions of Heresy, turning a player into the Heretic for the rest of the run
- An event that fails to end no longer keeps the other events running
- An event that fails to load no longer keeps the events after it from loading
- Fix errors when a monster with an event's items dies
- Items that events drop look like temporary items and aren't shared by ShareSuite
- StrongEnemies and FallingEnemies no longer happen before Simulacrum boss waves, some of which only spawn their boss
- Fix the mod not loading when MonsterTransform's custom body list names a body twice
- Fix errors in RandomTeams, SkillsOnly and Zombies, with event weights of 0, and the log filling up with errors
- Config descriptions show each event's condition
- Requires the new PerClientSync library

# 1.1.3

- Update for Alloyed Collective
- Fix Artifact of Engima causing equipment handout during Monster Transform event
- Add Artifact of Prestige to blacklist for Random Artifacts event

# 1.1.2

- Fix version string

# 1.1.1

- Fix language tokens missing
- Fix dependencies
- Remove ServerSideTweaks as a soft dependency
- Fix invincible wisp spawning on each event
- Reset cooldown of equipment when `EquipmentOnly` event starts

# 1.1.0

- Update Icon
- Add `MonsterTransform` Event - players turn into monsters
- Fix `BulletHell` to actually increase Fire Rate of monsters
- Prevent `Aritfact of Enigma` and `EquipmentOnly` from occuring at the same time
- Fix event conlusion message appearing even if no event is active

# 1.0.1

- Improve Icon
- Add changelog

# 1.0.0

- Initial Release
