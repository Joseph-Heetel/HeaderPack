using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SingleHeaderLibBuilder
{
    /// <summary>
    /// Manages configuration options
    /// </summary>
    [JsonSerializable(typeof(ConfigFile))]
    internal class ConfigFile
    {
        /// <summary>
        /// File which contains a disclaimer to put at the top of the file output
        /// </summary>
        [JsonPropertyName("disclaimer")]
        public string Disclaimer { get; set; } = string.Empty;
        /// <summary>
        /// C++ code file where the user references all initial code files to iterate
        /// </summary>
        [JsonPropertyName("include")]
        public string Include { get; set; } = string.Empty;
        /// <summary>
        /// Relative output file path
        /// </summary>
        [JsonPropertyName("output")]
        public string Output { get; set; } = String.Empty;
        /// <summary>
        /// What line terminator the writer should use
        /// </summary>
        [JsonPropertyName("lineterminator")]
        public string LineTerminator { get; set; } = "\n";
    }
}
