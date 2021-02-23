using System.Collections.Generic;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.DotNet.HotReload.Utils
{
    public class DeltaGeneratorTask : Task
    {
        [Required]
        public string? ScriptPath { get; set; }
        [Output]

        /// Each item is a full path of the generated dmeta file
        /// and has metadata DeltaIL and DeltaPDB with the full paths of the dmeta and dpdb files.
        public ITaskItem[]? GeneratedDeltas { get; set; }
        public override bool Execute()
        {
            if (ScriptPath is not string p) {
                Log.LogError ("no ScriptPath");
                return false;
            }
            if (!System.IO.File.Exists(p)) {
                Log.LogError ($"script file {p} does not exist");
                return false;
            }

            var items = new ITaskItem[1];
            items[0] = new TaskItem("/tmp/Foo.0.dmeta", new Dictionary<string,string> {
                { "DeltaIL", "/tmp/Foo.0.dil"},
                { "DeltaPDB", "/tmp/Foo.0.dpdb"}
            });

            GeneratedDeltas = items;
            return true;
        }
    }
}
