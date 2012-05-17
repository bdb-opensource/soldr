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
        private Dictionary<Project, FileInfo> _mapProjectToSLN = new Dictionary<Project, FileInfo>();
        private Dictionary<AssemblyReference, IEnumerable<Project>> _mapAssemblyReferenceToProject;

        private static readonly Regex csProjInSLNRegex = new Regex(@"""[^""]*\.csproj""", RegexOptions.IgnoreCase);

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
            if (null == this._mapAssemblyReferenceToProject)
            {
                this._mapAssemblyReferenceToProject = new Dictionary<AssemblyReference, IEnumerable<Project>>();
            }
            IEnumerable<Project> result = null;
            if (false == this._mapAssemblyReferenceToProject.TryGetValue(assemblyReference, out result))
            {
                result = this._projects.Where(x => AssemblyNameFromFullName(assemblyReference).Equals(x.Name, StringComparison.InvariantCultureIgnoreCase))
                                       .ToArray();
                this._mapAssemblyReferenceToProject.Add(assemblyReference, result);
            }
            return result;
        }

        private static string AssemblyNameFromFullName(AssemblyReference assemblyReference)
        {
            return assemblyReference.Name.Split(',').First();
        }

        public FileInfo GetSLNFileForProject(Project project)
        {
            FileInfo slnFileInfo;
            if (false == this._mapProjectToSLN.TryGetValue(project, out slnFileInfo))
            {
                throw new ArgumentException("No .sln found for project: " + project);
            }
            return slnFileInfo;
        }

        public IEnumerable<Project> GetProjectsOfSLN(string slnFilePath)
        {
            return this._mapProjectToSLN.Where(x => x.Value.FullName.Equals(slnFilePath, StringComparison.InvariantCultureIgnoreCase))
                                        .Select(x => x.Key);
        }

        public IEnumerable<Project> GetProjectsOfSLN(FileInfo slnFileInfo)
        {
            return this._mapProjectToSLN.Where(x => x.Value.Equals(slnFileInfo)).Select(x => x.Key);
        }



        private void MapSLNsToProjects()
        {
            foreach (var slnFileInfo in this._SLNFileInfos)
            {
                var slnFile = slnFileInfo.OpenText();
                var data = slnFile.ReadToEnd();
                foreach (var match in csProjInSLNRegex.Matches(data).Cast<Match>().Where(x => x.Success))
                {
                    var quotedProjectFilePath = match.Value;
                    var projectFilePath = Project.ResolvePath(slnFileInfo.DirectoryName, quotedProjectFilePath.Substring(1, quotedProjectFilePath.Length - 2));
                    var project = this._projects.Where(x => x.Path.Equals(projectFilePath, StringComparison.InvariantCultureIgnoreCase)).SingleOrDefault();
                    if (false == project.Path.ToLowerInvariant().Contains(slnFileInfo.DirectoryName.ToLowerInvariant()))
                    {
                        Console.Error.WriteLine("WARNING: Skipping potential mapping to SLN file {0} because it is not in a parent directory of the project {1}", slnFileInfo.FullName, project.Path);
                        continue;
                    }
                    if (null != project)
                    {
                        if (this._mapProjectToSLN.ContainsKey(project))
                        {
                            throw new Exception(String.Format("Project {0} has ambiguous SLN {1}, {2}: ", project.Path, slnFileInfo.FullName, this._mapProjectToSLN[project].FullName));
                        }
                        this._mapProjectToSLN.Add(project, slnFileInfo);
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
            var message = "Multiple projects with same name found - cannot realiably calculate assembly dependencies:\n"
                          + Tabify(collidingProjects.Select(CollidingProjectsDescriptionString));
            Console.Error.WriteLine("WARNING: " + message);
            if (false == allowAssemblyProjectAmbiguities)
            {
                throw new ArgumentException(message);
            }
        }

        private static string Tabify(IEnumerable<string> strings)
        {
            return "\t" + String.Join("\n\t", strings.SelectMany(x => x.Split('\n')));
        }

        private static string CollidingProjectsDescriptionString(IGrouping<string, Project> group)
        {
            return String.Format("{0}:\n{1}", group.Key, Tabify(group.Select(y => y.Path)));
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
