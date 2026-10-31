using SafetyProto.Runtime.Feedback;
using SafetyProto.Runtime.PPE;
using SafetyProto.Runtime.Scaffolding;
using UnityEngine;

namespace SafetyProto.Runtime.Practice
{
    /// <summary>
    /// Marks an object as a practice prop: the dashboard's highlight catalog and the hint coach
    /// leave it out, and the practice area puts it back where it started each time it opens and
    /// closes. A scaffold piece or a lanyard on the same object reports its interaction (installed,
    /// connected) to the open practice area.
    /// </summary>
    public sealed class PracticeProp : MonoBehaviour, IPracticeResettable
    {
        [Tooltip("Interaction id this prop reports when it is installed or connected; empty for none.")]
        [SerializeField] private string interactionId;

        private ScaffoldPieceInstaller _installer;
        private RetractableLanyardController _lanyard;
        private ReturnObjectHome _home;
        private PracticeSnapItem _snapItem;

        /// <summary>Whether <paramref name="target"/> belongs to the practice area.</summary>
        public static bool IsPractice(Component target) =>
            target != null && (target.GetComponentInParent<PracticeProp>(true) != null
                               || target.GetComponentInParent<PracticeArea>(true) != null);

        private void Awake()
        {
            _installer = GetComponent<ScaffoldPieceInstaller>();
            _lanyard = GetComponent<RetractableLanyardController>();
            _home = GetComponent<ReturnObjectHome>();
            _snapItem = GetComponent<PracticeSnapItem>();
            if (_installer != null) _installer.onInstalled.AddListener(Done);
            if (_lanyard != null) _lanyard.onLanyardLocked.AddListener(_ => Done());
        }

        private void OnDestroy()
        {
            if (_installer != null) _installer.onInstalled.RemoveListener(Done);
        }

        /// <summary>Reports this prop's interaction to the open practice area.</summary>
        public void Done()
        {
            if (PracticeArea.Active != null) PracticeArea.Active.MarkDone(interactionId);
        }

        public void ResetForPractice()
        {
            if (_installer != null) _installer.ResetInstall();
            if (_lanyard != null) _lanyard.ResetSession();
            if (_snapItem != null) _snapItem.ResetForPractice();
            if (_home != null) _home.ResetSession();
        }
    }
}
