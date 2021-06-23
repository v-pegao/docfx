// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;

namespace Microsoft.Docs.Build
{
    internal class ErrorWriter : ErrorBuilder, IDisposable
    {
        private readonly object _outputLock = new();
        private readonly Lazy<TextWriter> _output;

        private int _errorCount;
        private int _warningCount;
        private int _suggestionCount;

        public int ErrorCount => _errorCount;

        public int WarningCount => _warningCount;

        public int SuggestionCount => _suggestionCount;

        public override bool HasError => Volatile.Read(ref _errorCount) > 0;

        public override bool FileHasError(FilePath file) => throw new NotSupportedException();

        public ErrorWriter(string? outputPath = null)
        {
            _output = new(() => outputPath is null ? TextWriter.Null : CreateOutput(outputPath));
        }

        public override void Add(Error error)
        {
            _ = error.Level switch
            {
                ErrorLevel.Error => Interlocked.Increment(ref _errorCount),
                ErrorLevel.Warning => Interlocked.Increment(ref _warningCount),
                ErrorLevel.Suggestion => Interlocked.Increment(ref _suggestionCount),
                _ => 0,
            };

            Telemetry.TrackErrorCount(error);

            if (_output != null)
            {
                lock (_outputLock)
                {
                    var value = error.ToString();
                    if (value == null || !value.Trim().EndsWith("}"))
                    {
                        Console.WriteLine($"incomplete log: {value}");
                    }
                    _output.Value.WriteLine(value);
                }
            }

            PrintError(error);
        }

        [SuppressMessage("Reliability", "CA2002", Justification = "Lock Console.Out")]
        public void PrintSummary()
        {
            lock (Console.Out)
            {
                if (_errorCount > 0 || _warningCount > 0 || _suggestionCount > 0)
                {
                    Console.ForegroundColor = _errorCount > 0 ? ConsoleColor.Red
                                            : _warningCount > 0 ? ConsoleColor.Yellow
                                            : ConsoleColor.Magenta;
                    Console.WriteLine();
                    Console.WriteLine($"  {_errorCount} Error(s), {_warningCount} Warning(s), {_suggestionCount} Suggestion(s)");
                }

                Console.ResetColor();
            }
        }

        public void Dispose()
        {
            Console.WriteLine("Dispose ErrorWriter");
            lock (_outputLock)
            {
                Console.WriteLine($"IsValueCreated {_output.IsValueCreated}");
                if (_output.IsValueCreated)
                {
                    Console.WriteLine("Flush TextWriter");
                    _output.Value.Flush();
                    Console.WriteLine("Close TextWriter");
                    _output.Value.Close();
                    Console.WriteLine("Dispose TextWriter");
                    _output.Value.Dispose();
                }
            }
        }

        [SuppressMessage("Reliability", "CA2002", Justification = "Lock Console.Out")]
        private static void PrintError(Error error)
        {
            lock (Console.Out)
            {
                var output = error.Level == ErrorLevel.Error ? Console.Error : Console.Out;
                Console.ForegroundColor = GetColor(error.Level);
                output.Write(error.Code + " ");
                Console.ResetColor();

                if (error.Source != null)
                {
                    output.WriteLine($"./{error.Source.File}({error.Source.Line},{error.Source.Column}): {error.Message}");
                }
                else
                {
                    output.WriteLine(error.Message);
                }
            }
        }

        private static TextWriter CreateOutput(string outputPath)
        {
            var outputFilePath = Path.GetFullPath(outputPath);

            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath) ?? ".");

            return File.AppendText(outputFilePath);
        }

        private static ConsoleColor GetColor(ErrorLevel level)
        {
            return level switch
            {
                ErrorLevel.Error => ConsoleColor.Red,
                ErrorLevel.Warning => ConsoleColor.Yellow,
                ErrorLevel.Suggestion => ConsoleColor.Magenta,
                ErrorLevel.Info => ConsoleColor.DarkGray,
                _ => ConsoleColor.DarkGray,
            };
        }
    }
}
