using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildDependencyReader.ProjectFileParser;
using System.IO;
using System.Text.RegularExpressions;

namespace BuildDependencyReader.BuildDependencyResolver
{
    public class ProjectFinder : IProjectFinder
    {
        private string _searchRootPath;
        private HashSet<FileInfo> _CSProjFileInfos;
        private HashSet<FileInfo> _SLNFileInfos;
        private Project[] _projects;
        private Dictionary<Project, FileInfo> MapProjectToSLN = new Dictionary<Project, FileInfo>();
        //private Dictionary<FileInfo, Project> MapSLNToProject = new Dictionary<FileInfo, Project>();

        private static readonly string csProjInSLNPattern = @"""[^""]*\.csproj""";

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

            this.CheckForAssemblyProjectAmbiguities(allowAssemblyProjectAmbiguities);

            this.MapSLNsToProjects();
        }

        public IEnumerable<Project> FindProjectForAssemblyReference(AssemblyReference assemblyReference)
        {
            return this._projects.Where(x => assemblyReference.Name.Contains(x.Name));
        }

        public FileInfo GetSLNFileForProject(Project project)
        {
            FileInfo slnFileInfo;
            if (false == this.MapProjectToSLN.TryGetValue(project, out slnFileInfo))
            {
                throw new ArgumentException("No .sln found for project: " + project);
            }
            return slnFileInfo;
        }

        private void MapSLNsToProjects()
        {
            foreach (var slnFileInfo in this._SLNFileInfos)
            {
                var slnFile = slnFileInfo.OpenText();
                var data = slnFile.ReadToEnd();
                foreach (var match in Regex.Matches(data, csProjInSLNPattern).Cast<Match>().Where(x => x.Success))
                {
                    var quotedProjectFilePath = match.Value;
                    var projectFilePath = Project.ResolvePath(slnFileInfo.DirectoryName, quotedProjectFilePath.Substring(1, quotedProjectFilePath.Length - 2));
                    var project = this._projects.Where(x => x.Path.ToLowerInvariant().Equals(projectFilePath.ToLowerInvariant())).SingleOrDefault();
                    if (false == project.Path.ToLowerInvariant().Contains(slnFileInfo.DirectoryName.ToLowerInvariant()))
                    {
                        Console.Error.WriteLine("WARNING: Skipping potential mapping to SLN file {0} because it is not in a parent directory of the project {1}", slnFileInfo.FullName, project.Path);
                        continue;
                    }
                    if (null != project)
                    {
                        if (this.MapProjectToSLN.ContainsKey(project))
                        {
                            throw new Exception(String.Format("Project {0} has ambiguous SLN {1}, {2}: ", project.Path, slnFileInfo.FullName, this.MapProjectToSLN[project].FullName));
                        }
                        this.MapProjectToSLN.Add(project, slnFileInfo);
                    }
                }
            }
        }

        private void CheckForAssemblyProjectAmbiguities(bool allowAssemblyProjectAmbiguities)
        {
            var collidingProjects = this._projects.GroupBy(x => x.Name.ToLowerInvariant().Trim())
                                                  .Where(x => x.Count() > 1)
                                                  .ToArray();
            if (false == collidingProjects.Any())
            {
                return;
            }
            var message = String.Format("Multiple projects with same name found - cannot realiably calculate assembly dependencies:\n\t{0}",
                                        String.Join("\n\t", collidingProjects.Select(CollidingProjectsDescriptionString)));
            Console.Error.WriteLine("Warning: " + message);
            if (false == allowAssemblyProjectAmbiguities)
            {
                throw new ArgumentException(message);
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

    }
}
