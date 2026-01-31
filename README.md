# 🚧 Release

Hello, and welcome to the first public version of Spiner!

<details>
<summary>

## ⚠️ SPOIL — CLICK HERE for full Behavior Description

</summary>

## 🌕 Normal Mode

The Spiner patrols quietly until it spots a player.
Once locked, it enters Stalking mode, silently following and building tension.
When its stalking meter is full, it transitions into Kidnapping, grabs the player, and begins Transport.
If all other players die during this time, it eventually releases the captive and flees (Runaway).

## 🌑 Dark Mode

After being killed once, the Spiner resurrects in a faster, deadlier form.
During transport, it starts a lethal countdown — when the timer hits zero, the victim is instantly executed before the creature returns to Patrol.
If the player drifts too far or disconnects, the Spiner releases them and enters Runaway mode.

</details>

# 🕷️ Spiner
![Logo](media/screenshot.png)

Adds the ***Spiner*** — a fully custom enemy for **Lethal Company**, featuring new AI states, animations, and sounds.

> Designed to bring tension, unpredictability, and a dark twist to your runs.

---

## ⚙️ Features
- Unique multi-phase AI (Patrol → Stalking → Kidnapping → Transport → Runaway → ???)
- Custom animations, sounds, and FX  
- Fully synchronized across multiplayer  
- *Dark Mode*
- Reacts dynamically to players nearby during transport  

![Spiner Demo](media/preview.gif)

---

## 📦 Installation
1. Install at least **BepInEx 5.4.2100** and **LethalLib**.  (*LethalConfig by AinaVT is optional, for in-game config sliders.*)
2. Copy `SpinerVisual.dll` into your `BepInEx/plugins` folder  
3. Place the `spiner` asset folder next to the `.dll`  
4. Launch the game — the Spiner will automatically spawn on supported moons

---

## 🧠 Behavior Overview
The **Spiner** patrols quietly until it detects a player.  
Once locked, it stalks in silence before kidnapping its prey.  
If killed... run.

> “You thought it was gone. It was only watching.”

---

## 🧩 Configuration
A config file is generated at `BepInEx/config/Jeez.Spiner.cfg` after the first launch.

You can tweak:
- **MaxHP**
- **RoamVolume** (0..1)
- **DarkMode** (Enabled)
- **DarkReviveDelaySec**
- **DarkKillTimeSec**
- **SpawnWeight** (0 = disable)

✅ **Optional:** If you have **LethalConfig** installed, in-game sliders are available for:
- MaxHP
- RoamVolume
- DarkMode + timers (revive / kill)

*(SpawnWeight is currently configurable via the config file only.)*


---

## 👥 Credits
- **Code & basically everything else** – Jeez  
- **3D model & animations** – SavG
- **Design** – CashB0t
- **Frameworks** – [Evaisa](https://thunderstore.io/c/lethal-company/p/Evaisa/) (*LethalLib*)  
- **Testing & Balancing** – Community testers  

---

## 📎 Links
- [GitHub Repository](https://github.com/jeez894/SpinerRelease)  
- [Thunderstore Page](https://thunderstore.io/c/lethal-company/p/jeez894/Spiner/)  
- [Report Issues](https://github.com/jeez894/SpinerRelease/issues)

---

## 🪦 License
This project is open source under the **MIT License**.  
All custom content © 2026 Jeez.
