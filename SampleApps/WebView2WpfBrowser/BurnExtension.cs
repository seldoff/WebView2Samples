#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
// using DuckDuckGo.Windows;
//using DuckDuckGo.Windows.Utils;

//using static DuckDuckGo.WebView.DevToolsProtocol.Constants;

namespace WebView2WpfBrowser;

internal class DevToolsProtocolEventReceivedEventArgs(CoreWebView2DevToolsProtocolEventReceivedEventArgs coreWebView2DevToolsProtocolEventReceivedEventArgs) : EventArgs
{
    public string ParameterObjectAsJson => coreWebView2DevToolsProtocolEventReceivedEventArgs.ParameterObjectAsJson;

    public string SessionId => coreWebView2DevToolsProtocolEventReceivedEventArgs.SessionId;
}

public enum BurnExtensionOperation
{
    Init,
    UnpackExtension,
    AddExtension,
    FindExtensionTarget,
    PollForExtensionTarget,
    AttachToExtensionTarget,
    SetupRuntimeBinding,
    CreateBurnCompletedTask,
    StartBurn,
    WaitingForBurnCompletion,
    DetachFromExtensionTarget
}

public class BurnExtensionException(Exception exception, BurnExtensionOperation operation)
    : Exception($"Failure during operation {operation}: {exception.Message}", exception)
{
    public BurnExtensionOperation Operation { get; private init; } = operation;
}

