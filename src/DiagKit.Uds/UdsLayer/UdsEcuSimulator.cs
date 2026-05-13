using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using DiagKit.Uds.Contracts;
using DiagKit.Uds.Services;

namespace DiagKit.Uds.UdsLayer;

/// <summary>
/// 动态 DID 数据提供器。<br/>A dynamic DID data provider.
/// </summary>
/// <param name="did">数据标识符。<br/>The data identifier.</param>
/// <param name="cancellationToken">取消令牌。<br/>The cancellation token.</param>
/// <returns>DID 数据。<br/>The DID data.</returns>
public delegate Task<ReadOnlyMemory<byte>> UdsDataIdentifierProviderAsync(ushort did, CancellationToken cancellationToken);

/// <summary>
/// RoutineControl 仿真处理器。返回 routineStatusRecord，不包含 SID、子功能和 routineId。<br/>
/// RoutineControl simulation handler. Returns the routineStatusRecord, excluding SID, sub-function, and routineId.
/// </summary>
/// <param name="type">例程控制类型。<br/>The routine control type.</param>
/// <param name="routineId">例程标识符。<br/>The routine identifier.</param>
/// <param name="routineData">例程数据。<br/>The routine data.</param>
/// <param name="cancellationToken">取消令牌。<br/>The cancellation token.</param>
/// <returns>例程状态记录。<br/>The routine status record.</returns>
public delegate Task<ReadOnlyMemory<byte>> UdsRoutineControlHandlerAsync(
    RoutineControlType type,
    ushort routineId,
    ReadOnlyMemory<byte> routineData,
    CancellationToken cancellationToken);

/// <summary>
/// 高层 UDS ECU 仿真器，用于测试中快速注册服务、数据和例程。<br/>
/// High-level UDS ECU simulator for quickly registering services, data, and routines in tests.
/// </summary>
public sealed class UdsEcuSimulator : IAsyncUdsServer, IAsyncDisposable
{
    private delegate Task<IReadOnlyList<UdsServerResponse>> UdsSimulatorResponderAsync(
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken);

    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _sendAsync;
    private readonly Func<CancellationToken, Task<ReadOnlyMemory<byte>>> _receiveAsync;
    private readonly ConcurrentDictionary<byte, UdsSimulatorResponderAsync> _responders = new();
    private readonly ConcurrentDictionary<DiagnosticSessionType, SessionTiming> _sessions = new();
    private readonly ConcurrentDictionary<ushort, UdsDataIdentifierProviderAsync> _dids = new();
    private readonly ConcurrentDictionary<byte, SecurityAccessEntry> _securityAccess = new();
    private readonly ConcurrentDictionary<ushort, UdsRoutineControlHandlerAsync> _routines = new();
    private readonly ConcurrentDictionary<uint, DtcStatus> _dtcs = new();
    private readonly SemaphoreSlim _receiveGate = new(1, 1);
    private readonly object _lifetimeSync = new();
    private readonly UdsEcuSimulatorOptions _options;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private int _disposed;
    private DiagnosticSessionType _activeSession;

    /// <summary>
    /// ECU 仿真器配置快照。修改返回对象不会影响正在运行的仿真器。<br/>
    /// Snapshot of the ECU simulator options. Modifying the returned object does not affect the running simulator.
    /// </summary>
    public UdsEcuSimulatorOptions Options => _options.Clone();

    /// <summary>当前诊断会话。<br/>The currently active diagnostic session.</summary>
    public DiagnosticSessionType ActiveSession => _activeSession;

    /// <summary>
    /// 当前或最近一次后台运行循环任务；未启动时为已完成任务。
    /// 传输层异常会使该任务进入 Faulted 状态。<br/>
    /// The current or most recent background run-loop task; a completed task if never started.
    /// Transport-layer exceptions will cause this task to enter a Faulted state.
    /// </summary>
    public Task Completion => Volatile.Read(ref _runTask) ?? Task.CompletedTask;

