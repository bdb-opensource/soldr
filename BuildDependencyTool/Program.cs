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
using System.Text.RegularExpressions;
using BuildDependencyReader.Common;

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
        public bool UpdateComponents;
        public int RecursionLevel = -1;
        public Regex[] MatchingAssemblyRegexes;
        public bool FlipIgnore;
        public bool IgnoreMissingAssemblies;
        //public bool Dependents;
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

            try
            {
                PerformCommands(exlcudedSlns, inputFiles, optionValues);
            }
            catch (Exception e)
            {
                _logger.Error("Error reached top-level. To view exception stack trace and details use verbose (-v) flag");
                _logger.Info("Error reached top-level, details:\n", e);
                return 2;
            }

            return 0;
        }

        private static void PerformCommands(List<string> exlcudedSlns, List<string> inputFiles, OptionValues optionValues)
        {
            var projectFinder = new ProjectFinder(optionValues.BasePath, true);

            foreach (var project in projectFinder.AllProjectsInPath())
            {
                ValidateProject(project, optionValues);
            }

            var dependencyInfo = BuildDependencyResolver.BuildDependencyResolver
                                                        .GetDependencyInfo(projectFinder, inputFiles, exlcudedSlns, optionValues.RecursionLevel); //, optionValues.Dependents);

            if (optionValues.GenerateGraphviz)
            {
                GenerateGraphViz(dependencyInfo.SolutionDependencyGraph);
            }

            if (optionValues.PrintSolutionBuildOrder)
            {
                PrintSolutionBuildOrder(dependencyInfo);
            }

            if (optionValues.UpdateComponents)
            {
                PerformUpdateComponents(projectFinder, dependencyInfo, optionValues);
            }
            else if (optionValues.Build)
            {
                PerformBuild(projectFinder, dependencyInfo, optionValues);
            }
        }

        private static void ValidateProject(Project project, OptionValues optionValues)
        {
            project.ValidateHintPaths(optionValues.MatchingAssemblyRegexes, optionValues.FlipIgnore);
            var basePath = PathExtensions.GetFullPath(optionValues.BasePath).Trim();
            foreach (var assemblyReference in project.AssemblyReferences.Where(x => false == String.IsNullOrWhiteSpace(x.HintPath)))
            {
                if (System.IO.Path.IsPathRooted(assemblyReference.ExplicitHintPath))
                {
                    var errorMessage = String.Format("Absolute path found in HintPath in assembly reference '{0}', project: '{1}' (will break easily when trying to compile on another machine!)", assemblyReference, project);
                    _logger.Warn(errorMessage);
                }
                var hintPathPrefix = PathExtensions.GetFullPath(assemblyReference.HintPath.Trim())
                                                   .Substring(0, basePath.Length);
                if (false == basePath.Equals(
                        hintPathPrefix, 
                        StringComparison.InvariantCultureIgnoreCase))
                {
                    var errorMessage = String.Format(
                        "HintPath is outside the given base path for finding projects in assembly reference '{0}', project: '{1}', base path: '{2}' expected to equal the prefix of the hint path: '{3}'",
                        assemblyReference, project, basePath, hintPathPrefix);
                    _logger.Warn(errorMessage);
                }
            }
        }

        protected static void UpdateLog4NetLevel(bool verbose)
        {
            Level level = verbose ? log4net.Core.Level.Trace
                                  : log4net.Core.Level.Warn;
            log4net.LogManager.GetRepository().Threshold = level;
        }

        protected static void PrintSolutionBuildOrder(BuildDependencyInfo dependencyInfo)
        {
            foreach (var solutionFileName in GetDependencySortedSolutionNames(dependencyInfo))
            {
                Console.WriteLine(solutionFileName);
            }
        }

        private static IEnumerable<string> GetDependencySortedSolutionNames(BuildDependencyInfo dependencyInfo)
        {
            try
            {
                return dependencyInfo.TrimmedSolutionDependencyGraph.TopologicalSort();
            }
            catch (NonAcyclicGraphException)
            {
                _logger.Error("Can't resolve solution dependencies because there is a cyclic dependency somewhere in the graph. Use the graph output option for more information on the dependecy graph.");
                throw;
            }
        }

        protected static void PerformUpdateComponents(ProjectFinder projectFinder, BuildDependencyInfo dependencyInfo, OptionValues optionValues)
        {
            var graph = dependencyInfo.TrimmedSolutionDependencyGraph;
            var sortedSolutions = graph.TopologicalSort();
            if (optionValues.Build)
            {
                foreach (var solutionFileName in sortedSolutions.Where(x => graph.OutEdges(x).Any()))
                {
                    Builder.BuildSolution(projectFinder, solutionFileName, optionValues.MatchingAssemblyRegexes, optionValues.FlipIgnore, optionValues.IgnoreMissingAssemblies);
                }
            }
            foreach (var solutionFileName in sortedSolutions.Where(x => false == graph.OutEdges(x).Any()))
            {
                Builder.UpdateComponentsFromBuiltProjects(projectFinder, solutionFileName, optionValues.MatchingAssemblyRegexes, optionValues.FlipIgnore, optionValues.IgnoreMissingAssemblies);
            }
        }

        protected static void PerformBuild(ProjectFinder projectFinder, BuildDependencyInfo dependencyInfo, OptionValues optionValues)
        {
            foreach (var solutionFileName in dependencyInfo.TrimmedSolutionDependencyGraph.TopologicalSort())
            {
                Builder.BuildSolution(projectFinder, solutionFileName, optionValues.MatchingAssemblyRegexes, optionValues.FlipIgnore, optionValues.IgnoreMissingAssemblies);
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
            _logger.InfoFormat("Dependency graph written to: " + fileName);
            Console.Error.WriteLine("Dependency graph written to: " + fileName);
            if (Environment.UserInteractive)
            {
                Process.Start(fileName);
            }
        }

        

        protected static bool ParseOptions(string[] args, List<string> exlcudedSlns, List<string> inputFiles, OptionValues optionValues)
        {
            bool userRequestsHelp = false;

            optionValues.BasePath = null;
            optionValues.Verbose = false;
            optionValues.GenerateGraphviz = false;
            optionValues.Build = false;
            optionValues.PrintSolutionBuildOrder = false;
            var assemblyMatchPatterns = new List<Regex>();

            var options = new OptionSet();
            options.Add("b|base-path=",
                        "(required) Base path for searching for sln / csproj files.",
                        x => optionValues.BasePath = x);
            options.Add("c|compile",
                        "Compile (using msbuild) the inputs using the calculated dependency order.",
                        x => optionValues.Build = (null != x));
            options.Add("u|update-dependencies",
                        "Update dependencies (components) of the input solutions. Finds the project that builds each dependent assembly and copies the project's outputs to the HintPath given in the input project's definition (.csproj).\nCombine this with -c (--compile) to also compile whatever is neccesary for building the dependency assemblies and then copy them.",
                        x => optionValues.UpdateComponents = (null != x));
            options.Add("r=|recursion-level=",
                          "How many levels should the builder recurse when building a project's dependencies. Default is infinity (you can specify it by passing -1)." + Environment.NewLine
                        + "Zero means only the direct dependencies of the project itself will be considered.",
                        (int x) => optionValues.RecursionLevel = x);
            options.Add("p|print-slns",
                        "Print the .sln files of all dependencies in the calculated dependency order",
                        x => optionValues.PrintSolutionBuildOrder = (null != x));
            options.Add("x|exclude=", 
                        "Exclude this .sln when resolving dependency order (useful when temporarily ignoring cyclic dependencies)", 
                        x => exlcudedSlns.Add(x));
            //options.Add("dependents",
            //            "Finds anything that depends on the projects/solutions and performs operations as if they were the inputs (builds, prints, updates components, etc. on the dependents)",
            //            x => optionValues.Dependents = (null != x));
            options.Add("m=|match-assembly=",
                        "When finding dependencies and copying components, ignore ALL referenced assemblies EXCEPT those matching the given regex pattern (case insensitive). May be given multiple times to accumulate patterns. Useful for ignoring system and third-party assemblies.",
                        x => assemblyMatchPatterns.Add(new Regex(x, RegexOptions.IgnoreCase)));
            options.Add("flip-ignore",
                        "Flips the meaning of match-assembly (-m) to ignore ONLY the matched patterns, and not ignore anything that doesn't match.",
                        x => optionValues.FlipIgnore = (null != x));
            options.Add("ignore-missing",
                        "When copying dependency assemblies (components), ignore those that are missing - meaning, the ones that can't be copied because the compiled assembly file to be copied is not found.",
                        x => optionValues.IgnoreMissingAssemblies = (null != x));
            options.Add("v|verbose",
                        "Print verbose output (will go to stderr)",
                        x => optionValues.Verbose = (null != x));
            options.Add("g|graph",
                        "Generate dependency graph output (requires GraphViz)",
                        x => optionValues.GenerateGraphviz = (null != x));
            options.Add("h|help", 
                        "Show help", 
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
            optionValues.MatchingAssemblyRegexes = assemblyMatchPatterns.ToArray();
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
            Console.Error.WriteLine();
            options.WriteOptionDescriptions(Console.Error);
        }

        static void graphviz_FormatVertex(object sender, FormatVertexEventArgs<String> e)
        {
            e.VertexFormatter.Label = e.Vertex.Replace("\\", "\\\\");
        }
    }
}
