using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Events;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Scoring;
using SafetyProto.Runtime.Task;
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

        private readonly HashSet<string> _chargedDistractors = new HashSet<string>();

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
                return true;

            foreach (PPEType ppe in requiredPPEList)
            {
                if (!_wornPPE.TryGetValue(ppe, out var obj) || obj == null)
                    return false;
            }
            return true;
        }

        /// <summary>Marks as not worn any listed PPE that has drifted beyond the compliance distance.</summary>
        public void EvictDistantPPE(List<PPEType> ppeList)
        {
            if (ppeList == null || ppeList.Count == 0 || complianceDistance <= 0f)
                return;

            var referencePos = _playerTransform != null ? _playerTransform.position : transform.position;
            foreach (var ppe in ppeList)
            {
                if (!_wornPPE.TryGetValue(ppe, out var obj) || obj == null)
                    continue;

                if (obj.activeInHierarchy &&
                    Vector3.Distance(referencePos, obj.transform.position) > complianceDistance)
                {
                    _wornPPE.Remove(ppe);
                    PPEEvents.RaisePpeStateChanged(new PPEStateChangedEventArgs(ppe, false));
                }
            }
        }

        /// <summary>
        /// Called by PPESnapSlot when a distractor item tries to snap. Always logs a
        /// WRONG_PPE_SELECTED violation (selection among decoys is a primary retention
        /// measure); charges the minor-tier base penalty only on the first attempt of
        /// each unique item, so fumbling the same decoy repeatedly cannot spiral.
        /// </summary>
        public void ReportDistractorAttempt(PPEType ppeType, string itemName)
        {
            var scoring = TaskManager.Instance != null ? TaskManager.Instance.Scoring : ScoringConfig.Default;
            IncidentalChargePolicy.ChargeWrongPpeSelected(
                EventBus.Instance, ScoreService.Instance, scoring,
                GetPpeLabel(ppeType), itemName, firstAttemptForItem: TryChargeDistractor(itemName));
        }

        private bool TryChargeDistractor(string itemName) => _chargedDistractors.Add(itemName);

        private static string GetPpeLabel(PPEType ppeType)
        {
            return ppeType switch
            {
                PPEType.Helmet => "Capacete",
                PPEType.Goggles => "Óculos de proteção",
                PPEType.Harness => "Cinto paraquedista",
                PPEType.Vest => "Colete de segurança",
                PPEType.Boots => "Botina de segurança",
                PPEType.GloveLeft => "Luva esquerda",
                PPEType.GloveRight => "Luva direita",
                _ => "EPI não identificado"
            };
        }

        public void ResetSession()
        {
            _wornPPE.Clear();
            _chargedDistractors.Clear();
        }
    }
}
