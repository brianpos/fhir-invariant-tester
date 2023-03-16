using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification;
using Hl7.Fhir.Specification.Source;
using Hl7.FhirPath;
using Hl7.FhirPath.Expressions;
using System.Linq;

namespace fhir_invariant_tester
{
    internal class Program
    {
        static IStructureDefinitionSummaryProvider _provider;
        static CachedResolver _cacheResolver;
        static int Main(string[] args)
        {
            Console.WriteLine("FHIR R5 Invariant tester!");
            Console.WriteLine("---------------------------------------------------------------");
            if (args.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Requires the FHIR specification git folder on the local disk to be provided as an argument");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("Usage:");
                Console.WriteLine("dotnet run --project fhir-invariant-tester.csproj /mnt/e/git/HL7/take3-core-r5");
                Console.WriteLine("Optionally provide the resource Type to filter things");
                Console.WriteLine("dotnet run --project fhir-invariant-tester.csproj /git/hl7/fhir Account");
                return -1;
            }

            // Scan over all the files in the specification folder (arg[0]) to find all the Profile definitions.
            string directory = args[0].TrimEnd('\\').TrimEnd('/');
            string resourceType = null;
            Console.WriteLine($"Testing FHIR source folder:\t{directory}");
            if (args.Length > 1)
            {
                resourceType = args[1];
                Console.WriteLine($"Filtering to resource Type:\t{resourceType}");
            }

            List<StructureDefinitionSkeleton> sds;
            int totalInvariants;
            ScanAllInvariantsInSpecification(Path.Combine(directory, "publish"), out sds, out totalInvariants);

            // now report the number of overall invariants in the spec
            Console.WriteLine("");
            Console.WriteLine("---------------------------------------------------------------");
            Console.WriteLine($"{totalInvariants} in {sds.Count()} resource types");
            Console.WriteLine("---------------------------------------------------------------");
            Console.WriteLine("");

            // now scan all the test files!
            var symbols = new SymbolTable(FhirPathCompiler.DefaultSymbolTable);
            symbols.AddFhirExtensions();
            symbols.Add("resolve", (IEnumerable<ITypedElement> f, EvaluationContext ctx) => f.Select(fi => resolver(fi, ctx)), doNullProp: false);
            static ITypedElement resolver(ITypedElement f, EvaluationContext ctx)
            {
                return ctx is FhirEvaluationContext fctx ? f.Resolve(fctx.ElementResolver) : f.Resolve();
            }
            FhirPathCompiler fpc = new FhirPathCompiler(symbols);

            _cacheResolver = new CachedResolver(new SpecSourceStructureDefinitionResolver(sds));
            _provider = new Hl7.Fhir.Specification.StructureDefinitionSummaryProvider(_cacheResolver);

            // Parse all the files directly in the source folder of each resource type (known succeeding resources).
            var skipFiles = new[] { "Workbook", "div" };
            Dictionary<string, StructureDefinitionSkeleton> monitorFileChanges = new(StringComparer.OrdinalIgnoreCase);
            foreach (var sd in sds)
            {
                ScanExamplesForStructureDefinition(resourceType, sd, directory, skipFiles, fpc, monitorFileChanges);
            }

            Console.WriteLine("");
            Console.WriteLine("---------------------------------------------------------------");
            Console.WriteLine($"Results");
            Console.WriteLine("---------------------------------------------------------------");
            Console.WriteLine("");
            foreach (var sd in sds)
            {
                if (!string.IsNullOrEmpty(resourceType) && sd.ResourceType != resourceType)
                    continue; // commandline has filtered out this resource type
                if (sd.IsDataType) continue;
                ReportSdInvariantStats(sd);
            }

            // Now that we're all done, lets do a file changes monitor...
            Console.WriteLine();
            Console.WriteLine("---------------------------------------------------------------");
            Console.WriteLine($"Tracking...");
            Console.WriteLine("---------------------------------------------------------------");
            System.IO.FileSystemWatcher fsw = new FileSystemWatcher(directory);
            fsw.NotifyFilter = NotifyFilters.Size;
            fsw.IncludeSubdirectories = true;
            fsw.Renamed += (object sender, RenamedEventArgs e) => 
            {
                if (monitorFileChanges.ContainsKey(e.OldName))
                    Console.WriteLine($"   Reprocess {e.OldName} => {e.Name}");
            };
            DateTime? lastChangeTimestamp = null;
            fsw.Changed += (object sender, FileSystemEventArgs e) => 
            {
                if (monitorFileChanges.ContainsKey(e.Name))
                {
                    if (lastChangeTimestamp == new FileInfo(e.FullPath).LastWriteTimeUtc)
                        return;
                    lastChangeTimestamp = new FileInfo(e.FullPath).LastWriteTimeUtc;
                    Thread.Sleep(100);// sleep a little to give the writer time to complete
                    Console.WriteLine("---------------------------------------------------------------");
                    Console.WriteLine($"Detected change to: {e.Name} {e.ChangeType}");
                    var sd = monitorFileChanges[e.Name];
                    sd.ResetStats();
                    ScanExamplesForStructureDefinition(resourceType, sd, directory, skipFiles, fpc, monitorFileChanges);
                    ReportSdInvariantStats(sd);
                }
            };
            fsw.EnableRaisingEvents = true;
            do
            {
                fsw.WaitForChanged(WatcherChangeTypes.All);
            } while (true);
            return 0;
        }

