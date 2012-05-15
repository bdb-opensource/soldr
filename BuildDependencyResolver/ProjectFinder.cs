using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildDependencyReader.ProjectFileParser;
using System.IO;

namespace BuildDependencyReader.BuildDependencyResolver
{
    public class ProjectFinder : IProjectFinder
    {
        private string _searchRootPath;
        private HashSet<FileInfo> _CSProjFileInfos;
        private HashSet<FileInfo> _SLNFileInfos;
        private Project[] _projects;

        public ProjectFinder(string searchRootPath, bool allowAssemblyProjectAmbiguities)
        {
            ValidateDirectoryExists(searchRootPath);
            this._searchRootPath = searchRootPath;

            var directoryInfo = new DirectoryInfo(this._searchRootPath);
            this._SLNFileInfos = new HashSet<FileInfo>(directoryInfo.EnumerateFiles("*.sln", System.IO.SearchOption.AllDirectories));
            this._CSProjFileInfos = new HashSet<FileInfo>(directoryInfo.EnumerateFiles("*.csproj", System.IO.SearchOption.AllDirectories));

            this._projects = this._CSProjFileInfos
                                 .Select(x => Project.FromCSProj(x.FullName))
                                 .ToArray();


            var collidingProjects = this._projects.GroupBy(x => x.Name.ToLowerInvariant().Trim())
                                                  .Where(x => x.Count() > 1)
                                                  .ToArray();
            if (collidingProjects.Any())
            {
                var message = String.Format("Multiple projects with same name found - cannot realiably calculate assembly dependencies:\n\t{0}",
                                            String.Join("\n\t", collidingProjects.Select(CollidingProjectsDescriptionString)));
                Console.Error.WriteLine("Warning: " + message);
                if (false == allowAssemblyProjectAmbiguities)
                {
                    throw new ArgumentException(message);
                }
            }

        }

        private static string CollidingProjectsDescriptionString(IGrouping<string, Project> x)
        {
            return x.Key + " - " + String.Join(", ", x.Select(y => y.Path));
        }

        private static void ValidateDirectoryExists(string searchRootPath)
        {
            if (false == System.IO.Directory.Exists(searchRootPath))
            {
                throw new ArgumentException("Directory does not exist: " + searchRootPath);
            }
        }

        public IEnumerable<Project> FindProjectForAssemblyReference(AssemblyReference assemblyReference)
        {
            return this._projects.Where(x => assemblyReference.Name.Contains(x.Name));
        }
    }
}
