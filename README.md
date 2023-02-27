# fhir-invariant-tester
This utility can be used to test all sample files in the fhir specification against 
invariants defined in their core StructureDefinition.

The only commandline parameter to the tool is the path to the core specification git repo loaded locally.
You must have sucessfully build the spec in order to run the tester as it uses
the snapshots in the structure definitions in the publish folder to provide type
information to the FHIRPath engine.

It will look inside the following folders:
* `publish/*.profile.xml` - StructureDefinitions generated containing snapshots
* `source/(resourceType)/(resourceType-*.xml)` - any example files (filters other known spec generation source files)
* `source/(resourceType)/invariant-tests/(invariantKey).[*.](pass|fail).(xml|json)` - test files that pass or fail the specified invariant

``` cmd
> dotnet fhir-invariant-tester /git/hl7/fhir
```

The output reports the list of files that were tested, output from any invariants that didn't evaluate as anticipated.

Then a simple list of the invariants and pass/fail/unexpected outcome.

> Note: at this stage there are 2 known issues with this implementation, it does not 
have the terminology `memberOf` function implemented (so get a few errors with that) and the 
`evv-2` invariant has no expression, is known to be invalid.
The implementation of the `resolve()` method only resolves a contained resource or fakes the existence of the resource
with the ID specified if it's there. (really only suitable when you want to test it's type as is done with search parameters)
