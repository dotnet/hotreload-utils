using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.DotNet.HotReload.Utils
{
    public class DeltaGeneratorTask : ToolTask
    {
        [Required]
        public string? ScriptPath { get; set; }

        [Required]
        public string? ProjectFile {get ; set; }

        /// Additional msbuild properties to pass to hotreload-delta-gen
        ///  in Key=Value form
        public string[]? BuildProperties {get; set;}

        protected override string ToolName {
            get => "hotreload-delta-gen";
        }

        /// Each item is a full path of the generated dmeta file
        /// and has metadata DeltaIL and DeltaPDB with the full paths of the dmeta and dpdb files.
        [Output]
        public ITaskItem[]? GeneratedDeltas { get; set; }

        /// (Optional)
        /// If set, the results will be written to the given json file.
        /// If not set, a temporary file will be used
        public string? OutputSummaryPath { get; set; }

        protected override bool ValidateParameters() {
            if (ScriptPath is not string script) {
                Log.LogError ("no ScriptPath");
                return false;
            }
            if (!System.IO.File.Exists(script)) {
                Log.LogError ($"script file {script} does not exist");
                return false;
            }
            if (ProjectFile is not string proj) {
                Log.LogError ("no ProjectFile");
                return false;
            }
            if (!System.IO.File.Exists(proj)) {
                Log.LogError ($"project file {proj} does not exist");
                return false;
            }
            return true;
        }

        private string computedOutputSummaryPath = "";

        public override bool Execute()
        {
            bool temporary = false;
            if (OutputSummaryPath is string s)
                computedOutputSummaryPath = s;
            else {
                computedOutputSummaryPath = System.IO.Path.GetTempFileName();
                temporary = true;
            }
            ITaskItem[] items;
            try {
                if (!base.Execute())
                    return false;
                if (ExitCode != 0)
                    return false;
                items = ReadSummary (computedOutputSummaryPath).Result;
            } finally {
                if (temporary)
                    DeleteTempFile(computedOutputSummaryPath);
            }

            GeneratedDeltas = items;
            return true;
        }

        private static async Task<ITaskItem[]> ReadSummary (string summaryPath) {
            using var stream = System.IO.File.OpenRead(summaryPath);
            var json = await System.Text.Json.JsonSerializer.DeserializeAsync<OutputSummary.OutputSummary>(stream);
            var deltas = json?.Deltas;
            var n = deltas?.Length ?? 0;
            var u = new ITaskItem[n];
            for (int i = 0; i < n; ++i) {
                var x = deltas?[i]!;
                u[i] = new TaskItem(x.Metadata, new Dictionary<string,string> {
                    {"Assembly", x.Assembly!},
                    { "DeltaIL", x.IL!},
                    { "DeltaPDB", x.PDB!}
                });
            }
            return u;
        }

        protected override string GenerateFullPathToTool() => null!;
        protected override string GenerateCommandLineCommands()
        {
            var builder = new CommandLineBuilder();
            builder.AppendSwitchIfNotNull("-msbuild:", ProjectFile);
            builder.AppendSwitchIfNotNull("-script:", ScriptPath);
            builder.AppendSwitchIfNotNull("-outputSummary:", computedOutputSummaryPath);
            if (BuildProperties is string[] props) {
                foreach (var p in props) {
                    builder.AppendSwitchIfNotNull("-p:", p);
                }
            }
            return builder.ToString();
        }
    }
}
