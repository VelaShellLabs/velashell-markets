using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VelaShell.Market.Infrastructure.Scanning;

/// <summary>ClamAV 连接配置。</summary>
public sealed class ClamAvOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "ClamAv";

    /// <summary>是否启用。关掉时病毒扫描这一步被跳过,并在报告里留一条 Info —— 绝不静默略过。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>clamd 主机名。</summary>
    public string Host { get; set; } = "clamav";

    /// <summary>clamd 端口。</summary>
    public int Port { get; set; } = 3310;

    /// <summary>单次扫描超时。</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>扫描结论。</summary>
/// <param name="IsClean">是否干净。</param>
/// <param name="Signature">命中的病毒特征名(干净时为空)。</param>
/// <param name="EngineVersion">引擎与病毒库版本,用于报告的可复现性。</param>
public sealed record ClamAvResult(bool IsClean, string? Signature, string EngineVersion);

/// <summary>
/// clamd 的最小客户端,直接说 INSTREAM 协议(<c>zINSTREAM\0</c> + 分块 + 零长块结束)。
/// <para>
/// 为什么不引第三方封装:这个协议一共就三条命令,而扫描是**安全边界上的一步** ——
/// 这里每一个字节怎么发、超时怎么算、连不上算什么结论,都得是我们自己说了算,
/// 不能藏在一个不知道怎么处理错误的封装后面。连不上一律按"检测失败"处理,绝不当成"干净"。
/// </para>
/// </summary>
public sealed class ClamAvScanner(IOptions<ClamAvOptions> options, ILogger<ClamAvScanner> logger)
{
    /// <summary>clamd 的分块上限是 StreamMaxLength(默认 25MB),这里按 64KB 发,稳妥且不占内存。</summary>
    private const int ChunkSize = 64 * 1024;

    private readonly ClamAvOptions _options = options.Value;

    /// <summary>是否启用。</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>扫描一段流。流会被从当前位置读到末尾。</summary>
    /// <exception cref="ClamAvUnavailableException">连不上或协议异常 —— 调用方必须按"检测未完成"处理。</exception>
    public async Task<ClamAvResult> ScanAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        string version = await GetVersionAsync(cancellationToken).ConfigureAwait(false);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.Timeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_options.Host, _options.Port, cts.Token).ConfigureAwait(false);
            await using NetworkStream stream = client.GetStream();

            await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), cts.Token).ConfigureAwait(false);
            byte[] buffer = new byte[ChunkSize];
            byte[] length = new byte[4];
            int read;
            while ((read = await content.ReadAsync(buffer, cts.Token).ConfigureAwait(false)) > 0)
            {
                // 块头是**大端** 4 字节长度,这是 clamd 协议的规定,不是我们的选择。
                BinaryPrimitives.WriteInt32BigEndian(length, read);
                await stream.WriteAsync(length, cts.Token).ConfigureAwait(false);
                await stream.WriteAsync(buffer.AsMemory(0, read), cts.Token).ConfigureAwait(false);
            }
            BinaryPrimitives.WriteInt32BigEndian(length, 0); // 零长块 = 传完了
            await stream.WriteAsync(length, cts.Token).ConfigureAwait(false);
            await stream.FlushAsync(cts.Token).ConfigureAwait(false);

            string response = await ReadResponseAsync(stream, cts.Token).ConfigureAwait(false);
            // 形如 "stream: OK" 或 "stream: Eicar-Test-Signature FOUND"
            if (response.EndsWith("OK", StringComparison.Ordinal))
            {
                return new(true, null, version);
            }
            if (response.EndsWith("FOUND", StringComparison.Ordinal))
            {
                string signature = response[(response.IndexOf(':') + 1)..].Replace("FOUND", "").Trim();
                logger.LogWarning("ClamAV hit: {Signature}", signature);
                return new(false, signature, version);
            }
            throw new ClamAvUnavailableException($"Unexpected clamd response: {response}");
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new ClamAvUnavailableException($"clamd at {_options.Host}:{_options.Port} is unreachable or timed out.", ex);
        }
    }

    /// <summary>取引擎与病毒库版本(写进扫描报告)。拿不到时不致命,报告里记 unknown。</summary>
    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            using var client = new TcpClient();
            await client.ConnectAsync(_options.Host, _options.Port, cts.Token).ConfigureAwait(false);
            await using NetworkStream stream = client.GetStream();
            await stream.WriteAsync("zVERSION\0"u8.ToArray(), cts.Token).ConfigureAwait(false);
            return await ReadResponseAsync(stream, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            return "unknown";
        }
    }

    private static async Task<string> ReadResponseAsync(Stream stream, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        byte[] buffer = new byte[256];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            builder.Append(Encoding.ASCII.GetString(buffer, 0, read));
            if (builder.Length > 0 && builder[^1] == '\0')
            {
                break; // z 前缀的命令以 NUL 结束应答
            }
        }
        return builder.ToString().TrimEnd('\0', '\n', '\r').Trim();
    }
}

/// <summary>
/// 病毒扫描引擎不可用。**独立异常类型**是有意的:调用方必须把它与"扫出了病毒"区分开 ——
/// 前者是我们的基础设施问题(应重试、不该判作者的包有罪),后者是拒收。
/// 把两者混成一个 bool 返回值,迟早会写出"连不上就当干净"这种默认放行。
/// </summary>
public sealed class ClamAvUnavailableException : Exception
{
    /// <summary>以消息构造。</summary>
    public ClamAvUnavailableException(string message) : base(message)
    {
    }

    /// <summary>以消息与内层异常构造。</summary>
    public ClamAvUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