    /// <summary>
    /// 使用原始发送/接收委托创建 UDS ECU 仿真器。<br/>Creates a UDS ECU simulator with raw send/receive delegates.
    /// </summary>
    /// <param name="sendAsync">异步发送 UDS 响应的委托。<br/>Delegate to send a UDS response asynchronously.</param>
    /// <param name="receiveAsync">异步接收 UDS 请求的委托。<br/>Delegate to receive a UDS request asynchronously.</param>
    /// <param name="options">仿真器参数（可选，使用默认值）。<br/>Optional simulator parameters.</param>
    public UdsEcuSimulator(
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync,
        Func<CancellationToken, Task<ReadOnlyMemory<byte>>> receiveAsync,
        UdsEcuSimulatorOptions? options = null)
    {
        _sendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
        _receiveAsync = receiveAsync ?? throw new ArgumentNullException(nameof(receiveAsync));
        _options = (options ?? new UdsEcuSimulatorOptions()).Clone();
        _options.Validate();
        _activeSession = _options.InitialSession;

        ConfigureSession(DiagnosticSessionType.Default);
        ConfigureSession(DiagnosticSessionType.Programming);
        ConfigureSession(DiagnosticSessionType.ExtendedDiagnostic);
        ConfigureSession(DiagnosticSessionType.SafetySystemDiagnostic);

        if (_options.RegisterDefaultServices)
            RegisterDefaultServices();
    }

    /// <summary>
    /// 从异步传输器接口创建 UDS ECU 仿真器。<br/>Creates a UDS ECU simulator from an <see cref="IAsyncTransmitter{T}"/>.
    /// </summary>
    /// <param name="transport">异步 UDS 传输器接口。<br/>The async UDS transport interface.</param>
    /// <param name="options">仿真器参数（可选）。<br/>Optional simulator parameters.</param>
    public UdsEcuSimulator(IAsyncTransmitter<ReadOnlyMemory<byte>> transport, UdsEcuSimulatorOptions? options = null)
        : this(
            (data, ct) => transport.SendAsync(data, ct),
            transport.ReceiveAsync,
            options)
    {
    }

    /// <summary>注册原始 SID 处理器。处理器返回完整 UDS payload。<br/>Register a raw SID handler. The handler returns the full UDS payload.</summary>
    /// <param name="serviceId">UDS 服务标识符。<br/>The UDS service identifier.</param>
    /// <param name="handler">请求处理程序委托。<br/>The request handler delegate.</param>
    public void Register(byte serviceId, UdsRequestHandlerAsync handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _responders[serviceId] = async (request, cancellationToken) =>
        {
            var response = await handler(request, cancellationToken).ConfigureAwait(false);
            return [new UdsServerResponse(response)];
        };
    }

    /// <summary>注册原始 SID 处理器。处理器返回完整 UDS payload。<br/>Register a raw SID handler. The handler returns the full UDS payload.</summary>
    /// <param name="serviceId">UDS 服务标识符。<br/>The UDS service identifier.</param>
    /// <param name="handler">请求处理程序委托。<br/>The request handler delegate.</param>
    public void Register(UdsServiceId serviceId, UdsRequestHandlerAsync handler) => Register((byte)serviceId, handler);

    /// <summary>注册脚本化响应。每次收到该 SID 请求时按顺序执行这些响应步骤。<br/>Register a scripted response. The response steps are executed in order each time a request for this SID is received.</summary>
    /// <param name="serviceId">UDS 服务标识符。<br/>The UDS service identifier.</param>
    /// <param name="responses">按顺序执行的响应步骤。<br/>The response steps to execute in order.</param>
    public void RegisterScript(byte serviceId, params UdsServerResponse[] responses)
    {
        ArgumentNullException.ThrowIfNull(responses);
        var copy = responses.ToArray();
        _responders[serviceId] = (_, _) => Task.FromResult<IReadOnlyList<UdsServerResponse>>(copy);
    }

    /// <summary>注册脚本化响应。每次收到该 SID 请求时按顺序执行这些响应步骤。<br/>Register a scripted response. The response steps are executed in order each time a request for this SID is received.</summary>
    /// <param name="serviceId">UDS 服务标识符。<br/>The UDS service identifier.</param>
    /// <param name="responses">按顺序执行的响应步骤。<br/>The response steps to execute in order.</param>
    public void RegisterScript(UdsServiceId serviceId, params UdsServerResponse[] responses) => RegisterScript((byte)serviceId, responses);

    /// <summary>移除指定 SID 处理器。<br/>Remove the handler for the specified SID.</summary>
    /// <param name="serviceId">UDS 服务标识符。<br/>The UDS service identifier.</param>
    /// <returns>如果找到并移除了处理器则为 true。<br/>True if a handler was found and removed.</returns>
    public bool Unregister(byte serviceId) => _responders.TryRemove(serviceId, out _);

