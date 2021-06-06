// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.HotReload.Utils.Generator {
    public abstract class Runner {

        public static Runner Make (Config config)
        {
            if (config.Live)
                return new Runners.LiveRunner (config);
            else
                return new Runners.ScriptRunner (config);
        }
        public async Task Run (CancellationToken ct = default) {
            var baselineArtifacts = await SetupBaseline (ct);

            var deltaProject = new DeltaProject (baselineArtifacts, PrepareCapabilities());
            var derivedInputs = SetupDeltas (baselineArtifacts, ct);

            await GenerateDeltas (deltaProject, derivedInputs, makeOutputs: MakeOutputs, outputsReady: OutputsReady, ct: ct);
            // FIXME: do something for LiveRunner
            if (OutputsDone != null)
                await OutputsDone (ct);
        }

        readonly protected Config config;
        protected Runner (Config config) {
            this.config = config;
        }

        /// Delegate that is called to create the delta output streams.
        /// If not set, a default is used that writes the deltas to files.
        protected  Func<DeltaNaming,DeltaOutputStreams>? MakeOutputs {get; set; } = null;

        /// Delegate that is called after the outputs have been emitted.
        /// If not set, a default is used that does nothing.
        protected  Action<DeltaNaming,DeltaOutputStreams>? OutputsReady {get; set; } = null;

        /// Called when all the outputs have been emitted.
        protected Func<CancellationToken,Task>? OutputsDone {get; set;} = null;

        public async Task<BaselineArtifacts> SetupBaseline (CancellationToken ct = default) {
            BaselineProject? baselineProject;
            InitMSBuild();
            baselineProject = await Microsoft.DotNet.HotReload.Utils.Generator.BaselineProject.Make (config, ct);

            var baselineArtifacts = await baselineProject.PrepareBaseline(ct);

            Console.WriteLine ("baseline ready");
            return baselineArtifacts;
        }

        protected abstract EnC.EditAndContinueCapabilities PrepareCapabilitiesCore ();

        protected EnC.EditAndContinueCapabilities PrepareCapabilities() {
            EnC.EditAndContinueCapabilities caps = EnC.EditAndContinueCapabilities.None;
            (var configuredCaps, var unknowns) = EditAndContinueCapabilitiesParser.Parse (config.EditAndContinueCapabilities);
            foreach (var c in configuredCaps) {
                caps |= c;
            }
            var runnerCaps = PrepareCapabilitiesCore ();
            caps |= runnerCaps;
            if (caps == EnC.EditAndContinueCapabilities.None)
                caps = DefaultCapabilities ();
            if (!config.NoWarnUnknownCapabilities) {
                foreach (var unk in unknowns) {
                    Console.WriteLine ("Warning: Unknown EnC capability '{0}', ignored.", unk);
                }
            }
            return caps;
        }

        protected EnC.EditAndContinueCapabilities DefaultCapabilities ()
        {
            var allCaps = EnC.EditAndContinueCapabilities.Baseline | EnC.EditAndContinueCapabilities.AddMethodToExistingType
                | EnC.EditAndContinueCapabilities.AddStaticFieldToExistingType | EnC.EditAndContinueCapabilities.AddInstanceFieldToExistingType
                | EnC.EditAndContinueCapabilities.NewTypeDefinition;
            return allCaps;
        }
        public abstract IAsyncEnumerable<Delta> SetupDeltas (BaselineArtifacts baselineArtifacts, CancellationToken ct = default);

        public async Task GenerateDeltas (DeltaProject deltaProject, IAsyncEnumerable<Delta> deltas,
                                          Func<DeltaNaming,DeltaOutputStreams>? makeOutputs = null,
                                          Action<DeltaNaming, DeltaOutputStreams>? outputsReady = null,
                                          CancellationToken ct =  default)
        {
            await foreach (var delta in deltas.WithCancellation(ct)) {
                Console.WriteLine ("got a change");
                /* fixme: why does FSW sometimes queue up 2 events in quick succession after a single save? */
                deltaProject = await deltaProject.BuildDelta (delta, ignoreUnchanged: config.Live, makeOutputs: makeOutputs, outputsReady: outputsReady, ct: ct);
            }
        }

        private static void InitMSBuild ()
        {
            Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();
        }

    }
}
