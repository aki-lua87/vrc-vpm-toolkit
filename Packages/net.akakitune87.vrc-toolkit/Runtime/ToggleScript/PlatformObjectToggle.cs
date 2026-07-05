
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PlatformObjectToggle : UdonSharpBehaviour
{
    [Header("VRモード: 有効化するオブジェクト")]
    public GameObject[] vrEnableObjects;

    [Header("VRモード: 無効化するオブジェクト")]
    public GameObject[] vrDisableObjects;

    [Header("デスクトップモード: 有効化するオブジェクト")]
    public GameObject[] desktopEnableObjects;

    [Header("デスクトップモード: 無効化するオブジェクト")]
    public GameObject[] desktopDisableObjects;

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (!player.isLocal) return;

        ApplyToggle(player.IsUserInVR());
    }

    private void ApplyToggle(bool isVR)
    {
        GameObject[] toEnable = isVR ? vrEnableObjects : desktopEnableObjects;
        GameObject[] toDisable = isVR ? vrDisableObjects : desktopDisableObjects;

        for (int i = 0; i < toEnable.Length; i++) if (toEnable[i] != null) toEnable[i].SetActive(true);
        for (int i = 0; i < toDisable.Length; i++) if (toDisable[i] != null) toDisable[i].SetActive(false);
    }
}
