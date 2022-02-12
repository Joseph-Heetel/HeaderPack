using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SingleHeaderLibBuilder
{
    /// <summary>
    /// A Header or Inline file of C++ Code
    /// </summary>
    internal class CodeFile
    {
        /// <summary>
        /// FileInfo manages the filesystem side
        /// </summary>
        public FileInfo File { get; set; }
        /// <summary>
        /// Mostly used for debug purposes, contains the name of the file relative to the base directory
        /// </summary>
        public string RelativeFileName { get; set; } = string.Empty;
        /// <summary>
        /// Headers included in the C++ files pointing to an external source: &lt;HEADER.hpp&gt;
        /// </summary>
        public List<string> ExternalIncludes { get; } = new List<string>();
        /// <summary>
        /// Full paths of headers included in local format: "HEADER.hpp"
        /// </summary>
        public List<string> UnresolvedInternalIncludes { get; } = new List<string>();
        /// <summary>
        /// List of resolved C++ code files this file includes
        /// </summary>
        public List<CodeFile> InternalIncludes { get; } = new List<CodeFile>();
        /// <summary>
        /// List of resolved C++ code files which include this file
        /// </summary>
        public List<CodeFile> Dependants { get; } = new List<CodeFile>();
        /// <summary>
        /// Order by which all includes can be resolved. -1 = not assigned. Higher value == less files depend on it
        /// </summary>
        public int Order { get; set; } = -1;

        /// <summary></summary>
        /// <param name="file">FileInfo of the Code File</param>
        /// <param name="basedir">Base/Working directory</param>
        public CodeFile(FileInfo file, DirectoryInfo basedir)
        {
            File = file;
            RelativeFileName = Path.GetRelativePath(basedir.FullName, file.FullName);
        }

        /// <summary>
        /// Reads the entire file gathering "#include ..." statements. Populates <see cref="ExternalIncludes"/> and <see cref="UnresolvedInternalIncludes"/> lists
        /// </summary>
        public Task GatherIncludes()
        {
            using FileStream fileStream = File.OpenRead();
            using StreamReader reader = new StreamReader(fileStream, Encoding.UTF8);
            for (string? line = reader.ReadLine(); line != null; line = reader.ReadLine())
            {
                if (line.StartsWith("#include"))
                {
                    string includeName = line.Substring("#include".Length).Trim();
                    if (includeName.StartsWith('<'))
                    {
                        ExternalIncludes.Add(includeName.Trim('<', '>'));
                    }
                    else if (includeName.StartsWith('\"'))
                    {
                        AddInternalInclude(includeName.Trim('\"'));
                    }
                }
            }
            return Task.CompletedTask;
        }

        private void AddInternalInclude(string includeName)
        {
            DirectoryInfo? directory = File.Directory;
            Debug.Assert(directory != null);
            FileInfo includedFile = new FileInfo(Path.Combine(directory.FullName, includeName));
            UnresolvedInternalIncludes.Add(includedFile.FullName);
        }

        /// <summary>
        /// Replaces <see cref="UnresolvedInternalIncludes"/> entries with entries in <see cref="InternalIncludes"/>
        /// </summary>
        public int ResolveIncludes(Dictionary<string, CodeFile> files)
        {
            int result = UnresolvedInternalIncludes.Count;
            foreach (var unresolvedInclude in UnresolvedInternalIncludes)
            {
                if (files.TryGetValue(unresolvedInclude, out CodeFile? include))
                {
                    result--;
                    InternalIncludes.Add(include);
                    include.Dependants.Add(this);
                }
            }
            return result;
        }

        /// <summary>
        /// Writes the file to the output stream
        /// </summary>
        /// <param name="output">Output stream</param>
        public Task WriteToOutput(StreamWriter output)
        {
            using FileStream fileStream = File.OpenRead();
            using StreamReader reader = new StreamReader(fileStream, Encoding.UTF8);
            for (string? line = reader.ReadLine(); line != null; line = reader.ReadLine())
            {
                if (line.StartsWith("#pragma once") || line.StartsWith("#include"))
                {
                    continue;
                }
                output.WriteLine(line);
            }
            return Task.CompletedTask;
        }
    }
}
