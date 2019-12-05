// Enable DUMP_TRANSLATIONS to write all translations available in the game into a lang.csv file
// #define DUMP_TRANSLATIONS

namespace TrafficManager.UI.Localization {
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using CSUtil.Commons;

    public class LookupTable {
        public LookupTable(string lookupTableName) {
            Name = lookupTableName;
            Load();
        }

        public string Get(string key) {
            string lang = Translation.GetCurrentLanguage();
            return Get(lang, key);
        }

        public string Get(string lang, string key) {
#if DEBUG
            if (!AllLanguages.ContainsKey(lang)) {
                Log.Error($"Translation: Unknown language {lang}");
                return key;
            }
#endif

            // Try find translation in the current language first
            if (AllLanguages[lang].TryGetValue(key, out string ret))
            {
                return ret;
            }

            // If not found, try also get translation in the default English
            // Untranslated keys are prefixed with ¶
            return AllLanguages[Translation.DEFAULT_LANGUAGE_CODE]
                       .TryGetValue(key, out string ret2)
                       ? ret2
                       : "¶" + key;
        }

        public bool HasString(string key) {
            string lang = Translation.GetCurrentLanguage();

            // Assume the language always exists in self.translations, so only check the string
            return AllLanguages[lang].ContainsKey(key);
        }

        private string Name { get; }

        /// <summary>
        /// Stores all languages (first key), and for each language stores translations
        /// (indexed by the second key in the value)
        /// </summary>
        internal Dictionary<string, Dictionary<string, string>> AllLanguages;

