using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildDependencyReader.ProjectFileParser;
using QuickGraph.Graphviz;
using System.Diagnostics;
using QuickGraph;
using System.IO;

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
        static void Main(string[] args)
        {
            var projects = args.Select(x => x.Trim())
                               .Distinct()
                               .Select(x => Project.FromCSProj(x));

            var graph = BuildDependencyResolver.BuildDependencyResolver.DependencyGraph(projects);

            var graphviz = new GraphvizAlgorithm<Project, SEdge<Project>>(graph);
            graphviz.FormatVertex += new FormatVertexEventHandler<Project>(graphviz_FormatVertex);

            var outFileName = graphviz.Generate(new DotEngine(), "graph.png");

            foreach (var project in BuildDependencyResolver.BuildDependencyResolver.SortByDependencies(projects))
            {
                Console.WriteLine(project);
            }
        }

        static void graphviz_FormatVertex(object sender, FormatVertexEventArgs<Project> e)
        {
            e.VertexFormatter.Label = e.Vertex.Name;
        }
    }
}
