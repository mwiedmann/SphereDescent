using UnityEngine;

namespace SphereDescent
{
    public class PlayProbe : MonoBehaviour
    {
        public static bool Done;

        void Start() => gameObject.AddComponent<VideoPilot>();
    }
}
