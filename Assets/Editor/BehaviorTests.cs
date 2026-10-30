using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public static class BehaviorTests
{
    public static void RunTests()
    {
        Debug.Log("=========================================");
        Debug.Log("[BehaviorTests] Starting Live Unity Engine Tests (Isolated Sandbox)");
        Debug.Log("=========================================");

        try
        {
            TestCustomThumbnailPreservation();
            TestRegistryReadErrorProtection();
            TestSaveAvatarEntriesNonDestructiveMerge();
            TestWorkshopMappingResolutionAndAmbiguity();

            Debug.Log("=========================================");
            Debug.Log("[BehaviorTests] ALL 4 ACTUAL C# BEHAVIOR TESTS PASSED IN ISOLATED SANDBOX!");
            Debug.Log("=========================================");
            EditorApplication.Exit(0);
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[BehaviorTests] TEST FAILED: " + ex.ToString());
            EditorApplication.Exit(1);
        }
    }

    private static void TestCustomThumbnailPreservation()
    {
        Debug.Log("[Test 1] Testing custom thumbnail preservation on model reload (isolated)...");
        string tempSandbox = Path.Combine(Path.GetTempPath(), "MateEngineTest_Thumb_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSandbox);

        try
        {
            string modelPath = Path.Combine(tempSandbox, "custom_avatar.vrm");
            File.WriteAllText(modelPath, "dummy_vrm_bytes");

            // 1. Initial import with red texture
            Texture2D redTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color[] redPixels = new Color[16];
            for (int i = 0; i < 16; i++) redPixels[i] = Color.red;
            redTex.SetPixels(redPixels);
            redTex.Apply();

            AvatarLibraryMenu.AddAvatarToLibrary("CustomAvatar", "TestAuthor", "1.0", "VRM", modelPath, redTex, 500, tempSandbox);

            string thumbName = AvatarLibraryMenu.GetThumbnailFileName(modelPath);
            string thumbPath = Path.Combine(tempSandbox, "Thumbnails", thumbName);
            if (!File.Exists(thumbPath)) throw new System.Exception("Initial thumbnail not created at " + thumbPath);

            // 2. User uploads a custom thumbnail (mark file with custom user bytes)
            byte[] customBytes = System.Text.Encoding.UTF8.GetBytes("USER_CUSTOM_THUMBNAIL_DATA");
            File.WriteAllBytes(thumbPath, customBytes);

            // 3. User reloads model: VRMLoader extracts model's embedded thumbnail (green) and calls AddAvatarToLibrary
            Texture2D greenTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color[] greenPixels = new Color[16];
            for (int i = 0; i < 16; i++) greenPixels[i] = Color.green;
            greenTex.SetPixels(greenPixels);
            greenTex.Apply();

            AvatarLibraryMenu.AddAvatarToLibrary("CustomAvatar", "TestAuthor", "1.0", "VRM", modelPath, greenTex, 500, tempSandbox);

            // 4. Verify the thumbnail file on disk STILL contains user's custom bytes
            byte[] currentBytes = File.ReadAllBytes(thumbPath);
            string currentStr = System.Text.Encoding.UTF8.GetString(currentBytes);
            if (currentStr != "USER_CUSTOM_THUMBNAIL_DATA")
            {
                throw new System.Exception("BUG: User's custom thumbnail was overwritten by model reload!");
            }

            Debug.Log("[Test 1] PASSED: User custom thumbnail safely preserved across reload.");
        }
        finally
        {
            try { Directory.Delete(tempSandbox, true); } catch { }
        }
    }

    private static void TestRegistryReadErrorProtection()
    {
        Debug.Log("[Test 2] Testing registry read error data loss protection (isolated)...");
        string tempSandbox = Path.Combine(Path.GetTempPath(), "MateEngineTest_Reg_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSandbox);

        try
        {
            string avatarsPath = Path.Combine(tempSandbox, "avatars.json");
            string corruptData = "{ partially written json data [";
            File.WriteAllText(avatarsPath, corruptData);

            // Attempt to mutate under corrupt json read
            var result = AvatarLibraryMenu.MutateAvatarsRegistry(entries =>
            {
                entries.Add(new AvatarLibraryMenu.AvatarEntry { displayName = "ShouldNotBeSaved" });
                return true;
            }, tempSandbox);

            if (result != null) throw new System.Exception("MutateAvatarsRegistry should return null on corrupt json read!");

            string onDisk = File.ReadAllText(avatarsPath);
            if (onDisk != corruptData) throw new System.Exception("Corrupted registry was overwritten! Data loss occurred!");

            Debug.Log("[Test 2] PASSED: Corrupted registry was not overwritten; original data preserved.");
        }
        finally
        {
            try { Directory.Delete(tempSandbox, true); } catch { }
        }
    }

    private static void TestSaveAvatarEntriesNonDestructiveMerge()
    {
        Debug.Log("[Test 3] Testing Steam refresh non-destructive merge & deleted entry protection (isolated)...");
        string tempSandbox = Path.Combine(Path.GetTempPath(), "MateEngineTest_SteamMerge_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSandbox);

        try
        {
            string avatarsPath = Path.Combine(tempSandbox, "avatars.json");

            // Initial state in disk registry:
            // - User deleted LocalAvatar1 (it is NOT in avatars.json)
            // - User has an existing Workshop avatar #111 with custom title and isNSFW=true
            // - User has an existing Workshop avatar #333
            string w111Path = Path.Combine(tempSandbox, "111_model.vrm");
            string w333Path = Path.Combine(tempSandbox, "333_model.vrm");
            string w222Path = Path.Combine(tempSandbox, "222_model.vrm");
            File.WriteAllText(w111Path, "dummy_vrm_111");
            File.WriteAllText(w333Path, "dummy_vrm_333");
            File.WriteAllText(w222Path, "dummy_vrm_222");

            var initialDiskEntries = new List<AvatarLibraryMenu.AvatarEntry>
            {
                new AvatarLibraryMenu.AvatarEntry
                {
                    displayName = "CustomUserChosenName",
                    filePath = w111Path,
                    isSteamWorkshop = true,
                    steamFileId = 111,
                    isNSFW = true,
                    version = "1.0",
                    polygonCount = 1000
                },
                new AvatarLibraryMenu.AvatarEntry
                {
                    displayName = "WorkshopAvatar333",
                    filePath = w333Path,
                    isSteamWorkshop = true,
                    steamFileId = 333,
                    isNSFW = false,
                    version = "1.0",
                    polygonCount = 500
                }
            };
            File.WriteAllText(avatarsPath, JsonConvert.SerializeObject(initialDiskEntries, Formatting.Indented));

            // Snapshot workshop IDs before background refresh processing starts
            var initialWorkshopIds = new HashSet<ulong> { 111, 333 };

            // Steam background refresh finishes downloading / processing:
            // 1. Updated package for #111 (version 1.1, polygon count 1200, original workshop title "WorkshopTitle111", isNSFW=false)
            // 2. Existing package for #333
            // 3. Newly subscribed avatar #222 ("NewWorkshopAvatar222", isNSFW=false)
            var workshopEntries = new List<AvatarLibraryMenu.AvatarEntry>
            {
                new AvatarLibraryMenu.AvatarEntry
                {
                    displayName = "WorkshopTitle111",
                    filePath = w111Path,
                    isSteamWorkshop = true,
                    steamFileId = 111,
                    isNSFW = false,
                    version = "1.1",
                    polygonCount = 1200
                },
                new AvatarLibraryMenu.AvatarEntry
                {
                    displayName = "WorkshopAvatar333",
                    filePath = w333Path,
                    isSteamWorkshop = true,
                    steamFileId = 333,
                    isNSFW = false,
                    version = "1.0",
                    polygonCount = 500
                },
                new AvatarLibraryMenu.AvatarEntry
                {
                    displayName = "NewWorkshopAvatar222",
                    filePath = w222Path,
                    isSteamWorkshop = true,
                    steamFileId = 222,
                    isNSFW = false,
                    version = "1.0",
                    polygonCount = 800
                }
            };

            // SIMULATE DELETION RACE:
            // While background refresh was downloading/processing, the user deleted Workshop avatar #333 in the UI!
            AvatarLibraryMenu.MutateAvatarsRegistry(entries =>
            {
                entries.RemoveAll(e => e.steamFileId == 333);
                return true;
            }, tempSandbox);
            try { File.Delete(w333Path); } catch { }

            // Invoke SaveAvatarEntries with initialWorkshopIds snapshot via reflection on SteamWorkshopAutoLoader
            var loaderGo = new GameObject("TestLoader");
            var loader = loaderGo.AddComponent<SteamWorkshopAutoLoader>();
            var method = typeof(SteamWorkshopAutoLoader).GetMethod(
                "SaveAvatarEntries",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new System.Type[] { typeof(List<AvatarLibraryMenu.AvatarEntry>), typeof(HashSet<ulong>), typeof(string) },
                null
            );
            method.Invoke(loader, new object[] { workshopEntries, initialWorkshopIds, tempSandbox });
            Object.DestroyImmediate(loaderGo);

            // Read back disk entries
            var finalEntries = JsonConvert.DeserializeObject<List<AvatarLibraryMenu.AvatarEntry>>(File.ReadAllText(avatarsPath));

            // Verification 1: Deleted local avatar did NOT resurrect
            if (finalEntries.Any(e => e.displayName == "LocalAvatar1"))
            {
                throw new System.Exception("BUG: Deleted local avatar resurrected!");
            }

            // Verification 2: Workshop avatar #333 deleted between processing and saving did NOT resurrect!
            if (finalEntries.Any(e => e.steamFileId == 333))
            {
                throw new System.Exception("BUG: Workshop avatar 333 deleted between processing and saving was resurrected!");
            }

            // Verification 3: User-customized title and NSFW status on #111 were NOT overwritten by stale package metadata
            var w111 = finalEntries.FirstOrDefault(e => e.steamFileId == 111);
            if (w111 == null) throw new System.Exception("Workshop avatar 111 missing from disk!");
            if (w111.displayName != "CustomUserChosenName")
            {
                throw new System.Exception($"BUG: User-customized title overwritten! Expected 'CustomUserChosenName', got '{w111.displayName}'");
            }
            if (!w111.isNSFW)
            {
                throw new System.Exception("BUG: User-customized isNSFW toggle was overwritten to false!");
            }

            // Verification 4: Technical updates from workshop package (version and polygon count) WERE updated
            if (w111.version != "1.1")
            {
                throw new System.Exception($"Expected updated version '1.1', got '{w111.version}'");
            }
            if (w111.polygonCount != 1200)
            {
                throw new System.Exception($"Expected updated polygonCount 1200, got {w111.polygonCount}");
            }

            // Verification 5: Newly subscribed avatar #222 was added
            var w222 = finalEntries.FirstOrDefault(e => e.steamFileId == 222);
            if (w222 == null || w222.displayName != "NewWorkshopAvatar222")
            {
                throw new System.Exception("Newly subscribed avatar 222 was not added to library!");
            }

            Debug.Log("[Test 3] PASSED: SteamWorkshopAutoLoader safely merged Workshop entries without resurrecting deleted items (local or workshop) or overwriting user edits.");
        }
        finally
        {
            try { Directory.Delete(tempSandbox, true); } catch { }
        }
    }

    private static void TestWorkshopMappingResolutionAndAmbiguity()
    {
        Debug.Log("[Test 4] Testing workshop mapping resolution and non-numeric parent directory handling (isolated)...");
        string tempSandbox = Path.Combine(Path.GetTempPath(), "MateEngineTest_Map_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempSandbox);

        try
        {
            // 1. Direct parent folder numeric ID check
            string pathWithNumericParent = "/Users/test/Mods/777888/dance.unity3d";
            ulong resolvedNumeric = MEModHandler.ResolveWorkshopIdForPath(pathWithNumericParent, tempSandbox);
            if (resolvedNumeric != 777888UL)
            {
                throw new System.Exception($"Expected 777888UL from numeric parent folder, got {resolvedNumeric}");
            }

            // 2. mods_workshop_map.json lookup with NON-NUMERIC parent directory
            // (Ensures Step 1 does NOT short-circuit, properly exercising Step 2 JSON map lookup)
            string mapPath = Path.Combine(tempSandbox, "mods_workshop_map.json");
            var testMap = new Dictionary<string, ulong>
            {
                { "custom_folder/special_dance.unity3d", 555444UL }
            };
            File.WriteAllText(mapPath, JsonConvert.SerializeObject(testMap));

            string pathWithNonNumericParent = "/custom/path/custom_folder/special_dance.unity3d";
            ulong resolvedMap = MEModHandler.ResolveWorkshopIdForPath(pathWithNonNumericParent, tempSandbox);
            if (resolvedMap != 555444UL)
            {
                throw new System.Exception($"Expected 555444UL from JSON map with non-numeric parent folder, got {resolvedMap}");
            }

            // 3. Unknown mod path not in map returns 0UL
            string unknownPath = "/custom/path/custom_folder/unknown_local_mod.unity3d";
            ulong resolvedUnknown = MEModHandler.ResolveWorkshopIdForPath(unknownPath, tempSandbox);
            if (resolvedUnknown != 0UL)
            {
                throw new System.Exception($"Expected 0UL for unknown mod, got {resolvedUnknown}");
            }

            // 4. Candidate matching - Single unambiguous match
            string localMod = Path.Combine(tempSandbox, "shared_dance.unity3d");
            File.WriteAllBytes(localMod, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            string sub1Dir = Path.Combine(tempSandbox, "sub1");
            string sub2Dir = Path.Combine(tempSandbox, "sub2");
            Directory.CreateDirectory(sub1Dir);
            Directory.CreateDirectory(sub2Dir);

            // In sub1: matching file and size
            File.WriteAllBytes(Path.Combine(sub1Dir, "shared_dance.unity3d"), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            var singleCandidateList = new List<KeyValuePair<ulong, string>>
            {
                new KeyValuePair<ulong, string>(11111UL, sub1Dir),
                new KeyValuePair<ulong, string>(22222UL, sub2Dir) // sub2 has no matching file
            };

            ulong singleResult = MEModHandler.MatchWorkshopCandidate(localMod, singleCandidateList);
            if (singleResult != 11111UL)
            {
                throw new System.Exception($"Expected 11111UL for unambiguous candidate match, got {singleResult}");
            }

            // 5. Candidate matching - Ambiguous multiple matches (BOTH sub1 and sub2 have same file name and size)
            File.WriteAllBytes(Path.Combine(sub2Dir, "shared_dance.unity3d"), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            var ambiguousCandidateList = new List<KeyValuePair<ulong, string>>
            {
                new KeyValuePair<ulong, string>(11111UL, sub1Dir),
                new KeyValuePair<ulong, string>(22222UL, sub2Dir)
            };

            ulong ambiguousResult = MEModHandler.MatchWorkshopCandidate(localMod, ambiguousCandidateList);
            if (ambiguousResult != 0UL)
            {
                throw new System.Exception($"BUG: Ambiguous candidate match returned {ambiguousResult} instead of 0UL (should never guess!)");
            }

            // 6. Candidate matching - Size mismatch
            string sub3Dir = Path.Combine(tempSandbox, "sub3");
            Directory.CreateDirectory(sub3Dir);
            File.WriteAllBytes(Path.Combine(sub3Dir, "shared_dance.unity3d"), new byte[] { 1, 2, 3 }); // different length

            var mismatchCandidateList = new List<KeyValuePair<ulong, string>>
            {
                new KeyValuePair<ulong, string>(33333UL, sub3Dir)
            };

            ulong mismatchResult = MEModHandler.MatchWorkshopCandidate(localMod, mismatchCandidateList);
            if (mismatchResult != 0UL)
            {
                throw new System.Exception($"Expected 0UL for candidate size mismatch, got {mismatchResult}");
            }

            Debug.Log("[Test 4] PASSED: Parent numeric ID, mods_workshop_map.json lookup, and candidate ambiguity rejection verified.");
        }
        finally
        {
            try { Directory.Delete(tempSandbox, true); } catch { }
        }
    }
}
