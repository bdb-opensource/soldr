using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuickGraph;
using BuildDependencyReader.ProjectFileParser;

namespace BuildDependencyReader.BuildDependencyResolver
{
    public class BuildDependencyInfo
    {
        /// <summary>
        /// Full dependency graph including for each project all depenencies, including resolved assembly references. May be cyclic.
        /// </summary>
        public readonly AdjacencyGraph<Project, SEdge<Project>> FullProjectDependencyGraph;
        /// <summary>
        /// Same as 'SolutionDependencyGraph' - but without the excluded solutions (useful for dependency sorting when a cyclic reference exists)
        /// </summary>
        public readonly AdjacencyGraph<String, SEdge<String>> TrimmedSolutionDependencyGraph;
        /// <summary>
        /// Graph of dependencies between solution (absolute) file names that build the projects in 'FullProjectDependencyGraph'
        /// </summary>
        public readonly AdjacencyGraph<String, SEdge<String>> SolutionDependencyGraph;

        public BuildDependencyInfo(AdjacencyGraph<Project, SEdge<Project>> _fullProjectDependencyGraph, AdjacencyGraph<string, SEdge<string>> _solutionDependencyGraph, string[] excludedSLNs)
        {
            this.FullProjectDependencyGraph = _fullProjectDependencyGraph;
            this.SolutionDependencyGraph = _solutionDependencyGraph;

            this.TrimmedSolutionDependencyGraph = new AdjacencyGraph<string,SEdge<string>>();
            // The following is only safe because our vertices are immutable values (strings):
            this.TrimmedSolutionDependencyGraph.AddVertexRange(_solutionDependencyGraph.Vertices);
            this.TrimmedSolutionDependencyGraph.AddEdgeRange(_solutionDependencyGraph.Edges);
            this.TrimmedSolutionDependencyGraph.RemoveVertexIf(x => excludedSLNs.Contains(x.ToLowerInvariant()));
        }

    }
}
