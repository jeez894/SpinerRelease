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
        public AudioClip? kidnappingSound;
        public AudioClip? moveSound;
        public AudioClip? creepSound;
        public AudioClip? transportSound;
        public AudioClip? runawaySound;
        public AudioClip? detectionSound;
        public AudioClip? deathSound;
        public AudioClip? deathSound2;
        public AudioClip? deathSound3;
        public AudioClip? roamingSound;
        public AudioClip? roamingSound2;
        public AudioClip? roamingSound3;
        public AudioClip? roamingSound4;
        public AudioClip? roamingSound5;
        public AudioClip? roamingSound6;

        public string? animStalk = "Stalk";
        public string? animWalk = "Walk";
        public string? animAttack = "Attack";
        public string? animGrab = "Grab";
        public string? animHurt = "Hurt";
        public string? animHurt2 = "Hurt2";
        public string? animHurt3 = "Hurt3";
        public string? animDeath = "Death";
        public string? animDeath2 = "Death2";
        public string? animSpin = "Spin";

        public Transform kidnapCarryPoint;
        public PlayerControllerB chasingPlayer;
    }
}
