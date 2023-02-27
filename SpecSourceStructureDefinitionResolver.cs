using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification.Source;

namespace fhir_invariant_tester
{
    internal class SpecSourceStructureDefinitionResolver : IResourceResolver
    {
        public SpecSourceStructureDefinitionResolver(List<StructureDefinitionSkeleton> sds)
        {
            _sds = sds;
        }
        List<StructureDefinitionSkeleton> _sds;

        public Resource ResolveByCanonicalUri(string uri)
        {
            var filtered = _sds.Where(sd => sd.CanonicalUrl == uri);
            if (filtered.Any())
            {
                var xml = File.ReadAllText(filtered.First().Filename);
                if (!string.IsNullOrEmpty(xml))
                {
                    var node = FhirXmlNode.Parse(xml);
                    var sd = node.ToPoco<StructureDefinition>(new PocoBuilderSettings() { AllowUnrecognizedEnums = true, IgnoreUnknownMembers = true });
                    return sd;
                }
            }
            return null;
        }

        public Resource ResolveByUri(string uri)
        {
            throw new NotImplementedException();
        }
    }
}
