using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildDependencyReader.ProjectFileParser;
using QuickGraph.Graphviz;
using System.Diagnostics;
using QuickGraph;
using System.IO;
using BuildDependencyReader.BuildDependencyResolver;

namespace BuildDependencyReader.PrintProjectDependencies
{
    class DotEngine : IDotEngine
    {
        public string Run(QuickGraph.Graphviz.Dot.GraphvizImageType imageType, string dot, string outputFileName)
        {
            Console.WriteLine("Running GraphViz...");
            Console.WriteLine("Running from: " + System.Environment.CurrentDirectory);

            var tempFileName = System.IO.Path.GetTempFileName();
            File.AppendAllText(tempFileName, dot);
            Console.WriteLine("Wrote dot to: " + tempFileName);

            var processStartInfo = new ProcessStartInfo(@"D:\Program Files (x86)\Graphviz 2.28\bin\dot.exe", 
                String.Format("-T{0} -o{1} {2}", 
                              imageType.ToString().ToLowerInvariant(), 
                              outputFileName, 
                              tempFileName));
            //processStartInfo.RedirectStandardInput = true;
            //processStartInfo.RedirectStandardOutput = true;
            //processStartInfo.RedirectStandardError = true;
            processStartInfo.UseShellExecute = false;
            processStartInfo.CreateNoWindow = true;
            Console.WriteLine(processStartInfo.FileName + " " + processStartInfo.Arguments);

            processStartInfo.WorkingDirectory = System.Environment.CurrentDirectory;

            //Console.WriteLine();

            Process process = Process.Start(processStartInfo);
            //process.StandardInput.Close(); // line added to stop process from hanging on ReadToEnd()
            //string outputString = process.StandardOutput.ReadToEnd();
            //string errorString = process.StandardError.ReadToEnd();
            //Console.Out.Write(outputString);
            //Console.Out.Write(errorString);
            //Console.Out.Flush();

            Console.WriteLine("Wrote graph image to: " + outputFileName);

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

            var projectFinder = new ProjectFinder(args[0]);

            var graph = BuildDependencyResolver.BuildDependencyResolver.DependencyGraph(projects, true);

            foreach (var project in graph.Vertices)
            {
                foreach (var targetProject in project.AssemblyReferences.SelectMany(projectFinder.FindProjectForAssemblyReference))
                {
                    graph.AddEdge(new SEdge<Project>(targetProject, project));
                }
            }

            var graphviz = new GraphvizAlgorithm<Project, SEdge<Project>>(graph);
            graphviz.GraphFormat.RankSeparation = 2;
            graphviz.GraphFormat.IsConcentrated = true;

            graphviz.FormatVertex += new FormatVertexEventHandler<Project>(graphviz_FormatVertex);

            var outFileName = graphviz.Generate(new DotEngine(), "graph.png");

            foreach (var project in BuildDependencyResolver.BuildDependencyResolver.BuildOrder(projects))
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
