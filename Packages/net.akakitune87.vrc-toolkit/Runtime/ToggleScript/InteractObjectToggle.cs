
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class InteractObjectToggle : UdonSharpBehaviour
{
    [SerializeField] private GameObject[] targets;

    [Tooltip("On: 全員に同期されるグローバル動作。Off: 自分だけに反映されるローカル動作。")]
    [SerializeField] private bool isGlobal = true;

    [UdonSynced] private bool _isActive = false;

    public override void Interact()
    {
        _isActive = !_isActive;

        if (isGlobal)
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            RequestSerialization();
        }

        ApplyState();
    }

    public override void OnDeserialization()
    {
        ApplyState();
    }

    private void ApplyState()
    {
        foreach (var target in targets)
        {
            if (target != null)
                target.SetActive(_isActive);
        }
    }
}
