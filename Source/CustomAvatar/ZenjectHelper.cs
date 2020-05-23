﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;
using Logger = IPA.Logging.Logger;

namespace CustomAvatar
{
    public class ZenjectHelper
    {
        private static Logger _ipaLogger;

        internal static void ApplyPatches(Harmony harmony, Logger logger)
        {
            _ipaLogger = logger;

            var methodToPatch = typeof(AppCoreInstaller).GetMethod("InstallBindings", BindingFlags.Public | BindingFlags.Instance);
            var patch = new HarmonyMethod(typeof(ZenjectHelper).GetMethod(nameof(InstallBindings), BindingFlags.NonPublic | BindingFlags.Static));

            harmony.Patch(methodToPatch, null, patch);
        }

        public static void GetMainSceneContextAsync(Action<SceneContext> success)
        {
            GetSceneContextAsync(success, "PCInit");
        }

        public static void GetGameSceneContextAsync(Action<SceneContext> success)
        {
            GetSceneContextAsync(success, "GameplayCore");
        }

        private static void GetSceneContextAsync(Action<SceneContext> success, string sceneName)
        {
            if (!SceneManager.GetSceneByName(sceneName).isLoaded) throw new Exception($"Scene '{sceneName}' is not loaded");

            List<SceneContext> sceneContexts = Resources.FindObjectsOfTypeAll<SceneContext>().Where(sc => sc.gameObject.scene.name == sceneName).ToList();

            if (sceneContexts.Count == 0)
            {
                throw new Exception($"Scene context not found in scene '{sceneName}'");
            }

            if (sceneContexts.Count > 1)
            {
                throw new Exception($"More than one scene context found in scene '{sceneName}'");
            }

            SceneContext sceneContext = sceneContexts[0];

            if (sceneContext.HasInstalled)
            {
                success(sceneContext);
            }
            else
            {
                sceneContext.OnPostInstall.AddListener(() => success(sceneContext));
            }
        }

        private static void InstallBindings(AppCoreInstaller __instance)
        {
            DiContainer container = new Traverse(__instance).Property<DiContainer>("Container").Value;

            container.Install<CustomAvatarsInstaller>(new object[] { _ipaLogger });
            container.Install<UIInstaller>();
        }
    }
}
