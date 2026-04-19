using System.Collections.Generic;
using System.Linq;
using SafetyProto.Core.Interfaces;
using SafetyProto.Data.Enums;
using SafetyProto.Runtime.PPE;

namespace SafetyProto.Runtime.Safety
{
    internal sealed class PPEComplianceAdapter : IPPEComplianceChecker
    {
        private readonly PPEManager _ppeManager;

        public PPEComplianceAdapter(PPEManager ppeManager)
        {
            _ppeManager = ppeManager;
        }

        public bool IsCompliant(IReadOnlyCollection<PPEType> requiredPpe)
        {
            if (requiredPpe == null || requiredPpe.Count == 0) return true;

            var list = requiredPpe is List<PPEType> existing ? existing : requiredPpe.ToList();
            return _ppeManager.VerifyPPECompliance(list);
        }
    }
}
