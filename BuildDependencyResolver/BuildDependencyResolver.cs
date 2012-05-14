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
        public static IEnumerable<Project> BuildOrder(IEnumerable<Project> projects)
        {
            return DependencyGraph(projects, true)
                           .TopologicalSort();
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
        public static AdjacencyGraph<Project, SEdge<Project>> DependencyGraph(IEnumerable<Project> projects, bool reverse)
        {
            return projects.SelectMany(x => x.DeepDependencies())
                           .Distinct()
                           .Select(x => new SEdge<Project>(reverse ? x.Key : x.Value, reverse ? x.Value : x.Key))
                           .ToAdjacencyGraph<Project, SEdge<Project>>(false);
        }
    }
}
