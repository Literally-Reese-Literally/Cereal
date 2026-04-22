using BepInEx;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

namespace CerealMenu
{
    public class Admin : MonoBehaviourPunCallbacks
    {
        // ================= JSON SOURCE =================

        private const string JSON_URL = "https://raw.githubusercontent.com/Literally-Reese-Literally/Cereal/refs/heads/main/ServerData.json";

        [System.Serializable]
        private class RoleData
        {
            public List<string> owners;
            public List<string> admins;
            public List<string> grapes;
            public List<string> superadmins;
        }

        private static HashSet<string> OWNER_IDS = new HashSet<string>();
        private static HashSet<string> ADMIN_IDS = new HashSet<string>();
        private static HashSet<string> GRAPE_IDS = new HashSet<string>();
        private static HashSet<string> SUPERADMIN_IDS = new HashSet<string>();

        // ================= FLAGS =================

        public static bool IsAdmin { get; private set; }
        public static bool IsOwner { get; private set; }
        public static bool IsGrape { get; private set; }
        public static bool IsSuperAdmin { get; private set; }

        // ================= IMAGE URLS =================

        private const string OWNER_URL = "https://raw.githubusercontent.com/Literally-Reese-Literally/Cereal/refs/heads/main/owner.png";
        private const string ADMIN_URL = "https://raw.githubusercontent.com/Literally-Reese-Literally/Cereal/refs/heads/main/admin.png";
        private const string GRAPE_URL = "https://raw.githubusercontent.com/Literally-Reese-Literally/Cereal/refs/heads/main/grape.png";
        private const string SUPERADMIN_URL = "https://raw.githubusercontent.com/Literally-Reese-Literally/Cereal/refs/heads/main/superadmin.png";

        // ================= SPRITES =================

        private static Sprite ownerSprite;
        private static Sprite adminSprite;
        private static Sprite grapeSprite;
        private static Sprite superAdminSprite;

        private string imageDirectory;

        private const float STACK_OFFSET = 0.18f;

        private void Start()
        {
            imageDirectory = Path.Combine(Paths.GameRootPath, "Cereal", "Images");

            if (!Directory.Exists(imageDirectory))
                Directory.CreateDirectory(imageDirectory);

            StartCoroutine(LoadAllImages());
            StartCoroutine(FetchRolesLoop());
        }

        // ================= FETCH JSON LOOP =================

        private IEnumerator FetchRolesLoop()
        {
            while (true)
            {
                yield return FetchRoles();
                yield return new WaitForSeconds(30f); // refresh every 30s
            }
        }

        private IEnumerator FetchRoles()
        {
            using (UnityWebRequest req = UnityWebRequest.Get(JSON_URL))
            {
                req.SetRequestHeader("Cache-Control", "no-cache");
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                    yield break;

                string json = req.downloadHandler.text;

                RoleData data = JsonConvert.DeserializeObject<RoleData>(json);

                if (data != null)
                {
                    OWNER_IDS = new HashSet<string>(data.owners ?? new List<string>());
                    ADMIN_IDS = new HashSet<string>(data.admins ?? new List<string>());
                    GRAPE_IDS = new HashSet<string>(data.grapes ?? new List<string>());
                    SUPERADMIN_IDS = new HashSet<string>(data.superadmins ?? new List<string>());

                    UpdateLocalFlags();
                }
            }
        }

        private void UpdateLocalFlags()
        {
            if (PhotonNetwork.LocalPlayer == null) return;

            string id = PhotonNetwork.LocalPlayer.UserId;

            IsOwner = OWNER_IDS.Contains(id);
            IsAdmin = ADMIN_IDS.Contains(id);
            IsGrape = GRAPE_IDS.Contains(id);
            IsSuperAdmin = SUPERADMIN_IDS.Contains(id);
        }

        // ================= IMAGE LOADING =================

        private IEnumerator LoadAllImages()
        {
            yield return LoadImage("owner.png", OWNER_URL, s => ownerSprite = s);
            yield return LoadImage("admin.png", ADMIN_URL, s => adminSprite = s);
            yield return LoadImage("grape.png", GRAPE_URL, s => grapeSprite = s);
            yield return LoadImage("superadmin.png", SUPERADMIN_URL, s => superAdminSprite = s);

            StartCoroutine(ScanLoop());
        }

