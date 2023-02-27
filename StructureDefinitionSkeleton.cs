
namespace fhir_invariant_tester
{
    internal class StructureDefinitionSkeleton
    {
        public string Filename { get; set; }
        public string ResourceType { get; set; }
        public string? CanonicalUrl { get; set; }
        public bool IsProfile { get; set; }
        public bool IsDataType { get; set; }

        public List<InvariantSkeleton> Invariants { get; } = new List<InvariantSkeleton>();

        public void ResetStats()
        {
            foreach (var inv in Invariants)
            {
                inv.successCount = 0;
                inv.failCount = 0;
                inv.errorCount = 0;
            }
        }
    }

    internal class InvariantSkeleton
    {
        public string key { get; set; }
        public string severity { get; set; }
        public string context { get; set; }
        public string expression { get; set; }
        public int successCount { get; set; }
        public int failCount { get; set; }
        public int errorCount { get; set; }
    }
}
