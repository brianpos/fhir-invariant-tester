using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification.Source;
using Hl7.FhirPath;
using Hl7.FhirPath.Expressions;

namespace fhir_invariant_tester
{
    internal class Program
    {
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
            static ITypedElement resolver(ITypedElement f, EvaluationContext ctx)
            {
                return ctx is FhirEvaluationContext fctx ? f.Resolve(fctx.ElementResolver) : f.Resolve();
            }
            var symbols = new SymbolTable(FhirPathCompiler.DefaultSymbolTable);
            //symbols.Add("hasValue", (ITypedElement f) => f.HasValue(), doNullProp: false);
            //symbols.Add("resolve", (ITypedElement f, EvaluationContext ctx) => resolver(f, ctx), doNullProp: false);
            symbols.AddFhirExtensions();
            //symbols.Add("resolve", (ITypedElement focus) => { return focus; });
            //symbols.Add("hasValue", (ITypedElement focus) => { return focus?.HasValue(); });
            //symbols.Add("memberOf", (ITypedElement focus, string url) => { Console.WriteLine(url); return focus?.HasValue(); });
            FhirPathCompiler fpc = new FhirPathCompiler(symbols);


            var source = new CachedResolver(new SpecSourceStructureDefinitionResolver(sds));
            var sdsp = new Hl7.Fhir.Specification.StructureDefinitionSummaryProvider(source);

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
                            continue; // 

                        // Load the file
                        var xml = File.ReadAllText(file);
                        if (!string.IsNullOrEmpty(xml))
                        {
                            try
                            {
                                var node = FhirXmlNode.Parse(xml);
                                if (skipFiles.Contains(node.Name))
                                    continue;

                                if (sd.IsProfile)
                                {
                                    continue; // skipping profiles for now
                                }

                                Console.WriteLine();
                                Console.WriteLine($"{node.Name}/{node.Children("id").FirstOrDefault()?.Text}  {file.Replace(directory, "")}");

                                var te = new ScopedNode(node.ToTypedElement(sdsp, null, new TypedElementSettings() { ErrorMode = TypedElementSettings.TypeErrorMode.Passthrough }));
                                var context = new FhirEvaluationContext(te);
                                context.ElementResolver = (string reference) =>
                                {
                                    // Fake implementation of this
                                    if (string.IsNullOrEmpty(reference)) return null;
                                    var ri = new Hl7.Fhir.Rest.ResourceIdentity(reference);
                                    if (!string.IsNullOrEmpty(ri.ResourceType))
                                    {
                                        var dummyNode = FhirXmlNode.Parse($"<{ri.ResourceType} xmlns=\"http://hl7.org/fhir\"><id value=\"{ri.Id}\"/></{ri.ResourceType}>");
                                        return new ScopedNode(dummyNode.ToTypedElement(sdsp));
                                    }
                                    return null;
                                };

                                // Now test each of the invariants on the resource
                                foreach (var inv in sd.Invariants)
                                {
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

                                        foreach (var contextValue in contexts)
                                        {
                                            var expr = fpc.Compile(inv.expression);
                                            var result = expr(contextValue, context);
                                            if (result.Count() == 1 && result.First().Value is bool b && b)
                                            {
                                                // Console.WriteLine($"  {inv.key}({inv.severity})  {inv.context}  {inv.expression}  {result.First().Value}");
                                                inv.successCount++;
                                            }
                                            else
                                            {
                                                Console.WriteLine($"  {inv.key}({inv.severity})  {inv.context}  {inv.expression}  {result.FirstOrDefault()?.Value ?? "(null)"}");
                                                inv.failCount++;
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
                catch
                {
                }
            }

            //var sds = Directory.GetFiles(args[0], "*.xml", SearchOption.AllDirectories);
            //var files = Directory.GetFiles(args[0], "*.json", SearchOption.AllDirectories).Union(Directory.GetFiles(args[0], "*.xml", SearchOption.AllDirectories));
            //var node2 = FhirJsonNode.Parse(file);
            //Dictionary<string, object> values = new Dictionary<string, object>();
            //node.HarvestValues(values, "", "");

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
                    Console.WriteLine($"  {sd.ResourceType}\t\t{inv.key}({inv.severity})\t{inv.successCount}/{inv.failCount}/{inv.errorCount}\t\t{(sd.IsProfile ? "(profile)" : "")}");
                    inv.successCount++;
                }
            }
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
    }
}