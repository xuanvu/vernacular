//
// Entry.cs
//
// Author:
//   Aaron Bockover <abock@rd.io>
//
// Copyright 2012 Rdio, Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.IO;
using System.Collections.Generic;

using Mono.Options;

using Vernacular.Parsers;
using Vernacular.Analyzers;
using Vernacular.Generators;

namespace Vernacular.Tool
{
    public static class Entry
    {
        public static void Main (string [] args)
        {
            var input_paths = new List<string> ();
            string output_path = "-";
            string generator_name = String.Empty;
            string source_root_path = null;
            string reduce_master_path = null;
            string reduce_retain_path = null;
            string android_input_strings_xml = null;
            string android_output_strings_xml = null;
            bool analyze = false;
            bool log = false;
            bool verbose = false;
            bool show_help = false;

            Generator generator = null;

            var options = new OptionSet {
                { "i|input=", "Input directory, search pattern, or file to parse (non-recursive)", v => input_paths.Add (v) },
                { "o|output=", "Output file for extracted string resources", v => output_path = v },
                { "r|source-root=", "Root directory of source code", v => source_root_path = v },
                { "g|generator=", String.Format ("Generator to use ({0})",
                    String.Join ("|", Generator.GeneratorNames)), v => generator_name = v },
                { "a|analyze", "Run the string analyzer after generation", v => analyze = v != null },
                { "reduce-master=", "Reduce a master localized PO file, " +
                    "keeping only strings defined by another unlocalized PO[T] file", v => reduce_master_path = v },
                { "reduce-retain=", "An unlocalized PO[T] file used to " +
                    "determine which strings from reduce-master should be retained", v => reduce_retain_path = v },
                { "android-input-strings-xml=", "Input file of unlocalized Android Strings.xml " +
                    "for preserving hand-maintained string resources", v => android_input_strings_xml = v },
                { "android-output-strings-xml=", "Output file of localized Android Strings.xml " +
                    "for preserving hand-maintained string resources", v => android_output_strings_xml = v },
                { "l|log", "Display logging", v => log = v != null },
                { "v|verbose", "Verbose logging", v => verbose = v != null },
                { "h|help", "Show this help message and exit", v => show_help = v != null }
            };

            try {
                options.Parse (args);

                if (show_help) {
                    Console.WriteLine ("Usage: vernacular [OPTIONS]+");
                    Console.WriteLine ();
                    Console.WriteLine ("Options:");
                    options.WriteOptionDescriptions (Console.Out);
                    return;
                }

                if (source_root_path != null) {
                    if (!Directory.Exists (source_root_path)) {
                        throw new OptionException ("invalid source-root", "source-root");
                    }

                    source_root_path = new DirectoryInfo (source_root_path).FullName;
                }

                generator = Generator.GetGeneratorForName (generator_name.ToLower ());
                if (generator == null) {
                    throw new OptionException ("invalid generator", "generator");
                }

                if (reduce_master_path != null && reduce_retain_path == null) {
                    throw new OptionException ("reduce-retain must be specified if reduce-master is", "reduce-retain");
                } else if (reduce_master_path == null && reduce_retain_path != null) {
                    throw new OptionException ("reduce-master must be specified if reduce-retain is", "reduce-master");
                } else if (reduce_master_path != null && reduce_retain_path != null) {
                    var reduce_master = new PoParser { SourceRootPath = source_root_path };
                    var reduce_retain = new PoParser { SourceRootPath = source_root_path };

                    reduce_master.Add (reduce_master_path);
                    reduce_retain.Add (reduce_retain_path);

                    generator.Reduce (reduce_master, reduce_retain);

                    generator.Generate (output_path);

                    return;
                }
            } catch (OptionException e) {
                Console.WriteLine ("vernacular: {0}", e.Message);
                Console.WriteLine ("Try `vernacular --help` for more information.");
                return;
            }

            var parser = new AggregateParser {
                SourceRootPath = source_root_path
            };

            if (verbose) {
                parser.LogLevel = 2;
            } else if (log) {
                parser.LogLevel = 1;
            }

            StringAnalyzer analyzer = null;

            if (analyze) {
                analyzer = new StringAnalyzer ();
            }

            foreach (var input_path in input_paths) {
                if (File.Exists (input_path)) {
                    parser.Add (input_path);
                    continue;
                }

                var search_pattern = "*";
                var dir = input_path;

                if (!Directory.Exists (dir)) {
                    search_pattern = Path.GetFileName (dir);
                    dir = Path.GetDirectoryName (dir);
                    if (!Directory.Exists (dir)) {
                        continue;
                    }
                }

                foreach (var path in Directory.EnumerateFiles (dir, search_pattern, SearchOption.TopDirectoryOnly)) {
                    parser.Add (path);
                }
            }

            foreach (var localized_string in parser.Parse ()) {
                generator.Add (localized_string);

                if (analyzer != null) {
                    analyzer.Add (localized_string);
                }
            }

            if (analyzer != null) {
                analyzer.Analyze ();
            }

            generator.Generate (output_path);

            if (generator is AndroidGenerator && android_input_strings_xml != null && android_output_strings_xml != null) {
                ((AndroidGenerator)generator).LocalizeManualStringsXml (android_input_strings_xml, android_output_strings_xml);
            }
        }
    }
}
