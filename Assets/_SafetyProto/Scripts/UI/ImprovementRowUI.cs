using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SafetyProto.UI
{
    public class ImprovementRowUI : MonoBehaviour
    {
        [SerializeField] private Image bulletIcon;
        [SerializeField] private TMP_Text improvementText;

        public void Setup(string message)
        {
            improvementText.text = message;
        }
    }
}