    /// <summary>移除指定 SID 处理器。<br/>Remove the handler for the specified SID.</summary>
    /// <param name="serviceId">UDS 服务标识符。<br/>The UDS service identifier.</param>
    /// <returns>如果找到并移除了处理器则为 true。<br/>True if a handler was found and removed.</returns>
    public bool Unregister(UdsServiceId serviceId) => Unregister((byte)serviceId);

    /// <summary>配置可进入的诊断会话。<br/>Configure a diagnostic session that can be entered.</summary>
    /// <param name="session">诊断会话类型。<br/>The diagnostic session type.</param>
    /// <param name="p2Server">P2 server 时间（可选）。<br/>Optional P2 server time.</param>
    /// <param name="p2ServerExtended">P2* server 时间（可选）。<br/>Optional P2* server time.</param>
    public void ConfigureSession(
        DiagnosticSessionType session,
        TimeSpan? p2Server = null,
        TimeSpan? p2ServerExtended = null)
    {
        if (!Enum.IsDefined(session))
            throw new ArgumentOutOfRangeException(nameof(session), session, "Invalid diagnostic session.");

        var timing = new SessionTiming(
            p2Server ?? _options.P2Server,
            p2ServerExtended ?? _options.P2ServerExtended);
        ValidateSessionTiming(timing);
        _sessions[session] = timing;
    }

    /// <summary>移除指定诊断会话配置；之后 0x10 请求该会话会返回 NRC 0x12。<br/>Remove a diagnostic session configuration; subsequent 0x10 requests for this session will return NRC 0x12.</summary>
    /// <param name="session">诊断会话类型。<br/>The diagnostic session type.</param>
    /// <returns>如果找到并移除了会话配置则为 true。<br/>True if the session configuration was found and removed.</returns>
    public bool RemoveSession(DiagnosticSessionType session) => _sessions.TryRemove(session, out _);

    /// <summary>设置静态 DID 数据。<br/>Set static DID data.</summary>
    /// <param name="did">数据标识符。<br/>The data identifier.</param>
    /// <param name="data">DID 数据。<br/>The DID data.</param>
    public void SetDataIdentifier(ushort did, ReadOnlyMemory<byte> data)
    {
        ReadOnlyMemory<byte> copy = data.ToArray();
        _dids[did] = (_, _) => Task.FromResult(copy);
    }

    /// <summary>注册动态 DID 数据提供器。<br/>Register a dynamic DID data provider.</summary>
    /// <param name="did">数据标识符。<br/>The data identifier.</param>
    /// <param name="provider">DID 数据提供器委托。<br/>The DID data provider delegate.</param>
    public void RegisterDataIdentifier(ushort did, UdsDataIdentifierProviderAsync provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _dids[did] = async (requestedDid, cancellationToken) =>
        {
            var data = await provider(requestedDid, cancellationToken).ConfigureAwait(false);
            return data.ToArray();
        };
    }

    /// <summary>移除 DID 数据。<br/>Remove DID data.</summary>
    /// <param name="did">数据标识符。<br/>The data identifier.</param>
    /// <returns>如果找到并移除了 DID 则为 true。<br/>True if the DID was found and removed.</returns>
    public bool RemoveDataIdentifier(ushort did) => _dids.TryRemove(did, out _);

    /// <summary>注册 SecurityAccess 等级，使用 seedToKey 计算期望 key。<br/>Register a SecurityAccess level, using seedToKey to compute the expected key.</summary>
    /// <param name="requestSeedLevel">安全访问请求种子等级。<br/>The security access request seed level.</param>
    /// <param name="seed">种子值。<br/>The seed value.</param>
    /// <param name="seedToKey">根据种子计算期望密钥的函数。<br/>Function to compute the expected key from the seed.</param>
    public void RegisterSecurityAccess(byte requestSeedLevel, ReadOnlyMemory<byte> seed, Func<byte[], byte[]> seedToKey)
    {
        ArgumentNullException.ThrowIfNull(seedToKey);
        RegisterSecurityAccess(requestSeedLevel, seed, (seedBytes, keyBytes) =>
        {
            var expected = seedToKey(seedBytes);
            return expected is not null && expected.AsSpan().SequenceEqual(keyBytes);
        });
    }

