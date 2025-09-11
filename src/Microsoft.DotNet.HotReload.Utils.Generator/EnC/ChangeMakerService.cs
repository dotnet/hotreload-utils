using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host;

namespace Microsoft.DotNet.HotReload.Utils.Generator.EnC;

public class ChangeMakerService
{
    private const string csharpCodeAnalysisAssemblyName = "Microsoft.CodeAnalysis.Features";

    private const string watchServiceName = "Microsoft.CodeAnalysis.ExternalAccess.Watch.Api.WatchHotReloadService";
    private readonly Type _watchServiceType;
    private readonly object _watchHotReloadService;
    private ChangeMakerService(Type watchServiceType, object watchHotReloadService)
    {
        _watchServiceType = watchServiceType;
        _watchHotReloadService = watchHotReloadService;
    }

    public static ChangeMakerService Make (HostWorkspaceServices hostWorkspaceServices, EditAndContinueCapabilities capabilities) {
        ImmutableArray<string> caps = CapabilitiesToStrings(capabilities);
        Console.WriteLine("initializing ChangeMakerService with capabilities: " + string.Join(", ", caps));
        (var watchServiceType, var watchHotReloadService) = InstantiateWatchHotReloadService(hostWorkspaceServices, caps);
        return new ChangeMakerService(watchServiceType, watchHotReloadService);
    }

    public readonly record struct Update (Guid ModuleId, ImmutableArray<byte> ILDelta, ImmutableArray<byte> MetadataDelta, ImmutableArray<byte> PdbDelta, ImmutableArray<int> UpdatedTypes);

    public enum Status
    {
        /// <summary>
        /// No significant changes made that need to be applied.
        /// </summary>
        NoChangesToApply,

        /// <summary>
        /// Changes can be applied either via updates or restart.
        /// </summary>
        ReadyToApply,

        /// <summary>
        /// Some changes are errors that block rebuild of the module.
        /// This means that the code is in a broken state that cannot be resolved by restarting the application.
        /// </summary>
        Blocked,
    }

    public readonly struct Updates2
    {
        /// <summary>
        /// Status of the updates.
        /// </summary>
        public readonly Status Status { get; init; }

        /// <summary>
        /// Syntactic, semantic and emit diagnostics.
        /// </summary>
        /// <remarks>
        /// <see cref="Status"/> is <see cref="Status.Blocked"/> if these diagnostics contain any errors.
        /// </remarks>
        public required ImmutableArray<Diagnostic> CompilationDiagnostics { get; init; }

        /// <summary>
        /// Rude edits per project.
        /// </summary>
        public required ImmutableArray<(ProjectId project, ImmutableArray<Diagnostic> diagnostics)> RudeEdits { get; init; }

        /// <summary>
        /// Updates to be applied to modules. Empty if there are blocking rude edits.
        /// Only updates to projects that are not included in <see cref="ProjectsToRebuild"/> are listed.
        /// </summary>
        public ImmutableArray<Update> ProjectUpdates { get; init; }

        /// <summary>
        /// Running projects that need to be restarted due to rude edits in order to apply changes.
        /// </summary>
        public ImmutableDictionary<ProjectId, ImmutableArray<ProjectId>> ProjectsToRestart { get; init; }

        /// <summary>
        /// Projects with changes that need to be rebuilt in order to apply changes.
        /// </summary>
        public ImmutableArray<ProjectId> ProjectsToRebuild { get; init; }
    }

