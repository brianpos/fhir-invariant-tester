using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification.Summary;
using Hl7.FhirPath;
using Hl7.FhirPath.Expressions;
using System.Diagnostics.SymbolStore;

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
            ScanAllInvariantsInSpecification(directory, out sds, out totalInvariants);

            // now report the number of overall invariants in the spec
            Console.WriteLine("");
            Console.WriteLine("---------------------------------------------------------------");
            Console.WriteLine($"{totalInvariants} in {sds.Count()} resource types");
            Console.WriteLine("---------------------------------------------------------------");
            Console.WriteLine("");

            // now scan all the test files!
            var symbols = new SymbolTable(FhirPathCompiler.DefaultSymbolTable);
            symbols.AddStandardFP();
            symbols.AddFhirExtensions();
            //symbols.Add("resolve", (ITypedElement focus) => { return focus; });
            //symbols.Add("hasValue", (ITypedElement focus) => { return focus?.HasValue(); });
            //symbols.Add("memberOf", (ITypedElement focus, string url) => { Console.WriteLine(url); return focus?.HasValue(); });
            FhirPathCompiler fpc = new FhirPathCompiler(symbols);

            // Parse all the files directly in the source folder of each resource type (known succeeding resources).
            var skipFiles = new[] { "Workbook", "div" };
            foreach (var sd in sds)
            {
                if (!sd.Invariants.Any())
                    continue; // no point checking for test examples if there are no invariants
                foreach (var file in Directory.GetFiles(Path.Combine(directory, sd.ResourceType), $"{sd.ResourceType.ToLower()}-*.xml"))
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

                    // Load the file
                    var xml = File.ReadAllText(file);
                    if (!string.IsNullOrEmpty(xml))
                    {
                        try
                        {
                            var node = FhirXmlNode.Parse(xml);
                            if (skipFiles.Contains(node.Name))
                                continue;

                            Console.WriteLine();
                            Console.WriteLine($"{node.Name}/{node.Children("id").FirstOrDefault()?.Text}  {file.Replace(directory, "")}");

                            var te = node.ToTypedElement();

                            // Now test each of the invariants on the resource
                            foreach (var inv in sd.Invariants)
                            {
                                try
                                {
                                    var expr = fpc.Compile(inv.expression);
                                    if (string.IsNullOrEmpty(inv.context) || inv.context == sd.ResourceType)
                                    {
                                        var context = new FhirEvaluationContext(te);
                                        context.ElementResolver = (string reference) => { return null; };
                                        var result = expr(te, context);
                                        if (result.Count() == 1 && result.First().Value is bool b && b)
                                        {
                                            Console.WriteLine($"  {inv.key}  {inv.context}  {inv.expression}  {result.First().Value}");
                                            inv.successCount++;
                                        }
                                        else
                                        {
                                            Console.WriteLine($"  {inv.key}  {inv.context}  {inv.expression}  {result.FirstOrDefault()?.Value ?? "(null)"}");
                                            inv.failCount++;
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine($"  {inv.key}  {inv.context}  {inv.expression}  (skipped)");
                                    }
                                }
                                catch(Exception ex)
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine($"  {inv.key}  {inv.context}  {inv.expression}  {ex.Message}");
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
                foreach (var inv in sd.Invariants)
                {
                    Console.WriteLine($"  {sd.ResourceType}\t\t{inv.key}\t{inv.successCount}/{inv.failCount}/{inv.errorCount}");
                    inv.successCount++;
                }
            }
        }

        private static void ScanAllInvariantsInSpecification(string directory, out List<StructureDefinitionSkeleton> sds, out int totalInvariants)
        {
            sds = new List<StructureDefinitionSkeleton>();
            totalInvariants = 0;
            var skipFiles = new[] { "Workbook", "div" };
            foreach (var file in Directory.GetFiles(directory, "structuredefinition-*.xml", SearchOption.AllDirectories))
            {
                // skip some files
                if (file.Contains("Archive"))
                    continue;

                // Load the file
                var xml = File.ReadAllText(file);
                if (!string.IsNullOrEmpty(xml))
                {
                    try
                    {
                        var node = FhirXmlNode.Parse(xml);
                        if (skipFiles.Contains(node.Name))
                            continue;

                        Console.WriteLine();
                        Console.WriteLine($"{node.Children("type").FirstOrDefault()?.Text}  {node.Children("url").FirstOrDefault()?.Text}");
                        var sd = new StructureDefinitionSkeleton()
                        {
                            Filename = file,
                            ResourceType = node.Children("type").FirstOrDefault()?.Text,
                            CanonicalUrl = node.Children("url").FirstOrDefault()?.Text
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
                                    context = element.Children("path").FirstOrDefault()?.Text,
                                    expression = constraint.Children("expression").FirstOrDefault()?.Text
                                };
                                sd.Invariants.Add(inv);
                                if (string.IsNullOrEmpty(inv.expression))
                                    Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"     {inv.key}  {inv.context}   {inv.expression}");
                                if (string.IsNullOrEmpty(inv.expression))
                                    Console.ForegroundColor = ConsoleColor.White;
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