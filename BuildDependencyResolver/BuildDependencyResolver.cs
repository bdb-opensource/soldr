using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildDependencyReader.ProjectFileParser;
using QuickGraph;
using QuickGraph.Algorithms;

namespace BuildDependencyReader.BuildDependencyResolver
{
    public class BuildDependencyResolver
    {
        public static IEnumerable<Project> BuildOrder(IProjectFinder projectFinder, IEnumerable<Project> projects)
        {
            return DependencyGraph(projectFinder, projects, true).TopologicalSort();
        }

        /// <summary>
        /// Creates a graph representing all the dependencies within the given projects. 
        /// The edges will be from dependent project to dependency, unless <paramref name="reverse"/> is True, 
        /// in which case the edges will be from dependency to dependent (which is more useful for topological sorting - 
        /// which in this way will return the projects in build order)
        /// </summary>
        /// <param name="projects"></param>
        /// <param name="direction"></param>
        /// <returns></returns>
        public static AdjacencyGraph<Project, SEdge<Project>> DependencyGraph(IProjectFinder projectFinder, IEnumerable<Project> projects, bool reverse)
        {
            return DeepDependencies(projectFinder, projects)
                    .Distinct()
                    .Select(x => new SEdge<Project>(reverse ? x.Key : x.Value, reverse ? x.Value : x.Key))
                    .ToAdjacencyGraph<Project, SEdge<Project>>(false);
        }


        public static IEnumerable<KeyValuePair<Project, Project>> DeepDependencies(IProjectFinder projectFinder, IEnumerable<Project> projects)
        {
            var projectsToTraverse = new Queue<KeyValuePair<Project, Project>>(projects.Select(x => new KeyValuePair<Project, Project>(x, x)));

            var traversedProjects = new HashSet<Project>();

            while (projectsToTraverse.Any())
            {
                var projectPair = projectsToTraverse.Dequeue();
                var project = projectPair.Value;

                if (projectPair.Key != projectPair.Value)
                {
                    yield return projectPair;
                }

                if (traversedProjects.Contains(project))
                {
                    continue;
                }
                traversedProjects.Add(project);

                foreach (var subProject in project.ProjectReferences)
                {
                    projectsToTraverse.Enqueue(new KeyValuePair<Project, Project>(project, subProject));
                }
                if (null != projectFinder)
                {
                    foreach (var assemblySubProject in project.AssemblyReferences.SelectMany(projectFinder.FindProjectForAssemblyReference))
                    {
                        projectsToTraverse.Enqueue(new KeyValuePair<Project, Project>(project, assemblySubProject));
                    }
                }
            }
        }

    }
}
