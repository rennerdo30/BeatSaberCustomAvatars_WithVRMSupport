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

            public struct Finger
    {
        public Quaternion one;
        public Quaternion two;
        public Quaternion three;

        public Finger(Quaternion one, Quaternion two, Quaternion three)
        {
            this.one = one;
            this.two = two;
            this.three = three;
        }
    }
    class HandPositionConstants
    {
        public static Finger index = new Finger(
                                                new Quaternion(0.008440408f, -0.001334298f, 0.2419432f, 0.9702529f),
                                                new Quaternion(0.006469311f, -0.005582907f, 0.7170327f, 0.6969872f),
                                                new Quaternion(0.005353101f, -0.006660718f, 0.8312484f, 0.5558357f)
                                                );
        public static Finger little = new Finger(
                                                new Quaternion(0.008255607f, -0.00205866f, 0.241947f, 0.9702522f),
                                                new Quaternion(0.005930441f, -0.006101066f, 0.7170366f, 0.6969836f),
                                                new Quaternion(0.004364784f, -0.007303547f, 0.8583599f, 0.5129775f)
                                                );
        public static Finger middle = new Finger(
                                                new Quaternion(0.008255607f, -0.00205866f, 0.241947f, 0.9702522f),
                                                new Quaternion(0.005930441f, -0.006101066f, 0.7170366f, 0.6969836f),
                                                new Quaternion(0.001539939f, -0.0083679f, 0.98344f, 0.1809834f)
                                                );
        public static Finger ring = new Finger(
                                                new Quaternion(0.008255607f, -0.00205866f, 0.241947f, 0.9702522f),
                                                new Quaternion(0.005930441f, -0.006101066f, 0.7170366f, 0.6969836f),
                                                new Quaternion(0.002387176f, -0.008166672f, 0.9597998f, 0.2805563f)
                                                );
        public static Finger thumb = new Finger(
                                                new Quaternion(0.5142931f, -0.0385387f, -0.02314265f, 0.8564355f),
                                                new Quaternion(0.502104f, -0.1893172f, -0.1136858f, 0.8361376f),
                                                new Quaternion(0.4902184f, -0.2618173f, -0.1572224f, 0.8163448f)
                                                );
        public static Vector3 handOffset = new Vector3(0.0873f, 0.0348f, 0f);

        private static Quaternion Reflect(Quaternion quat, bool reflect)
        {
            if (!reflect)
            {
                return quat;
            }
            else
            {
                Quaternion temp = quat;
                temp.z = -temp.z;
                temp.y = -temp.y;
                return temp;
            }
        }

        public static void ApplyToHand(Transform hand, bool right)
        {
            if (hand == null)
                return; //nothing can be done.

            //NOTE: Hand rotation can't be done here as the Tracking Device is mapped directly to the hand, overwriting rotations.

            int nFingers = 5;
            if (hand.GetChildCount() < nFingers)
                nFingers = hand.GetChildCount();

            for (int i = 0; i < nFingers; i++)
            {
                Transform fingey = hand.GetChild(i);

                if (fingey == null)
                    continue; //nothing can be done.

                Finger fingerThing = new Finger(Quaternion.identity, Quaternion.identity, Quaternion.identity);
                switch (i)
                {
                    case 0:
                        fingerThing = index;
                        break;
                    case 1:
                        fingerThing = little;
                        break;
                    case 2:
                        fingerThing = middle;
                        break;
                    case 3:
                        fingerThing = ring;
                        break;
                    case 4:
                        fingerThing = thumb;
                        break;
                    default:
                        break;
                }
                fingey.rotation = Reflect(fingerThing.one, right);
                fingey.GetChild(0).rotation = Reflect(fingerThing.two, right);
                fingey.GetChild(0).GetChild(0).rotation = Reflect(fingerThing.three, right);
            }
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
                Debug.Log("New VRM Avatar");
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

                var cameraRig = new GameObject("_CameraRig_");
                cameraRig.transform.SetParent(avatar.transform);

                var controller_Left = new GameObject("_Controller_Left_");
                controller_Left.transform.SetParent(cameraRig.transform);

                var leftHandTarget = new GameObject("LeftHandTarget");
                leftHandTarget.transform.SetParent(controller_Left.transform);
                leftHandTarget.transform.position = new Vector3(-0.03f, 0.025f, -0.12f);
                leftHandTarget.transform.eulerAngles = new Vector3(-40f, 0f, 90f);
                HandPositionConstants.ApplyToHand(ik.references_leftHand, false); //curl fingers.

                var controller_Right = new GameObject("_Controller_Right_");
                controller_Right.transform.SetParent(cameraRig.transform);

                var rightHandTarget = new GameObject("RightHandTarget");
                rightHandTarget.transform.SetParent(controller_Right.transform);
                HandPositionConstants.ApplyToHand(ik.references_rightHand, true); //curl fingers.

                ik.solver_leftArm_target = leftHandTarget.transform;
                ik.solver_rightArm_target = rightHandTarget.transform;

                Transform vrmFirstPersonHeadBone = firstPerson.FirstPersonBone;
                var vrmFirstPersonOffset = firstPerson.FirstPersonOffset;

                var head = new GameObject("Head");
                head.transform.SetParent(avatar.transform);
                head.transform.position = ik.references_head.position;// = vrmFirstPersonHeadBone.position + vrmFirstPersonOffset;

                var headViewpoint = new GameObject("HeadViewPoint");
                headViewpoint.transform.SetParent(head.transform);
                headViewpoint.transform.position = vrmFirstPersonHeadBone.position - vrmFirstPersonOffset;

                ik.solver_spine_headTarget = headViewpoint.transform;

                var leftHand = new GameObject("LeftHand");
                leftHand.transform.SetParent(avatar.transform);
                var rightHand_dummy = new GameObject("RightHand");
                rightHand_dummy.transform.SetParent(avatar.transform);

                //ik.solver_leftArm_target = leftHand.transform;


                //ik.solver_leftArm_target = leftHand_dummy.transform;
                //ik.solver_rightArm_target = rightHand_dummy.transform;

                var descriptor = avatar.AddComponent<AvatarDescriptor>();
                VRMMeta meta = instance.GetComponent<VRMMeta>();
                if (meta == null)
                {
                    descriptor.name = "";
                    descriptor.author = "";
                    descriptor.cover = null;
                }
                else
                {
                    descriptor.name = meta.Meta.Title;
                    descriptor.author = meta.Meta.Author;
                    if (meta.Meta.Thumbnail != null)
                        descriptor.cover = Sprite.Create(meta.Meta.Thumbnail, new Rect(0, 0, meta.Meta.Thumbnail.width, meta.Meta.Thumbnail.height), Vector2.zero);
                    if (descriptor.name.Length == 0)
                        descriptor.name = "";
                }

                if (descriptor.name == "")
                    descriptor.name = System.IO.Path.GetFileName(path);

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
                    Debug.Log($"Loading Existing VRM from cache {fullPath}");

                    __result = task;
                    return false;
                }

                Debug.Log($"Loading New VRM from File {fullPath}");
                task = LoadVRM(fullPath, progress, cancellationToken, ____tasks, ____container);

                //____tasks.Add(fullPath, task); //reload avatar from cache not working atm for some reason.
                __result = task;
                return false;
            }

            //AssetBundle
            return true;
        }
    }
}