    /// <summary>注册 SecurityAccess 等级，使用自定义 key 校验器。<br/>Register a SecurityAccess level with a custom key validator.</summary>
    /// <param name="requestSeedLevel">安全访问请求种子等级。<br/>The security access request seed level.</param>
    /// <param name="seed">种子值。<br/>The seed value.</param>
    /// <param name="validateKey">根据种子和密钥验证的函数。<br/>Function to validate the key against the seed.</param>
    public void RegisterSecurityAccess(byte requestSeedLevel, ReadOnlyMemory<byte> seed, Func<byte[], byte[], bool> validateKey)
    {
        ArgumentNullException.ThrowIfNull(validateKey);
        if (requestSeedLevel == 0 || (requestSeedLevel & 1) == 0)
            throw new ArgumentException("Seed-request sub-function must be odd and non-zero.", nameof(requestSeedLevel));

        _securityAccess[requestSeedLevel] = new SecurityAccessEntry(seed.ToArray(), validateKey);
    }

    /// <summary>返回指定 SecurityAccess 等级是否已解锁。<br/>Returns whether the specified SecurityAccess level is unlocked.</summary>
    /// <param name="requestSeedLevel">安全访问请求种子等级。<br/>The security access request seed level.</param>
    /// <returns>如果该等级已解锁则为 true。<br/>True if the level is unlocked.</returns>
    public bool IsSecurityUnlocked(byte requestSeedLevel)
        => _securityAccess.TryGetValue(requestSeedLevel, out var entry) && entry.IsUnlocked;

    /// <summary>锁定指定 SecurityAccess 等级。<br/>Lock the specified SecurityAccess level.</summary>
    /// <param name="requestSeedLevel">安全访问请求种子等级。<br/>The security access request seed level.</param>
    /// <returns>如果找到并锁定了该等级则为 true。<br/>True if the level was found and locked.</returns>
    public bool LockSecurityAccess(byte requestSeedLevel)
    {
        if (!_securityAccess.TryGetValue(requestSeedLevel, out var entry))
            return false;
        entry.Lock();
        return true;
    }

    /// <summary>注册固定 RoutineControl 响应状态记录。<br/>Register a fixed RoutineControl response status record.</summary>
    /// <param name="routineId">例程标识符。<br/>The routine identifier.</param>
    /// <param name="statusRecord">状态记录数据。<br/>The status record data.</param>
    public void RegisterRoutine(ushort routineId, ReadOnlyMemory<byte> statusRecord)
    {
        ReadOnlyMemory<byte> copy = statusRecord.ToArray();
        _routines[routineId] = (_, _, _, _) => Task.FromResult(copy);
    }

    /// <summary>注册 RoutineControl 动态处理器。<br/>Register a dynamic RoutineControl handler.</summary>
    /// <param name="routineId">例程标识符。<br/>The routine identifier.</param>
    /// <param name="handler">例程处理器委托。<br/>The routine handler delegate.</param>
    public void RegisterRoutine(ushort routineId, UdsRoutineControlHandlerAsync handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _routines[routineId] = async (type, requestedRoutineId, routineData, cancellationToken) =>
        {
            var status = await handler(type, requestedRoutineId, routineData.ToArray(), cancellationToken).ConfigureAwait(false);
            return status.ToArray();
        };
    }

    /// <summary>移除 RoutineControl 处理器。<br/>Remove a RoutineControl handler.</summary>
    /// <param name="routineId">例程标识符。<br/>The routine identifier.</param>
    /// <returns>如果找到并移除了处理器则为 true。<br/>True if a handler was found and removed.</returns>
    public bool UnregisterRoutine(ushort routineId) => _routines.TryRemove(routineId, out _);

    /// <summary>设置 DTC 状态。<br/>Set the DTC status.</summary>
    /// <param name="dtcCode">DTC 代码。<br/>The DTC code.</param>
    /// <param name="status">DTC 状态。<br/>The DTC status.</param>
    public void SetDtc(uint dtcCode, DtcStatus status) => _dtcs[dtcCode & 0x00FFFFFF] = status;

