using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using log4net.Core;
using Mono.Options;
using QuickGraph;
using QuickGraph.Algorithms;
using QuickGraph.Graphviz;
using Soldr.Common;
using Soldr.ProjectFileParser;
using Soldr.Resolver;

namespace Soldr.PrintProjectDependencies
{
    internal class DotEngine : IDotEngine
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

    internal class OptionValues
    {
        public string BasePath;
        public bool GenerateGraphviz;
        public log4net.Core.Level LogLevel = log4net.Core.Level.Warn;
        public bool PrintSolutionBuildOrder;
        public bool Build;
        public bool UpdateComponents;
        public int RecursionLevel = -1;
        public Regex[] MatchingAssemblyRegexes;
        public bool FlipIgnore;
        public bool IgnoreMissingAssemblies;
        public bool CleanBeforeBuild = true;
        public bool RunTests;
        public bool IgnoreFailedTests;
        public bool IncludeAllSLNsAsInputs;
        public bool OutputMultipleMSBuildFiles;
        public bool GenerateMSBuildFiles;
        public bool GenerateNUSpecFiles;
        public bool PrintProjectBuildOrder;
        public bool NUSpecWithoutDeps;
        public bool GenerateNugetPackagesConfig;
    }

    internal class Program
    {
        protected static readonly log4net.ILog _logger = log4net.LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        protected static readonly Level[] _levels = { Level.Error, Level.Warn, Level.Info, Level.Debug };
        private static string MSBUILD_OUTPUT_FILENAME = "build.proj";

        private static Level IncreaseLogLevel(Level level)
        {
            return _levels.FirstOrDefault(x => x.Value < level.Value) ?? Level.All;
        }

        private static int Main(string[] args)
        {
            log4net.Config.XmlConfigurator.Configure();

            var exlcudedSlns = new List<string>();
            var inputFiles = new List<string>();
            var optionValues = new OptionValues();

            if (false == ParseOptions(args, exlcudedSlns, inputFiles, optionValues))
            {
                return 1;
            }

            UpdateLog4NetLevel(optionValues.LogLevel);

            try
            {
                PerformCommands(new HashSet<string>(exlcudedSlns), new HashSet<string>(inputFiles), optionValues);
            }
            catch (Exception e)
            {
                _logger.Error("Error reached top-level. To view exception stack trace and details use verbose (-v) flag");
                _logger.Info("Error reached top-level, details:\n", e);
                return 2;
            }

            return 0;
        }

        private static void PerformCommands(ISet<string> exlcudedSlns, ISet<string> inputFiles, OptionValues optionValues)
        {
            var projectFinder = new ProjectFinder(optionValues.BasePath, true);

            foreach (var project in projectFinder.AllProjectsInPath())
            {
                ValidateProject(project, optionValues);
            }

            if (optionValues.IncludeAllSLNsAsInputs)
            {
                var slnFiles = projectFinder.AllProjectsInPath()
                                            .Where(projectFinder.ProjectHasMatchingSLNFile)
                                            .Select(projectFinder.GetSLNFileForProject)
                                            .Select(x => x.FullName)
                                            .Distinct();
                _logger.Info("Adding SLN inputs:");
                foreach (var slnFile in slnFiles.OrderBy(x => x))
                {
                    inputFiles.Add(slnFile);
                    _logger.Info("\t" + slnFile);
                }
            }

            var dependencyInfo = Soldr.Resolver.BuildDependencyResolver.GetDependencyInfo(projectFinder, inputFiles, exlcudedSlns, optionValues.RecursionLevel); //, optionValues.Dependents);

            if (optionValues.GenerateGraphviz)
            {
                GenerateGraphViz(dependencyInfo.SolutionDependencyGraph);
            }

            if (optionValues.PrintSolutionBuildOrder)
            {
                PrintSolutionBuildOrder(dependencyInfo);
            }

            if (optionValues.PrintProjectBuildOrder)
            {
                PrintProjectBuildOrder(dependencyInfo);
            }

            if (optionValues.GenerateMSBuildFiles)
            {
                GenerateMSBuildFiles(dependencyInfo, false == optionValues.OutputMultipleMSBuildFiles);
            }

            if (optionValues.GenerateNUSpecFiles)
            {
                GenerateNUSpecFiles(dependencyInfo, optionValues);
            }

            if (optionValues.GenerateNugetPackagesConfig)
            {
                GenerateNugetPackagesConfig(dependencyInfo, optionValues);
            }

            if (optionValues.UpdateComponents)
            {
                PerformUpdateComponents(projectFinder, dependencyInfo, optionValues);
                Console.Error.WriteLine("Dependencies updated.");
            }
            else if (optionValues.Build)
            {
                PerformBuild(projectFinder, dependencyInfo, optionValues);
                Console.Error.WriteLine("Build complete.");
            }
        }

