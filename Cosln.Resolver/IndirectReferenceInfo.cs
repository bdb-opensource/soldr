using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cosln.ProjectFileParser;

namespace Cosln.Resolver
{
    public struct IndirectReferenceInfo
    {
        public readonly AssemblyReference DirectReference;
        public readonly Project DirectReferenceProject;
        public readonly AssemblyReference IndirectReference;
        public readonly Project IndirectReferenceProject;

        public IndirectReferenceInfo(AssemblyReference directReference,
            Project directReferenceProject,
            AssemblyReference indirectReference,
            Project indirectReferenceProject)
        {
            this.DirectReference = directReference;
            this.IndirectReference = indirectReference;
            this.DirectReferenceProject = directReferenceProject;
            this.IndirectReferenceProject = indirectReferenceProject;
        }
    }
}
