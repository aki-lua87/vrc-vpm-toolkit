
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class AreaInsideToggleObjects : UdonSharpBehaviour
{
    public GameObject[] targets;

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!Networking.LocalPlayer.Equals(player)) return;
        foreach (GameObject target in targets)
        {
            if (target == null) continue;
            target.SetActive(true);
        }
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (!Networking.LocalPlayer.Equals(player)) return;
        foreach (GameObject target in targets)
        {
            if (target == null) continue;
            target.SetActive(false);
        }
    }
}
