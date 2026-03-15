# 2.0.0

* Replaced the default duck sound pack with the critters sound pack.

* Fixed a culling issue.

* Added an option to switch between the normal sound pack and the quack sound pack
  via the config file or LethalConfig.
  (Old config files must be deleted. See the description for the file location.)

# 1.0.9

* Major rework of the transport mechanics

* Improved capture safety checks to prevent invalid recaptures

* Fixed an issue preventing Spiner from being stunned


# 1.0.8

* Reworked animation synchronization

* Reworked player collision behavior

# 1.0.7

* Fixed an issue where a carried player could remain linked to Spiner if Spiner died
  during transport

* Fixed a critical transport-release bug that could cause the carried player to fall
  through the map

* Added a pathfinding “stuck” safeguard

* Fixed target persistence when a chased player exits the facility


# 1.0.6

* Configuration file descriptions translated to English  
  (delete `Jeez.Spiner.cfg` to regenerate and apply updated descriptions)

* Terminal bestiary entry added

# 1.0.5

* UI update

# 1.0.3

* Information update for full public release

* Full rewrite of stalking behavior

* Speed balancing

* Removal of prerelease debug features

# 1.0.2

* Fixed a bug causing incorrect respawn after the end of a day when a player was captured

* Improved stability of creeping behavior

* Reduced footstep volume

# 1.0.1

* Corrections to README and manifest

* Changes in SpinerPlugin.cs to make the use of LethalConfig optional

# 1.0.0

* Initial official release
