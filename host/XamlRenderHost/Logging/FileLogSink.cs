using System.IO;
using System.Text;

namespace XamlRenderHost.Logging;

/// <summary>
/// 크기 기준으로 회전하는 파일 로그 출력 대상(host.log, host.1.log, … 최대 <c>maxFiles</c>개).
/// Owner: <see cref="HostLogger"/>. Lifetime: 로거와 같음. 스레드: HostLogger의 쓰기 스레드 하나에서만 호출되므로 자체 동기화는 없다.
/// 파일 쓰기 실패(디스크 가득, 권한 등)는 호스트 동작을 막지 않도록 삼킨다 — 로그는 보조 기능이다.
/// </summary>
public sealed class FileLogSink : ILogSink
{
    /// <summary>회전 기준 파일 크기 기본값(1MB, doc/01 5절).</summary>
    public const long DefaultMaxBytes = 1024 * 1024;

    /// <summary>보관 파일 수 기본값(host.log 포함 5개).</summary>
    public const int DefaultMaxFiles = 5;

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly string _directory;
    private readonly string _baseName;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private StreamWriter? _writer;
    private long _currentBytes;

    public FileLogSink(string directory, string baseName = "host", long maxBytes = DefaultMaxBytes, int maxFiles = DefaultMaxFiles)
    {
        _directory = directory;
        _baseName = baseName;
        _maxBytes = maxBytes;
        _maxFiles = maxFiles;
        Directory.CreateDirectory(directory);
    }

    private string PathFor(int index) => Path.Combine(_directory, index == 0 ? $"{_baseName}.log" : $"{_baseName}.{index}.log");

    /// <summary>한 줄을 쓰고 즉시 flush한다. 현재 파일이 크기 한도를 넘게 되면 먼저 회전한다.</summary>
    public void WriteLine(string line)
    {
        try
        {
            EnsureWriter();
            var bytes = Utf8NoBom.GetByteCount(line) + Environment.NewLine.Length;
            if (_currentBytes > 0 && _currentBytes + bytes > _maxBytes)
            {
                Rotate();
                EnsureWriter();
            }
            else
            {
                // 한도 이내: 현재 파일에 이어 쓴다.
            }
            _writer!.WriteLine(line);
            _writer.Flush();
            _currentBytes += bytes;
        }
        catch (IOException)
        {
            // 로그 실패는 삼킨다(호스트 동작 우선).
        }
        catch (UnauthorizedAccessException)
        {
            // 위와 같음.
        }
    }

    private void EnsureWriter()
    {
        if (_writer == null)
        {
            var path = PathFor(0);
            _currentBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
            _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Utf8NoBom);
        }
        else
        {
            // 이미 열려 있음.
        }
    }

    /// <summary>host.(n-1).log → host.n.log 로 밀고 가장 오래된 파일을 지운다. host.log는 새로 시작한다.</summary>
    private void Rotate()
    {
        _writer?.Dispose();
        _writer = null;

        var oldest = PathFor(_maxFiles - 1);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }
        else
        {
            // 가장 오래된 파일이 아직 없음.
        }
        for (var i = _maxFiles - 2; i >= 0; i--)
        {
            var from = PathFor(i);
            if (File.Exists(from))
            {
                File.Move(from, PathFor(i + 1));
            }
            else
            {
                // 해당 번호 파일 없음: 건너뜀.
            }
        }
        _currentBytes = 0;
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _writer = null;
    }
}
