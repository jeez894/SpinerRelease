using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Unity.Netcode;
using GameNetcodeStuff;
using LethalLib.Modules;
using System.Linq;
using UnityEngine.AI;

namespace Spiner
{
    public class SpinerAI : EnemyAI
    {
        // ─────────────────────────────────────────────
        //  Audio sources (for inspector wiring)
        // ─────────────────────────────────────────────
        public AudioSource walkAudioSource;   // looped footsteps
        public AudioSource sfxAudioSource;    // one‑shot SFX

        // ─────────────────────────────────────────────
        //  Core SFX clips
        // ─────────────────────────────────────────────
        public AudioClip kidnappingSound;
        public AudioClip moveSound;
        public AudioClip creepSound;
        public AudioClip transportSound;
        public AudioClip runawaySound;
        public AudioClip detectionSound;

        //  Death variations
        public AudioClip deathSound;

        //  Roaming variations
        public AudioClip roamingSound;
        public AudioClip roamingSound2;
        public AudioClip roamingSound3;
        // ─────────────────────────────────────────────
        //  Quack SFX clips
        // ─────────────────────────────────────────────
        public AudioClip quackKidnappingSound;
        public AudioClip quackCreepSound;
        public AudioClip quackTransportSound;
        public AudioClip quackRunawaySound;
        public AudioClip quackDetectionSound;

        //  Death variations
        public AudioClip quackDeathSound;

        //  Roaming variations
        public AudioClip quackRoamingSound;
        public AudioClip quackRoamingSound2;
        public AudioClip quackRoamingSound3;

        // ─────────────────────────────────────────────
        //  Animator (Mecanim)
        // ─────────────────────────────────────────────

        //  Animator state / trigger names
        public string animStalk = "Stalk";
        public string animWalk = "Walk";
        public string animAttack = "Attack";
        public string animGrab = "Grab";
        public string animHurt = "Hurt";
        public string animHurt2 = "Hurt2";
        public string animHurt3 = "Hurt3";
        public string animDeath = "Death";
        public string animDeath2 = "Death2";
        public string animSpin = "Spin";
        public string animSpinTest = "Spintest";

        // ─────────────────────────────────────────────
        //  Scene references
        // ─────────────────────────────────────────────
        public Transform kidnapCarryPoint;
        public PlayerControllerB chasingPlayer;
    }
}