        private static void ReportSdInvariantStats(StructureDefinitionSkeleton sd)
        {
            foreach (var inv in sd.Invariants)
            {
                if (inv.errorCount > 0)
                    Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  {sd.ResourceType}\t\t{inv.key}({inv.severity})\t{inv.successCount}/{inv.failCount}/{inv.errorCount}\t\t{(sd.IsProfile ? $"(profile - {sd.CanonicalUrl})" : "")}");
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        private static void ScanExamplesForStructureDefinition(string resourceType, StructureDefinitionSkeleton sd, string directory, string[] skipFiles, FhirPathCompiler fpc, Dictionary<string, StructureDefinitionSkeleton> monitorFileChanges)
        {
            if (!string.IsNullOrEmpty(resourceType) && sd.ResourceType != resourceType)
                return; // commandline has filtered out this resource type
            if (sd.IsDataType)
                return; // no checking of datatype level invariants at this stage

            if (!sd.Invariants.Any())
                return; // no point checking for test examples if there are no invariants
            try
            {
                foreach (var file in Directory.GetFiles(Path.Combine(directory, "source", sd.ResourceType), $"{sd.ResourceType.ToLower()}-*.xml"))
                {
                    // skip some files
                    if (file.Contains("Archive"))
                        continue;
                    if (file.Contains("-examples-header"))
                        continue;
                    if (file.Contains("-introduction"))
                        continue;
                    if (file.Contains("-notes"))
                        continue;
                    if (file.Contains("-search-params.xml"))
                        continue; // These have some issues due to no type property value (or if there missing other values - fullURL mostly)
                    // Other known invalid files to skip
                    if (file.Contains("patient-examples-cypress-template.xml"))
                        continue;
                    if (file.Contains("testscript-ats.xml"))
                        continue;
                    if (file.Contains("diagnosticreport-examples-lab-text.xml"))
                        continue;
                    if (file.Contains("questionnaireresponse-example-ussg-fht-answers.xml"))
                        continue;
                    if (file.EndsWith("-exceptions.xml"))
                        continue;

                    TestExampleForInvariants(sd, directory, file, skipFiles, fpc, true);
                    string briefPath = file.Replace(directory, null).Substring(1);
                    if (!monitorFileChanges.ContainsKey(briefPath))
                        monitorFileChanges.Add(briefPath, sd);
                }

                // Now scan for any test files in the examples folder (unit test resources)
                string invariantTestFolder = Path.Combine(directory, "source", sd.ResourceType, "invariant-tests");
                if (!Path.Exists(invariantTestFolder)&& !string.IsNullOrEmpty(resourceType))
                {
                    // there's no folder, so lets create one and copy some tests in there
                    Directory.CreateDirectory(invariantTestFolder);
                    string defaultExampleFile = Path.Combine(directory, "source", sd.ResourceType, $"{sd.ResourceType.ToLower()}-example.xml");
                    if (File.Exists(defaultExampleFile))
                    {
                        foreach (var inv in sd.Invariants)
                            File.Copy(defaultExampleFile, Path.Combine(invariantTestFolder, $"{inv.key}.f1.fail.xml"));
                    }
                }
                if (Path.Exists(Path.Combine(directory, "source", sd.ResourceType, "invariant-tests")))
                {
                    foreach (var file in Directory.GetFiles(Path.Combine(directory, "source", sd.ResourceType, "invariant-tests"), $"*.xml")
                        .Union(Directory.GetFiles(Path.Combine(directory, "source", sd.ResourceType, "invariant-tests"), $"*.json")))
                    {
                        string key = Path.GetFileNameWithoutExtension(file);
                        TestExampleForInvariants(sd, directory, file, skipFiles, fpc, !key.EndsWith(".fail"));
                        string briefPath = file.Replace(directory, null).Substring(1);
                        if (!monitorFileChanges.ContainsKey(briefPath))
                            monitorFileChanges.Add(briefPath, sd);
                    }
                }
            }
            catch(Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Huh? {ex.Message}");
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        private static ITypedElement FakeResolver(string reference, ISourceNode root)
        {
            // Fake implementation of this
            if (string.IsNullOrEmpty(reference)) return null;
            var ri = new Hl7.Fhir.Rest.ResourceIdentity(reference);
            if (reference.StartsWith("#"))
            {
                // See if there is a contained resource with it inside
                foreach (var child in root.Children("contained"))
                {
                    if (child.Children("id").FirstOrDefault()?.Text == reference.Substring(1))
                        return child.ToTypedElement(_provider);
                }
            }
            else if (!string.IsNullOrEmpty(ri.ResourceType))
            {
                var dummyNode = FhirXmlNode.Parse($"<{ri.ResourceType} xmlns=\"http://hl7.org/fhir\"><id value=\"{ri.Id}\"/></{ri.ResourceType}>");
                return dummyNode.ToTypedElement(_provider);
            }
            return null;
        }

        private static void ScanAllInvariantsInSpecification(string directory, out List<StructureDefinitionSkeleton> sds, out int totalInvariants)
        {
            // this dictionary of paths is used to replace an existing context definition
            // with another expression - this is used to redirect the expressions for the
            // context of fields that use the definition of another field via contentReference
            Dictionary<string, string> alternateContextPath = new Dictionary<string, string>();
            alternateContextPath.Add("Questionnaire.item", "Questionnaire.repeat(item)");
            alternateContextPath.Add("QuestionnaireResponse.item", "QuestionnaireResponse.repeat(item)");
            alternateContextPath.Add("StructureMap.group.rule", "StructureMap.group.repeat(rule)");
            alternateContextPath.Add("CodeSystem.concept", "CodeSystem.repeat(concept)");
            alternateContextPath.Add("Composition.section", "Composition.repeat(section)");
            alternateContextPath.Add("ValueSet.expansion.contains", "ValueSet.expansion.repeat(contains)");
            alternateContextPath.Add("RequestOrchestration.action", "RequestOrchestration.repeat(action)");
            alternateContextPath.Add("PlanDefinition.action", "PlanDefinition.repeat(action)");
            alternateContextPath.Add("OperationDefinition.parameter", "OperationDefinition.parameter | OperationDefinition.parameter.repeat(part)");
            alternateContextPath.Add("Parameters.parameter", "Parameters.parameter | Parameters.parameter.repeat(part)");
            alternateContextPath.Add("ImplementationGuide.definition.page", "ImplementationGuide.definition.repeat(page)");

            // Not sure about these context re-mappings
            alternateContextPath.Add("ValueSet.compose.include", "ValueSet.compose.include | ValueSet.compose.exclude");
            alternateContextPath.Add("ValueSet.compose.include.concept.designation", "ValueSet.compose.include.concept.designation | ValueSet.expansion.contains.designation");
            alternateContextPath.Add("Provenance.agent", "Provenance.agent | Provenance.entity.agent");
            alternateContextPath.Add("Observation.referenceRange", "Observation.referenceRange | Observation.component.referenceRange");
            alternateContextPath.Add("EvidenceVariable.characteristic", "EvidenceVariable.characteristic | EvidenceVariable.characteristic.definitionByCombination.characteristic");
            alternateContextPath.Add("ConceptMap.group.element.target.dependsOn", "ConceptMap.group.element.target.dependsOn | ConceptMap.group.element.target.product");
            alternateContextPath.Add("ExampleScenario.instance.containedInstance", "ExampleScenario.instance.containedInstance | ExampleScenario.process.step.operation.request | ExampleScenario.process.step.operation.response");
            alternateContextPath.Add("ExampleScenario.process.step", "ExampleScenario.process.step | ExampleScenario.process.step.alternative.step");
            alternateContextPath.Add("ExampleScenario.process", "ExampleScenario.process | ExampleScenario.process.step.process");
            alternateContextPath.Add("TestScript.setup.action.assert", "TestScript.setup.action.assert | TestScript.test.action.assert");
            alternateContextPath.Add("TestScript.setup.action.operation", "TestScript.setup.action.operation | TestScript.test.action.operation | TestScript.teardown.action.operation");

            sds = new List<StructureDefinitionSkeleton>();
            totalInvariants = 0;
            var skipFiles = new[] { "Workbook", "div" };
            foreach (var file in Directory.GetFiles(directory, "*.profile.xml"))
            {
                // Load the file
                var xml = File.ReadAllText(file);
                if (!string.IsNullOrEmpty(xml))
                {
                    try
                    {
                        var node = FhirXmlNode.Parse(xml);
                        if (skipFiles.Contains(node.Name))
                            continue;

                        // Console.WriteLine();
                        // Console.WriteLine($"{node.Children("type").FirstOrDefault()?.Text}  {node.Children("url").FirstOrDefault()?.Text}");
                        var sd = new StructureDefinitionSkeleton()
                        {
                            Filename = file,
                            ResourceType = node.Children("type").FirstOrDefault()?.Text,
                            CanonicalUrl = node.Children("url").FirstOrDefault()?.Text,
                            IsDataType = node.Children("kind").FirstOrDefault()?.Text == "complex-type",
                            IsProfile = node.Children("derivation").FirstOrDefault()?.Text == "constraint"
                        };
                        sds.Add(sd);

                        // Now scan the SD for it's invariants
                        foreach (var element in node.Children("differential").Children("element"))
                        {
                            var elementPath = element.Children("path").FirstOrDefault()?.Text;
                            foreach (var constraint in element.Children("constraint"))
                            {
                                var inv = new InvariantSkeleton()
                                {
                                    key = constraint.Children("key").FirstOrDefault()?.Text,
                                    severity = constraint.Children("severity").FirstOrDefault()?.Text,
                                    context = elementPath,
                                    expression = constraint.Children("expression").FirstOrDefault()?.Text
                                };
                                if (alternateContextPath.ContainsKey(elementPath))
                                    inv.context = alternateContextPath[elementPath];
                                else
                                {
                                    // Check if any of the keys are a prefix in the context
                                    foreach (var kvp in alternateContextPath)
                                    {
                                        if (elementPath.StartsWith(kvp.Key+"."))
                                        {
                                            inv.context = elementPath.Replace(kvp.Key + ".", $"({alternateContextPath[kvp.Key]}).");
                                            break;
                                        }
                                    }
                                }
                                sd.Invariants.Add(inv);
                                //if (string.IsNullOrEmpty(inv.expression))
                                //    Console.ForegroundColor = ConsoleColor.Red;
                                //Console.WriteLine($"     {inv.key}  {inv.context}   {inv.expression}");
                                //if (string.IsNullOrEmpty(inv.expression))
                                //    Console.ForegroundColor = ConsoleColor.White;
                                totalInvariants++;
                            }

                            // Now check if there is a context that uses this type too
                            var contentReference = element.Children("contentReference").FirstOrDefault()?.Text;
                            if (!string.IsNullOrEmpty(contentReference) && sd.Invariants.Any() && !alternateContextPath.ContainsKey(contentReference.Substring(1)))
                            {
                                // scan to find any invariants with the referenced type
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"     Dependent Type: {sd.ResourceType} {element.Children("path").FirstOrDefault()?.Text} uses path {contentReference}");
                                
                                foreach (var inv in sd.Invariants)
                                {
                                    if (inv.context.StartsWith(contentReference.Substring(1)))
                                    {
                                        Console.WriteLine($"       impacts {inv.key}");

                                        if (elementPath.StartsWith(inv.context))
                                        {
                                            Console.WriteLine($"       impacts {inv.key} nested!");
                                        }
                                    }
                                }
                                Console.ForegroundColor = ConsoleColor.White;
                            }
                        }
                    }
                    catch (FormatException)
                    {
                        Console.WriteLine($"nope... {file.Replace(directory, "")}");
                    }
                }
            }
        }

        private static void TestExampleForInvariants(StructureDefinitionSkeleton sd, string directory, string file, string[] skipFiles, FhirPathCompiler fpc, bool expectSuccess)
        {
            string content = null;
            try
            {
                using (var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(stream))
                {
                    content = sr.ReadToEnd();
                }
                //content = File.ReadAllText(file);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Failed to read the example file {file}");
                Console.WriteLine($"     {ex.Message}");
                Console.ForegroundColor = ConsoleColor.White;
                return;
            }
            if (!string.IsNullOrEmpty(content))
            {
                try
                {
                    ISourceNode node;
                    if (file.EndsWith(".xml"))
                        node = FhirXmlNode.Parse(content, new FhirXmlParsingSettings { PermissiveParsing = false });
                    else
                        node = FhirJsonNode.Parse(content, null, new FhirJsonParsingSettings { PermissiveParsing = false });
                    if (skipFiles.Contains(node.Name))
                        return;

                    if (sd.IsProfile)
                    {
                        return; // skipping profiles for now
                    }

                    Console.WriteLine();
                    Console.WriteLine($"{node.Name}/{node.Children("id").FirstOrDefault()?.Text}  {file.Replace(directory, "")}");

                    var te = node.ToTypedElement(_provider, null, new TypedElementSettings() { ErrorMode = TypedElementSettings.TypeErrorMode.Passthrough });
                    var context = new FhirEvaluationContext(te);
                    context.ElementResolver = (string reference) => FakeResolver(reference, node);

                    // Before we do anything with it, lets validate with the firely validator
                    var settings = new Hl7.Fhir.Validation.ValidationSettings()
                    {
                        ResourceResolver = _cacheResolver,
                        TerminologyService = new FakeTerminologyService(), //new Hl7.Fhir.Specification.Terminology.LocalTerminologyService(AsyncSource, new Hl7.Fhir.Specification.Terminology.ValueSetExpanderSettings() { MaxExpansionSize = 1500 }),
                        FhirPathCompiler = fpc,
                        ConstraintsToIgnore = new string[] { },
                        SkipConstraintValidation = true
                    };
                    var validator = new Hl7.Fhir.Validation.Validator(settings);
                    var outcome = validator.Validate(te);
                    // Ignore any errors and warnings from the validator
                    outcome.Issue.RemoveAll(t => t.Severity == OperationOutcome.IssueSeverity.Warning || t.Severity == OperationOutcome.IssueSeverity.Information);
                    outcome.Issue.RemoveAll(t => t.Severity == OperationOutcome.IssueSeverity.Error && t.Code ==  OperationOutcome.IssueType.Informational);
                    // Ignore any can't resolve profile messages too
                    outcome.Issue.RemoveAll(t => t.Severity == OperationOutcome.IssueSeverity.Error
                                                && t.Code == OperationOutcome.IssueType.Invalid
                                                && t.Details?.Text?.Contains("which is not a known FHIR core type") == true);
                    // And another biproduct of this is error 1003
                    outcome.Issue.RemoveAll(t => t.Severity == OperationOutcome.IssueSeverity.Error
                                                && t.Code == OperationOutcome.IssueType.Incomplete
                                                && t.Details?.Coding?.Any(c => c.Code == "4000") == true);
                    outcome.Issue.RemoveAll(t => t.Severity == OperationOutcome.IssueSeverity.Error
                                                && t.Code == OperationOutcome.IssueType.Invalid
                                                && t.Details?.Coding?.Any(c => c.Code == "1003") == true);
                    // and for whatever reason any internal errors (can't help those at this point)
                    outcome.Issue.RemoveAll(t => t.Severity == OperationOutcome.IssueSeverity.Fatal && t.Details?.Coding?.Any(c => c.Code == "5003") == true);
                    if (!outcome.Success)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"  Validation Err: {file}");
                        Console.WriteLine(outcome.ToXml(new FhirXmlSerializationSettings() { Pretty = true }));
                        Console.ForegroundColor = ConsoleColor.White;
                    }

                    // Now do the actual invariant testing
                    string key = Path.GetFileNameWithoutExtension(file);
                    bool specificInvariantTest = (key.EndsWith("fail") || key.EndsWith("pass")) && sd.Invariants.Any(i => key.StartsWith(i.key+"."));

                    // Now test each of the invariants on the resource
                    foreach (var inv in sd.Invariants)
                    {
                        if (specificInvariantTest && !key.StartsWith(inv.key + "."))
                            continue;
                        try
                        {
                            var contexts = new List<ITypedElement>();
                            if (string.IsNullOrEmpty(inv.context) || inv.context == sd.ResourceType)
                            {
                                contexts.Add(te);
                            }
                            else
                            {
                                var exprContext = fpc.Compile(inv.context.Replace("[x]", ""));
                                contexts.AddRange(exprContext(te, context));
                            }

                            var expr = fpc.Compile(inv.expression);

                            var results = contexts.Select(contextValue => 
                            {
                                var result = expr(contextValue, context);
                                if (!result.Any()) return (bool?)null;
                                return (result.Count() == 1 && result.First().Value is bool b && b); 
                            }).ToArray();

                            var allTrue = results.All(v => v == true);
                            var anyFail = results.Any(v => v == false || !v.HasValue);

                            if (specificInvariantTest)
                            {
                                if (expectSuccess)
                                {
                                    if (allTrue && results.Count() > 0)
                                        inv.successCount++;
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Magenta;
                                        Console.WriteLine($"  {inv.key}({inv.severity})  {inv.context}  {inv.expression}");
                                        inv.errorCount++;
                                        Console.ForegroundColor = ConsoleColor.White;
                                    }
                                }
                                else
                                {
                                    if (anyFail && results.Count() > 0)
                                        inv.failCount++;
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Magenta;
                                        Console.WriteLine($"  {inv.key}({inv.severity})  {inv.context}  {inv.expression}");
                                        inv.errorCount++;
                                        Console.ForegroundColor = ConsoleColor.White;
                                    }
                                }
                            }
                            else
                            {
                                foreach (var result in results)
                                {
                                    if (result == true)
                                    {
                                        if (expectSuccess)
                                            inv.successCount++;
                                        else if (!anyFail)
                                        {
                                            Console.ForegroundColor = ConsoleColor.Magenta;
                                            Console.WriteLine($"  {inv.key}({inv.severity})  {inv.context}  {inv.expression}  {result?.ToString() ?? "(null)"}");
                                            inv.errorCount++;
                                            Console.ForegroundColor = ConsoleColor.White;
                                        }
                                    }
                                    else
                                    {
                                        if (!expectSuccess || (!specificInvariantTest && inv.severity == "warning"))
                                            inv.failCount++;
                                        else
                                        {
                                            Console.ForegroundColor = ConsoleColor.Magenta;
                                            Console.WriteLine($"  {inv.key}({inv.severity})  {inv.context}  {inv.expression}  {result?.ToString() ?? "(null)"}");
                                            inv.errorCount++;
                                            Console.ForegroundColor = ConsoleColor.White;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  {inv.key}({inv.severity})  {inv.context}  {inv.expression}  {ex.Message}");
                            inv.errorCount++;
                            Console.ForegroundColor = ConsoleColor.White;
                        }
                    }
                }
                catch (FormatException ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"nope... {file.Replace(directory, "")} {ex.Message}");
                    Console.ForegroundColor = ConsoleColor.White;
                }
            }
        }
    }
}