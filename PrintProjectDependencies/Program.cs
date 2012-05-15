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
                Console.WriteLine("Usage: {0} <base path for searching for sln files>", Process.GetCurrentProcess().ProcessName);
                return 1;
            }
            var projects = args.Skip(1)
                               .Select(x => x.Trim())
                               .Distinct()
                               .Select(x => Project.FromCSProj(x));

            var projectFinder = new ProjectFinder(args[0], true);

            var graph = BuildDependencyResolver.BuildDependencyResolver.DependencyGraph(projectFinder, projects, false);

            var graphviz = new GraphvizAlgorithm<Project, SEdge<Project>>(graph, "graph", QuickGraph.Graphviz.Dot.GraphvizImageType.Svg );
            graphviz.GraphFormat.RankSeparation = 2;
            //graphviz.GraphFormat.IsConcentrated = true;

            graphviz.FormatVertex += new FormatVertexEventHandler<Project>(graphviz_FormatVertex);

            var outFileName = graphviz.Generate(new DotEngine(), "graph.svg");

            foreach (var project in graph.TopologicalSort())
            {
                Console.WriteLine(project);
            }

            return 0;
        }

        static void graphviz_FormatVertex(object sender, FormatVertexEventArgs<Project> e)
        {
            e.VertexFormatter.Label = e.Vertex.Name;
        }
    }
}
