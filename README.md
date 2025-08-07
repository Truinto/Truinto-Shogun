# Truinto Shogun
Mod for Shogun Showdown

Index
-----------
* [Disclaimers](#disclaimers)
* [Installation](#installation)
* [Contact](#contact)
* [Content](#content)
* [Build](#build)

Disclaimers
-----------
* I do not take any responsibility for broken saves or any other damage. Use this software at your own risk.
* BE AWARE that all mods are WIP and may fail.

Installation
-----------
* You will need [BepInEx](https://github.com/BepInEx/BepInEx/releases).
* Follow the installation procedure.
* Download a release [https://github.com/Truinto/Truinto-Shogun/releases](https://github.com/Truinto/Truinto-PotionCraft/releases).
* Extract dll into BepInEx/plugins folder.

Contact
-----------
Start discussion on Github.

Content
-----------
All mods are in one package. You can remove any you don't want.
* ShockwaveTriggerAfterPush: KiPush, Tanegashima, TwinTessen, and DragonPunch trigger their effect after the push. That way shockwave can connect to more enemies. Also shockwave is simpler, triggering with any target.
* ThronsMelee: When using Thorns in melee, you deal damage and entangle the enemy (3 turns ice).
* TrapStopDash: Whenever an enemy charges into a trap, their movement is stopped.
* ShogunCheat: Collection of buffs.
  * Removed Tile Upgrades (Sacrifice, Attack +1 CD +1)
  * Make some Tile Upgrades more likely (depending on your deck)
  * Allow tile upgrades that would put Attack/CD over minimum/maximum
  * Shop upgrade cost increases by 10 (20 -> 30 -> 40)
  * Warriors Gamble always applies a attack effect
  * All weapons start with 4 slots
  * Reduce CD BackStrike, Tetsubo
  * Crossbow reload is a free action
  * BlazingSuisei deals its damage to all targets (explosion only if you wouldn't hit yourself)
  * CorruptedProgeny uses barrage instead of explosion

Build
-----------
* Clone repo
* Create a copy of Directory.Build.props.default named Directory.Build.props.user
* Open and edit Directory.Build.props.user with your game location
