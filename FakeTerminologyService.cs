using Hl7.Fhir.Model;
using Hl7.Fhir.Specification.Terminology;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace fhir_invariant_tester
{
    internal class FakeTerminologyService : ITerminologyService
    {
        public Task<Resource> Closure(Parameters parameters, bool useGet = false)
        {
            throw new NotImplementedException();
        }

        public Task<Parameters> CodeSystemValidateCode(Parameters parameters, string id = null, bool useGet = false)
        {
            throw new NotImplementedException();
        }

        public Task<Resource> Expand(Parameters parameters, string id = null, bool useGet = false)
        {
            throw new NotImplementedException();
        }

        public Task<Parameters> Lookup(Parameters parameters, bool useGet = false)
        {
            throw new NotImplementedException();
        }

        public Task<Parameters> Subsumes(Parameters parameters, string id = null, bool useGet = false)
        {
            throw new NotImplementedException();
        }

        public Task<Parameters> Translate(Parameters parameters, string id = null, bool useGet = false)
        {
            throw new NotImplementedException();
        }

        public Task<Parameters> ValueSetValidateCode(Parameters parameters, string id = null, bool useGet = false)
        {
            var result = new Parameters();
            result.Add("result", new FhirBoolean(true));
            return Task<Parameters>.FromResult(result);
        }
    }
}
