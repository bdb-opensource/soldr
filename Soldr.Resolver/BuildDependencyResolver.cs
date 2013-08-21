using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Soldr.ProjectFileParser;
using QuickGraph;
using QuickGraph.Algorithms;
using Soldr.Common;

namespace Soldr.Resolver
{
    public class BuildDependencyResolver
    {
        protected static readonly log4net.ILog _logger = log4net.LogManager.GetLogger(
                   System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public const string CSPROJ_EXTENSION = ".csproj";
        public const string SLN_EXTENSION = ".sln";


        public static IEnumerable<Project> BuildOrder(IProjectFinder projectFinder, IEnumerable<Project> projects, int maxRecursionLevel)
        {
            return ProjectDependencyGraph(projectFinder, projects, true, maxRecursionLevel).TopologicalSort();
        }

        /// <summary>
        /// Creates a graph representing all the dependencies within the given projects. 
        /// The edges will be from dependent project to dependency, unless <paramref name="reverse"/> is True, 
        /// in which case the edges will be from dependency to dependent (which is more useful for topological sorting - 
        /// which in this way will return the projects in build order)
        /// </summary>
        /// <param name="projects"></param>
        /// <returns></returns>
        public static AdjacencyGraph<Project, SEdge<Project>> ProjectDependencyGraph(IProjectFinder projectFinder, IEnumerable<Project> projects, bool reverse, int maxRecursionLevel)
        {
            // First add all the dependencies (edges)
            var graph = DeepDependencies(projectFinder, projects, false, maxRecursionLevel)
                    .Distinct()
                    .Select(x => new SEdge<Project>(reverse ? x.Key : x.Value, reverse ? x.Value : x.Key))
                    .ToAdjacencyGraph<Project, SEdge<Project>>(false);
            // Then make sure we have vertices for each input "root" project, regardless of if it has dependencies or not
            graph.AddVertexRange(projects);
            return graph;
        }

        /// <summary>
        /// Builds a dependency graph between solutions. The vertices in the graph are the solution full file names.
        /// </summary>
        public static AdjacencyGraph<String, SEdge<String>> SolutionDependencyGraph(IProjectFinder projectFinder, IEnumerable<Project> projects, bool reverse, int maxRecursionLevel)
        {
            var graph = DeepDependencies(projectFinder, projects, true, maxRecursionLevel)
                        .Where(x => x.Key != x.Value)
                        .Select(x => ProjectEdgeToSLNEdge(projectFinder, x))
                        .Where(x => false == SolutionNamesEqual(x))
                        .Distinct()
                        .Select(x => new SEdge<String>(reverse ? x.Key : x.Value, reverse ? x.Value : x.Key))
                        .ToAdjacencyGraph<String, SEdge<String>>(false);
            graph.AddVertexRange(projects.Select(x => SLNVertexName(projectFinder, x)));
            return graph;
        }

        private static bool SolutionNamesEqual(KeyValuePair<string, string> x)
        {
            return x.Key.ToLowerInvariant().Equals(x.Value.ToLowerInvariant());
        }

        /// <summary>
        /// Builds a graph of dependencies between solution files, from a list of .csproj and .sln files. 
        /// </summary>
        /// <param name="inputFiles">Project (.csproj) and solution (.sln) files to start the dependency search from</param>
        /// <param name="_excludedSLNs">Solution (.sln) files that should be excluded from the final dependency graph - useful for temporarily ignoring cyclic dependencies. 
        /// Note that .sln files may appear in the final graph even if they are not given in the input files list, if something in the input depends on them.</param>
        /// <param name="maxRecursionLevel">How deep to resolve dependencies of the given inputs. 0 means no dependency resolution is performed. -1 means infinity.</param>
        public static BuildDependencyInfo GetDependencyInfo(IProjectFinder projectFinder, IEnumerable<string> inputFiles, IEnumerable<string> _excludedSLNs, int maxRecursionLevel) //, bool findAllDependents)
        {
            string[] projectFiles;
            string[] slnFiles;
            ProcessInputFiles(inputFiles.Select(CanonicalPath), out projectFiles, out slnFiles);

            var excludedSLNs = _excludedSLNs.Select(CanonicalPath)
                                            .Select(x => x.ToLowerInvariant())
                                            .ToArray();

            if (excludedSLNs.Any(x => false == SLN_EXTENSION.Equals(System.IO.Path.GetExtension(x))))
            {
                var errorMessage = "excluded files must have extension: " + SLN_EXTENSION;
                _logger.Error(errorMessage);
                throw new ArgumentException(errorMessage, "_excludedSLNs");
            }

            var csprojProjects = projectFiles.Select(Project.FromCSProj);
            var slnProjects = slnFiles.SelectMany(projectFinder.GetProjectsOfSLN);
            var inputProjects = csprojProjects.Union(slnProjects).ToArray();

            PrintInputInfo(excludedSLNs, projectFiles, slnFiles, inputProjects);

            //Project[] projectsForDependencyGraph = findAllDependents 
            //                                     ? projectFinder.AllProjectsInPath().ToArray() 
            //                                     : inputProjects;

            return new BuildDependencyInfo(ProjectDependencyGraph(projectFinder, inputProjects, false, maxRecursionLevel),
                                                SolutionDependencyGraph(projectFinder, inputProjects, false, maxRecursionLevel), 
                                                excludedSLNs);
        }

        protected static IEnumerable<Project> GetAllProjectsInSolutionsOfProject(IProjectFinder projectFinder, Project project)
        {
            return projectFinder.GetProjectsOfSLN(projectFinder.GetSLNFileForProject(project));
        }

        protected static KeyValuePair<string, string> ProjectEdgeToSLNEdge(IProjectFinder projectFinder, KeyValuePair<Project, Project> x)
        {
            return new KeyValuePair<String, String>(SLNVertexName(projectFinder, x.Key),
                                                    SLNVertexName(projectFinder, x.Value));
        }

        private static string SLNVertexName(IProjectFinder projectFinder, Project project)
        {
            return projectFinder.GetSLNFileForProject(project).FullName;
        }

        protected struct ResolvedProjectDependencyInfo
        {
            public readonly int RecursionLevel;
            public readonly Project Source;
            public readonly Project Target;
            public ResolvedProjectDependencyInfo(int _recursionLevel, Project _source, Project _target)
            {
                this.RecursionLevel = _recursionLevel;
                this.Source = _source;
                this.Target = _target;
            }
        }

        /// <summary>
        /// Finds all pairs of dependencies source -> target of projects that depend on each other.
        /// </summary>
        /// <param name="maxRecursionLevel">How deep to resolve dependencies of the given inputs. 0 means no dependency resolution is performed. -1 means infinity.</param>
        /// <returns></returns>
        public static IEnumerable<KeyValuePair<Project, Project>> DeepDependencies(IProjectFinder projectFinder, IEnumerable<Project> projects, bool includeAllProjectsInSolution, int maxRecursionLevel)
        {
            var projectsToTraverse = new Queue<ResolvedProjectDependencyInfo>(projects.Select(x => new ResolvedProjectDependencyInfo(0, x, x)));

            var traversedProjects = new HashSet<Project>();

            while (projectsToTraverse.Any())
            {
                var projectPair = projectsToTraverse.Dequeue();
                var project = projectPair.Target;

                if (projectPair.Source != projectPair.Target)
                {
                    yield return new KeyValuePair<Project, Project>(projectPair.Source, projectPair.Target);
                }

                if ((0 <= maxRecursionLevel) && (projectPair.RecursionLevel >= maxRecursionLevel))
                {
                    continue;
                }

                if (traversedProjects.Contains(project))
                {
                    continue;
                }
                traversedProjects.Add(project);

                if (includeAllProjectsInSolution)
                {
                    foreach (var projectInSameSolution in GetAllProjectsInSolutionsOfProject(projectFinder, project)
                                                            .Where(x => false == traversedProjects.Contains(x)))
                    {
                        projectsToTraverse.Enqueue(new ResolvedProjectDependencyInfo(projectPair.RecursionLevel + 1, projectInSameSolution, projectInSameSolution));
                    }
                }
                foreach (var subProject in project.ProjectReferences)
                {
                    projectsToTraverse.Enqueue(new ResolvedProjectDependencyInfo(projectPair.RecursionLevel + 1, project, subProject));
                }
                // TODO: Why do we allow a null projectFinder at all here?
                if (null != projectFinder)
                {
                    foreach (var assemblySubProject in project.AssemblyReferences.SelectMany(projectFinder.FindProjectForAssemblyReference))
                    {
                        projectsToTraverse.Enqueue(new ResolvedProjectDependencyInfo(projectPair.RecursionLevel + 1, project, assemblySubProject));
                    }
                }
            }
        }

        protected static void ProcessInputFiles(IEnumerable<string> inputFiles, out string[] projectFiles, out string[] slnFiles)
        {
            slnFiles = new string[] { };
            projectFiles = new string[] { };
            var filesByExtensions = inputFiles.GroupBy(System.IO.Path.GetExtension);
            foreach (var extensionGroup in filesByExtensions)
            {
                switch (extensionGroup.Key)
                {
                    case CSPROJ_EXTENSION:
                        projectFiles = extensionGroup.ToArray();
                        break;
                    case SLN_EXTENSION:
                        slnFiles = extensionGroup.ToArray();
                        break;

                    default:
                        {
                            var errorMessage = String.Format("Unknown file type: '{0}' in {1}", extensionGroup.Key, String.Join(", ", extensionGroup));
                            _logger.Error(errorMessage);
                            throw new ArgumentException(errorMessage, "_inputFiles");
                        }
                }
            }
        }

        protected static void PrintInputInfo(string[] excludedSLNs, IEnumerable<string> projectFiles, IEnumerable<string> slnFiles, Project[] projects)
        {
            if (projectFiles.Any())
            {
                _logger.DebugFormat("Input CSPROJ files:\n" + StringExtensions.Tabify(projectFiles));
            }
            if (slnFiles.Any())
            {
                _logger.DebugFormat("Input SLN files:\n" + StringExtensions.Tabify(slnFiles));
            }
            if (projects.Any())
            {
                _logger.DebugFormat("Input projects:\n" + StringExtensions.Tabify(projects.Select(x => x.Path)));
            }

            if (excludedSLNs.Any())
            {
                _logger.InfoFormat("Excluding solutions:\n" + StringExtensions.Tabify(excludedSLNs));
            }
        }



        protected static string CanonicalPath(string x)
        {
            return PathExtensions.GetFullPath(x.Trim());
        }

    }
}

