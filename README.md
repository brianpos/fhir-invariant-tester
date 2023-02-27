# fhir-invariant-tester
This utility can be used to test all sample files in the fhir specification against 
invariants defined in their core StructureDefinition.

The commandline parameter to the tool is the path to the core specification git repo loaded locally.
You must have sucessfully build the spec in order to run the tester as it uses
the snapshots in the structure definitions in the publish folder to provide type
information to the FHIRPath engine. 
(and if you update your invariant to resolve an issue, rebuild the spec locally before rechecking tests)

A second optional parameter is to be able to filter down to just a single resource type
(if you provide this optional resource type value, and there is no `invariant-tests` folder, 
then the tool will create it and copy in default test files for each invariant to edit)

It will look inside the following folders:
* `publish/*.profile.xml` - StructureDefinitions generated containing snapshots
* `source/(resourceType)/(resourceType-*.xml)` - any example files (filters other known spec generation source files)
* `source/(resourceType)/invariant-tests/(invariantKey).[*.](pass|fail).(xml|json)` - test files that pass or fail the specified invariant

``` cmd
> dotnet run --project fhir-invariant-tester.csproj E:\\git\\HL7\\take3-core-r5 Questionnaire
```

The output reports the list of files that were tested, output from any invariants that didn't evaluate as anticipated.

Then a simple list of the invariants and pass/fail/unexpected outcome.

Once the results are displayed the utility will monitor the files that it processed for changes to the examples/unit tests and will 
re-evaluate the resource type if a change is detected.
Press Ctrl+C to close the application/stop tracking the files.

``` txt
  RiskAssessment        ras-2(error)    10/0/2
  RiskAssessment        ras-1(error)    0/0/0
  SearchParameter       cnl-0(warning)  5/0/0
  SearchParameter       spd-1(error)    5/0/0
  SearchParameter       spd-2(error)    5/0/0
  SearchParameter       spd-3(error)    5/0/0
  SearchParameter       cnl-1(warning)  5/0/0
  ServiceRequest        bdystr-1(error) 18/0/0
  ServiceRequest        prr-1(error)    18/0/0
  CodeSystem            scs-1(error)    0/0/0     (profile - http://hl7.org/fhir/StructureDefinition/shareablecodesystem)
  CodeSystem            scs-2(error)    0/0/0     (profile - http://hl7.org/fhir/StructureDefinition/shareablecodesystem)
```

## Known Issues
* the terminology `memberOf` function is not implemented (so get a few errors with that)
* the `evv-2` invariant has no expression, is known to be invalid.
* the `resolve()` method only resolves a contained resource or fakes the existence of the resource
with the ID specified if it's there. (really only suitable when you want to test it's type as is done with search parameters)
* profile invariants are not tested (though it does detect them but reports no results - future work)

## Installing dotnet7 if you don't have it
(linux)
https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu

(macOC)
https://learn.microsoft.com/en-us/dotnet/core/install/macos
