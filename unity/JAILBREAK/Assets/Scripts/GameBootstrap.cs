using Jailbreak.Network;
using UnityEngine;

namespace Jailbreak
{
    /// <summary>
    /// Ensures NetworkManager exists at scene start.
    /// Connection and auth are handled by StartScreenController.
    /// Attach to a persistent GameObject in scenes that need NetworkManager
    /// but don't create it themselves.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            if (NetworkManager.Instance == null)
            {
                var go = new GameObject("NetworkManager");
                go.AddComponent<NetworkManager>();
                DontDestroyOnLoad(go);
            }
        }
    }
}
