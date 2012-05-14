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
        public static IEnumerable<Project> SortByDependencies(IEnumerable<Project> projects)
        {
            return DependencyGraph(projects)
                           .TopologicalSort();
        }

        public static AdjacencyGraph<Project, SEdge<Project>> DependencyGraph(IEnumerable<Project> projects)
        {
            return projects.SelectMany(x => x.DeepDependencies())
                           .Distinct()
                           .Select(x => new SEdge<Project>(x.Key, x.Value))
                           .ToAdjacencyGraph<Project, SEdge<Project>>(false);
        }
    }
}
