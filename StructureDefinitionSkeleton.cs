using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace fhir_invariant_tester
{
    internal class StructureDefinitionSkeleton
    {
        public string Filename { get; set; }
        public string ResourceType { get; set; }
        public string? CanonicalUrl { get; set; }

        public List<InvariantSkeleton> Invariants { get; } = new List<InvariantSkeleton>();
    }

    internal class InvariantSkeleton
    {
        public string key { get; set; }
        public string context { get; set; }
        public string expression { get; set; }
    }
}
