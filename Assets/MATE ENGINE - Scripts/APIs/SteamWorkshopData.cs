using System;
using System.Collections.Generic;

namespace MateEngine.Workshop
{
    public enum WorkshopCategory
    {
        All,
        Avatar,
        Dance,
        Mod
    }

    public enum WorkshopSort
    {
        Trend,                  // Most Popular (Weekly)
        MostRecent,             // Newest
        TotalSubscribers,       // Most Subscribed
        Starred                 // Top Rated
    }

    [Serializable]
    public class WorkshopItem
    {
        public string id = "";
        public string title = "";
        public string author = "Workshop";
        public string previewUrl = "";
        public long fileSizeBytes = 0;
        public string fileSizeFormatted = "";
        public List<string> tags = new List<string>();
        public int votes = 0;
        public int subscriptions = 0;
        public int views = 0;
        public string description = "";
        public string shortDescription = "";
        
        public bool isInstalled = false;
        public bool isDownloading = false;
        public float downloadProgress = 0f;
        public string installedFilePath = "";

        public bool IsAvatar => tags.Contains("Avatar") || tags.Contains("VRM0") || tags.Contains("VRM1") || (!IsDance && !IsMod);
        public bool IsDance => tags.Contains("Dance");
        public bool IsMod => tags.Contains("Mod") || tags.Contains("Mods");

        public string GetTagSummary()
        {
            if (IsDance) return "Dance";
            if (tags.Contains("VRM1")) return "Avatar (VRM 1.0)";
            if (tags.Contains("VRM0")) return "Avatar (VRM 0.X)";
            if (IsAvatar) return "Avatar";
            if (IsMod) return "Mod";
            return "Item";
        }
    }

    [Serializable]
    public class WorkshopQueryResult
    {
        public List<WorkshopItem> items = new List<WorkshopItem>();
        public int totalCount = 0;
        public int totalPages = 1;
        public int currentPage = 1;
        public string errorMessage = "";
        public bool isSuccess => string.IsNullOrEmpty(errorMessage);
    }
}
