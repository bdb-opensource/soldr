using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildDependencyReader.ProjectFileParser;
using System.IO;
using System.Text.RegularExpressions;
using BuildDependencyReader.Common;

namespace BuildDependencyReader.BuildDependencyResolver
{
    public class ProjectFinder : IProjectFinder
    {
        protected static readonly log4net.ILog _logger = log4net.LogManager.GetLogger(
                   System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        protected string _searchRootPath;
        protected HashSet<FileInfo> _CSProjFileInfos;
        protected HashSet<FileInfo> _SLNFileInfos;
        protected Project[] _projects;
        protected Dictionary<Project, FileInfo> _mapProjectToSLN = new Dictionary<Project, FileInfo>();
        protected Dictionary<string, IEnumerable<Project>> _mapAssemblyReferenceToProject;

        protected static readonly Regex csProjInSLNRegex = new Regex(@"""[^""]*\.csproj""", RegexOptions.IgnoreCase);

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

        public IEnumerable<Project> AllProjectsInPath()
        {
            return this._projects;
        }

        public IEnumerable<Project> FindProjectForAssemblyReference(AssemblyReference assemblyReference)
        {
            return FindProjectForAssemblyName(assemblyReference.AssemblyNameFromFullName());
        }

        public IEnumerable<Project> FindProjectForAssemblyName(string assemblyName)
        {
            if (null == this._mapAssemblyReferenceToProject)
            {
                this._mapAssemblyReferenceToProject = new Dictionary<string, IEnumerable<Project>>();
            }
            IEnumerable<Project> result = null;
            if (false == this._mapAssemblyReferenceToProject.TryGetValue(assemblyName, out result))
            {
                result = this._projects.Where(x => assemblyName.Equals(x.Name, StringComparison.InvariantCultureIgnoreCase))
                                       .ToArray();
                this._mapAssemblyReferenceToProject.Add(assemblyName, result);
            }
            return result;
        }


        public FileInfo GetSLNFileForProject(Project project)
        {
            FileInfo slnFileInfo;
            if (false == this._mapProjectToSLN.TryGetValue(project, out slnFileInfo))
            {
                var errorMessage = "No .sln found for project: " + project;
                _logger.Error(errorMessage);
                throw new ArgumentException(errorMessage, "project");
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



        protected void MapSLNsToProjects()
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
                        _logger.WarnFormat("Skipping potential mapping to SLN file {0} because it is not in a parent directory of the project {1}", slnFileInfo.FullName, project.Path);
                        continue;
                    }
                    if (null != project)
                    {
                        if (this._mapProjectToSLN.ContainsKey(project))
                        {
                            var errorMessage = String.Format("Project {0} has ambiguous SLN {1}, {2}: ", project.Path, slnFileInfo.FullName, this._mapProjectToSLN[project].FullName);
                            _logger.Error(errorMessage);
                            throw new Exception(errorMessage);
                        }
                        this._mapProjectToSLN.Add(project, slnFileInfo);
                    }
                }
            }
        }

        protected void CheckForAssemblyProjectAmbiguities(bool allowAssemblyProjectAmbiguities)
        {
            var collidingProjects = this._projects.GroupBy(x => x.Name.ToLowerInvariant().Trim())
                                                  .Where(x => x.Count() > 1)
                                                  .ToArray();
            if (false == collidingProjects.Any())
            {
                return;
            }
            var message = "Multiple projects with same name found - cannot realiably calculate assembly dependencies:\n"
                          + StringExtensions.Tabify(collidingProjects.Select(CollidingProjectsDescriptionString));
            if (allowAssemblyProjectAmbiguities)
            {
                _logger.Warn(message);
            } 
            else 
            {
                _logger.Error(message);
                throw new ArgumentException(message);
            }
        }

        protected static string CollidingProjectsDescriptionString(IGrouping<string, Project> group)
        {
            return String.Format("{0}:\n{1}", group.Key, StringExtensions.Tabify(group.Select(y => y.Path)));
        }

        protected static void ValidateDirectoryExists(string searchRootPath)
        {
            if (false == System.IO.Directory.Exists(searchRootPath))
            {
                var errorMessage = "Directory does not exist: " + searchRootPath;
                _logger.Error(errorMessage);
                throw new ArgumentException(errorMessage);
            }
        }

    }
}
