using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BuildDependencyReader.BuildDependencyResolver;
using BuildDependencyReader.ProjectFileParser;
using QuickGraph;
using QuickGraph.Algorithms;
using QuickGraph.Graphviz;

namespace BuildDependencyReader.PrintProjectDependencies
{
    class DotEngine : IDotEngine
    {
        public string Run(QuickGraph.Graphviz.Dot.GraphvizImageType imageType, string dot, string outputFileName)
        {
            var tempFileName = System.IO.Path.GetTempFileName();
            File.AppendAllText(tempFileName, dot);
            var processStartInfo = new ProcessStartInfo(@"D:\Program Files (x86)\Graphviz 2.28\bin\dot.exe", 
                String.Format("-T{0} -o{1} {2}", 
                              imageType.ToString().ToLowerInvariant(), 
                              outputFileName, 
                              tempFileName));
            processStartInfo.UseShellExecute = false;
            processStartInfo.RedirectStandardError = true;
            processStartInfo.RedirectStandardOutput = true;
            processStartInfo.WorkingDirectory = System.Environment.CurrentDirectory;

            Process process = Process.Start(processStartInfo);
            Console.Error.Write(process.StandardError.ReadToEnd());
            Console.Out.Write(process.StandardOutput.ReadToEnd());
            return outputFileName;
        }
    }

    class Program
    {
        static int Main(string[] args)
        {
            if (false == args.Any())
            {
                Console.Error.WriteLine("Usage: {0} <base path for searching for sln files> <csproj/sln file>... -[excluded solution file 1] -[excluded solution file 2] ... ", Process.GetCurrentProcess().ProcessName);
                return 1;
            }
            var trimmedArgs = args.Skip(1)
                                  .Select(x => x.Trim())
                                  .Distinct();

            // TODO : Accept also SLN files
            var projects = trimmedArgs.Where(x => false == x.StartsWith("-"))
                                      .Select(x => Project.FromCSProj(x))
                                      .ToArray();

            var excludedSLNs = trimmedArgs.Where(x => x.StartsWith("-"))
                                          .Select(x => System.IO.Path.GetFullPath(x.Substring(1)).ToLowerInvariant())
                                          .ToArray();

            Console.Error.WriteLine("Excluding solutions:\n\t" + String.Join("\n\t", excludedSLNs));

            var projectFinder = new ProjectFinder(args[0], true);

            var graph = BuildDependencyResolver.BuildDependencyResolver.SolutionDependencyGraph(projectFinder, projects, false);

            graph.RemoveVertexIf(x => excludedSLNs.Contains(x.ToLowerInvariant()));

            var graphviz = new GraphvizAlgorithm<String, SEdge<String>>(graph, "graph", QuickGraph.Graphviz.Dot.GraphvizImageType.Svg);
            graphviz.GraphFormat.RankSeparation = 2;
            //graphviz.GraphFormat.IsConcentrated = true;

            graphviz.FormatVertex += new FormatVertexEventHandler<String>(graphviz_FormatVertex);

            var fileName = System.IO.Path.GetTempFileName() + ".svg";
            var outFileName = graphviz.Generate(new DotEngine(), fileName);
            Console.Error.WriteLine("GraphViz Output to: " + fileName);

            foreach (var project in graph.TopologicalSort())
            {
                Console.WriteLine(project);
            }

            return 0;
        }

        static void graphviz_FormatVertex(object sender, FormatVertexEventArgs<String> e)
        {
            e.VertexFormatter.Label = e.Vertex.Replace("\\", "\\\\");
        }
    }
}