    private static ImmutableArray<string> CapabilitiesToStrings(EditAndContinueCapabilities capabilities)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var names = Enum.GetNames(typeof(EditAndContinueCapabilities));
        foreach (var name in names)
        {
            var val = Enum.Parse<EditAndContinueCapabilities>(name);
            if (val == EditAndContinueCapabilities.None)
                continue;
            if (capabilities.HasFlag(val))
            {
                builder.Add(name);
            }
        }
        return builder.ToImmutable();
    }

    private static Update WrapUpdate (object update)
    {
        var updateType = update.GetType()!;
        var moduleId = updateType.GetField("ModuleId")!.GetValue(update)!;
        var ilDelta = updateType.GetField("ILDelta")!.GetValue(update)!;
        var metadataDelta = updateType.GetField("MetadataDelta")!.GetValue(update)!;
        var pdbDelta = updateType.GetField("PdbDelta")!.GetValue(update)!;
        var updatedTypes = updateType.GetField("UpdatedTypes")!.GetValue(update)!;
        return new Update((Guid)moduleId, (ImmutableArray<byte>)ilDelta, (ImmutableArray<byte>)metadataDelta, (ImmutableArray<byte>)pdbDelta, (ImmutableArray<int>)updatedTypes);

    }

    private static Updates2 WrapUpdates(object updates)
    {
        return new Updates2
        {
            Status = (Status)updates.GetType().GetProperty("Status")!.GetValue(updates)!,
            CompilationDiagnostics = (ImmutableArray<Diagnostic>)updates.GetType().GetProperty("CompilationDiagnostics")!.GetValue(updates)!,
            RudeEdits = (ImmutableArray<(ProjectId, ImmutableArray<Diagnostic>)>)updates.GetType().GetProperty("RudeEdits")!.GetValue(updates)!,
            ProjectUpdates = [.. ((IEnumerable)updates.GetType().GetProperty("ProjectUpdates")!.GetValue(updates)!).Cast<object>().Select(WrapUpdate)],
            ProjectsToRestart = (ImmutableDictionary<ProjectId, ImmutableArray<ProjectId>>)updates.GetType().GetProperty("ProjectsToRestart")!.GetValue(updates)!,
            ProjectsToRebuild = (ImmutableArray<ProjectId>)updates.GetType().GetProperty("ProjectsToRebuild")!.GetValue(updates)!
        };
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
        mi.Invoke(_watchHotReloadService, Array.Empty<object>());
    }

    public void CommitUpdate()
    {
        var mi = _watchServiceType.GetMethod("CommitUpdate");
        if (mi == null)
        {
            throw new Exception($"could not find method {watchServiceName}.CommitUpdate");
        }
        mi.Invoke(_watchHotReloadService, Array.Empty<object>());
    }

    public Task<Updates2> EmitSolutionUpdateAsync(Solution solution, CancellationToken cancellationToken)
    {
        var runningProjectInfoType = _watchServiceType.GetNestedType("RunningProjectInfo", BindingFlags.Public)!;
        var immutableDictionaryCreateRangeMethod = typeof(ImmutableDictionary).GetMethod(
            "CreateRange",
            BindingFlags.Static | BindingFlags.Public,
            [ typeof(IEnumerable<>).MakeGenericType(Type.MakeGenericSignatureType(typeof(KeyValuePair<,>), Type.MakeGenericMethodParameter(0), Type.MakeGenericMethodParameter(1))) ])!
            .MakeGenericMethod(typeof(ProjectId), runningProjectInfoType);

        var mi = _watchServiceType.GetMethod("GetUpdatesAsync", BindingFlags.Public | BindingFlags.Instance, [typeof(Solution), immutableDictionaryCreateRangeMethod.ReturnType, typeof(CancellationToken)]);
        if (mi == null) {
            throw new Exception($"could not find method {watchServiceName}.GetUpdatesAsync");
        }

        var kvpProjectIdRunningProjectInfoType = typeof(KeyValuePair<,>).MakeGenericType(typeof(ProjectId), runningProjectInfoType);

        var runningProjectsList = Array.CreateInstance(kvpProjectIdRunningProjectInfoType, solution.ProjectIds.Count);

        for (int i = 0; i < solution.ProjectIds.Count; i++)
        {
            var projectId = solution.ProjectIds[i];
            var runningProjectInfo = Activator.CreateInstance(runningProjectInfoType)!;
            runningProjectsList.SetValue(Activator.CreateInstance(kvpProjectIdRunningProjectInfoType, projectId, runningProjectInfo)!, i);
        }

        object resultTask = mi.Invoke(_watchHotReloadService, [solution, immutableDictionaryCreateRangeMethod.Invoke(null, [ runningProjectsList ])!, cancellationToken])!;

        // The task returns Updates2, except that
        // the Update2 type is a nested struct in WatchHotReloadService, so we can't write the type directly.
        // Instead we take apart the type and convert the first component to our own Update2 type.
        //
        // We basically want to do
        //   resultTask.ContinueWith ((t) => WrapUpdate(t.Result));
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


        var tcs = new TaskCompletionSource<Updates2>();

        var awaiter = resultTask.GetType().GetMethod("GetAwaiter")!.Invoke(resultTask, Array.Empty<object>())!;

        Action continuation = delegate {
            try {
                var result = awaiter.GetType().GetMethod("GetResult")!.Invoke(awaiter, Array.Empty<object>())!;
                tcs.SetResult (WrapUpdates (result));
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
