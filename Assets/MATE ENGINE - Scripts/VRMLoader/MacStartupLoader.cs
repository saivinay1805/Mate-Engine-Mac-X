using UnityEngine;
using System.Collections;

/// <summary>
/// Mac startup: shows a load button if no VRM is saved.
/// Runs independently of scene hierarchy via RuntimeInitializeOnLoadMethod.
/// </summary>
public class MacStartupLoader : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnSceneLoaded()
    {
        // Startup load buttons disabled: MateEngineX launches directly with default/saved avatar
    }
}