        private void Load() {
            // Load all languages together
            AllLanguages = new Dictionary<string, Dictionary<string, string>>();

            // Load all translations CSV file with UTF8
            string filename = $"{Translation.RESOURCES_PREFIX}Translations.{Name}.csv";
            Log.Info($"Loading translations: {filename}");

            string[] lines;
            using (Stream st = Assembly.GetExecutingAssembly()
                                       .GetManifestResourceStream(filename)) {
                using (var sr = new StreamReader(st, Encoding.UTF8)) {
                    lines = ReadLines(sr);
                }
            }

            // Read each line as CSV line
            // Read language order in the first line
            string firstLine = lines[0];
            var languageCodes = new List<string>();
            using (var sr = new StringReader(firstLine)) {
                ReadCsvCell(sr); // skip
                while (true) {
                    string langName = ReadCsvCell(sr);
                    if (langName.Length == 0) {
                        break;
                    }

                    // Language might be a full name, or might be a ready-to-use locale code
                    string langCode = Translation.CsvColumnsToLocales.ContainsKey(langName)
                                          ? Translation.CsvColumnsToLocales[langName]
                                          : langName.ToLower();
                    languageCodes.Add(langCode);
                }
            }

            CollectTranslations(lines, languageCodes, out AllLanguages);

#if DUMP_TRANSLATIONS
            DumpTranslationsToCsv();
#endif
            Log._Debug($"Loaded {AllLanguages.Count} different languages for {Name}");
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="lines"></param>
        /// <param name="languageCodes"></param>
        /// <param name="allLanguages">result dictionary where all translations will be collected</param>
        private static void CollectTranslations(string[] lines, List<string> languageCodes, out Dictionary<string, Dictionary<string, string>> allLanguages) {
            allLanguages = new Dictionary<string, Dictionary<string, string>>();
            // Initialize empty dicts for each language
            foreach (string lang in languageCodes) {
                allLanguages[lang] = new Dictionary<string, string>();
            }

            // first column is the translation key
            // Following columns are languages, following the order in AvailableLanguageCodes
            foreach (string line in lines.Skip(1)) {
                using (var sr = new StringReader(line)) {
                    string key = ReadCsvCell(sr);
                    if (key.Length == 0) {
                        break; // last line is empty
                    }

                    foreach (string lang in languageCodes) {
                        string cell = ReadCsvCell(sr);
                        // Empty translations are not accepted for all languages other than English
                        // We don't load those keys
                        if (string.IsNullOrEmpty(cell) &&
                            lang != Translation.DEFAULT_LANGUAGE_CODE) {
                            continue;
                        }

                        allLanguages[lang][key] = cell;
                    }
                }
            }
        }

        /// <summary>
        /// Read all lines, validate and join separated lines
        /// </summary>
        /// <param name="sr">stream to read from</param>
        /// <returns>collection of valid translation rows</returns>
        private static string[] ReadLines(StreamReader sr) {
            string[] lines = sr.ReadToEnd().Split(new[] { "\n", "\r\n" }, StringSplitOptions.None);
            return ValidateAndJoinLines(lines);
        }

        /// <summary>
        /// Validates lines by joining separated incomplete strings with new line characters
        /// </summary>
        /// <param name="lines">Lines of translation file to validate</param>
        /// <returns>Validated and joined collection of translation strings - one per row</returns>
        private static string[] ValidateAndJoinLines(string[] lines) {
            int lastIncompleteLineIndex = 0;
            for (int i = 0; i < lines.Length; i++) {
                //skip empty lines & search for incomplete parts of translation string until line is valid
                if (lines[i].Length == 0 || (lines[i].StartsWith("\"") && lines[lastIncompleteLineIndex].EndsWith("\""))) {
                    continue;
                }

                // copy index of incomplete translation string value
                int x = i;
                // copy incomplete translation string value
                string line = lines[i];
                // reset line value
                lines[i] = null;
                // move backwards and search for not empty line to concatenate separated part of translation string
                while (x > 0) {
                    //skip reset lines
                    if (lines[x] == null) {
                        x--;
                        continue;
                    }
                    //join translation string part with head of incomplete separated string
                    lastIncompleteLineIndex = x;
                    lines[lastIncompleteLineIndex] = lines[lastIncompleteLineIndex] + "\n" + line;
                    break;
                }
            }

            //collect lines - skip null
            return lines.Where(line => !string.IsNullOrEmpty(line)).ToArray();
        }

        /// <summary>
        /// Given a stringReader, read a CSV cell which can be a string until next comma, or quoted
        /// string (in this case double quotes are decoded to a quote character) and respects
        /// newlines \n too.
        /// </summary>
        /// <param name="sr">Source for reading CSV</param>
        /// <returns>Cell contents</returns>
        private static string ReadCsvCell(StringReader sr) {
            var sb = new StringBuilder();
            if (sr.Peek() == '"') {
                sr.Read(); // skip the leading \"

                // The cell begins with a \" character, special reading rules apply
                while (true) {
                    int next = sr.Read();
                    if (next == -1) {
                        break; // end of the line
                    }

                    switch (next) {
                        case '\\': {
                            int special = sr.Read();
                            if (special == 'n') {
                                // Recognized a new line
                                sb.Append("\n");
                            } else {
                                // Not recognized, append as is
                                sb.Append("\\");
                                sb.Append((char)special, 1);
                            }

                            break;
                        }
                        case '\"': {
                            // Found a '""', or a '",'
                            int peek = sr.Peek();
                            switch (peek) {
                                case '\"': {
                                    sr.Read(); // consume the double quote
                                    sb.Append("\"");
                                    break;
                                }
                                case ',':
                                case -1: {
                                    // Followed by a comma or end-of-string
                                    sr.Read(); // Consume the comma
                                    return sb.ToString();
                                }
                                default: {
                                    // Followed by a non-comma, non-end-of-string
                                    sb.Append("\"");
                                    break;
                                }
                            }
                            break;
                        }
                        default: {
                            sb.Append((char)next, 1);
                            break;
                        }
                    }
                }
            } else {
                // Simple reading rules apply, read to the next comma or end-of-string
                while (true) {
                    int next = sr.Read();
                    if (next == -1 || next == ',') {
                        break; // end-of-string or a comma
                    }

                    sb.Append((char)next, 1);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Used only once to write out existing translations
        /// </summary>
        [Conditional("DUMP_TRANSLATIONS")]
        private void DumpTranslationsToCsv() {
            string Quote(string s) {
                return s.Replace("\"", "\"\"")
                        .Replace("\n", "\\n")
                        .Replace("\t", "\\t");
            }

            var sb = new StringBuilder();
            sb.Append("Original,");
            foreach (string lang in Translation.AvailableLanguageCodes) {
                sb.Append($"{lang},");
            }

            sb.Append("\n");

            foreach (KeyValuePair<string, string> englishStr
                in AllLanguages[Translation.DEFAULT_LANGUAGE_CODE])
            {
                sb.Append($"\"{Quote(englishStr.Key)}\",");
                foreach (string lang in Translation.AvailableLanguageCodes) {
                    sb.Append(
                        AllLanguages[lang].TryGetValue(englishStr.Key, out string localizedStr)
                            ? $"\"{Quote(localizedStr)}\","
                            : ",");
                }

                sb.Append("\n");
            }

            File.WriteAllText("lang.csv", sb.ToString(), Encoding.UTF8);
        }

    } // end class
}
