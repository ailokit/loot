using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Loot.Logging;
using Microsoft.Extensions.Logging;

namespace Loot.Plugins;

/// <summary>Managed owner for Loot's small QuickJS-NG C ABI shim.</summary>
public sealed class QuickJsPluginScriptRuntime : IPluginScriptRuntime
{
    private const string LibraryName = "loot_qjs";
    private static readonly NativeMessageCallback Callback = OnNativeMessage;
    private static readonly object InstancesLock = new();
    private static readonly Dictionary<nint, WeakReference<QuickJsPluginScriptRuntime>> Instances = [];
    private readonly PluginRuntimeExecutionThread _executionThread = new();
    private nint _handle;

    public event EventHandler<PluginRuntimeMessageEventArgs>? Message;

    public async Task StartAsync(string script, string fileName, CancellationToken cancellationToken = default)
    {
        await _executionThread.RunAsync(() =>
        {
            EnsureCreated();
            var status = Native.Evaluate(_handle, script, fileName, out var error);
            if (status != 0)
            {
                var message = ReadUtf8(error) ?? "QuickJS script failed to evaluate.";
                Native.FreeString(error);
                throw new InvalidOperationException(message);
            }
            Native.FreeString(error);
            status = Native.CallOpen(_handle, out error);
            if (status != 0)
            {
                var message = ReadUtf8(error) ?? "QuickJS onOpen handler failed.";
                Native.FreeString(error);
                throw new InvalidOperationException(message);
            }
            Native.FreeString(error);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DispatchActionAsync(string action, JsonObject payload, CancellationToken cancellationToken = default)
    {
        var json = payload.ToJsonString();
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 1024 * 1024)
            throw new InvalidOperationException("Plugin action payload exceeds 1 MiB.");
        await _executionThread.RunAsync(() =>
        {
            EnsureCreated();
            var status = Native.DispatchAction(_handle, action, json, out var error);
            if (status != 0)
            {
                var message = ReadUtf8(error) ?? "QuickJS action handler failed.";
                Native.FreeString(error);
                throw new InvalidOperationException(message);
            }
            Native.FreeString(error);
        }, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => new(_executionThread.StopAsync(() =>
    {
        if (_handle != 0)
        {
            lock (InstancesLock) Instances.Remove(_handle);
            Native.Destroy(_handle);
            _handle = 0;
        }
    }));

    private void EnsureCreated()
    {
        if (_handle != 0) return;
        try
        {
            _handle = Native.Create(32 * 1024 * 1024, 1024 * 1024, Callback);
            if (_handle == 0) throw new InvalidOperationException("QuickJS runtime could not be created.");
            lock (InstancesLock) Instances[_handle] = new WeakReference<QuickJsPluginScriptRuntime>(this);
        }
        catch (DllNotFoundException exception)
        {
            ExceptionLogger.Log(exception, nameof(QuickJsPluginScriptRuntime), "QuickJS-NG native library could not be loaded", LogLevel.Error);
            throw new InvalidOperationException("QuickJS-NG library is missing next to Loot. Rebuild the application with native plugin runtime support.", exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            ExceptionLogger.Log(exception, nameof(QuickJsPluginScriptRuntime), "QuickJS-NG native library has an incompatible ABI", LogLevel.Error);
            throw new InvalidOperationException("The installed loot_qjs library does not expose the required ABI.", exception);
        }
        catch (BadImageFormatException exception)
        {
            ExceptionLogger.Log(exception, nameof(QuickJsPluginScriptRuntime), "QuickJS-NG native library has an incompatible architecture", LogLevel.Error);
            throw new InvalidOperationException("The installed loot_qjs library is not compatible with this process architecture.", exception);
        }
    }

    private static void OnNativeMessage(nint runtime, nint name, nint payload)
    {
        QuickJsPluginScriptRuntime? instance;
        lock (InstancesLock)
            instance = Instances.TryGetValue(runtime, out var reference) && reference.TryGetTarget(out var target) ? target : null;
        if (instance is null) return;
        try { instance.Message?.Invoke(instance, new PluginRuntimeMessageEventArgs(ReadUtf8(name) ?? string.Empty, JsonNode.Parse(ReadUtf8(payload) ?? "null"))); }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(QuickJsPluginScriptRuntime), "Ignored an invalid message from plugin JavaScript", LogLevel.Warning);
        }
    }

    private static string? ReadUtf8(nint value) => value == 0 ? null : Marshal.PtrToStringUTF8(value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeMessageCallback(nint runtime, nint name, nint payload);

    private static class Native
    {
        [DllImport(LibraryName, EntryPoint = "loot_qjs_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint Create(nuint maxHeapBytes, nuint maxStackBytes, NativeMessageCallback callback);
        [DllImport(LibraryName, EntryPoint = "loot_qjs_eval", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Evaluate(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string source, [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName, out nint error);
        [DllImport(LibraryName, EntryPoint = "loot_qjs_dispatch_action", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DispatchAction(nint runtime, [MarshalAs(UnmanagedType.LPUTF8Str)] string action, [MarshalAs(UnmanagedType.LPUTF8Str)] string payload, out nint error);
        [DllImport(LibraryName, EntryPoint = "loot_qjs_call_open", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int CallOpen(nint runtime, out nint error);
        [DllImport(LibraryName, EntryPoint = "loot_qjs_free_string", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FreeString(nint value);
        [DllImport(LibraryName, EntryPoint = "loot_qjs_destroy", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Destroy(nint runtime);
    }
}
