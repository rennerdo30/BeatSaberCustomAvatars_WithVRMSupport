using CustomAvatar;
using CustomAvatar.Avatar;
using CustomAvatar.Player;
using HarmonyLib;
using IPA.Utilities;
using RootMotion.FinalIK;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UniGLTF;
using UnityEngine;
using UniVRM10;
using VRM;
using Zenject;

namespace VRMAvatar
{
    [HarmonyPatch(typeof(PlayerAvatarManager), "GetAvatarFileNames")]
    public class VRMAvatar_ListFiles
    {
        static void Postfix(ref List<string> __result)
        {
            __result.AddRange(Directory.GetFiles(PlayerAvatarManager.kCustomAvatarsPath, "*.vrm", SearchOption.TopDirectoryOnly).Select(f => Path.GetFileName(f)).OrderBy(f => f).ToList());
        }
    }

    [HarmonyPatch(typeof(AvatarLoader), "LoadFromFileAsync", new[] { typeof(string), typeof(IProgress<float>), typeof(CancellationToken) })]
    public class VRMAvatar_LoadFromFile
    {
        private static IMaterialDescriptorGenerator GetVrmMaterialGenerator(bool useUrp, VRM.glTF_VRM_extensions vrm)
        {
            if (useUrp)
            {
                return new VRM.VRMUrpMaterialDescriptorGenerator(vrm);
            }
            else
            {
                return new VRM.VRMMaterialDescriptorGenerator(vrm);
            }
        }

