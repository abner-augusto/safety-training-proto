using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Utils;
using UnityEngine;
using SafetyProto.Core.Logging;

namespace SafetyProto.Runtime.PPE
{
    public class PPEManager : MonoBehaviour, ISessionResettable
    {
        [SerializeField]
        [Tooltip("Optional proximity check radius to validate worn PPE is still near the snap zone reference.")]
        private float complianceDistance = 0.75f;

        private readonly Dictionary<PPEType, GameObject> _wornPPE = new Dictionary<PPEType, GameObject>();
        private Transform _playerTransform;

        private void Start()
        {
            if (!this.IsEventBusReady())
            {
                enabled = false;
                return;
            }
            _playerTransform = Camera.main != null ? Camera.main.transform : null;
        }

        public void ReportPPEStateChange(PPEType ppeType, bool isNowInsideZone, GameObject ppeObject)
        {
            bool previouslyWorn = _wornPPE.ContainsKey(ppeType);
            _wornPPE.TryGetValue(ppeType, out GameObject currentWornObject);

            if (isNowInsideZone)
            {
                if (!previouslyWorn || currentWornObject != ppeObject)
                {
                    _wornPPE[ppeType] = ppeObject;
                    PPEEvents.RaisePpeStateChanged(new PPEStateChangedEventArgs(ppeType, true));
                    SafetyLog.Info($"PPEManager: {ppeType} is now WORN (Item: {ppeObject.name}).", this);
                }
            }
            else if (previouslyWorn && currentWornObject == ppeObject)
            {
                _wornPPE.Remove(ppeType);
                PPEEvents.RaisePpeStateChanged(new PPEStateChangedEventArgs(ppeType, false));
                SafetyLog.Info($"PPEManager: {ppeType} is now NOT WORN (Item: {ppeObject.name} exited).", this);
            }
        }

        public void UnregisterIfOwned(PPEType ppeType, GameObject ppeObject)
        {
            if (ppeType == PPEType.None || ppeObject == null)
            {
                return;
            }

            if (_wornPPE.TryGetValue(ppeType, out var current) && current == ppeObject)
            {
                _wornPPE.Remove(ppeType);
                PPEEvents.RaisePpeStateChanged(new PPEStateChangedEventArgs(ppeType, false));
            }
        }

        public bool IsWearing(PPEType ppeType)
        {
            return _wornPPE.ContainsKey(ppeType);
        }

        public bool AreAllRequiredPPEWorn(List<PPEType> requiredPPEList)
        {
            if (requiredPPEList == null || requiredPPEList.Count == 0)
            {
                return true;
            }

            foreach (PPEType ppe in requiredPPEList)
            {
                if (!IsWearing(ppe))
                {
                    return false;
                }
            }
            return true;
        }

        // Checks compliance and evicts any PPE that has drifted too far from the player.
        // Callers should expect this to modify worn PPE state as a side effect.
        public bool CheckAndEvictPPECompliance(List<PPEType> requiredPpe)
        {
            if (requiredPpe == null || requiredPpe.Count == 0)
                return true;

            bool allValid = true;
            foreach (var ppe in requiredPpe)
            {
                if (!_wornPPE.TryGetValue(ppe, out var obj) || obj == null)
                {
                    allValid = false;
                    continue;
                }

                var referencePos = _playerTransform != null ? _playerTransform.position : transform.position;
                if (complianceDistance > 0f && obj.activeInHierarchy &&
                    Vector3.Distance(referencePos, obj.transform.position) > complianceDistance)
                {
                    _wornPPE.Remove(ppe);
                    PPEEvents.RaisePpeStateChanged(new PPEStateChangedEventArgs(ppe, false));
                    allValid = false;
                }
            }

            return allValid;
        }

        public void ResetSession()
        {
            _wornPPE.Clear();
        }
    }
}