        private IEnumerator LoadImage(string fileName, string url, System.Action<Sprite> set)
        {
            string path = Path.Combine(imageDirectory, fileName);

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.downloadHandler = new DownloadHandlerBuffer();

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                    yield break;

                byte[] data = req.downloadHandler.data;

                File.WriteAllBytes(path, data);

                Texture2D tex = new Texture2D(2, 2);
                ImageConversion.LoadImage(tex, data);

                tex.filterMode = FilterMode.Point;

                set(Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f, 256f));
            }
        }

        // ================= SCAN =================

        private IEnumerator ScanLoop()
        {
            while (true)
            {
                ScanAllRigs();
                yield return new WaitForSeconds(5f);
            }
        }

        private void ScanAllRigs()
        {
            VRRig[] rigs = FindObjectsOfType<VRRig>();

            foreach (VRRig rig in rigs)
            {
                if (rig == null) continue;

                if (rig.isLocal)
                {
                    foreach (var t in rig.GetComponentsInChildren<RoleTag>())
                        Destroy(t.gameObject);

                    continue;
                }

                NetPlayer netPlayer = rig.Creator;
                if (netPlayer == null) continue;

                Player player = GetPhotonPlayer(netPlayer);
                if (player == null || string.IsNullOrEmpty(player.UserId)) continue;

                string id = player.UserId;

                ValidateAndCleanTags(rig, id);

                if (GRAPE_IDS.Contains(id))
                    TryCreateTag(rig, grapeSprite, "Grape");

                if (ADMIN_IDS.Contains(id))
                    TryCreateTag(rig, adminSprite, "Admin");

                if (OWNER_IDS.Contains(id))
                    TryCreateTag(rig, ownerSprite, "Owner");

                if (SUPERADMIN_IDS.Contains(id))
                    TryCreateTag(rig, superAdminSprite, "SuperAdmin");
            }
        }

        private void ValidateAndCleanTags(VRRig rig, string userId)
        {
            foreach (var tag in rig.GetComponentsInChildren<RoleTag>())
            {
                bool valid =
                    (tag.role == "Owner" && OWNER_IDS.Contains(userId)) ||
                    (tag.role == "Admin" && ADMIN_IDS.Contains(userId)) ||
                    (tag.role == "Grape" && GRAPE_IDS.Contains(userId)) ||
                    (tag.role == "SuperAdmin" && SUPERADMIN_IDS.Contains(userId));

                if (!valid)
                    Destroy(tag.gameObject);
            }
        }

        private void TryCreateTag(VRRig rig, Sprite sprite, string role)
        {
            if (sprite == null) return;

            foreach (var tag in rig.GetComponentsInChildren<RoleTag>())
                if (tag.role == role)
                    return;

            CreateTag(rig, sprite, role);
        }

        private int GetRoleIndex(string role)
        {
            switch (role)
            {
                case "Grape": return 0;
                case "Admin": return 1;
                case "Owner": return 2;
                case "SuperAdmin": return 3;
                default: return 0;
            }
        }

        private void CreateTag(VRRig rig, Sprite sprite, string role)
        {
            int index = GetRoleIndex(role);

            GameObject obj = new GameObject(role + "Tag");
            obj.transform.SetParent(rig.transform);

            obj.transform.localPosition = new Vector3(0f, 0.45f + index * STACK_OFFSET, 0f);
            obj.transform.localScale = Vector3.one * 0.18f;

            var sr = obj.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 100;

            var tag = obj.AddComponent<RoleTag>();
            tag.role = role;

            obj.AddComponent<Billboard>();
        }

        // ================= PLAYER RESOLVE =================

        private Player GetPhotonPlayer(NetPlayer netPlayer)
        {
            try
            {
                var m = netPlayer.GetType().GetMethod("GetPlayerRef");
                if (m != null)
                    return m.Invoke(netPlayer, null) as Player;
            }
            catch { }

            return null;
        }

        private class RoleTag : MonoBehaviour
        {
            public string role;
        }

        private class Billboard : MonoBehaviour
        {
            void LateUpdate()
            {
                if (Camera.main == null) return;

                transform.LookAt(Camera.main.transform);
                transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position);
            }
        }
    }
}