        private static void PrintProjectBuildOrder(BuildDependencyInfo dependencyInfo)
        {
            foreach (var project in dependencyInfo.FullProjectDependencyGraph.TopologicalSort())
            {
                Console.WriteLine(project.Path);
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

        protected static void UpdateLog4NetLevel(log4net.Core.Level level)
        {
            log4net.LogManager.GetRepository().Threshold = level;
        }

        protected static void GenerateMSBuildFiles(BuildDependencyInfo dependencyInfo, bool singleFile)
        {
            GenerateMSBuildProjFiles(dependencyInfo, singleFile);
        }

        protected static void PrintSolutionBuildOrder(BuildDependencyInfo dependencyInfo)
        {
            foreach (var solutionFileName in GetDependencySortedSolutionNames(dependencyInfo))
            {
                Console.WriteLine(solutionFileName);
            }
        }

        protected static void GenerateNUSpecFiles(BuildDependencyInfo dependencyInfo, OptionValues optionValues)
        {
            foreach (var project in dependencyInfo.FullProjectDependencyGraph.Vertices)
            {
                StringBuilder dependenciesBuilder = new StringBuilder();
                if (false == optionValues.NUSpecWithoutDeps)
                {
                    var edges = dependencyInfo.FullProjectDependencyGraph.Edges.Where(x => x.Target == project);
                    foreach (var edge in edges)
                    {
                        dependenciesBuilder.AppendFormat("        <dependency id=\"{0}\" version=\"\" />\n",
                            edge.Source.Name
                            //edge.Target.ver
                            );
                    }
                }
                var data = String.Format(@"<?xml version=""1.0""?>
<package >
  <metadata>
    <id>{0}</id>
    <version>$version$</version>
    <title>{0}</title>
    <authors>Unknown</authors>
    <description>Built from {1}</description>
    <dependencies>
{2}
    </dependencies>

  </metadata>
</package>
",
                    project.Name,
                    project.Path,
                    dependenciesBuilder.ToString());

                var fileName = NUSpecFileName(project);
                _logger.InfoFormat("Generating file: {0} for project {1}", fileName, project.Name);
                File.WriteAllText(fileName, data);
            }
        }

        protected static void GenerateNugetPackagesConfig(BuildDependencyInfo dependencyInfo, OptionValues optionValues)
        {
            var versions = new Dictionary<string, string>();
            foreach (var project in dependencyInfo.FullProjectDependencyGraph.Vertices)
            {
                StringBuilder dependenciesBuilder = new StringBuilder();
                var edges = dependencyInfo.FullProjectDependencyGraph.Edges.Where(x => x.Target == project);
                foreach (var edge in edges)
                {
                    string version;
                    if (project.ProjectReferences.Contains(edge.Source))
                    {
                        continue;
                    }
                    var dependencyName = edge.Source.Name;
                    if (false == versions.TryGetValue(dependencyName, out version)) {
                        var processStartInfo = new ProcessStartInfo("nuget.exe", "list " + dependencyName);
                        processStartInfo.UseShellExecute = false;
                        processStartInfo.RedirectStandardOutput = true;
                        processStartInfo.WorkingDirectory = System.Environment.CurrentDirectory;
                        var process = Process.Start(processStartInfo);
                        while (true) {
                            var line = process.StandardOutput.ReadLine();
                            if (null == line)
                            {
                                version = "unknown";
                                break;
                            } 
                            var parts = line.Split(' ');
                            if (parts[0].Equals(dependencyName, StringComparison.InvariantCultureIgnoreCase))
                            {
                                version = parts[1];
                                break;
                            }
                        }
                        _logger.DebugFormat("{0} = {1}", dependencyName, version);
                        versions[dependencyName] = version;
                    }
                    dependenciesBuilder.AppendFormat("    <package id=\"{0}\" version=\"{1}\" />\n",
                        dependencyName,
                        version
                        );
                }
                var data = String.Format(@"<?xml version=""1.0"" encoding=""utf-8""?>
<packages>
{0}
</packages>
",
                dependenciesBuilder.ToString());
                var fileName = PackagesConfigFileName(project);
                _logger.InfoFormat("Generating file: {0} for project {1}", fileName, project.Name);
                File.WriteAllText(fileName, data);
            }
        }

        protected static void GenerateMSBuildProjFiles(BuildDependencyInfo dependencyInfo, bool singleFile)
        {
            var singleFileData = new StringBuilder();
            AppendMSBuildProjectPrefix(singleFileData, new string[] { });
            var solutionFileNames = GetDependencySortedSolutionNames(dependencyInfo);
            foreach (var solutionFileName in solutionFileNames)
            {
                var targetString = GenerateMSBuildTarget(dependencyInfo, solutionFileName);
                if (false == singleFile)
                {
                    var specificFileBuilder = new StringBuilder();
                    AppendMSBuildProjectPrefix(specificFileBuilder, new string[] { SLNToTargetName(solutionFileName) });
                    AppendMSBuildDependencyTargetImports(dependencyInfo, solutionFileName, specificFileBuilder);
                    specificFileBuilder.AppendLine(targetString);
                    AppendMSBuildProjectSuffix(specificFileBuilder);
                    File.WriteAllText(TargetMSBuildOutputFileName(solutionFileName), specificFileBuilder.ToString());
                }
                else
                {
                    singleFileData.AppendLine(targetString);
                }
            }
            if (singleFile)
            {
                var allTargets = String.Join(";", solutionFileNames.Select(SLNToTargetName));
                singleFileData.AppendLine("<Target Name=\"All\" DependsOnTargets=\"" + allTargets + "\"></Target>");
                AppendMSBuildProjectSuffix(singleFileData);
                File.WriteAllText(MSBUILD_OUTPUT_FILENAME, singleFileData.ToString());
            }
        }

        private static void AppendMSBuildDependencyTargetImports(BuildDependencyInfo dependencyInfo, string solutionFileName, StringBuilder specificFileBuilder)
        {
            foreach (var dependencySolutionFileName in GetSLNDependencies(dependencyInfo, solutionFileName))
            {
                specificFileBuilder.AppendLine(
                    String.Format("\t<Import Project=\"{0}\"/>", TargetMSBuildOutputFileName(dependencySolutionFileName)));
            }
        }

        private static string TargetMSBuildOutputFileName(string solutionFileName)
        {
            return Path.Combine(Path.GetDirectoryName(solutionFileName), MSBUILD_OUTPUT_FILENAME);
        }

        private static string NUSpecFileName(Project project)
        {
            return Path.Combine(Path.GetDirectoryName(project.Path), Path.GetFileNameWithoutExtension(project.Path) + ".nuspec");
        }

        private static string PackagesConfigFileName(Project project)
        {
            return Path.Combine(Path.GetDirectoryName(project.Path), "packages.config");
        }
        
        private static void AppendMSBuildProjectPrefix(StringBuilder specificFileBuilder, string[] defaultTargets)
        {
            specificFileBuilder.AppendLine(
                String.Format("<Project  DefaultTargets=\"{0}\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">",
                    String.Join(";", defaultTargets)
                ));
        }

        private static void AppendMSBuildProjectSuffix(StringBuilder singleFileData)
        {
            singleFileData.AppendLine("</Project>");
        }

        private static string GenerateMSBuildTarget(BuildDependencyInfo dependencyInfo, string solutionFileName)
        {
            var stringBuilder = new StringBuilder();
            var targetName = SLNToTargetName(solutionFileName);
            stringBuilder.AppendLine(Environment.NewLine + "\t<!-- " + solutionFileName + "-->");
            var dependencyTargets = GetSLNDependencies(dependencyInfo, solutionFileName)
                              .Select(x => SLNToTargetName(x));

            var dependencyTargetsList = String.Join(";", dependencyTargets);
            stringBuilder.AppendLine(String.Format(
                "\t<Target Name=\"{0}\" DependsOnTargets=\"{1}\">",
                targetName,
                dependencyTargetsList));
            foreach (var dependencyTarget in dependencyTargets)
            {
                stringBuilder.AppendLine(
                    String.Format("\t\t<Copy SourceFiles=\"@({0}_Outputs)\" DestinationFolder=\"{1}\\Components\\{2}\" SkipUnchangedFiles=\"True\"/>",
                        dependencyTarget,
                        Path.GetDirectoryName(solutionFileName),
                        dependencyTarget.Replace("_", ".")
                    ));
            }

            stringBuilder.AppendLine(String.Format("\t\t<MSBuild Projects=\"{0}\" ToolsVersion=\"4.0\">", solutionFileName));
            stringBuilder.AppendLine(String.Format("\t\t\t<Output ItemName=\"{0}_Outputs\" TaskParameter=\"TargetOutputs\"/>",
                targetName));
            stringBuilder.AppendLine("\t\t</MSBuild>");

            stringBuilder.AppendLine("\t</Target>");
            return stringBuilder.ToString();
        }

        private static IEnumerable<string> GetSLNDependencies(BuildDependencyInfo dependencyInfo, string solutionFileName)
        {
            return dependencyInfo.SolutionDependencyGraph
                                          .Edges
                                          .Where(x => x.Target == solutionFileName)
                                          .Select(x => x.Source);
        }

        private static string SLNToTargetName(string solutionFileName)
        {
            return Path.GetFileNameWithoutExtension(solutionFileName).Replace(".", "_");
        }

        private static IEnumerable<string> GetDependencySortedSolutionNames(BuildDependencyInfo dependencyInfo)
        {
            try
            {
                return dependencyInfo.TrimmedSolutionDependencyGraph.TopologicalSort();
            }
            catch (NonAcyclicGraphException)
            {
                _logger.Error("Cyclic dependency found - can't resolve solution dependencies because there is a cyclic dependency somewhere in the graph. Use the graph output option for more information on the dependecy graph.");
                throw;
            }
        }

        protected static void PerformUpdateComponents(ProjectFinder projectFinder, BuildDependencyInfo dependencyInfo, OptionValues optionValues)
        {
            var graph = dependencyInfo.TrimmedSolutionDependencyGraph;
            var sortedSolutions = GetDependencySortedSolutionNames(dependencyInfo);
            if (optionValues.Build)
            {
                foreach (var solutionFileName in sortedSolutions.Where(x => graph.OutEdges(x).Any()))
                {
                    Builder.BuildSolution(projectFinder, solutionFileName, optionValues.MatchingAssemblyRegexes, optionValues.FlipIgnore, optionValues.IgnoreMissingAssemblies, optionValues.CleanBeforeBuild, optionValues.RunTests, optionValues.IgnoreFailedTests);
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
                Builder.BuildSolution(projectFinder, solutionFileName, optionValues.MatchingAssemblyRegexes, optionValues.FlipIgnore, optionValues.IgnoreMissingAssemblies, optionValues.CleanBeforeBuild, optionValues.RunTests, optionValues.IgnoreFailedTests);
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
            optionValues.GenerateGraphviz = false;
            optionValues.Build = false;
            optionValues.PrintSolutionBuildOrder = false;
            var assemblyMatchPatterns = new List<Regex>();

            var options = new OptionSet();
            options.LineWidth = 150;
            options.OptionWidth = 30;
            options.Add("b|base-path=",
                        "(required) Base path for searching for sln / csproj files.",
                        x => optionValues.BasePath = x);
            options.Add("all-slns",
                        "Find all .sln files under base path and use them as inputs.",
                        x => optionValues.IncludeAllSLNsAsInputs = (null != x));
            options.Add("o|output-proj",
                        "Generate a single MSBuild (named " + MSBUILD_OUTPUT_FILENAME + @") that includes all inputs,
with dependency information (can be used for building the inter-sln dependencies correctly using MSBuild)",
                        x => optionValues.GenerateMSBuildFiles = (null != x));
            options.Add("split-proj",
                        "(requires -o) Generates the MSBuild project file as multiple files - generates a one per .sln (named " + MSBUILD_OUTPUT_FILENAME + ", in the .sln's directory)",
                        x => optionValues.OutputMultipleMSBuildFiles = (null != x));
            options.Add("nuspec",
                        "Generate .nuspec files for nuget packaging, one next to each .csproj file",
                        x => optionValues.GenerateNUSpecFiles = (null != x));
            options.Add("nuspec-no-deps",
                        "(requires --nuspec) When generating .nuspec files, don't include dependencies explicitly - for use with nuget's option -IncludeReferencedProjects",
                        x => optionValues.NUSpecWithoutDeps = (null != x));
            options.Add("packages-config",
                        "Generate a nuget packages.config file, one next to each .csproj file",
                        x => optionValues.GenerateNugetPackagesConfig = (null != x));
            options.Add("c|compile",
                        @"Full compile of the given inputs. Combine with -u to only build the dependencies (but not the direct input solutions).
Includes (recursively on all dependencies, using the calculated dependency order):
1. Find the full dependency graph (can be limited by -r)
2. Update the dependency assemblies
3. Run msbuild",
                        x => optionValues.Build = (null != x));
            options.Add("no-clean", @"Don't clean (don't run msbuild /t:clean) before building. Default is to clean.", x => optionValues.CleanBeforeBuild = false == (null != x));
            options.Add("u|update-dependencies",
                        @"Update dependencies (components) of the input solutions.
Finds the project that builds each dependent assembly and copies the project's outputs to the HintPath given in the input project's definition (.csproj).
Combine this with -c (--compile) to also compile whatever is neccesary for building the dependency assemblies and then copy them.",
                        x => optionValues.UpdateComponents = (null != x));
            options.Add("r=|recursion-level=",
                          "How many levels should the builder recurse when building a project's dependencies. Default is infinity (you can specify it by passing -1)." + Environment.NewLine
                        + "Zero means only the direct dependencies of the project itself will be considered.",
                        (int x) => optionValues.RecursionLevel = x);
            options.Add("p|print-slns",
                        "Print the .sln files of all dependencies in the calculated dependency order",
                        x => optionValues.PrintSolutionBuildOrder = (null != x));
            options.Add("print-csprojs",
                        "Print the .csprojs files of all dependencies in the calculated dependency order",
                        x => optionValues.PrintProjectBuildOrder = (null != x));
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
                        "Flips the meaning of match-assembly (-m) to ignore ONLY the matched patterns, and not ignore anything that doesn't match. If no -m is given, this option is ignored and all assemblies are included in the build.",
                        x => optionValues.FlipIgnore = (null != x));
            options.Add("ignore-missing",
                        "When copying dependency assemblies (components), ignore those that are missing - meaning, the ones that can't be copied because the compiled assembly file to be copied is not found.",
                        x => optionValues.IgnoreMissingAssemblies = (null != x));
            options.Add("v|verbose",
                        "Verbose output. Repeat this flag (e.g. -vvv) for more verbose output (will go to stderr)",
                        x => optionValues.LogLevel = IncreaseLogLevel(optionValues.LogLevel));
            options.Add("g|graph",
                        "Generate dependency graph output (requires GraphViz)",
                        x => optionValues.GenerateGraphviz = (null != x));
            options.Add("t|run-tests",
                        "Run all unit tests (using MSTest)",
                        x => optionValues.RunTests = (null != x));
            options.Add("ignore-failed-tests",
                        "When running tests, don't stop the build on failure of tests.",
                        x => optionValues.IgnoreFailedTests = (null != x));
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

        private static void graphviz_FormatVertex(object sender, FormatVertexEventArgs<String> e)
        {
            e.VertexFormatter.Label = e.Vertex.Replace("\\", "\\\\");
        }
    }
}
