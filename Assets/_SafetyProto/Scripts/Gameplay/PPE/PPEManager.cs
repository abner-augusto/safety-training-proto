using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Data.Enums;
using SafetyProto.Utils;
using UnityEngine;

namespace SafetyProto.Gameplay.PPE
{
    public class PPEManager : MonoBehaviour, ISessionResettable
    {
        [SerializeField]
        [Tooltip("Optional proximity check radius to validate worn PPE is still near the snap zone reference.")]
        private float complianceDistance = 0.75f;

        private readonly Dictionary<PPEType, GameObject> _wornPPE = new Dictionary<PPEType, GameObject>();

        private void Start()
        {
            this.IsEventBusReady();
        }

        public void ReportPPEStateChange(PPEType ppeType, bool isNowInsideZone, GameObject ppeObject)
        {
            bool previouslyWorn = _wornPPE.ContainsKey(ppeType);
            GameObject currentWornObject = previouslyWorn ? _wornPPE[ppeType] : null;

            if (isNowInsideZone)
            {
                if (!previouslyWorn || currentWornObject != ppeObject)
                {
                    _wornPPE[ppeType] = ppeObject;
                    EventBus.Instance.RaisePpeStateChanged(new PPEStateChangedEventArgs(ppeType, true));
                    Debug.Log($"PPEManager: {ppeType} is now WORN (Item: {ppeObject.name}).");
                }
            }
            else
            {
                if (previouslyWorn && currentWornObject == ppeObject)
                {
                    _wornPPE.Remove(ppeType);
                    EventBus.Instance.RaisePpeStateChanged(new PPEStateChangedEventArgs(ppeType, false));
                    Debug.Log($"PPEManager: {ppeType} is now NOT WORN (Item: {ppeObject.name} exited).");
                }
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
                EventBus.Instance.RaisePpeStateChanged(new PPEStateChangedEventArgs(ppeType, false));
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

        public bool VerifyPPECompliance(List<PPEType> requiredPpe)
        {
            if (requiredPpe == null || requiredPpe.Count == 0)
            {
                return true;
            }

            bool allValid = true;
            foreach (var ppe in requiredPpe)
            {
                if (!_wornPPE.TryGetValue(ppe, out var obj) || obj == null)
                {
                    allValid = false;
                    continue;
                }

                if (!obj.activeInHierarchy)
                {
                    _wornPPE.Remove(ppe);
                    allValid = false;
                    continue;
                }

                if (complianceDistance > 0f && Vector3.Distance(transform.position, obj.transform.position) > complianceDistance)
                {
                    _wornPPE.Remove(ppe);
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
