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
        private static string CSPROJ_EXTENSION = ".csproj";
        private static string SLN_EXTENSION = ".sln";

        static int Main(string[] args)
        {
            var exlcudedSlns = new List<string>();
            var inputFiles = new List<string>();
            string basePath;
            if (false == ParseOptions(args, exlcudedSlns, inputFiles, out basePath))
            {
                return 1;
            }

            PrintProjectDependencies(inputFiles, exlcudedSlns, basePath);

            return 0;
        }

        private static string CanonicalPath(string x)
        {
            return System.IO.Path.GetFullPath(x.Trim());
        }

        private static void PrintProjectDependencies(IEnumerable<string> _inputFiles, IEnumerable<string> _excludedSLNs, string basePath)
        {
            var inputFiles = _inputFiles.Select(CanonicalPath);
            var excludedSLNs = _excludedSLNs.Select(CanonicalPath)
                                            .Select(x => x.ToLowerInvariant())
                                            .ToArray();

            var projectFinder = new ProjectFinder(basePath, true);

            //var csProjInputs = 
            // TODO : Accept also SLN files
            var projectFiles = inputFiles.Where(x => HasExtension(x, CSPROJ_EXTENSION));
            var slnFiles = inputFiles.Where(x => HasExtension(x, SLN_EXTENSION));

            var csprojProjects = projectFiles.Select(Project.FromCSProj);
            var slnProjects = slnFiles.SelectMany(projectFinder.GetProjectsOfSLN);

            var projects = csprojProjects.Union(slnProjects).ToArray();
            PrintInputInfo(excludedSLNs, projectFiles, slnFiles, projects);

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
        }

        private static void PrintInputInfo(string[] excludedSLNs, IEnumerable<string> projectFiles, IEnumerable<string> slnFiles, Project[] projects)
        {
            Console.Error.WriteLine("Input CSPROJ files:\n\t" + String.Join("\n\t", projectFiles));
            Console.Error.WriteLine("Input SLN files:\n\t" + String.Join("\n\t", slnFiles));
            Console.Error.WriteLine("Input projects:\n\t" + String.Join("\n\t", projects.Select(x => x.Path)));

            Console.Error.WriteLine("Excluding solutions:\n\t" + String.Join("\n\t", excludedSLNs));
        }

        private static bool HasExtension(string fileName, string extension)
        {
            return fileName.ToLowerInvariant().EndsWith(extension);
        }

        private static bool ParseOptions(string[] args, List<string> exlcudedSlns, List<string> inputFiles, out string basePath)
        {
            bool userRequestsHelp = false;
            bool showHelp = false;
            var options = new OptionSet();

            string _basePath = null;
            basePath = null;


            options.Add("b|basePath=",
                        "base path for searching for sln / csproj files",
                        x => _basePath = x);
            options.Add("x|exclude=", 
                        "exclude this .sln when resolving dependency order (useful when temporarily ignoring cyclic dependencies)", 
                        x => exlcudedSlns.Add(x));
            options.Add("h|help", 
                        "show help", 
                        x => userRequestsHelp = (null != x));

            string message = String.Empty;

            try
            {
                inputFiles.AddRange(options.Parse(args));
                basePath = _basePath;
            }
            catch (OptionException e)
            {
                message = e.Message;
                showHelp = true;
            }

            if (userRequestsHelp)
            {
                message = "Showing Help";
                showHelp = true;
            }
            else if (String.IsNullOrWhiteSpace(basePath))
            {
                message = "Missing base path";
                showHelp = true;
            }
            else if (false == System.IO.Directory.Exists(basePath))
            {
                message = "Base path does not exist: '" + basePath + "'";
                showHelp = true;
            }

            if (showHelp) 
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
