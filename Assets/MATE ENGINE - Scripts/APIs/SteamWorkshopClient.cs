using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;

namespace MateEngine.Workshop
{
    public class SteamWorkshopClient : MonoBehaviour
    {
        public static SteamWorkshopClient Instance { get; private set; }

        private const string AppId = "3625270";
        private const string BaseBrowseUrl = "https://steamcommunity.com/workshop/browse/";
        
        private readonly Dictionary<string, Texture2D> memoryThumbnailCache = new Dictionary<string, Texture2D>();
        private string diskThumbnailCacheDir => Path.Combine(Application.temporaryCachePath, "WorkshopThumbCache");

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);

            try
            {
                if (!Directory.Exists(diskThumbnailCacheDir))
                    Directory.CreateDirectory(diskThumbnailCacheDir);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SteamWorkshopClient] Could not create thumb cache dir: " + ex.Message);
            }
        }

        public void QueryWorkshop(
            int page,
            WorkshopCategory category,
            WorkshopSort sort,
            string searchText,
            Action<WorkshopQueryResult> onComplete)
        {
            StartCoroutine(QueryWorkshopRoutine(page, category, sort, searchText, onComplete));
        }

        private IEnumerator QueryWorkshopRoutine(
            int page,
            WorkshopCategory category,
            WorkshopSort sort,
            string searchText,
            Action<WorkshopQueryResult> onComplete)
        {
            var result = new WorkshopQueryResult { currentPage = Mathf.Max(1, page) };

            string sortStr = "trend";
            switch (sort)
            {
                case WorkshopSort.Trend: sortStr = "trend"; break;
                case WorkshopSort.MostRecent: sortStr = "mostrecent"; break;
                case WorkshopSort.TotalSubscribers: sortStr = "totaluniquesubscribers"; break;
                case WorkshopSort.Starred: sortStr = "starred"; break;
            }

            string url = $"{BaseBrowseUrl}?appid={AppId}&browsesort={sortStr}&section=readytouseitems&p={result.currentPage}";

            if (!string.IsNullOrEmpty(searchText))
            {
                url += $"&searchtext={UnityWebRequest.EscapeURL(searchText.Trim())}";
            }

            if (category != WorkshopCategory.All)
            {
                string tag = category.ToString();
                url += $"&requiredtags%5B%5D={UnityWebRequest.EscapeURL(tag)}";
            }

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = 15;
                req.SetRequestHeader("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    result.errorMessage = $"Network error ({req.responseCode}): {req.error}";
                    onComplete?.Invoke(result);
                    yield break;
                }

                string html = req.downloadHandler.text;
                if (string.IsNullOrEmpty(html))
                {
                    result.errorMessage = "Empty response received from Steam Workshop.";
                    onComplete?.Invoke(result);
                    yield break;
                }

                try
                {
                    ParseWorkshopHtml(html, result);
                }
                catch (Exception ex)
                {
                    result.errorMessage = "Failed to parse workshop data: " + ex.Message;
                    Debug.LogWarning("[SteamWorkshopClient] Parse error: " + ex);
                }

                onComplete?.Invoke(result);
            }
        }

        private void ParseWorkshopHtml(string html, WorkshopQueryResult result)
        {
            // Steam SSR data is injected as JSON inside <script id="valve-ssr-data">
            var regex = new Regex(@"<script[^>]*id=""valve-ssr-data""[^>]*>(.*?)</script>", RegexOptions.Singleline);
            var match = regex.Match(html);

            if (!match.Success)
            {
                result.errorMessage = "Could not locate Steam workshop catalog data on page.";
                return;
            }

            string jsonStr = match.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(jsonStr))
            {
                result.errorMessage = "Steam workshop catalog data is empty.";
                return;
            }

            JObject root = JObject.Parse(jsonStr);
            var queryDataToken = root.SelectToken("renderContext.queryData");
            if (queryDataToken == null)
            {
                result.errorMessage = "Missing renderContext.queryData in Steam payload.";
                return;
            }

            string queryDataRaw = queryDataToken.ToString();
            JObject queryData = JObject.Parse(queryDataRaw);

            JArray queries = queryData["queries"] as JArray;
            if (queries == null)
            {
                result.errorMessage = "No queries found in Steam workshop payload.";
                return;
            }

            JObject foundStateData = null;
            foreach (var q in queries)
            {
                var stateToken = q["state"];
                if (stateToken is JObject stateObj)
                {
                    var dataToken = stateObj["data"];
                    if (dataToken is JObject dataObj && dataObj["results"] is JArray)
                    {
                        foundStateData = dataObj;
                        break;
                    }
                }
            }

            if (foundStateData == null)
            {
                result.totalCount = 0;
                result.totalPages = 1;
                result.items = new List<WorkshopItem>();
                return;
            }

            result.totalCount = foundStateData.Value<int?>("total_count") ?? 0;
            result.totalPages = Mathf.Max(1, foundStateData.Value<int?>("total_pages") ?? 1);
            result.currentPage = Mathf.Max(1, foundStateData.Value<int?>("current_page") ?? result.currentPage);

            JArray resultsArray = foundStateData["results"] as JArray;
            if (resultsArray != null)
            {
                foreach (var itemToken in resultsArray)
                {
                    var item = new WorkshopItem
                    {
                        id = itemToken.Value<string>("publishedfileid") ?? "",
                        title = CleanTitle(itemToken.Value<string>("title") ?? "Untitled"),
                        previewUrl = itemToken.Value<string>("preview_url") ?? "",
                        shortDescription = itemToken.Value<string>("short_description") ?? "",
                        subscriptions = itemToken.Value<int?>("subscriptions") ?? 0,
                        views = itemToken.Value<int?>("views") ?? 0,
                        votes = itemToken.Value<int?>("total_votes") ?? 0
                    };

                    // File size
                    long sizeBytes = 0;
                    var sizeVal = itemToken["file_size"];
                    if (sizeVal != null)
                    {
                        long.TryParse(sizeVal.ToString(), out sizeBytes);
                    }
                    item.fileSizeBytes = sizeBytes;
                    item.fileSizeFormatted = FormatBytes(sizeBytes);

                    // Tags
                    var tagsArray = itemToken["tags"] as JArray;
                    if (tagsArray != null)
                    {
                        foreach (var tagToken in tagsArray)
                        {
                            string tagName = tagToken.Value<string>("tag");
                            if (!string.IsNullOrEmpty(tagName))
                                item.tags.Add(tagName);
                        }
                    }

                    // Author / creator from short_description if present (e.g. "Author: ...")
                    if (!string.IsNullOrEmpty(item.shortDescription))
                    {
                        var authorMatch = Regex.Match(item.shortDescription, @"Author:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                        if (authorMatch.Success)
                            item.author = authorMatch.Groups[1].Value.Trim();
                    }

                    result.items.Add(item);
                }
            }
        }

        private string CleanTitle(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "Untitled";
            // Unescape unicode or html entities
            string clean = raw.Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&lt;", "<").Replace("&gt;", ">");
            return clean.Trim();
        }

        private string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "";
            if (bytes < 1024 * 1024)
                return $"{(bytes / 1024f):F1} KB";
            return $"{(bytes / (1024f * 1024f)):F1} MB";
        }

        public void LoadThumbnail(string url, Action<Texture2D> onLoaded)
        {
            if (string.IsNullOrEmpty(url))
            {
                onLoaded?.Invoke(null);
                return;
            }

            if (memoryThumbnailCache.TryGetValue(url, out Texture2D cachedTex) && cachedTex != null)
            {
                onLoaded?.Invoke(cachedTex);
                return;
            }

            StartCoroutine(LoadThumbnailRoutine(url, onLoaded));
        }

        private IEnumerator LoadThumbnailRoutine(string url, Action<Texture2D> onLoaded)
        {
            string diskFileName = HashUrl(url) + ".png";
            string diskPath = Path.Combine(diskThumbnailCacheDir, diskFileName);

            // Check disk cache first
            if (File.Exists(diskPath))
            {
                byte[] diskBytes = null;
                try { diskBytes = File.ReadAllBytes(diskPath); } catch { }

                if (diskBytes != null && diskBytes.Length > 0)
                {
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(diskBytes))
                    {
                        memoryThumbnailCache[url] = tex;
                        onLoaded?.Invoke(tex);
                        yield break;
                    }
                }
            }

            // Download from web
            using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
            {
                req.timeout = 10;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    Texture2D downloadedTex = DownloadHandlerTexture.GetContent(req);
                    if (downloadedTex != null)
                    {
                        memoryThumbnailCache[url] = downloadedTex;

                        // Save to disk cache asynchronously
                        try
                        {
                            byte[] pngBytes = downloadedTex.EncodeToPNG();
                            if (pngBytes != null)
                                File.WriteAllBytes(diskPath, pngBytes);
                        }
                        catch { }

                        onLoaded?.Invoke(downloadedTex);
                        yield break;
                    }
                }
            }

            onLoaded?.Invoke(null);
        }

        private string HashUrl(string url)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant().Substring(0, 16);
            }
        }
    }
}