    /// <summary>移除 DTC。<br/>Remove a DTC.</summary>
    /// <param name="dtcCode">DTC 代码。<br/>The DTC code.</param>
    /// <returns>如果找到并移除了 DTC 则为 true。<br/>True if the DTC was found and removed.</returns>
    public bool RemoveDtc(uint dtcCode) => _dtcs.TryRemove(dtcCode & 0x00FFFFFF, out _);

    /// <summary>清空 DTC 表。<br/>Clear all DTCs.</summary>
    public void ClearDtcs() => _dtcs.Clear();

    /// <summary>
    /// 启动后台接收/响应循环。
    /// 停止后可再次启动；正在后台运行或正在执行单步接收时再次启动会抛出异常。<br/>
    /// Start the background receive/respond loop.
    /// Can be restarted after stopping; throws if the loop is already running
    /// or if a single-step receive is in progress.
    /// </summary>
    /// <param name="cancellationToken">取消令牌。<br/>The cancellation token.</param>
    /// <returns>已完成的任务。<br/>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (!_receiveGate.Wait(0, cancellationToken))
            throw new InvalidOperationException("The ECU simulator is already receiving a request.");

        try
        {
            lock (_lifetimeSync)
            {
                if (_runTask is { IsCompleted: false })
                    throw new InvalidOperationException("The ECU simulator background loop is already running.");

                _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _runTask = Task.Run(() => RunAsync(_runCts.Token), CancellationToken.None);
            }

            return Task.CompletedTask;
        }
        catch
        {
            lock (_lifetimeSync)
            {
                if (_runTask is null)
                {
                    _runCts?.Dispose();
                    _runCts = null;
                }
            }
            throw;
        }
        finally
        {
            _receiveGate.Release();
        }
    }

    /// <summary>
    /// 停止后台接收/响应循环。
    /// 如果后台循环已因传输层异常失败，等待该方法会重新抛出该异常。<br/>
    /// Stop the background receive/respond loop.
    /// If the loop has already faulted due to a transport-layer exception,
    /// awaiting this method will rethrow that exception.
    /// </summary>
    /// <param name="cancellationToken">取消令牌。<br/>The cancellation token.</param>
    /// <returns>表示停止操作的任务。<br/>A task that represents the stop operation.</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? task;
        CancellationTokenSource? cts;
        lock (_lifetimeSync)
        {
            task = _runTask;
            cts = _runCts;
        }

        if (task is null)
            return;

        cts?.Cancel();
        try
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (task.IsCompleted)
            {
                lock (_lifetimeSync)
                {
                    if (ReferenceEquals(_runTask, task))
                    {
                        _runCts?.Dispose();
                        _runCts = null;
                    }
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task<ReadOnlyMemory<byte>> ReceiveAndRespondAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (IsBackgroundRunning())
            throw new InvalidOperationException("ReceiveAndRespondAsync cannot be used while the ECU simulator background loop is running.");
        if (!await _receiveGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The ECU simulator is already receiving a request.");

        try
        {
            return await ReceiveAndRespondCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _receiveGate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _runCts?.Cancel();
        try
        {
            if (_runTask is not null)
                await _runTask.ConfigureAwait(false);
        }
        finally
        {
            _runCts?.Dispose();
            _receiveGate.Dispose();
        }
    }

    private async Task<ReadOnlyMemory<byte>> ReceiveAndRespondCoreAsync(CancellationToken cancellationToken)
    {
        var request = await _receiveAsync(cancellationToken).ConfigureAwait(false);
        if (request.Length == 0) return ReadOnlyMemory<byte>.Empty;

        var requestCopy = request.ToArray();
        byte sid = requestCopy[0];
        bool suppress = UdsMessage.IsSuppressPositiveResponse(requestCopy);
        IReadOnlyList<UdsServerResponse> responses;

        if (_responders.TryGetValue(sid, out var responder))
            responses = await responder(requestCopy, cancellationToken).ConfigureAwait(false);
        else
            responses = [UdsServerResponse.Negative(sid, NegativeResponseCode.ServiceNotSupported)];

        ReadOnlyMemory<byte> lastSent = ReadOnlyMemory<byte>.Empty;
        foreach (var step in responses)
        {
            if (step.Delay > TimeSpan.Zero)
                await Task.Delay(step.Delay, cancellationToken).ConfigureAwait(false);
            if (step.Payload.IsEmpty)
                continue;
            if (suppress && UdsMessage.IsPositiveResponseFor(requestCopy, step.Payload.Span))
                continue;

            await _sendAsync(step.Payload, cancellationToken).ConfigureAwait(false);
            lastSent = step.Payload;
        }

        return lastSent;
    }

    private void RegisterDefaultServices()
    {
        _responders[(byte)UdsServiceId.DiagnosticSessionControl] = HandleDiagnosticSessionControlAsync;
        _responders[(byte)UdsServiceId.TesterPresent] = HandleTesterPresentAsync;
        _responders[(byte)UdsServiceId.ReadDataByIdentifier] = HandleReadDataByIdentifierAsync;
        _responders[(byte)UdsServiceId.SecurityAccess] = HandleSecurityAccessAsync;
        _responders[(byte)UdsServiceId.RoutineControl] = HandleRoutineControlAsync;
        _responders[(byte)UdsServiceId.ReadDtcInformation] = HandleReadDtcInformationAsync;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _receiveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await ReceiveAndRespondCoreAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _receiveGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private Task<IReadOnlyList<UdsServerResponse>> HandleDiagnosticSessionControlAsync(
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var span = request.Span;
        if (span.Length != 2)
            return SingleNegativeAsync(0x10, NegativeResponseCode.IncorrectMessageLengthOrInvalidFormat);

        var sessionByte = (byte)(span[1] & 0x7F);
        if (!Enum.IsDefined(typeof(DiagnosticSessionType), sessionByte))
            return SingleNegativeAsync(0x10, NegativeResponseCode.SubFunctionNotSupported);

        var session = (DiagnosticSessionType)sessionByte;
        if (!_sessions.TryGetValue(session, out var timing))
            return SingleNegativeAsync(0x10, NegativeResponseCode.SubFunctionNotSupported);

        _activeSession = session;
        Span<byte> body = stackalloc byte[5];
        body[0] = sessionByte;
        BinaryPrimitives.WriteUInt16BigEndian(body[1..3], EncodeMilliseconds(timing.P2Server));
        BinaryPrimitives.WriteUInt16BigEndian(body[3..5], EncodeTenMilliseconds(timing.P2ServerExtended));
        return SinglePositiveAsync(0x10, body);
    }

    private Task<IReadOnlyList<UdsServerResponse>> HandleTesterPresentAsync(
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var span = request.Span;
        if (span.Length != 2)
            return SingleNegativeAsync(0x3E, NegativeResponseCode.IncorrectMessageLengthOrInvalidFormat);
        if ((span[1] & 0x7F) != 0)
            return SingleNegativeAsync(0x3E, NegativeResponseCode.SubFunctionNotSupported);

        return SinglePositiveAsync(0x3E, [0x00]);
    }

    private async Task<IReadOnlyList<UdsServerResponse>> HandleReadDataByIdentifierAsync(
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken)
    {
        var bytes = request.ToArray();
        if (bytes.Length < 3 || (bytes.Length - 1) % 2 != 0)
            return SingleNegative(0x22, NegativeResponseCode.IncorrectMessageLengthOrInvalidFormat);

        var response = new List<byte> { 0x62 };
        for (var pos = 1; pos < bytes.Length; pos += 2)
        {
            ushort did = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos, 2));
            if (!_dids.TryGetValue(did, out var provider))
                return SingleNegative(0x22, NegativeResponseCode.RequestOutOfRange);

            var data = await provider(did, cancellationToken).ConfigureAwait(false);
            response.Add((byte)(did >> 8));
            response.Add((byte)did);
            response.AddRange(data.ToArray());
        }

        return [new UdsServerResponse(response.ToArray())];
    }

    private Task<IReadOnlyList<UdsServerResponse>> HandleSecurityAccessAsync(
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var span = request.Span;
        if (span.Length < 2)
            return SingleNegativeAsync(0x27, NegativeResponseCode.IncorrectMessageLengthOrInvalidFormat);

        byte subFunction = (byte)(span[1] & 0x7F);
        if (subFunction == 0)
            return SingleNegativeAsync(0x27, NegativeResponseCode.SubFunctionNotSupported);

        if ((subFunction & 1) == 1)
        {
            if (span.Length != 2)
                return SingleNegativeAsync(0x27, NegativeResponseCode.IncorrectMessageLengthOrInvalidFormat);
            if (!_securityAccess.TryGetValue(subFunction, out var entry))
                return SingleNegativeAsync(0x27, NegativeResponseCode.SubFunctionNotSupported);

            var seed = entry.BeginSeedRequest();
            var response = new byte[2 + seed.Length];
            response[0] = 0x67;
            response[1] = subFunction;
            seed.CopyTo(response.AsSpan(2));
            return SingleAsync(response);
        }

        byte requestSeedLevel = (byte)(subFunction - 1);
        if (!_securityAccess.TryGetValue(requestSeedLevel, out var keyEntry))
            return SingleNegativeAsync(0x27, NegativeResponseCode.SubFunctionNotSupported);

        var key = span[2..].ToArray();
        var result = keyEntry.TryUnlock(key);
        if (result is not null)
            return SingleNegativeAsync(0x27, result.Value);

        return SingleAsync(new byte[] { 0x67, subFunction });
    }

    private async Task<IReadOnlyList<UdsServerResponse>> HandleRoutineControlAsync(
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken)
    {
        var bytes = request.ToArray();
        if (bytes.Length < 4)
            return SingleNegative(0x31, NegativeResponseCode.IncorrectMessageLengthOrInvalidFormat);

        var typeByte = (byte)(bytes[1] & 0x7F);
        if (!Enum.IsDefined(typeof(RoutineControlType), typeByte))
            return SingleNegative(0x31, NegativeResponseCode.SubFunctionNotSupported);

        var type = (RoutineControlType)typeByte;
        ushort routineId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2));
        if (!_routines.TryGetValue(routineId, out var handler))
            return SingleNegative(0x31, NegativeResponseCode.RequestOutOfRange);

        var status = await handler(type, routineId, bytes.AsMemory(4), cancellationToken).ConfigureAwait(false);
        var response = new byte[4 + status.Length];
        response[0] = 0x71;
        response[1] = typeByte;
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), routineId);
        status.CopyTo(response.AsMemory(4));
        return [new UdsServerResponse(response)];
    }

    private Task<IReadOnlyList<UdsServerResponse>> HandleReadDtcInformationAsync(
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var span = request.Span;
        if (span.Length < 2)
            return SingleNegativeAsync(0x19, NegativeResponseCode.IncorrectMessageLengthOrInvalidFormat);

        var subFunction = (ReadDtcSubFunction)(span[1] & 0x7F);
        switch (subFunction)
        {
            case ReadDtcSubFunction.ReportNumberOfDtcByStatusMask:
            case ReadDtcSubFunction.ReportDtcByStatusMask:
                if (span.Length != 3)
                    return SingleNegativeAsync(0x19, NegativeResponseCode.IncorrectMessageLengthOrInvalidFormat);
                var mask = (DtcStatus)span[2];
                return subFunction == ReadDtcSubFunction.ReportNumberOfDtcByStatusMask
                    ? BuildDtcCountResponse(mask)
                    : BuildDtcListResponse(mask);
            default:
                return SingleNegativeAsync(0x19, NegativeResponseCode.SubFunctionNotSupported);
        }
    }

    private Task<IReadOnlyList<UdsServerResponse>> BuildDtcCountResponse(DtcStatus mask)
    {
        var count = CountMatchingDtcs(mask);
        Span<byte> body = stackalloc byte[5];
        body[0] = (byte)ReadDtcSubFunction.ReportNumberOfDtcByStatusMask;
        body[1] = (byte)_options.DtcStatusAvailabilityMask;
        body[2] = _options.DtcFormatIdentifier;
        BinaryPrimitives.WriteUInt16BigEndian(body[3..5], count);
        return SinglePositiveAsync(0x19, body);
    }

    private Task<IReadOnlyList<UdsServerResponse>> BuildDtcListResponse(DtcStatus mask)
    {
        var records = GetMatchingDtcs(mask);
        var response = new byte[3 + records.Count * 4];
        response[0] = 0x59;
        response[1] = (byte)ReadDtcSubFunction.ReportDtcByStatusMask;
        response[2] = (byte)_options.DtcStatusAvailabilityMask;

        var pos = 3;
        foreach (var (code, status) in records)
        {
            response[pos++] = (byte)(code >> 16);
            response[pos++] = (byte)(code >> 8);
            response[pos++] = (byte)code;
            response[pos++] = (byte)status;
        }

        return SingleAsync(response);
    }

    private ushort CountMatchingDtcs(DtcStatus mask)
    {
        var count = 0;
        foreach (var status in _dtcs.Values)
        {
            if ((status & mask) != 0)
                count++;
        }
        return count > ushort.MaxValue ? ushort.MaxValue : (ushort)count;
    }

    private List<KeyValuePair<uint, DtcStatus>> GetMatchingDtcs(DtcStatus mask)
    {
        var list = new List<KeyValuePair<uint, DtcStatus>>();
        foreach (var item in _dtcs)
        {
            if ((item.Value & mask) != 0)
                list.Add(item);
        }
        list.Sort(static (a, b) => a.Key.CompareTo(b.Key));
        return list;
    }

    private static IReadOnlyList<UdsServerResponse> SingleNegative(byte serviceId, NegativeResponseCode code)
        => [UdsServerResponse.Negative(serviceId, code)];

    private static Task<IReadOnlyList<UdsServerResponse>> SingleNegativeAsync(byte serviceId, NegativeResponseCode code)
        => Task.FromResult(SingleNegative(serviceId, code));

    private static Task<IReadOnlyList<UdsServerResponse>> SinglePositiveAsync(byte serviceId, ReadOnlySpan<byte> body)
        => SingleAsync(AsyncUdsServer.BuildPositiveResponse(serviceId, body));

    private static Task<IReadOnlyList<UdsServerResponse>> SingleAsync(ReadOnlyMemory<byte> payload)
        => Task.FromResult<IReadOnlyList<UdsServerResponse>>([new UdsServerResponse(payload)]);

    private static void ValidateSessionTiming(SessionTiming timing)
    {
        ValidatePositiveTimeout(timing.P2Server, nameof(UdsEcuSimulatorOptions.P2Server));
        ValidatePositiveTimeout(timing.P2ServerExtended, nameof(UdsEcuSimulatorOptions.P2ServerExtended));
        _ = EncodeMilliseconds(timing.P2Server);
        _ = EncodeTenMilliseconds(timing.P2ServerExtended);
    }

    private static void ValidatePositiveTimeout(TimeSpan value, string paramName)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(paramName, value, "Timeout must be greater than zero.");
    }

    private static ushort EncodeMilliseconds(TimeSpan value)
    {
        var encoded = Math.Ceiling(value.TotalMilliseconds);
        if (encoded > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), value, "P2 timing value is too large to encode in a UDS response.");
        return (ushort)encoded;
    }

    private static ushort EncodeTenMilliseconds(TimeSpan value)
    {
        var encoded = Math.Ceiling(value.TotalMilliseconds / 10);
        if (encoded > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), value, "P2 timing value is too large to encode in a UDS response.");
        return (ushort)encoded;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(UdsEcuSimulator));
    }

    private bool IsBackgroundRunning()
    {
        lock (_lifetimeSync)
            return _runTask is { IsCompleted: false };
    }

    private readonly record struct SessionTiming(TimeSpan P2Server, TimeSpan P2ServerExtended);

    private sealed class SecurityAccessEntry
    {
        private readonly object _sync = new();
        private readonly byte[] _seed;
        private readonly Func<byte[], byte[], bool> _validateKey;
        private byte[]? _pendingSeed;
        private bool _unlocked;

        public SecurityAccessEntry(byte[] seed, Func<byte[], byte[], bool> validateKey)
        {
            _seed = seed;
            _validateKey = validateKey;
        }

        public bool IsUnlocked
        {
            get { lock (_sync) return _unlocked; }
        }

        public byte[] BeginSeedRequest()
        {
            lock (_sync)
            {
                if (_unlocked)
                    return new byte[_seed.Length];

                _pendingSeed = _seed.ToArray();
                return _pendingSeed.ToArray();
            }
        }

        public NegativeResponseCode? TryUnlock(byte[] key)
        {
            lock (_sync)
            {
                if (_pendingSeed is null)
                    return NegativeResponseCode.RequestSequenceError;

                if (!_validateKey(_pendingSeed.ToArray(), key.ToArray()))
                    return NegativeResponseCode.InvalidKey;

                _unlocked = true;
                _pendingSeed = null;
                return null;
            }
        }

        public void Lock()
        {
            lock (_sync)
            {
                _unlocked = false;
                _pendingSeed = null;
            }
        }
    }
}
