using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification;
using Hl7.Fhir.Specification.Source;
using Hl7.FhirPath;
using Hl7.FhirPath.Expressions;

namespace fhir_invariant_tester
{
    internal class Program
    {
        static IStructureDefinitionSummaryProvider _provider;
        static void Main(string[] args)
        {
            Console.WriteLine("FHIR R5 Invariant tester!");

            // Scan over all the files in the specification folder (arg[0]) to find all the Profile definitions.
            string directory = args[0];
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


            var source = new CachedResolver(new SpecSourceStructureDefinitionResolver(sds));
            _provider = new Hl7.Fhir.Specification.StructureDefinitionSummaryProvider(source);

            // Parse all the files directly in the source folder of each resource type (known succeeding resources).
            var skipFiles = new[] { "Workbook", "div" };
            foreach (var sd in sds)
            {
                if (sd.IsDataType) continue;

                if (!sd.Invariants.Any())
                    continue; // no point checking for test examples if there are no invariants
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

                        TestExampleForInvariants(sd, directory, file, skipFiles, fpc, true);
                    }

                    // Now scan for any test files in the examples folder (unit test resources)
                    if (Path.Exists(Path.Combine(directory, "source", sd.ResourceType, "invariant-tests")))
                    {
                        foreach (var file in Directory.GetFiles(Path.Combine(directory, "source", sd.ResourceType, "invariant-tests"), $"*.xml")
                            .Union(Directory.GetFiles(Path.Combine(directory, "source", sd.ResourceType, "invariant-tests"), $"*.json")))
                        {
                            string key = Path.GetFileNameWithoutExtension(file);
                            TestExampleForInvariants(sd, directory, file, skipFiles, fpc, !key.EndsWith(".fail"));
                        }
                    }
                }
                catch
                {
                }
            }

            Console.WriteLine("");
            Console.WriteLine("---------------------------------------------------------------");
            Console.WriteLine($"Results");
            Console.WriteLine("---------------------------------------------------------------");
            Console.WriteLine("");
            foreach (var sd in sds)
            {
                if (sd.IsDataType) continue;
                foreach (var inv in sd.Invariants)
                {
                    if (inv.errorCount > 0)
                        Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  {sd.ResourceType}\t\t{inv.key}({inv.severity})\t{inv.successCount}/{inv.failCount}/{inv.errorCount}\t\t{(sd.IsProfile ? $"(profile - {sd.CanonicalUrl})" : "")}");
                    Console.ForegroundColor = ConsoleColor.White;
                }
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
                            foreach (var constraint in element.Children("constraint"))
                            {
                                var inv = new InvariantSkeleton()
                                {
                                    key = constraint.Children("key").FirstOrDefault()?.Text,
                                    severity = constraint.Children("severity").FirstOrDefault()?.Text,
                                    context = element.Children("path").FirstOrDefault()?.Text,
                                    expression = constraint.Children("expression").FirstOrDefault()?.Text
                                };
                                sd.Invariants.Add(inv);
                                //if (string.IsNullOrEmpty(inv.expression))
                                //    Console.ForegroundColor = ConsoleColor.Red;
                                //Console.WriteLine($"     {inv.key}  {inv.context}   {inv.expression}");
                                //if (string.IsNullOrEmpty(inv.expression))
                                //    Console.ForegroundColor = ConsoleColor.White;
                                totalInvariants++;
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
            var content = File.ReadAllText(file);
            if (!string.IsNullOrEmpty(content))
            {
                try
                {
                    ISourceNode node;
                    if (file.EndsWith(".xml"))
                        node = FhirXmlNode.Parse(content);
                    else
                        node = FhirJsonNode.Parse(content);
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

                    string key = Path.GetFileNameWithoutExtension(file);
                    bool specificInvariantTest = (key.EndsWith("fail") || key.EndsWith("pass")) && sd.Invariants.Any(i => key.StartsWith(i.key));

                    // Now test each of the invariants on the resource
                    foreach (var inv in sd.Invariants)
                    {
                        if (specificInvariantTest && !key.StartsWith(inv.key))
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
                                var exprContext = fpc.Compile(inv.context);
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
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"  {inv.key}({inv.severity})  {inv.context}  {inv.expression}  {ex.Message}");
                            inv.errorCount++;
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
}