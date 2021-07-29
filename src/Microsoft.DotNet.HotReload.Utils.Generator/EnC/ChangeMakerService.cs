using System;
using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.CSharp.Features;

namespace Microsoft.DotNet.HotReload.Utils.Generator.EnC
{
    public class ChangeMakerService
    {
        private const string csharpCodeAnalysisAssemblyName = "Microsoft.CodeAnalysis.Features";

        private const string watchServiceName = "Microsoft.CodeAnalysis.ExternalAccess.Watch.Api.WatchHotReloadService";
        private Type _watchServiceType;
        private object _watchHotReloadService;
        public ChangeMakerService(HostWorkspaceServices hostWorkspaceServices, EditAndContinueCapabilities capabilities) {
            (_watchServiceType, _watchHotReloadService) = InstantiateWatchHotReloadService(hostWorkspaceServices, CapabilitiesToStrings(capabilities));
        }

        public struct Update
        {
            public readonly Guid ModuleId;
            public readonly ImmutableArray<byte> ILDelta;
            public readonly ImmutableArray<byte> MetadataDelta;
            public readonly ImmutableArray<byte> PdbDelta;
            public readonly ImmutableArray<int> UpdatedTypes;

            public Update(Guid moduleId, ImmutableArray<byte> ilDelta, ImmutableArray<byte> metadataDelta, ImmutableArray<byte> pdbDelta, ImmutableArray<int> updatedTypes)
            {
                ModuleId = moduleId;
                ILDelta = ilDelta;
                MetadataDelta = metadataDelta;
                PdbDelta = pdbDelta;
                UpdatedTypes = updatedTypes;
            }
        }

        private static ImmutableArray<string> CapabilitiesToStrings(EditAndContinueCapabilities capabilities)
        {
            var builder = ImmutableArray.CreateBuilder<string>();
            var names = Enum.GetNames(typeof(EditAndContinueCapabilities));
            Console.Write ("initializing with capabilities ");
            foreach (var name in names) {
                var val = Enum.Parse<EditAndContinueCapabilities>(name);
                if (val == EditAndContinueCapabilities.None)
                    continue;
                if (capabilities.HasFlag(val)) {
                    builder.Add(name);
                    Console.Write($"{name} ");
                }
            }
            Console.WriteLine();
            return builder.ToImmutable();
        }

        private Update WrapUpdate (object update)
        {
            var updateType = update.GetType()!;
            var moduleId = updateType.GetField("ModuleId")!.GetValue(update)!;
            var ilDelta = updateType.GetField("ILDelta")!.GetValue(update)!;
            var metadataDelta = updateType.GetField("MetadataDelta")!.GetValue(update)!;
            var pdbDelta = updateType.GetField("PdbDelta")!.GetValue(update)!;
            var updatedTypes = updateType.GetField("UpdatedTypes")!.GetValue(update)!;
            return new Update((Guid)moduleId, (ImmutableArray<byte>)ilDelta, (ImmutableArray<byte>)metadataDelta, (ImmutableArray<byte>)pdbDelta, (ImmutableArray<int>)updatedTypes);

        }

        private ImmutableArray<Update> WrapUpdates (object updates)
        {
            IEnumerable updatesEnumerable = (IEnumerable)updates;
            var builder = ImmutableArray.CreateBuilder<Update>();
            foreach (var update in updatesEnumerable)
            {
                builder.Add(WrapUpdate(update));
            }
            return builder.ToImmutable();
        }
        public static (Type, object) InstantiateWatchHotReloadService(HostWorkspaceServices hostWorkspaceServices, ImmutableArray<string> capabilities)
        {
            var an = new AssemblyName(csharpCodeAnalysisAssemblyName);
            var assm = AssemblyLoadContext.Default.LoadFromAssemblyName(an);
            if (assm == null) {
                throw new Exception($"could not load assembly {an}");
            }
            var type = assm.GetType(watchServiceName);
            if (type == null) {
                throw new Exception($"could not load type {watchServiceName}");
            }
            var argTys = new Type[] { typeof(HostWorkspaceServices), typeof(ImmutableArray<string>) };
            var ctor = type.GetConstructor(argTys);
            if (ctor == null) {
                throw new Exception ($"could not find ctor {watchServiceName} ({argTys[0]}, {argTys[1]})");
            }
            object service = ctor!.Invoke(new object[] { hostWorkspaceServices, capabilities })!;
            return (type, service);
        }

        public Task StartSessionAsync (Solution solution, CancellationToken ct = default)
        {
            var mi = _watchServiceType.GetMethod("StartSessionAsync");
            if (mi == null) {
                throw new Exception($"could not find method {watchServiceName}.StartSessionAsync");
            }
            return (Task)mi.Invoke(_watchHotReloadService, new object[] { solution, ct })!;
        }

        public void EndSession ()
        {
            var mi = _watchServiceType.GetMethod("EndSession");
            if (mi == null) {
                throw new Exception($"could not find method {watchServiceName}.EndSession");
            }
            mi.Invoke(_watchHotReloadService, new object[] { });
        }

        public Task<(ImmutableArray<Update> updates, ImmutableArray<Diagnostic> diagnostics)> EmitSolutionUpdateAsync(Solution solution, CancellationToken cancellationToken)
        {
            var mi = _watchServiceType.GetMethod("EmitSolutionUpdateAsync");
            if (mi == null) {
                throw new Exception($"could not find method {watchServiceName}.EmitSolutionUpdateAsync");
            }
            object resultTask = mi.Invoke(_watchHotReloadService, new object[] { solution, cancellationToken })!;

            // The task returns (ImmutableArray<Update>, ImmutableArray<Diagnostic>), except that
            // the Update is the nested class in WatchHotReloadService, so we can't write the type directly.
            // Instead we take apart the tuple and convert the first component to an array of our own Update type.
            //
            // We basically want to do
            //   resultTask.ContinueWith ((t) => (WrapUpdate(t.Result.Item1), t.Result.Item2);
            // but then we need to make a Func<T> that again mentions the internal Update type.
            //
            // So instead we do:
            //
            //  var tcs = new TaskCompletionSource<...>();
            //  var awaiter = resultTask.GetAwaiter();
            //  awaiter.OnCompleted(delegate {
            //     object result = awaiter.GetResult();
            //     tcs.SetResult(Wrap (result));
            //   });
            //  return tcs.Task;
            //
            //  because OnCompleted only needs an Action and we can use reflection to take the result apart


            var tcs = new TaskCompletionSource<(ImmutableArray<Update>, ImmutableArray<Diagnostic>)>();

            var awaiter = resultTask.GetType().GetMethod("GetAwaiter")!.Invoke(resultTask, Array.Empty<object>())!;

            Action continuation = delegate {
                try {
                    var result = awaiter.GetType().GetMethod("GetResult")!.Invoke(awaiter, Array.Empty<object>())!;
                    var resultType = result.GetType();

                    var updates = resultType.GetField("Item1")!.GetValue(result)!;
                    var diagnostics = (ImmutableArray<Diagnostic>)resultType.GetField("Item2")!.GetValue(result)!;
                    tcs.SetResult ((WrapUpdates (updates), diagnostics));
                } catch (TaskCanceledException e) {
                    tcs.TrySetCanceled(e.CancellationToken);
                } catch (Exception e) {
                    tcs.TrySetException (e);
                }
            };

            awaiter.GetType().GetMethod("OnCompleted")!.Invoke(awaiter, new object[] { continuation });

            return tcs.Task;
        }
    }
}
