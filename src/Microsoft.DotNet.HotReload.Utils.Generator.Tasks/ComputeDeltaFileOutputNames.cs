// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using Script = Microsoft.DotNet.HotReload.Utils.Generator.Script.Json.Script;

public class ComputeDeltaFileOutputNames : Microsoft.Build.Utilities.Task
{
    [Required]
    public string BaseAssemblyName { get; set; }
    [Required]
    public string DeltaScript {get; set; }

    [Output]
    public ITaskItem[] DeltaOutputs { get; set; }

    public ComputeDeltaFileOutputNames()
    {
        BaseAssemblyName = string.Empty;
        DeltaScript = string.Empty;
        DeltaOutputs = Array.Empty<ITaskItem>();
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
        ITaskItem[] result = new TaskItem[3*count];
        for (int i = 0; i < count; ++i)
        {
            int rev = 1+i;
            string dmeta = baseAssemblyName + $".{rev}.dmeta";
            string dil = baseAssemblyName + $".{rev}.dil";
            string dpdb = baseAssemblyName + $".{rev}.dpdb";
            result[3*i] = new TaskItem(dmeta);
            result[3*i+1] = new TaskItem(dil);
            result[3*i+2] = new TaskItem(dpdb);
        }
        DeltaOutputs = result;
        return true;
    }

    public static async Task<Script?> Parse(string scriptPath, CancellationToken ct = default)
    {
        using var stream = System.IO.File.OpenRead(scriptPath);
        var options = new JsonSerializerOptions {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
        };
        var json = await JsonSerializer.DeserializeAsync<Script>(stream, options: options, cancellationToken: ct);
        return json;
    }

}
