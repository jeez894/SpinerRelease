# 🚧 Pre-Release

Hello, and welcome to the first public version of Spiner!

## 🧩 Installation (testing build)

Click the <> Code → Download ZIP button, then extract the archive.

In r2modman, go to Settings → Browse profile folder.

Open BepInEx/plugins/.

Copy the extracted spiner folder into that directory.

Make sure you have these dependencies installed:

BepInExPack by BepInEx 5.4.2304

LethalLib by Evaisa 1.1.1

Launch the game once, then close it to generate local configuration files.
You're all set — the mod is ready to go! 🕷️

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

💬 This is a pre-release build, so bugs are expected.
Please report any issues on GitHub, or DM on Discord if you have access to the test group.
👉 When reporting, include as many details as possible — what happened before, during, and after the issue.




# 🕷️ Spiner
![Logo](https://i.imgur.com/xxxxxxxx.png)

Adds the ***Spiner*** — a fully custom enemy for **Lethal Company**, featuring new AI states, animations, and sounds.

> Designed to bring tension, unpredictability, and a dark twist to your runs.

---

## ⚙️ Features
- Unique multi-phase AI (Patrol → Stalking → Kidnapping → Transport → Runaway → ???)
- Custom animations, sounds, and FX  
- Fully synchronized across multiplayer  
- *Dark Mode*
- Reacts dynamically to players nearby during transport  

![Spiner Demo](https://i.imgur.com/yyyyyyyy.gif)

---

## 📦 Installation
1. Install **BepInEx 5.4.2100**, **LethalLib 0.15.1**, and **LCCustomAssets 1.1.4**
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
A runtime config (`BepInEx/config/Spiner.cfg`) lets you tweak:
- Max HP  
- Detection volume  
- Dark mode delay & kill timer  
- Sound frequency and intensity  

compatible with lethalconfig by AinaVT


---

## 👥 Credits
- **Code & AI** – Jeez  
- **Design** – SavG
- **Design** – CashB0t
- **Frameworks** – [Evaisa](https://thunderstore.io/c/lethal-company/p/Evaisa/) (*LethalLib*, *LCCustomAssets*)  
- **Testing & Balancing** – Community testers  

---

## 📎 Links
- [GitHub Repository](https://github.com/jeez894/SpinerRelease)  
- [Thunderstore Page](https://thunderstore.io/c/lethal-company/p/jeez894/Spiner/)  
- [Report Issues](https://github.com/jeez894/SpinerRelease/issues)

---

## 🪦 License
This project is open source under the **MIT License**.  
All custom content © 2025 Jeez.