        private async static Task<AvatarPrefab> LoadVRM(string path, IProgress<float> progress, CancellationToken cancellationToken, Dictionary<string, Task<AvatarPrefab>> tasks, DiContainer _container)
        {
            VRM.VRMFirstPerson.FIRSTPERSON_ONLY_LAYER = CustomAvatar.Avatar.AvatarLayers.kAlwaysVisible;
            VRM.VRMFirstPerson.THIRDPERSON_ONLY_LAYER = CustomAvatar.Avatar.AvatarLayers.kOnlyInThirdPerson;

            if (ExternalAssets.ShaderHelper.m_externalShaders == null)
            {
                //Shaders for VRM Avatars (Beat Saber Specific)
                Debug.Log("Load AssetBundle: vrmmaterialchange_bs_shaders.assets");
                var shadersBundleCreateRequest = AssetBundle.LoadFromStreamAsync(Assembly.GetExecutingAssembly().GetManifestResourceStream("CustomAvatar.Resources.vrmmaterialchange_bs_shaders.assets"));
                var assetBundle = shadersBundleCreateRequest.assetBundle;
                var assetBundleRequest = assetBundle.LoadAllAssetsAsync<Shader>();
                assetBundle = shadersBundleCreateRequest.assetBundle;
                ExternalAssets.ExternalAssetsHelper.LoadExternalAssets(assetBundle);
                assetBundle.Unload(false);

                //Shaders: Replace a General with Specific shader.
                Shader result = ExternalAssets.ShaderHelper.Find("BeatSaber/MToon");
                if (result)
                    ExternalAssets.ShaderHelper.AddExternalShader("VRM/MToon", result); //Replace "VRM/Toon" Shader with BeatSaber/MToon shader.

                result = ExternalAssets.ShaderHelper.Find("VRM/UnlitTexture");
                if (result)
                    ExternalAssets.ShaderHelper.AddExternalShader("VRM10/MToon10", result); //Replace "VRM/Toon" Shader with BeatSaber/MToon shader.
            }

#if USE_VRM_10 //NOTE: Cannot use as VRM1.0 requires Shader MToon10, which has not yet been converted to Beatsaber [and thus is white-out'ed].
            Debug.Log("Vrm1.0: loading.");
            Vrm10.LoadPathAsync(path, awaitCaller: awaitCaller); 
            Vrm10Instance instance = await Vrm10.LoadPathAsync(path);
#else
            Debug.Log("Vrm0.x: loading.");

            VRM.VRMFirstPerson.FIRSTPERSON_ONLY_LAYER = CustomAvatar.Avatar.AvatarLayers.kAlwaysVisible;
            VRM.VRMFirstPerson.THIRDPERSON_ONLY_LAYER = CustomAvatar.Avatar.AvatarLayers.kOnlyInThirdPerson;

            VrmUtility.MaterialGeneratorCallback materialCallback = (VRM.glTF_VRM_extensions vrm) => GetVrmMaterialGenerator(true, vrm);
            RuntimeGltfInstance instance = await VrmUtility.LoadAsync(path, null, materialCallback);
#endif

            var firstPerson = instance.GetComponent<VRMFirstPerson>();
            firstPerson.Setup();

            var animator = instance.GetComponent<Animator>();

            GameObject obj = null;

            {
                var avatar = new GameObject("Avatar");
                GameObject.DontDestroyOnLoad(avatar.gameObject);
                obj = avatar.gameObject;
                // obj.SetActive(false);
                //avatar.transform.position = new Vector3(0f, -100f, 0f);
                // RuntimeGltfInstance instance = loader.Load();
                instance.transform.SetParent(avatar.transform, false);
#if USE_VRM_10
#else
                instance.ShowMeshes();
#endif
                //instance.gameObject.SetActive(false); //don't set the prefab object as active. it will be instantiated later.

                var ik = instance.gameObject.AddComponent<VRIKManager>();
                ik.AutoDetectReferences();

                ik.references_leftThigh.Rotate(new Vector3(-0.1f, 0f, 0f), Space.World);
                ik.references_leftCalf.Rotate(new Vector3(0.1f, 0f, 0f), Space.World);

                ik.references_rightThigh.Rotate(new Vector3(-0.1f, 0f, 0f), Space.World);
                ik.references_rightCalf.Rotate(new Vector3(0.1f, 0f, 0f), Space.World);

                var leftHand = new GameObject("LeftHand"); //will be connected to Tracker by Name "LeftHand".
                leftHand.transform.position = ik.references_leftHand.position;
                leftHand.transform.SetParent(avatar.transform);
                HandPositionConstants.ApplyToHand(ik.references_leftHand, false);
                ik.solver_leftArm_target = leftHand.transform;

                var leftHand_localPose = new GameObject("LeftHandLocalPose");
                leftHand_localPose.transform.SetParent(avatar.transform);
                leftHand_localPose.transform.eulerAngles = new Vector3(-40f, 0, -90f);



                //var leftHandPalm = new GameObject("LeftHandPalm");
                //leftHandPalm.transform.SetParent(leftHand.transform);
                //ik.references_leftHand.transform.position = new Vector3(0.1f, 0.1f, 0.1f);
                //ik.references_leftHand.transform.eulerAngles = new Vector3(-40f, 0, -90f);

                var rightHand = new GameObject("RightHand");
                rightHand.transform.position = ik.references_rightHand.position;
                rightHand.transform.SetParent(avatar.transform);
                HandPositionConstants.ApplyToHand(ik.references_rightHand, true);
                ik.solver_rightArm_target = rightHand.transform;

                Transform vrmFirstPersonHeadBone = firstPerson.FirstPersonBone;
                var vrmFirstPersonOffset = firstPerson.FirstPersonOffset;

                var head = new GameObject("Head");
                head.transform.SetParent(avatar.transform);
                head.transform.position = ik.references_head.position;// = vrmFirstPersonHeadBone.position + vrmFirstPersonOffset;

                var headViewpoint = new GameObject("HeadViewPoint");
                headViewpoint.transform.SetParent(head.transform);
                headViewpoint.transform.position = vrmFirstPersonHeadBone.position - vrmFirstPersonOffset;

                ik.solver_spine_headTarget = headViewpoint.transform;

                var descriptor = avatar.AddComponent<AvatarDescriptor>();
                VRMMeta meta = instance.GetComponent<VRMMeta>();
                if (meta == null)
                {
                    descriptor.name = "null";
                    descriptor.author = "null";
                    descriptor.cover = null;
                }
                else
                {
                    descriptor.name = meta.Meta.Title;
                    descriptor.author = meta.Meta.Author;
                    if (meta.Meta.Thumbnail != null)
                        descriptor.cover = Sprite.Create(meta.Meta.Thumbnail, new Rect(0, 0, meta.Meta.Thumbnail.width, meta.Meta.Thumbnail.height), Vector2.zero);
                }
                descriptor.allowHeightCalibration = true;
            }

            AvatarPrefab avatarPrefab = _container.InstantiateComponent<AvatarPrefab>(obj, new object[] { path });
            avatarPrefab.avatarFormat = AvatarPrefab.AvatarFormat.AVATAR_FORMAT_VRM;
            avatarPrefab.name = $"AvatarPrefab({avatarPrefab.descriptor.name})";
            avatarPrefab.gameObject.SetActive(false); //set the AvatarPrefab as Not Active [instantiated avatars will be set as active].

            tasks.Remove(path);

            return avatarPrefab;
        }

        static bool Prefix(string path, IProgress<float> progress, CancellationToken cancellationToken, ref Task<AvatarPrefab> __result, Dictionary<string, Task<AvatarPrefab>> ____tasks, DiContainer ____container)
        {
            // Plugin.Log.Info(Path.GetExtension(path));
            if (Path.GetExtension(path) == ".vrm")
            {
                //VRM
                if (string.IsNullOrEmpty(path))
                {
                    throw new ArgumentNullException("path");
                }
                if (!UnityGame.OnMainThread)
                {
                    throw new InvalidOperationException("LoadFromFileAsync should only be called on the main thread");
                }
                string fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath))
                {
                    throw new IOException("File '" + fullPath + "' does not exist");
                }

                Task<AvatarPrefab> task;
                if (____tasks.TryGetValue(fullPath, out task))
                {
                    __result = task;
                    return false;
                }

                task = LoadVRM(fullPath, progress, cancellationToken, ____tasks, ____container);

                ____tasks.Add(fullPath, task);
                __result = task;
                return false;
            }

            //AssetBundle
            return true;
        }
    }
}