/// <summary>
/// Loads a custom browser extension into a WebView2 user profile and uses extension API to clear browsing data
/// </summary>
/// <param name="webView">CoreWebView2 instance to use</param>
/// <param name="extensionTargetDirectory">
/// Directory where the extension code should be put. It may not exist and its content might be removed.
/// </param>
/// <remarks>
/// To debug the extension code, use console object (e.g. console.log). See the output using the Inspect button on
/// the 'edge://serviceworker-internals/' special page. Create a bookmark with this URL to access it.
/// </remarks>
public class BurnExtension(
    CoreWebView2 webView,
    string extensionTargetDirectory//,
    /*OperationTracker<Enum> operationTracker*/)
{
    public static readonly string EmptyDevToolsProtocolCommandPayload = "{}";

    private record Target(
        [property: JsonPropertyName("targetId")] string TargetId,
        [property: JsonPropertyName("url")] string Url);

    private record GetTargetsResponse([property: JsonPropertyName("targetInfos")] Target[] TargetInfos);

    private record AttachToTargetResponse([property: JsonPropertyName("sessionId")] string SessionId);

    private record WebMessageBindingCalled(
        [property: JsonPropertyName("name")]
        string Name,

        [property: JsonPropertyName("payload")]
        string Payload,

        [property: JsonPropertyName("executionContextId")]
        int ExecutionContextId
    );

    private static string? extensionId;

    /// <summary>
    /// Uses chrome.browsingData.remove extension API to remove browsing data. See index.js file in the same directory.
    /// </summary>
    /// <param name="fromDate">An optional start date for burning</param>
    /// <param name="excludeDomains">Domains to exclude from burning. Domains should be in form of 'example.com'.</param>
    public async Task Burn(DateTime? fromDate, IReadOnlyCollection<string> excludeDomains)
    {
        var currentOperation = BurnExtensionOperation.Init;
        try
        {
            EnterOperation(BurnExtensionOperation.FindExtensionTarget, out currentOperation);
            var extensionTarget = extensionId != null ? await FindExtensionTarget(extensionId) : null;

            if (extensionTarget == null)
            {
                // Two possibilities here:
                // 1. The extension was not yet added during this browser session. We must add it to obtain the extension id. Extension id
                //    depends on the extension content so it is not feasible to persist it between browser sessions. E.g. if the next browser version
                //    changes the extension content, the extension id will change.
                // 2. The extension was already added during this browser session, but the extension target is not found. This happens because the
                //    extension target may be closed by the Chromium after a period of inactivity. See https://developer.chrome.com/docs/extensions/develop/concepts/service-workers/lifecycle#idle-shutdown.
                //    In this case, we must add the extension again to "wake up" the extension target.
                // BEWARE: calling AddBrowserExtensionAsync second time while the extension target is still active will succeed, but the call to
                // Runtime.addBinding inside SetupRuntimeBinding will hang indefinitely. It is unknown why this happens, but we must be sure to call
                // AddBrowserExtensionAsync only if there is no active extension target.

                EnterOperation(BurnExtensionOperation.UnpackExtension, out currentOperation);
                await UnpackExtension();

                EnterOperation(BurnExtensionOperation.AddExtension, out currentOperation);
                var extension = await webView.Profile.AddBrowserExtensionAsync(extensionTargetDirectory);
                extensionId = extension.Id;

                EnterOperation(BurnExtensionOperation.PollForExtensionTarget, out currentOperation);
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                extensionTarget = await PollForExtensionTarget(extension.Id, cts.Token);
            }

            var extensionTargetId = extensionTarget?.TargetId ?? throw new Exception("Extension target not found");

            EnterOperation(BurnExtensionOperation.AttachToExtensionTarget, out currentOperation);
            var sessionId = await AttachToExtensionTarget(extensionTargetId);

            EnterOperation(BurnExtensionOperation.SetupRuntimeBinding, out currentOperation);
            var messagesFromExtension = await SetupRuntimeBinding(sessionId);

            EnterOperation(BurnExtensionOperation.CreateBurnCompletedTask, out currentOperation);
            var burnCompleted = CreateBurnCompletedTask(messagesFromExtension);

            EnterOperation(BurnExtensionOperation.StartBurn, out currentOperation);
            await StartBurn(fromDate, excludeDomains, sessionId);

            EnterOperation(BurnExtensionOperation.WaitingForBurnCompletion, out currentOperation);
            await burnCompleted;

            EnterOperation(BurnExtensionOperation.DetachFromExtensionTarget, out currentOperation);
            await DetachFromExtensionTarget(sessionId);
        }
        catch (Exception e)
        {
            //Diagnostic.Log.Error($"Failure during operation {currentOperation}: {e.Message}");
            throw new BurnExtensionException(e, currentOperation);
        }
    }

    private void EnterOperation(BurnExtensionOperation enteredOperation, out BurnExtensionOperation operation)
    {
        operation = enteredOperation;
        App.Current.MainWindow.Title = $"BURNING: {operation}";
        Debug.WriteLine($"[{DateTime.Now.TimeOfDay}] ENTER OPERATION: {enteredOperation}");
        //Diagnostic.Log.BurnOperationStarted(operation.ToString());
        //operationTracker.EnterOperation(operation);
    }

    private async Task UnpackExtension()
    {
        if (!Directory.Exists(extensionTargetDirectory))
        {
            Directory.CreateDirectory(extensionTargetDirectory);
        }

        const string resourcePrefix = "WebView2WpfBrowser.Extension.";
        var resourceNames = Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Where(r => r.StartsWith(resourcePrefix));

        foreach (var resource in resourceNames)
        {
            var fileName = Path.Combine(extensionTargetDirectory, resource[resourcePrefix.Length..]);
            await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            await using var fileStream = File.Create(fileName);
            await stream.CopyToAsync(fileStream);
            await fileStream.FlushAsync();
        }
    }

    private async Task<string> AttachToExtensionTarget(string targetId)
    {
        var attachToTargetResponse = await webView.CallDevToolsProtocolMethodAsync("Target.attachToTarget",
            JsonSerializer.Serialize(new { targetId, flatten = true }));
        var attachToTargetResponseParsed = JsonSerializer.Deserialize<AttachToTargetResponse>(attachToTargetResponse);
        return attachToTargetResponseParsed.SessionId;
    }

    private async Task<Target?> PollForExtensionTarget(string extensionId, CancellationToken cancellationToken)
    {
        // We poll for the extension target until it appears or the cancellationToken is cancelled.
        while (!cancellationToken.IsCancellationRequested)
        {
            var extensionTarget = await FindExtensionTarget(extensionId);
            if (extensionTarget != null)
            {
                return extensionTarget;
            }

            await Task.Delay(50, cancellationToken);
        }

        return null;
    }

    private async Task<Target?> FindExtensionTarget(string extensionId)
    {
        var targets = await webView.CallDevToolsProtocolMethodAsync(
            "Target.getTargets", EmptyDevToolsProtocolCommandPayload);
        var targetsParsed = JsonSerializer.Deserialize<GetTargetsResponse>(targets);
        return targetsParsed.TargetInfos
            .SingleOrDefault(t => t.Url == $"chrome-extension://{extensionId}/index.js");
    }

    private async Task<IObservable<JsonObject>> SetupRuntimeBinding(string sessionId)
    {
        const string bindingName = "postNativeMessageBinding";

        await webView.CallDevToolsProtocolMethodForSessionAsync(sessionId, "Runtime.addBinding",
            JsonSerializer.Serialize(new { name = bindingName }));

        var receiver = webView.GetDevToolsProtocolEventReceiver("Runtime.bindingCalled");
        return Observable
            .FromEvent<EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs>, DevToolsProtocolEventReceivedEventArgs>(
                handler => (s, e) => handler(new DevToolsProtocolEventReceivedEventArgs(e)),
                handler => receiver.DevToolsProtocolEventReceived += handler,
                handler => receiver.DevToolsProtocolEventReceived -= handler)
            .Where(e => e.SessionId == sessionId)
            .Select(e => JsonSerializer.Deserialize<WebMessageBindingCalled>(e.ParameterObjectAsJson))
            .Where(e => e.Name == bindingName)
            .Select(e => JsonNode.Parse(e.Payload).AsObject())
            .Publish()
            .RefCount();
    }

    private async Task CreateBurnCompletedTask(IObservable<JsonObject> messagesFromExtension)
    {
        var burnCompleted = new TaskCompletionSource();
        using var _ = messagesFromExtension
            .Subscribe(m =>
            {
                switch (m["state"].AsValue().GetValue<string>())
                {
                    case "burned":
                        burnCompleted.SetResult();
                        break;
                    case "error":
                        var error = m.ContainsKey("error")
                            ? m["error"].AsValue().GetValue<string>()
                            : "Unknown error";
                        burnCompleted.SetException(new Exception(error));
                        break;
                }
            }, exception => burnCompleted.SetException(exception));

        // Awaiting here so that messagesFromExtension subscription above is not disposed prematurely.
        await burnCompleted.Task;
    }

    private async Task StartBurn(DateTime? fromDate, IReadOnlyCollection<string> excludeDomains, string sessionId)
    {
        var excludeOriginsList = GetExcludeOrigins(excludeDomains);
        var excludeOrigins = string.Join(',', excludeOriginsList.Select(d => $"'{d}'"));
        var fromDateStr = fromDate != null ? $"'{fromDate:o}'" : "null";
        await webView.CallDevToolsProtocolMethodForSessionAsync(sessionId, "Runtime.evaluate",
            JsonSerializer.Serialize(new { expression = $"burn({fromDateStr}, [{excludeOrigins}])" }));
    }

    private static List<string> GetExcludeOrigins(IReadOnlyCollection<string> excludeDomains)
    {
        // The excludeOrigins argument in the chrome.browsingData.remove API expects a list of origins, not domains.
        // The origin should contain a schema, and an optional port (https://html.spec.whatwg.org/multipage/browsers.html#origin).
        // For this particular call the actual schema seems to not matter, it could even be 'ftp://'.
        // Data for 'https://example.com' is still preserved if 'http://example.com' is passed to the API.
        // But to be extra sure, we include both 'http://' and 'https://' schemas.

        return excludeDomains.SelectMany<string, string>(d => [$"http://{d}", $"https://{d}"]).ToList();
    }

    private async Task DetachFromExtensionTarget(string sessionId)
    {
        // We're not forced to detach here, and the next burn will work even if we don't.
        // But detaching allows Chromium to clean up resources, see https://developer.chrome.com/docs/extensions/develop/concepts/service-workers/lifecycle#idle-shutdown.

        await webView.CallDevToolsProtocolMethodAsync("Target.detachFromTarget", JsonSerializer.Serialize(new { sessionId }));
    }
}