using UnityEngine;
using UnityEngine.UI;
using Steamworks;
using System.IO;
using System.Collections;
using System.Linq;

public class ModRemoveButton : MonoBehaviour
{
    public Button button;
    public string filePath;
    public ulong workshopId;

    void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (button != null) button.onClick.AddListener(OnClick);
    }

    void OnClick()
    {
        ulong id = workshopId;
        if (id == 0UL) id = ResolveWorkshopIdForPath(filePath);
        if (id != 0UL && SteamManager.Initialized)
        {
            var h = SteamWorkshopHandler.Instance;
            if (h != null) h.UnsubscribeAndDelete(new PublishedFileId_t(id));
        }

        var modHandler = FindAnyObjectByType<MEModHandler>();
        if (modHandler != null)
        {
            modHandler.RemoveModByPath(filePath, this.transform.parent != null ? this.transform.parent.gameObject : this.gameObject);
        }
        else
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }
            }
            var dance = FindAnyObjectByType<CustomDancePlayer.AvatarDanceHandler>();
            if (dance != null) dance.RescanMods();
        }
    }

    ulong ResolveWorkshopIdForPath(string localPath)
    {
        return MEModHandler.ResolveWorkshopIdForPath(localPath);
    }
}
