using SingleHeaderLibBuilder;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

public static class Program
{
    private const string CONFIGFILENAME = "headerpackconfig.json";

    private static ConfigFile? _Config = null;
    private readonly static DirectoryInfo _BaseDir = new DirectoryInfo(Environment.CurrentDirectory);
    private readonly static Dictionary<string, CodeFile> _CodeFiles = new Dictionary<string, CodeFile>();
    private readonly static SortedList<int, CodeFile> _OutputOrder = new SortedList<int, CodeFile>();
    private readonly static HashSet<string> _ExternalIncludes = new HashSet<string>();
    private readonly static DateTime _BeginTimeStamp = DateTime.UtcNow;

    public static async Task<int> Main(string[] args)
    {
        bool verbose = args.Contains("--verbose");
        if (verbose)
        {
            Console.WriteLine("--verbose: Printing additional information on single header packing process");
        }

        try
        {
            await ReadConfigFile(verbose);
            await DiscoverFiles(verbose);
            LinkFiles(verbose);
            CalculateFileOrder(verbose);
            await WriteOutput(verbose);
        }
        catch (HeaderpackException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(ex.Message);
            Console.ResetColor();
            return 1;
        }
#if !DEBUG
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"An unhandled exception occured!\nMessage: \"{ex.Message}\"");
            Console.Write("StackTrace:\n");
            Console.Write(ex.StackTrace);
            Console.ResetColor();
            return 2;
        }
