using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification.Summary;

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
            Console.WriteLine($"{totalInvariants} in {sds.Count()} resource types");

            // now scan all the test files!

            // 

            //var sds = Directory.GetFiles(args[0], "*.xml", SearchOption.AllDirectories);
            //var files = Directory.GetFiles(args[0], "*.json", SearchOption.AllDirectories).Union(Directory.GetFiles(args[0], "*.xml", SearchOption.AllDirectories));
            //var node2 = FhirJsonNode.Parse(file);
            //Dictionary<string, object> values = new Dictionary<string, object>();
            //node.HarvestValues(values, "", "");
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