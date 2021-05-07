// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;


namespace Microsoft.DotNet.HotReload.Utils.Generator.Tasks
{

    /// Given a DeltaScript, counts the number of elements and returns items for the .dmeta, .dil, and .dpdb
    /// files that the Generator would produce.
    public class HotReloadDeltaGeneratorComputeScriptOutputs : Microsoft.Build.Utilities.Task
    {
        /// The name of the assembly produced by the current project
        [Required]
        public string BaseAssemblyName { get; set; }
        /// The name of the json delta script
        [Required]
        public string DeltaScript {get; set; }


        [Output]
        public ITaskItem[] DeltaOutputs { get; set; }

        public HotReloadDeltaGeneratorComputeScriptOutputs()
        {
            BaseAssemblyName = string.Empty;
            DeltaScript = string.Empty;
            DeltaOutputs = Array.Empty<ITaskItem>();
        }

        enum DeltaOutputType {
            dmeta,
            dil,
            dpdb
        }

        public override bool Execute()
        {
            if (!System.IO.File.Exists(DeltaScript))
            {
                Log.LogError("Hot reload delta script {0} does not exist", DeltaScript);
                return false;
            }
            string baseAssemblyName = BaseAssemblyName;
            int count;
            try
            {
                var json = Parse(DeltaScript).Result;
                if (json?.Changes == null) {
                    Log.LogError("Hot reload delta script had no 'changes' array");
                    return false;
                }
                count = json.Changes.Length;
            }
            catch (JsonException exn)
            {
                Log.LogErrorFromException(exn, showStackTrace: true);
                return false;
            }
            const string deltaOutputTypeMetadata = "DeltaOutputType";
            ITaskItem[] result = new TaskItem[3*count];
            for (int i = 0; i < count; ++i)
            {
                int rev = 1+i;
                string dmeta = NameForOutput (baseAssemblyName, rev, DeltaOutputType.dmeta);
                string dil = NameForOutput (baseAssemblyName, rev, DeltaOutputType.dil);
                string dpdb = NameForOutput (baseAssemblyName, rev, DeltaOutputType.dpdb);
                result[3*i] = new TaskItem(dmeta, new Dictionary<string,string> { { deltaOutputTypeMetadata, nameof(DeltaOutputType.dmeta)}});
                result[3*i+1] = new TaskItem(dil, new Dictionary<string,string> { { deltaOutputTypeMetadata, nameof(DeltaOutputType.dil)}});
                result[3*i+2] = new TaskItem(dpdb, new Dictionary<string,string> { { deltaOutputTypeMetadata, nameof(DeltaOutputType.dpdb)}});
            }
            DeltaOutputs = result;
            return true;
        }

        private static string NameForOutput(string baseName, int rev, DeltaOutputType t)
        {
            string ext = t switch {
                DeltaOutputType.dmeta => "dmeta",
                DeltaOutputType.dil => "dil",
                DeltaOutputType.dpdb => "dpdb",
                _ => throw new Exception("unexpected")
            };
            return $"{baseName}.{rev}.{ext}";
        }

        public static async Task<Script.Json.Script?> Parse(string scriptPath, CancellationToken ct = default)
        {
            using var stream = System.IO.File.OpenRead(scriptPath);
            var options = new JsonSerializerOptions {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
            };
            var json = await JsonSerializer.DeserializeAsync<Script.Json.Script>(stream, options: options, cancellationToken: ct);
            return json;
        }

    }
}
