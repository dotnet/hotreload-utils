// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using CancellationToken = System.Threading.CancellationToken;

namespace Microsoft.DotNet.HotReload.Utils.Generator.Script.Json {
    public class Script {
        public Change[]? Changes {get; init;}

        [System.Text.Json.Serialization.JsonConstructor]
        public Script (Change[]? changes) {
            Changes = changes;
        }
    }

    public class Change {
        public string Document {get; init;}
        public string Update {get; init;}

        [System.Text.Json.Serialization.JsonConstructor]
        public Change (string document, string update) {
            Document = document;
            Update = update;
        }
    }

    /// Read a diff script from a json file
    public class Parser {

        public readonly string Path;
        private readonly string _absDir;
        public Parser (string path) {
            Path = path;
            _absDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        }
        public async ValueTask<Script?> ReadRawAsync (Stream stream, CancellationToken ct = default) {
            var options = new JsonSerializerOptions {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
            };
            try {
                var result = await JsonSerializer.DeserializeAsync<Script>(stream, options: options, cancellationToken: ct);
                return result;
            } catch (JsonException exn) {
                throw new DiffyException($"error parsing diff script '{Path}'", exn, exitStatus: 15);
            }
        }


        private string AbsPath (string relativePath) {
            return System.IO.Path.GetFullPath(relativePath, _absDir);
        }
        public async ValueTask<IReadOnlyCollection<Plan.Change<string,string>>> ReadAsync (Stream stream, CancellationToken ct = default) {
            var script = await ReadRawAsync(stream, ct);
            var changes = script?.Changes;
            if (changes == null)
                return Array.Empty<Plan.Change<string,string>>();
            var result = changes.Select(c => Plan.Change.Create(AbsPath(c.Document), AbsPath(c.Update))).ToArray();
            return result;
        }
    }
}
