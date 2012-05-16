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
using Mono.Options;

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
            var exlcudedSlns = new List<string>();
            var inputFiles = new List<string>();
            bool verbose = false;
            bool generateGraphviz = false;
            string basePath;
            if (false == ParseOptions(args, exlcudedSlns, inputFiles, out basePath, out verbose, out generateGraphviz))
            {
                return 1;
            }

            PrintProjectDependencies(inputFiles, exlcudedSlns, basePath, verbose, generateGraphviz);

            return 0;
        }

        private static void PrintProjectDependencies(IEnumerable<string> _inputFiles, IEnumerable<string> _excludedSLNs, string basePath, bool verbose, bool generateGraphviz)
        {
            var graph = BuildDependencyResolver.BuildDependencyResolver.SolutionDependencyGraph(_inputFiles, _excludedSLNs, basePath, verbose);

            if (generateGraphviz)
            {
                GenerateGraphViz(graph);
            }

            foreach (var project in graph.TopologicalSort())
            {
                Console.WriteLine(project);
            }
        }

        private static void GenerateGraphViz(AdjacencyGraph<string, SEdge<string>> graph)
        {
            var graphviz = new GraphvizAlgorithm<String, SEdge<String>>(graph, "graph", QuickGraph.Graphviz.Dot.GraphvizImageType.Svg);
            graphviz.GraphFormat.RankSeparation = 2;
            //graphviz.GraphFormat.IsConcentrated = true;

            graphviz.FormatVertex += new FormatVertexEventHandler<String>(graphviz_FormatVertex);

            var fileName = System.IO.Path.GetTempFileName() + ".svg";
            var outFileName = graphviz.Generate(new DotEngine(), fileName);
            Console.Error.WriteLine("GraphViz Output to: " + fileName);
        }

        

        private static bool ParseOptions(string[] args, List<string> exlcudedSlns, List<string> inputFiles, out string basePath, out bool verbose, out bool generateGraphviz)
        {
            bool userRequestsHelp = false;

            string _basePath = null;
            bool _verbose = false;
            bool _generateGraphviz = false;
            basePath = null;
            verbose = false;
            generateGraphviz = false;

            var options = new OptionSet();
            options.Add("b|basePath=",
                        "base path for searching for sln / csproj files",
                        x => _basePath = x);
            options.Add("x|exclude=", 
                        "exclude this .sln when resolving dependency order (useful when temporarily ignoring cyclic dependencies)", 
                        x => exlcudedSlns.Add(x));
            options.Add("v|verbose",
                        "print verbose output (will go to stderr)",
                        x => _verbose = (null != x));
            options.Add("g|graphviz",
                        "generate graphviz output",
                        x => _generateGraphviz = (null != x));
            options.Add("h|help", 
                        "show help", 
                        x => userRequestsHelp = (null != x));

            string errorMessage = null;
            try
            {
                inputFiles.AddRange(options.Parse(args));
            }
            catch (OptionException e)
            {
                errorMessage = e.Message;
            }
            basePath = _basePath;
            verbose = _verbose;
            generateGraphviz = _generateGraphviz;

            return ValidateOptions(basePath, userRequestsHelp, options, errorMessage);
        }

        private static bool ValidateOptions(string basePath, bool userRequestsHelp, OptionSet options, string errorMessage)
        {
            string message = null;
            if (null != errorMessage)
            {
                message = errorMessage;
            }
            else if (userRequestsHelp)
            {
                message = "Showing Help";
            }
            else if (String.IsNullOrWhiteSpace(basePath))
            {
                message = "Missing base path";
            }
            else if (false == System.IO.Directory.Exists(basePath))
            {
                message = "Base path does not exist: '" + basePath + "'";
            }

            if (null != message)
            {
                ShowHelp(message, options);
                return false;
            }
            return true;
        }

        private static void ShowHelp(string message, OptionSet options)
        {
            Console.Error.WriteLine(Process.GetCurrentProcess().ProcessName + ": " + message);
            options.WriteOptionDescriptions(Console.Error);
        }

        static void graphviz_FormatVertex(object sender, FormatVertexEventArgs<String> e)
        {
            e.VertexFormatter.Label = e.Vertex.Replace("\\", "\\\\");
        }
    }
}