#endif
        return 0;
    }

    private static async Task ReadConfigFile(bool verbose)
    {
        FileInfo configFile = new FileInfo(Path.Combine(Environment.CurrentDirectory, CONFIGFILENAME));
        ExecuteAssert(configFile.Exists, $"Config File has to exist! Expected \"{configFile.FullName}\"");

        using FileStream fileStream = configFile.OpenRead();
        _Config = await JsonSerializer.DeserializeAsync<ConfigFile>(fileStream);

        ExecuteAssert(_Config != null, "Failed to read or parse config file!");
        ExecuteAssert(!string.IsNullOrWhiteSpace(_Config!.Include), "No definition file set in config file!");
        ExecuteAssert(!string.IsNullOrWhiteSpace(_Config!.Output), "No output path set!");
        if (verbose)
        {
            Console.WriteLine($"Loaded Config File: {JsonSerializer.Serialize(_Config!)}");
        }
    }
    private static async Task DiscoverFiles(bool verbose)
    {
        Debug.Assert(_Config != null);

        HashSet<string> seenFiles = new HashSet<string>(); // All files which are either fully processed (_CodeFiles) or queued (unconfirmedFiles)

        Queue<CodeFile> unconfirmedFiles = new Queue<CodeFile>(); // Queue of files to process
        
        { // Load the _Config.Include file to start the process

            FileInfo fileInfo = new FileInfo(Path.Combine(Environment.CurrentDirectory, _Config!.Include));
            ExecuteAssert(fileInfo.Exists, $"Failed to locate included file \"{_Config!.Include}\"!");
            CodeFile codeFile = new CodeFile(fileInfo, _BaseDir);
            unconfirmedFiles.Enqueue(codeFile);
        }

        while (unconfirmedFiles.Count != 0)
        {
            CodeFile current = unconfirmedFiles.Dequeue();
            await current.GatherIncludes();

            _CodeFiles.Add(current.File.FullName, current);

            // Add all internal includes to the queue (unless already in the queue or processed)
            foreach (string path in current.UnresolvedInternalIncludes)
            {
                FileInfo fileInfo = new FileInfo(path);
                ExecuteAssert(fileInfo.Exists, $"Failed to find include \"{fileInfo.FullName}\", included in \"{current.RelativeFileName}\"");
                if (!seenFiles.Contains(fileInfo.FullName))
                {
                    unconfirmedFiles.Enqueue(new CodeFile(fileInfo, _BaseDir));
                    seenFiles.Add(fileInfo.FullName);
                }
            }

            // Gather external includes
            foreach (string externalInclude in current.ExternalIncludes)
            {
                _ExternalIncludes.Add(externalInclude);
            }
        }

        if (verbose)
        {
            Console.WriteLine($"Found {_CodeFiles.Count} files: {string.Join(", ", _CodeFiles.Values.Select(file => $"\"{file.RelativeFileName}\""))}");
        }
    }
    private static void LinkFiles(bool verbose)
    {
        foreach (var codeFile in _CodeFiles.Values)
        {
            codeFile.ResolveIncludes(_CodeFiles);
        }
    }
    private static void CalculateFileOrder(bool verbose)
    {
        int rootOrderPos = 0;

        // First all code files are added which don't have other files depending on them
        foreach (var codeFile in _CodeFiles.Values)
        {
            if (codeFile.Dependants.Count == 0)
            {
                codeFile.Order = rootOrderPos;
                rootOrderPos++;
            }
        }


        bool doneAll = false;
        int maxIterations = 2048;

        // Try iteratively to solve the order problem
        while (!doneAll && maxIterations > 0)
        {
            doneAll = true;
            foreach (var codeFile in _CodeFiles.Values)
            {
                // Skip this file as it is already ordered
                if (codeFile.Order >= 0)
                {
                    continue;
                }

                // Find a file, where all other files it depends on are already ordered
                if (codeFile.Dependants.Count > 0)
                {
                    bool allDependantsAccounted = true;
                    foreach (CodeFile dependant in codeFile.Dependants)
                    {
                        if (dependant.Order == -1)
                        {
                            allDependantsAccounted = false;
                        }
                    }
                    if (allDependantsAccounted)
                    {
                        codeFile.Order = rootOrderPos; // Setting the order marks this file as processed
                        rootOrderPos++;
                    }
                    else
                    {
                        doneAll = false;
                    }
                }
            }
            maxIterations--;
        }

        ExecuteAssert(doneAll, "Code file ordering algorithm failed with 2048 iterations! Circular dependency?");

        // With the order done, insert it into the sorted list
        foreach (var codeFile in _CodeFiles.Values)
        {
            _OutputOrder.Add(codeFile.Order, codeFile);
        }

        if (verbose)
        {
            Console.WriteLine($"Ordering Files complete");
        }
    }
    private static async Task WriteOutput(bool verbose)
    {
        Debug.Assert(_Config != null);

        FileInfo outFile = new FileInfo(Path.Combine(Environment.CurrentDirectory, _Config.Output));

        // Make sure the directory exists
        if (!outFile.Exists)
        {
            if (!outFile.Directory!.Exists)
            {
                outFile.Directory.Create();
            }
        }

        // Create or replace the file
        using FileStream fileStream = outFile.Create();
        using StreamWriter writer = new StreamWriter(fileStream, Encoding.UTF8);
        writer.NewLine = _Config.LineTerminator;

        writer.WriteLine($"#pragma once"); // The output is treated as a header file, so one should add #pragma once

        if (!string.IsNullOrWhiteSpace(_Config!.Disclaimer))
        {
            // Copy the disclaimer

            FileInfo file = new FileInfo(Path.Combine(_BaseDir.FullName, _Config!.Disclaimer));
            ExecuteAssert(file.Exists, $"Could not find disclaimer file \"{_Config!.Disclaimer}\"");
            using FileStream disclaimerFileStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read);
            using StreamReader reader = new StreamReader(disclaimerFileStream);
            for (string? line = reader.ReadLine(); line != null; line = reader.ReadLine())
            {
                writer.WriteLine(line); // copy line by line so the output file gets consistent line endings
            }
        }
        writer.WriteLine($"// This file was automatically generated by SingleHeaderLibBuilder v1.0.0 at {DateTime.UtcNow.ToString("u")}");
        writer.WriteLine();

        // Dump all external includes in first, so that name conflicts aren't happening

        writer.WriteLine($"// External includes");
        writer.WriteLine();
        foreach (string include in _ExternalIncludes)
        {
            writer.WriteLine($"#include <{include}>");
        }
        writer.WriteLine();

        // Write all input files, beginning with the least dependant, most dependent on

        foreach (var codeFile in _OutputOrder.Reverse())
        {
            if (verbose)
            {
                Console.WriteLine($"Writing \"{codeFile.Value.File.Name}\"");
            }
            writer.WriteLine($"// SingleHeaderLibBuilder: {codeFile.Value.RelativeFileName}");
            writer.WriteLine();
            await codeFile.Value.WriteToOutput(writer);
            writer.WriteLine();
        }

        // Report success in console

        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("Success");
        Console.ResetColor();
        TimeSpan elapsed = DateTime.UtcNow - _BeginTimeStamp;
        Console.WriteLine($" in {Math.Ceiling(elapsed.TotalMilliseconds)} milliseconds. Output: \"{outFile.FullName}\"");
    }

    /// <summary>
    /// Throws a <see cref="HeaderpackException"/> if condition isn't true
    /// </summary>
    /// <param name="condition"></param>
    /// <param name="message"></param>
    /// <exception cref="HeaderpackException"></exception>
    private static void ExecuteAssert(bool condition, string message)
    {
        if (!condition)
        {
            throw new HeaderpackException(message);
        }
    }

    public class HeaderpackException : Exception
    {
        public HeaderpackException(string message) : base(message) { }
    }
}