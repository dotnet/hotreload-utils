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
        public ChangeMakerService(HostWorkspaceServices hostWorkspaceServices, ImmutableArray<string> capabilities) {
            (_watchServiceType, _watchHotReloadService) = InstantiateWatchHotReloadService(hostWorkspaceServices, capabilities);
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
            var tcs = new TaskCompletionSource<(ImmutableArray<Update>, ImmutableArray<Diagnostic>)>();

            var awaiter = resultTask.GetType().GetMethod("GetAwaiter")!.Invoke(resultTask, Array.Empty<object>())!;

            Action continuation = delegate {
                var result = awaiter.GetType().GetMethod("GetResult")!.Invoke(awaiter, Array.Empty<object>())!;
                var resultType = result.GetType();

                var updates = resultType.GetField("Item1")!.GetValue(result)!;
                var diagnostics = (ImmutableArray<Diagnostic>)resultType.GetField("Item2")!.GetValue(result)!;
                tcs.SetResult ((WrapUpdates (updates), diagnostics));
            };

            awaiter.GetType().GetMethod("OnCompleted")!.Invoke(awaiter, new object[] { continuation });

            return tcs.Task;
        }
    }
}