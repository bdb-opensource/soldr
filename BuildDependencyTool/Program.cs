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
using log4net.Repository.Hierarchy;
using log4net;
using log4net.Core;
using Common;

namespace BuildDependencyReader.PrintProjectDependencies
{
    class DotEngine : IDotEngine
    {
        protected static readonly log4net.ILog _logger = log4net.LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

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
            _logger.Info(process.StandardError.ReadToEnd());
            _logger.Info(process.StandardOutput.ReadToEnd());
            return outputFileName;
        }
    }

    class OptionValues
    {
        public string BasePath;
        public bool GenerateGraphviz;
        public bool Verbose;
        public bool PrintSolutionBuildOrder;
        public bool Build;
    }

    class Program
    {
        protected static readonly log4net.ILog _logger = log4net.LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        static int Main(string[] args)
        {
            log4net.Config.XmlConfigurator.Configure();

            var exlcudedSlns = new List<string>();
            var inputFiles = new List<string>();
            var optionValues = new OptionValues();

            if (false == ParseOptions(args, exlcudedSlns, inputFiles, optionValues))
            {
                return 1;
            }

            UpdateLog4NetLevel(optionValues.Verbose);

            var projectFinder = new ProjectFinder(optionValues.BasePath, true);
            var dependencyInfo = BuildDependencyResolver.BuildDependencyResolver.DependencyInfo(projectFinder, inputFiles, exlcudedSlns);

            if (optionValues.GenerateGraphviz)
            {
                GenerateGraphViz(dependencyInfo.SolutionDependencyGraph);
            }

            if (optionValues.PrintSolutionBuildOrder)
            {
                PrintSolutionBuildOrder(dependencyInfo);
            }

            if (optionValues.Build)
            {
                PerformBuild(projectFinder, dependencyInfo);
            }

            return 0;
        }

        protected static void UpdateLog4NetLevel(bool verbose)
        {
            Level level = verbose ? log4net.Core.Level.Trace
                                  : log4net.Core.Level.Warn;
            log4net.LogManager.GetRepository().Threshold = level;
        }

        protected static void PrintSolutionBuildOrder(BuildDependencyInfo dependencyInfo)
        {
            foreach (var solutionFileName in dependencyInfo.TrimmedSolutionDependencyGraph.TopologicalSort())
            {
                Console.WriteLine(solutionFileName);
            }
        }

        protected static void PerformBuild(ProjectFinder projectFinder, BuildDependencyInfo dependencyInfo)
        {
            foreach (var solutionFileName in dependencyInfo.TrimmedSolutionDependencyGraph.TopologicalSort())
            {
                Builder.BuildSolution(projectFinder, solutionFileName);  
            }
        }


        protected static void GenerateGraphViz(AdjacencyGraph<string, SEdge<string>> graph)
        {
            var graphviz = new GraphvizAlgorithm<String, SEdge<String>>(graph, "graph", QuickGraph.Graphviz.Dot.GraphvizImageType.Svg);
            graphviz.GraphFormat.RankSeparation = 2;
            //graphviz.GraphFormat.IsConcentrated = true;

            graphviz.FormatVertex += new FormatVertexEventHandler<String>(graphviz_FormatVertex);

            var fileName = System.IO.Path.GetTempFileName() + ".svg";
            var outFileName = graphviz.Generate(new DotEngine(), fileName);
            _logger.InfoFormat("GraphViz Output to: " + fileName);
        }

        

        protected static bool ParseOptions(string[] args, List<string> exlcudedSlns, List<string> inputFiles, OptionValues optionValues)
        {
            bool userRequestsHelp = false;

            optionValues.BasePath = null;
            optionValues.Verbose = false;
            optionValues.GenerateGraphviz = false;
            optionValues.Build = false;
            optionValues.PrintSolutionBuildOrder = false;

            var options = new OptionSet();
            options.Add("b|base-path=",
                        "base path for searching for sln / csproj files",
                        x => optionValues.BasePath = x);
            options.Add("c|compile",
                        "compile (using msbuild) the inputs using the calculated dependency order",
                        x => optionValues.Build = (null != x));
            options.Add("p|print-slns",
                        "print the .sln files of all dependencies in the calculated dependency order",
                        x => optionValues.PrintSolutionBuildOrder = (null != x));
            options.Add("x|exclude=", 
                        "exclude this .sln when resolving dependency order (useful when temporarily ignoring cyclic dependencies)", 
                        x => exlcudedSlns.Add(x));
            options.Add("v|verbose",
                        "print verbose output (will go to stderr)",
                        x => optionValues.Verbose = (null != x));
            options.Add("g|graphviz",
                        "generate graphviz output",
                        x => optionValues.GenerateGraphviz = (null != x));
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

            return ValidateOptions(optionValues, userRequestsHelp, options, errorMessage);
        }

        protected static bool ValidateOptions(OptionValues optionValues, bool userRequestsHelp, OptionSet options, string errorMessage)
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
            else if (String.IsNullOrWhiteSpace(optionValues.BasePath))
            {
                message = "Missing base path";
            }
            else if (false == System.IO.Directory.Exists(optionValues.BasePath))
            {
                message = "Base path does not exist: '" + optionValues.BasePath + "'";
            }

            if (null != message)
            {
                ShowHelp(message, options);
                return false;
            }
            return true;
        }

        protected static void ShowHelp(string message, OptionSet options)
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
